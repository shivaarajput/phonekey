using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Ipc;
using PhoneKey.Core.Proximity;
using PhoneKey.Core.Storage;
using PhoneKey.Service.AutoLock;
using PhoneKey.Service.Ble;

namespace PhoneKey.Service.Ipc
{
    public sealed class NamedPipeServer : IAsyncDisposable
    {
        private readonly ILogger<NamedPipeServer> _logger;
        private readonly BleManager _bleManager;
        private readonly ProximityStateMachine _stateMachine;
        private readonly DeviceRegistry _deviceRegistry;
        private readonly LockController _lockController;
        private readonly byte[]? _cachedSealedCredentialBlob; // Stored DPAPI blob
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private bool _isDisposed;

        public NamedPipeServer(
            ILogger<NamedPipeServer> logger,
            BleManager bleManager,
            ProximityStateMachine stateMachine,
            DeviceRegistry deviceRegistry,
            LockController lockController,
            byte[]? sealedCredentialBlob)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bleManager = bleManager ?? throw new ArgumentNullException(nameof(bleManager));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
            _lockController = lockController ?? throw new ArgumentNullException(nameof(lockController));
            _cachedSealedCredentialBlob = sealedCredentialBlob;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _logger.LogInformation("PhoneKey Named Pipe Server listening on {PipeName}", IpcConstants.FullPipePath);
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipeSecurity = CreatePipeSecurity();

                    using var pipe = NamedPipeServerStreamAcl.Create(
                        IpcConstants.PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 4096,
                        outBufferSize: 4096,
                        pipeSecurity);

                    await pipe.WaitForConnectionAsync(ct);
                    await HandleClientAsync(pipe, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Named Pipe listener loop.");
                    await Task.Delay(500, ct);
                }
            }
        }

        private PipeSecurity CreatePipeSecurity()
        {
            var ps = new PipeSecurity();

            // Grant SYSTEM Full Control
            var sidSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            ps.AddAccessRule(new PipeAccessRule(sidSystem, PipeAccessRights.FullControl, AccessControlType.Allow));

            // Grant Authenticated Users Read/Write (for UI queries & pairing wizard)
            var sidUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            ps.AddAccessRule(new PipeAccessRule(sidUsers, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return ps;
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) return;

                var req = JsonSerializer.Deserialize<IpcRequest>(line);
                if (req == null) return;

                switch (req.Command)
                {
                    case IpcCommandType.GetStatus:
                        var status = BuildStatusResponse();
                        string statusJson = JsonSerializer.Serialize(status);
                        await writer.WriteLineAsync(statusJson);
                        break;

                    case IpcCommandType.ManualLock:
                        _lockController.TriggerLock("Manual lock request from UI");
                        await writer.WriteLineAsync("{\"success\":true}");
                        break;

                    case IpcCommandType.GetSerializedLogonBuffer:
                        await HandleLogonBufferRequestAsync(pipe, writer);
                        break;

                    case IpcCommandType.RevokeDevice:
                        if (!string.IsNullOrEmpty(req.Payload) && Guid.TryParse(req.Payload, out var devId))
                        {
                            bool revoked = _deviceRegistry.RevokeDevice(devId);
                            _bleManager.Disconnect();
                            await writer.WriteLineAsync(revoked ? "{\"success\":true}" : "{\"success\":false}");
                        }
                        break;

                    default:
                        await writer.WriteLineAsync("{\"error\":\"Unknown command\"}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling named pipe client request.");
            }
        }

        private async Task HandleLogonBufferRequestAsync(NamedPipeServerStream pipe, StreamWriter writer)
        {
            // Security verification: Only LogonUI / SYSTEM is allowed to extract logon credentials
            bool isCallerSystem = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                isCallerSystem = identity.IsSystem;
            });

            if (!isCallerSystem)
            {
                _logger.LogWarning("Access Denied: Non-SYSTEM client attempted to request serialized logon buffer.");
                var deniedResp = new IpcLogonBufferResponse { Success = false, ErrorMessage = "Access Denied: SYSTEM identity required." };
                await writer.WriteLineAsync(JsonSerializer.Serialize(deniedResp));
                return;
            }

            // Verify state: Must be in proximity and authenticated
            if (_stateMachine.CurrentState != ProximityState.InProximity || _bleManager.ActiveDevice == null)
            {
                _logger.LogWarning("Logon buffer requested while device is not in verified proximity.");
                var notReadyResp = new IpcLogonBufferResponse { Success = false, ErrorMessage = "Phone is not in verified proximity." };
                await writer.WriteLineAsync(JsonSerializer.Serialize(notReadyResp));
                return;
            }

            if (_cachedSealedCredentialBlob == null)
            {
                var noVaultResp = new IpcLogonBufferResponse { Success = false, ErrorMessage = "No stored credential vault configured." };
                await writer.WriteLineAsync(JsonSerializer.Serialize(noVaultResp));
                return;
            }

            byte[] phoneKey = _bleManager.ActiveDevice.GetSharedSecretBytes();
            if (DpapiVault.UnsealCredential(_cachedSealedCredentialBlob, phoneKey, out var domain, out var username, out var password))
            {
                var successResp = new IpcLogonBufferResponse
                {
                    Success = true,
                    Domain = domain ?? string.Empty,
                    Username = username ?? string.Empty,
                    Password = password ?? string.Empty
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(successResp));
                _logger.LogInformation("Supplied unsealed logon buffer to LogonUI via secure pipe.");
            }
            else
            {
                _logger.LogError("Failed to decrypt DPAPI credential stash with phone-bound secret.");
                var failResp = new IpcLogonBufferResponse { Success = false, ErrorMessage = "Decryption failed." };
                await writer.WriteLineAsync(JsonSerializer.Serialize(failResp));
            }
        }

        private IpcStatusResponse BuildStatusResponse()
        {
            var activeDev = _bleManager.ActiveDevice;
            return new IpcStatusResponse
            {
                IsServiceRunning = true,
                IsPhoneConnected = activeDev != null,
                IsAuthenticated = activeDev != null && _stateMachine.CurrentState >= ProximityState.AuthenticatedOutOfRange,
                IsInProximity = _stateMachine.CurrentState == ProximityState.InProximity,
                CurrentProximityState = _stateMachine.CurrentState,
                FilteredRssi = Math.Round(_stateMachine.FilteredRssi, 1),
                GracePeriodRemainingSeconds = (int)_stateMachine.GracePeriodRemaining.TotalSeconds,
                ConnectedPhoneName = activeDev?.DeviceName ?? string.Empty,
                HardwareSecurityLevel = activeDev?.HardwareLevel.ToString() ?? "None",
                BatteryPercent = 85,
                IsLogonBufferAvailable = _cachedSealedCredentialBlob != null
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts?.Cancel();
            if (_listenerTask != null)
            {
                try { await _listenerTask; } catch { }
            }
            _cts?.Dispose();
        }
    }
}
