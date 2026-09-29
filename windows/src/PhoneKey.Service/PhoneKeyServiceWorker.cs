using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Proximity;
using PhoneKey.Core.Storage;
using PhoneKey.Service.AutoLock;
using PhoneKey.Service.Ble;
using PhoneKey.Service.Ipc;

namespace PhoneKey.Service
{
    public sealed class PhoneKeyServiceWorker : BackgroundService
    {
        private readonly ILogger<PhoneKeyServiceWorker> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public PhoneKeyServiceWorker(ILogger<PhoneKeyServiceWorker> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PhoneKey Windows Service initializing in Session 0 (SYSTEM)...");

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string appDir = Path.Combine(programData, "PhoneKey");
            Directory.CreateDirectory(appDir);

            // Read or initialize persistent PC ID
            string pcIdPath = Path.Combine(appDir, "pcid.dat");
            Guid pcId;
            if (File.Exists(pcIdPath) && Guid.TryParse(File.ReadAllText(pcIdPath), out var existingGuid))
            {
                pcId = existingGuid;
            }
            else
            {
                pcId = Guid.NewGuid();
                File.WriteAllText(pcIdPath, pcId.ToString());
            }

            _logger.LogInformation("Workstation PhoneKey PC Identity GUID: {PcId}", pcId);

            var deviceRegistry = new DeviceRegistry();
            var nonceManager = new NonceManager();
            var proximityConfig = new ProximityConfig();
            var stateMachine = new ProximityStateMachine(proximityConfig);

            var lockLogger = _loggerFactory.CreateLogger<LockController>();
            var lockController = new LockController(lockLogger);

            stateMachine.LockTriggered += () =>
            {
                lockController.TriggerLock("Phone left proximity and grace period elapsed");
            };

            var bleLogger = _loggerFactory.CreateLogger<BleManager>();
            var bleManager = new BleManager(bleLogger, deviceRegistry, nonceManager, stateMachine, pcId);

            // Load sealed credential vault if present
            byte[]? sealedVaultBlob = null;
            string vaultPath = Path.Combine(appDir, "vault.dat");
            if (File.Exists(vaultPath))
            {
                try
                {
                    sealedVaultBlob = File.ReadAllBytes(vaultPath);
                    _logger.LogInformation("Loaded sealed credential vault ({Bytes} bytes).", sealedVaultBlob.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed loading credential vault file.");
                }
            }

            var pipeLogger = _loggerFactory.CreateLogger<NamedPipeServer>();
            await using var pipeServer = new NamedPipeServer(
                pipeLogger, 
                bleManager, 
                stateMachine, 
                deviceRegistry, 
                lockController, 
                sealedVaultBlob);

            pipeServer.Start();
            bleManager.StartScanning();

            _logger.LogInformation("PhoneKey Core Engine active. Monitoring proximity and BLE advertisements.");

            // Periodic Proximity State Engine Tick loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    stateMachine.ProcessTick();
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception in proximity ticker loop.");
                }
            }

            _logger.LogInformation("PhoneKey Windows Service shutting down...");
            bleManager.StopScanning();
            await bleManager.DisposeAsync();
        }
    }
}
