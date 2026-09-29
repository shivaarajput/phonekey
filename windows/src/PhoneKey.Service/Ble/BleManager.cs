using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Protocol;
using PhoneKey.Core.Proximity;
using PhoneKey.Core.Storage;

namespace PhoneKey.Service.Ble
{
    public sealed class BleManager : IAsyncDisposable
    {
        public static readonly Guid ServiceUuid = new("0000FEE0-7068-6F6E-656B-657900000001");
        public static readonly Guid VersionCharUuid = new("0000FEE1-7068-6F6E-656B-657900000001");
        public static readonly Guid ChallengeCharUuid = new("0000FEE2-7068-6F6E-656B-657900000001");
        public static readonly Guid HeartbeatCharUuid = new("0000FEE3-7068-6F6E-656B-657900000001");
        public static readonly Guid PublicKeyCharUuid = new("0000FEE4-7068-6F6E-656B-657900000001");

        private readonly ILogger<BleManager> _logger;
        private readonly DeviceRegistry _deviceRegistry;
        private readonly NonceManager _nonceManager;
        private readonly ProximityStateMachine _stateMachine;
        private readonly Guid _pcId;

        private BluetoothLEAdvertisementWatcher? _watcher;
        private BluetoothLEDevice? _connectedDevice;
        private GattCharacteristic? _challengeChar;
        private GattCharacteristic? _heartbeatChar;
        private GattCharacteristic? _publicKeyChar;
        private EnrolledDevice? _activeEnrolledDevice;
        private TaskCompletionSource<byte[]>? _challengeResponseTcs;
        private readonly SemaphoreSlim _authLock = new(1, 1);
        private bool _isDisposed;

        public BleManager(
            ILogger<BleManager> logger,
            DeviceRegistry deviceRegistry,
            NonceManager nonceManager,
            ProximityStateMachine stateMachine,
            Guid pcId)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
            _nonceManager = nonceManager ?? throw new ArgumentNullException(nameof(nonceManager));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _pcId = pcId;
        }

        public EnrolledDevice? ActiveDevice => _activeEnrolledDevice;

        public void StartScanning()
        {
            _logger.LogInformation("Starting BLE Advertisement Watcher for Service UUID {Uuid}", ServiceUuid);

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            // Filter for PhoneKey service advertisement
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(ServiceUuid);
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Start();
        }

        public void StopScanning()
        {
            if (_watcher != null)
            {
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stop();
                _watcher = null;
                _logger.LogInformation("BLE Advertisement Watcher stopped.");
            }
        }

        private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Update proximity state machine with live advertisement RSSI
            _stateMachine.ProcessRssi(args.RawSignalStrengthInDBm);

            if (_connectedDevice != null) return; // Already connected or negotiating

            var devices = _deviceRegistry.GetDevices().Where(d => !d.IsRevoked).ToList();
            if (devices.Count == 0)
            {
                return; // No enrolled phones yet
            }

            _logger.LogInformation("Discovered PhoneKey peripheral. Bluetooth Address: {Addr:X}, RSSI: {Rssi} dBm", 
                args.BluetoothAddress, args.RawSignalStrengthInDBm);

