using System;
using System.Buffers.Binary;
using System.IO;

namespace PhoneKey.Core.Protocol
{
    public static class BinaryWireCodec
    {
        public static byte[] EncodeChallengeReq(AuthChallengeReq req)
        {
            if (req.NoncePc == null || req.NoncePc.Length != 32)
                throw new ArgumentException("NoncePc must be 32 bytes");

            // Fixed size: Magic(2) + Version(1) + OpCode(1) + PcId(16) + NoncePc(32) + Timestamp(8) + Policy(1) = 61 bytes
            byte[] buffer = new byte[61];
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), AuthChallengeReq.MagicHeader);
            buffer[2] = AuthChallengeReq.ProtocolVersion;
            buffer[3] = (byte)OpCode.AuthChallengeReq;

            req.PcId.TryWriteBytes(buffer.AsSpan(4, 16));
            req.NoncePc.CopyTo(buffer.AsSpan(20, 32));
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(52, 8), req.TimestampUtcMs);
            buffer[60] = req.PolicyFlags;

            return buffer;
        }

        public static bool TryDecodeChallengeReq(ReadOnlySpan<byte> data, out AuthChallengeReq? req)
        {
            req = null;
            if (data.Length < 61) return false;

            ushort magic = BinaryPrimitives.ReadUInt16BigEndian(data[0..2]);
            if (magic != AuthChallengeReq.MagicHeader) return false;

            byte version = data[2];
            if (version != AuthChallengeReq.ProtocolVersion) return false;

            byte opCode = data[3];
            if (opCode != (byte)OpCode.AuthChallengeReq) return false;

            var pcId = new Guid(data.Slice(4, 16));
            byte[] nonce = data.Slice(20, 32).ToArray();
            long timestamp = BinaryPrimitives.ReadInt64BigEndian(data.Slice(52, 8));
            byte flags = data[60];

            req = new AuthChallengeReq
            {
                PcId = pcId,
                NoncePc = nonce,
                TimestampUtcMs = timestamp,
                PolicyFlags = flags
            };
            return true;
        }

        public static byte[] EncodeChallengeResp(AuthChallengeResp resp)
        {
            if (resp.NoncePhone == null || resp.NoncePhone.Length != 32)
                throw new ArgumentException("NoncePhone must be 32 bytes");
            if (resp.Signature == null)
                throw new ArgumentNullException(nameof(resp.Signature));

            int totalSize = 2 + 1 + 1 + 1 + 32 + 1 + 1 + 2 + resp.Signature.Length;
            byte[] buffer = new byte[totalSize];

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), AuthChallengeResp.MagicHeader);
            buffer[2] = AuthChallengeResp.ProtocolVersion;
            buffer[3] = (byte)OpCode.AuthChallengeResp;
            buffer[4] = (byte)resp.Status;
            resp.NoncePhone.CopyTo(buffer.AsSpan(5, 32));
            buffer[37] = (byte)resp.HardwareLevel;
            buffer[38] = resp.BatteryLevel;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(39, 2), (ushort)resp.Signature.Length);
            resp.Signature.CopyTo(buffer.AsSpan(41, resp.Signature.Length));

            return buffer;
        }

        public static bool TryDecodeChallengeResp(ReadOnlySpan<byte> data, out AuthChallengeResp? resp)
        {
            resp = null;
            if (data.Length < 41) return false;

            ushort magic = BinaryPrimitives.ReadUInt16BigEndian(data[0..2]);
            if (magic != AuthChallengeResp.MagicHeader) return false;

            byte version = data[2];
            if (version != AuthChallengeResp.ProtocolVersion) return false;

            byte opCode = data[3];
            if (opCode != (byte)OpCode.AuthChallengeResp) return false;

            var status = (StatusCode)data[4];
            byte[] noncePhone = data.Slice(5, 32).ToArray();
            var hwLevel = (HardwareSecurityLevel)data[37];
            byte battery = data[38];
            ushort sigLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(39, 2));

            if (data.Length < 41 + sigLen) return false;
            byte[] signature = data.Slice(41, sigLen).ToArray();

            resp = new AuthChallengeResp
            {
                Status = status,
                NoncePhone = noncePhone,
                HardwareLevel = hwLevel,
                BatteryLevel = battery,
                Signature = signature
            };
            return true;
        }

        public static byte[] EncodeHeartbeat(HeartbeatPacket hb)
        {
            byte[] buffer = new byte[6];
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), hb.Sequence);
            buffer[4] = hb.StatusFlags;
            buffer[5] = hb.BatteryPercent;
            return buffer;
        }

        public static bool TryDecodeHeartbeat(ReadOnlySpan<byte> data, out HeartbeatPacket? hb)
        {
            hb = null;
            if (data.Length < 6) return false;

            uint seq = BinaryPrimitives.ReadUInt32BigEndian(data[0..4]);
            byte flags = data[4];
            byte bat = data[5];

            hb = new HeartbeatPacket
            {
                Sequence = seq,
                StatusFlags = flags,
                BatteryPercent = bat
            };
            return true;
        }
    }
}
