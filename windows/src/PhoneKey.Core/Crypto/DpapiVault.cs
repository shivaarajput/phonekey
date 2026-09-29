using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhoneKey.Core.Crypto
{
    public sealed class DpapiVault
    {
        private static readonly byte[] EntropySalt = Encoding.UTF8.GetBytes("PhoneKey-LsaCredential-Entropy-v1");

        /// <summary>
        /// Encrypts the Windows logon password using a dual-layer scheme:
        /// Layer 1: AES-256-GCM sealed with a 256-bit phone-bound secret.
        /// Layer 2: Windows DPAPI (LocalMachine scope, restricted to SYSTEM).
        /// </summary>
        public static byte[] SealCredential(string username, string domain, string password, byte[] phoneSharedKey)
        {
            if (string.IsNullOrEmpty(username)) throw new ArgumentNullException(nameof(username));
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            if (phoneSharedKey == null || phoneSharedKey.Length != CryptoConstants.AesGcmKeySizeBytes)
                throw new ArgumentException($"Phone shared key must be {CryptoConstants.AesGcmKeySizeBytes} bytes", nameof(phoneSharedKey));

            byte[] plaintextPayload = Encoding.UTF8.GetBytes($"{domain}\0{username}\0{password}");
            byte[] nonce = new byte[CryptoConstants.AesGcmNonceSizeBytes];
            byte[] tag = new byte[CryptoConstants.AesGcmTagSizeBytes];
            byte[] ciphertext = new byte[plaintextPayload.Length];

            try
            {
                RandomNumberGenerator.Fill(nonce);
                using (var aesGcm = new AesGcm(phoneSharedKey, CryptoConstants.AesGcmTagSizeBytes))
                {
                    aesGcm.Encrypt(nonce, plaintextPayload, ciphertext, tag);
                }

                // Pack: Nonce (12) + Tag (16) + Ciphertext
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(nonce);
                writer.Write(tag);
                writer.Write(ciphertext);
                writer.Flush();
                byte[] packedGcm = ms.ToArray();

                // Layer 2: DPAPI Protect
                try
                {
                    return ProtectedData.Protect(packedGcm, EntropySalt, DataProtectionScope.LocalMachine);
                }
                catch (CryptographicException)
                {
                    return ProtectedData.Protect(packedGcm, EntropySalt, DataProtectionScope.CurrentUser);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextPayload);
            }
        }

        /// <summary>
        /// Decrypts the Windows credential payload using the phone-bound secret and DPAPI.
        /// </summary>
        public static bool UnsealCredential(
            byte[] sealedBlob, 
            byte[] phoneSharedKey, 
            out string? domain, 
            out string? username, 
            out string? password)
        {
            domain = null;
            username = null;
            password = null;

            if (sealedBlob == null || sealedBlob.Length == 0) return false;
            if (phoneSharedKey == null || phoneSharedKey.Length != CryptoConstants.AesGcmKeySizeBytes) return false;

            byte[]? decryptedGcm = null;
            byte[]? decryptedPlaintext = null;

            try
            {
                // Layer 1: DPAPI Unprotect
                try
                {
                    decryptedGcm = ProtectedData.Unprotect(sealedBlob, EntropySalt, DataProtectionScope.LocalMachine);
                }
                catch (CryptographicException)
                {
                    decryptedGcm = ProtectedData.Unprotect(sealedBlob, EntropySalt, DataProtectionScope.CurrentUser);
                }
                if (decryptedGcm.Length < CryptoConstants.AesGcmNonceSizeBytes + CryptoConstants.AesGcmTagSizeBytes)
                    return false;

                byte[] nonce = decryptedGcm[0..CryptoConstants.AesGcmNonceSizeBytes];
                byte[] tag = decryptedGcm[CryptoConstants.AesGcmNonceSizeBytes..(CryptoConstants.AesGcmNonceSizeBytes + CryptoConstants.AesGcmTagSizeBytes)];
                byte[] ciphertext = decryptedGcm[(CryptoConstants.AesGcmNonceSizeBytes + CryptoConstants.AesGcmTagSizeBytes)..];

                decryptedPlaintext = new byte[ciphertext.Length];
                using (var aesGcm = new AesGcm(phoneSharedKey, CryptoConstants.AesGcmTagSizeBytes))
                {
                    aesGcm.Decrypt(nonce, ciphertext, tag, decryptedPlaintext);
                }

                string raw = Encoding.UTF8.GetString(decryptedPlaintext);
                string[] parts = raw.Split('\0', 3);
                if (parts.Length != 3) return false;

                domain = parts[0];
                username = parts[1];
                password = parts[2];
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (decryptedGcm != null) CryptographicOperations.ZeroMemory(decryptedGcm);
                if (decryptedPlaintext != null) CryptographicOperations.ZeroMemory(decryptedPlaintext);
            }
        }
    }
}
