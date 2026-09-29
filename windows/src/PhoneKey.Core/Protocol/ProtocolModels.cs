using System;

namespace PhoneKey.Core.Protocol
{
    public enum OpCode : byte
    {
        AuthChallengeReq = 0x10,
        AuthChallengeResp = 0x11,
        Heartbeat = 0x12,
        EnrollReq = 0x20,
        EnrollResp = 0x21
    }

    public enum StatusCode : byte
    {
        Success = 0x00,
        DeviceLocked = 0x01,
        UserCancelled = 0x02,
        HardwareError = 0x03,
        InvalidChallenge = 0x04
    }

    public enum HardwareSecurityLevel : byte
    {
        Software = 0x01,
        Tee = 0x02,
        StrongBox = 0x03
    }

    public sealed class AuthChallengeReq
    {
        public const ushort MagicHeader = 0x504B; // 'PK'
        public const byte ProtocolVersion = 0x01;

        public Guid PcId { get; set; }
        public byte[] NoncePc { get; set; } = Array.Empty<byte>();
        public long TimestampUtcMs { get; set; }
        public byte PolicyFlags { get; set; } // Bit 0: Require Biometrics, Bit 1: Require Unlocked Screen
    }

    public sealed class AuthChallengeResp
    {
        public const ushort MagicHeader = 0x504B;
        public const byte ProtocolVersion = 0x01;

        public StatusCode Status { get; set; }
        public byte[] NoncePhone { get; set; } = Array.Empty<byte>();
        public HardwareSecurityLevel HardwareLevel { get; set; }
        public byte BatteryLevel { get; set; }
        public byte[] Signature { get; set; } = Array.Empty<byte>();
    }

    public sealed class HeartbeatPacket
    {
        public uint Sequence { get; set; }
        public byte StatusFlags { get; set; } // Bit 0: Screen On, Bit 1: User Present, Bit 2: Charging
        public byte BatteryPercent { get; set; }
    }

    public sealed class EnrollmentQrPayload
    {
        public string PcId { get; set; } = string.Empty;
        public string PcHostname { get; set; } = string.Empty;
        public string EphemeralPublicKeyBase64 { get; set; } = string.Empty;
        public string PairingSaltBase64 { get; set; } = string.Empty;
        public long CreatedAtUtc { get; set; }
    }
}
