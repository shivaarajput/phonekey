using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PhoneKey.Core.Crypto
{
    public sealed class NonceManager
    {
        private sealed record IssuedChallenge(
            byte[] NonceBytes,
            DateTimeOffset IssuedAtUtc,
            Guid PcId
        );

        private readonly ConcurrentDictionary<string, IssuedChallenge> _activeChallenges = new();
        private readonly TimeSpan _expirationWindow;

        public NonceManager(TimeSpan? expirationWindow = null)
        {
            _expirationWindow = expirationWindow ?? TimeSpan.FromSeconds(CryptoConstants.ChallengeExpirationSeconds);
        }

        /// <summary>
        /// Generates a fresh 256-bit cryptographically secure challenge nonce bound to the given PC ID.
        /// </summary>
        public byte[] GenerateChallenge(Guid pcId, out DateTimeOffset issuedAtUtc)
        {
            CleanupExpired();

            byte[] nonce = new byte[CryptoConstants.NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);

            issuedAtUtc = DateTimeOffset.UtcNow;
            string key = Convert.ToBase64String(nonce);

            _activeChallenges[key] = new IssuedChallenge(nonce, issuedAtUtc, pcId);
            return nonce;
        }

        /// <summary>
        /// Validates and atomically consumes a challenge nonce. Returns false if replayed, missing, or expired.
        /// </summary>
        public bool ValidateAndConsumeChallenge(byte[] nonce, Guid expectedPcId, out TimeSpan elapsed)
        {
            elapsed = TimeSpan.MaxValue;
            if (nonce == null || nonce.Length != CryptoConstants.NonceSizeBytes)
            {
                return false;
            }

            string key = Convert.ToBase64String(nonce);

            // Atomically remove so it cannot ever be reused (Anti-Replay)
            if (!_activeChallenges.TryRemove(key, out var challenge))
            {
                return false;
            }

            if (challenge.PcId != expectedPcId)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            elapsed = now - challenge.IssuedAtUtc;

            if (elapsed < TimeSpan.Zero || elapsed > _expirationWindow)
            {
                return false;
            }

            return true;
        }

        private void CleanupExpired()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _activeChallenges)
            {
                if (now - kvp.Value.IssuedAtUtc > _expirationWindow)
                {
                    _activeChallenges.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
