using System;

namespace PhoneKey.Core.Crypto
{
    public static class CryptoConstants
    {
        public const string ProtocolDomainV1 = "PhoneKey-v1-Auth";
        public const int NonceSizeBytes = 32; // 256 bits
        public const int AesGcmKeySizeBytes = 32; // 256 bits
        public const int AesGcmNonceSizeBytes = 12; // 96 bits
        public const int AesGcmTagSizeBytes = 16; // 128 bits
        public const int MaxRttMilliseconds = 400; // Relay attack mitigation threshold
        public const int ChallengeExpirationSeconds = 5;
    }
}