            await ConnectToDeviceAsync(args.BluetoothAddress);
        }

        private async Task ConnectToDeviceAsync(ulong bluetoothAddress)
        {
            await _authLock.WaitAsync();
            try
            {
                if (_connectedDevice != null) return;

                _stateMachine.NotifyConnected();
                _logger.LogInformation("Connecting to BLE peripheral {Addr:X}...", bluetoothAddress);

                _connectedDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (_connectedDevice == null)
                {
                    _logger.LogWarning("Failed to acquire BluetoothLEDevice instance.");
                    _stateMachine.NotifyDisconnected();
                    return;
                }

                _connectedDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

                var gattServicesResult = await _connectedDevice.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached);
                if (gattServicesResult.Status != GattCommunicationStatus.Success || !gattServicesResult.Services.Any())
                {
                    _logger.LogWarning("GATT Service discovery failed: {Status}", gattServicesResult.Status);
                    Disconnect();
                    return;
                }

                var service = gattServicesResult.Services.First();
                await RequestMtuAsync(service);

                // Discover Characteristics
                var charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (charResult.Status != GattCommunicationStatus.Success)
                {
                    _logger.LogWarning("GATT Characteristic discovery failed.");
                    Disconnect();
                    return;
                }

                _challengeChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == ChallengeCharUuid);
                _heartbeatChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == HeartbeatCharUuid);
                _publicKeyChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == PublicKeyCharUuid);

                // Auto-read and bind phone's public key from TEE/StrongBox if available
                if (_publicKeyChar != null)
                {
                    try
                    {
                        var pubKeyResult = await _publicKeyChar.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (pubKeyResult.Status == GattCommunicationStatus.Success && pubKeyResult.Value.Length > 0)
                        {
                            byte[] rawKey = pubKeyResult.Value.ToArray();
                            string keyB64 = Convert.ToBase64String(rawKey);
                            var devs = _deviceRegistry.GetDevices().Where(d => !d.IsRevoked).ToList();
                            foreach (var dev in devs)
                            {
                                if (string.IsNullOrEmpty(dev.PublicKeyBase64) || dev.PublicKeyBase64.Length < 20 || dev.DeviceName == "Enrolled Android Phone")
                                {
                                    dev.PublicKeyBase64 = keyB64;
                                    _deviceRegistry.SaveDevice(dev);
                                    _logger.LogInformation("Imported phone public key ({Len} bytes) via GATT into enrolled device registry.", rawKey.Length);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Public key characteristic read optional fallback.");
                    }
                }

                if (_challengeChar == null)
                {
                    _logger.LogError("Mandatory Challenge Characteristic missing from peripheral.");
                    Disconnect();
                    return;
                }

                // Subscribe to indications for challenge response
                _challengeChar.ValueChanged += OnChallengeResponseReceived;
                var cccdStatus = await _challengeChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Indicate);

                if (cccdStatus != GattCommunicationStatus.Success)
                {
                    _logger.LogWarning("Failed to configure Challenge Characteristic CCCD: {Status}", cccdStatus);
                    Disconnect();
                    return;
                }

                // Execute Cryptographic Challenge-Response Handshake
                bool authSuccess = await ExecuteChallengeHandshakeAsync();
                if (!authSuccess)
                {
                    _logger.LogWarning("Cryptographic Challenge-Response handshake rejected.");
                    Disconnect();
                    return;
                }

                _logger.LogInformation("Phone successfully authenticated cryptographically.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during BLE connection/handshake.");
                Disconnect();
            }
            finally
            {
                _authLock.Release();
            }
        }

        private async Task RequestMtuAsync(GattDeviceService service)
        {
            try
            {
                var bluetoothDeviceId = BluetoothDeviceId.FromId(service.DeviceId);
                var session = await GattSession.FromDeviceIdAsync(bluetoothDeviceId);
                if (session != null)
                {
                    _logger.LogInformation("GATT Session Max PDU Size: {Size}", session.MaxPduSize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MTU negotiation query optional fallback.");
            }
        }

        private async Task<bool> ExecuteChallengeHandshakeAsync()
        {
            if (_challengeChar == null) return false;

            var enrolledDevices = _deviceRegistry.GetDevices().Where(d => !d.IsRevoked).ToList();
            if (!enrolledDevices.Any()) return false;

            // Generate fresh challenge nonce bound to PC ID
            byte[] noncePc = _nonceManager.GenerateChallenge(_pcId, out var issuedAt);

            var req = new AuthChallengeReq
            {
                PcId = _pcId,
                NoncePc = noncePc,
                TimestampUtcMs = issuedAt.ToUnixTimeMilliseconds(),
                PolicyFlags = 0x00
            };

            byte[] requestPayload = BinaryWireCodec.EncodeChallengeReq(req);
            _challengeResponseTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            var sw = Stopwatch.StartNew();

            // Write Challenge Request to Android Characteristic
            var writeResult = await _challengeChar.WriteValueWithResultAsync(
                requestPayload.AsBuffer(), 
                GattWriteOption.WriteWithResponse);

            if (writeResult.Status != GattCommunicationStatus.Success)
            {
                _logger.LogWarning("Failed writing challenge request: {Status}", writeResult.Status);
                return false;
            }

            // Await response with timeout (Mitigates relay attack)
            var completedTask = await Task.WhenAny(_challengeResponseTcs.Task, Task.Delay(CryptoConstants.MaxRttMilliseconds));
            sw.Stop();

            if (completedTask != _challengeResponseTcs.Task)
            {
                _logger.LogWarning("Challenge response timed out or exceeded max RTT ({Elapsed} ms > {Max} ms). Relay attack suspected.",
                    sw.ElapsedMilliseconds, CryptoConstants.MaxRttMilliseconds);
                return false;
            }

            byte[] responseBytes = await _challengeResponseTcs.Task;
            if (!BinaryWireCodec.TryDecodeChallengeResp(responseBytes, out var resp) || resp == null)
            {
                _logger.LogWarning("Malformed Challenge Response packet.");
                return false;
            }

            if (resp.Status != StatusCode.Success)
            {
                _logger.LogWarning("Android returned error status: {Status}", resp.Status);
                return false;
            }

            // Validate Nonce freshness & consume atomically
            if (!_nonceManager.ValidateAndConsumeChallenge(noncePc, _pcId, out var roundTrip))
            {
                _logger.LogWarning("Nonce validation failed or expired. RTT: {Rtt} ms", roundTrip.TotalMilliseconds);
                return false;
            }

            // Construct Canonical Digest
            byte[] signableDigest = ECDsaValidator.BuildSignablePayload(
                _pcId, 
                noncePc, 
                req.TimestampUtcMs, 
                resp.NoncePhone);

            // Verify signature against all enrolled non-revoked public keys
            foreach (var dev in enrolledDevices)
            {
                byte[] pubKey = dev.GetPublicKeyBytes();
                if (ECDsaValidator.VerifySignature(pubKey, signableDigest, resp.Signature))
                {
                    _activeEnrolledDevice = dev;
                    dev.LastAuthenticatedUtc = DateTimeOffset.UtcNow;
                    dev.HardwareLevel = resp.HardwareLevel;
                    _deviceRegistry.SaveDevice(dev);

                    _stateMachine.NotifyAuthenticated();
                    _logger.LogInformation("Cryptographic verification PASSED for device '{Name}' ({HwLevel}). RTT: {Rtt} ms",
                        dev.DeviceName, resp.HardwareLevel, sw.ElapsedMilliseconds);
                    return true;
                }
            }

            _logger.LogWarning("ECDSA signature did not match any registered public key.");
            return false;
        }

        private void OnChallengeResponseReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data = args.CharacteristicValue.ToArray();
            _challengeResponseTcs?.TrySetResult(data);
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            _logger.LogInformation("BLE Connection Status Changed: {Status}", sender.ConnectionStatus);
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Disconnect();
            }
        }

        public void Disconnect()
        {
            _activeEnrolledDevice = null;
            if (_challengeChar != null)
            {
                _challengeChar.ValueChanged -= OnChallengeResponseReceived;
                _challengeChar = null;
            }
            _heartbeatChar = null;

            if (_connectedDevice != null)
            {
                _connectedDevice.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _connectedDevice.Dispose();
                _connectedDevice = null;
            }

            _stateMachine.NotifyDisconnected();
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopScanning();
            Disconnect();
            _authLock.Dispose();
            await Task.CompletedTask;
        }
    }
}
