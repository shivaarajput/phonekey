using System;
using PhoneKey.Core.Proximity;

namespace PhoneKey.Core.Ipc
{
    public static class IpcConstants
    {
        public const string PipeName = "PhoneKeyAuthPipe";
        public const string FullPipePath = @"\\.\pipe\" + PipeName;
    }

    public enum IpcCommandType
    {
        GetStatus = 1,
        GetSerializedLogonBuffer = 2,
        ManualLock = 3,
        StartEnrollment = 4,
        RevokeDevice = 5,
        UpdateConfig = 6,
        QuickPair = 7
    }

    public sealed class IpcRequest
    {
        public IpcCommandType Command { get; set; }
        public string? Payload { get; set; }
    }

    public sealed class QuickPairPayload
    {
        public ulong BluetoothAddress { get; set; }
        public string Password { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
    }

    public sealed class IpcStatusResponse
    {
        public bool IsServiceRunning { get; set; } = true;
        public bool IsPhoneConnected { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsInProximity { get; set; }
        public ProximityState CurrentProximityState { get; set; }
        public double FilteredRssi { get; set; }
        public int GracePeriodRemainingSeconds { get; set; }
        public string ConnectedPhoneName { get; set; } = string.Empty;
        public string HardwareSecurityLevel { get; set; } = string.Empty;
        public byte BatteryPercent { get; set; }
        public bool IsLogonBufferAvailable { get; set; }
        public bool HasDiscoveredPhone { get; set; }
        public ulong DiscoveredPhoneAddress { get; set; }
        public string DiscoveredPhoneName { get; set; } = string.Empty;
        public short DiscoveredPhoneRssi { get; set; }
    }

    public sealed class IpcLogonBufferResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
