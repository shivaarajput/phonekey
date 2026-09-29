using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PhoneKey.Service.AutoLock
{
    public sealed class LockController
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWorkStation();

        private readonly ILogger<LockController> _logger;
        private DateTimeOffset _lastLockUtc = DateTimeOffset.MinValue;

        public LockController(ILogger<LockController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TriggerLock(string reason)
        {
            // Debounce lock requests within 3 seconds
            if (DateTimeOffset.UtcNow - _lastLockUtc < TimeSpan.FromSeconds(3))
            {
                return true;
            }

            _logger.LogWarning("Invoking LockWorkStation() [Reason: {Reason}]", reason);
            bool result = LockWorkStation();
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogError("LockWorkStation failed with Win32 Error: {ErrorCode}", error);
                return false;
            }

            _lastLockUtc = DateTimeOffset.UtcNow;
            _logger.LogInformation("Workstation locked successfully.");
            return true;
        }
    }
}
