using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhoneKey.Core.Crypto
{
    public static class ECDsaValidator
    {
        /// <summary>
        /// Constructs the exact canonical payload that the Android device signs.
        /// Payload = SHA256("PhoneKey-v1-Auth" || PcId || NoncePc || TimestampUtcMs || NoncePhone)
        /// </summary>
        public static byte[] BuildSignablePayload(
            Guid pcId,
            byte[] noncePc,
            long timestampUtcMs,
            byte[] noncePhone)
        {
            if (noncePc == null || noncePc.Length != CryptoConstants.NonceSizeBytes)
                throw new ArgumentException($"NoncePc must be {CryptoConstants.NonceSizeBytes} bytes", nameof(noncePc));
            if (noncePhone == null || noncePhone.Length != CryptoConstants.NonceSizeBytes)
                throw new ArgumentException($"NoncePhone must be {CryptoConstants.NonceSizeBytes} bytes", nameof(noncePhone));

            // Domain (16 bytes) + PcId (16 bytes) + NoncePc (32 bytes) + Timestamp (8 bytes) + NoncePhone (32 bytes) = 104 bytes
            byte[] buffer = new byte[16 + 16 + 32 + 8 + 32];
            
            // 1. Domain Separator
            Encoding.UTF8.GetBytes(CryptoConstants.ProtocolDomainV1).CopyTo(buffer.AsSpan(0, 16));
            
            // 2. PcId in RFC 4122 Network Big-Endian
            pcId.TryWriteBytes(buffer.AsSpan(16, 16), bigEndian: true, out _);
            
            // 3. NoncePc
            noncePc.CopyTo(buffer.AsSpan(32, 32));
            
            // 4. Timestamp in Big-Endian (matching Java DataOutputStream.writeLong)
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(64, 8), timestampUtcMs);
            
            // 5. NoncePhone
            noncePhone.CopyTo(buffer.AsSpan(72, 32));

            return SHA256.HashData(buffer);
        }

        /// <summary>
        /// Verifies an ECDSA signature over the canonical signable digest against the given NIST P-256 public key.
        /// Automatically handles both IEEE P1363 (64 bytes r||s) and ASN.1 DER signature formats.
        /// </summary>
        public static bool VerifySignature(
            byte[] publicKeyDerOrX962,
            byte[] signableDigest,
            byte[] signature)
        {
            if (publicKeyDerOrX962 == null || publicKeyDerOrX962.Length == 0) return false;
            if (signableDigest == null || signableDigest.Length != 32) return false;
            if (signature == null || signature.Length == 0) return false;

            try
            {
                using var ecdsa = ECDsa.Create();
                
                // Try importing SubjectPublicKeyInfo (X.509 DER) or raw uncompressed EC point (0x04 || X || Y)
                if (publicKeyDerOrX962.Length == 65 && publicKeyDerOrX962[0] == 0x04)
                {
                    var ecParams = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint
                        {
                            X = publicKeyDerOrX962[1..33],
                            Y = publicKeyDerOrX962[33..65]
                        }
                    };
                    ecdsa.ImportParameters(ecParams);
                }
                else
                {
                    ecdsa.ImportSubjectPublicKeyInfo(publicKeyDerOrX962, out _);
                }

                // If signature is 64 bytes, it's IEEE P1363 (r || s). Otherwise, ASN.1 DER.
                var format = (signature.Length == 64) 
                    ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation 
                    : DSASignatureFormat.Rfc3279DerSequence;

                return ecdsa.VerifyHash(signableDigest, signature, format);
            }
            catch
            {
                return false;
            }
        }
    }
}
