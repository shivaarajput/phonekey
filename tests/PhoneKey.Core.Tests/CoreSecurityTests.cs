using System;
using System.Security.Cryptography;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Protocol;
using PhoneKey.Core.Proximity;
using Xunit;

namespace PhoneKey.Core.Tests
{
    public class CoreSecurityTests
    {
        [Fact]
        public void NonceManager_PreventsReplay_WhenConsumedTwice()
        {
            var manager = new NonceManager(TimeSpan.FromSeconds(5));
            var pcId = Guid.NewGuid();

            byte[] nonce = manager.GenerateChallenge(pcId, out _);

            // First validation succeeds
            bool firstAttempt = manager.ValidateAndConsumeChallenge(nonce, pcId, out var rtt);
            Assert.True(firstAttempt);
            Assert.True(rtt >= TimeSpan.Zero);

            // Second validation MUST fail (Replay prevention)
            bool replayAttempt = manager.ValidateAndConsumeChallenge(nonce, pcId, out _);
            Assert.False(replayAttempt);
        }

        [Fact]
        public void ECDsaValidator_ValidatesSignatureCorrectly_AndRejectsTamperedPayload()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] pubKey = ecdsa.ExportSubjectPublicKeyInfo();

            var pcId = Guid.NewGuid();
            byte[] noncePc = new byte[32];
            byte[] noncePhone = new byte[32];
            RandomNumberGenerator.Fill(noncePc);
            RandomNumberGenerator.Fill(noncePhone);
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            byte[] signableDigest = ECDsaValidator.BuildSignablePayload(pcId, noncePc, timestamp, noncePhone);
            byte[] signature = ecdsa.SignHash(signableDigest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            // Valid signature check
            bool isValid = ECDsaValidator.VerifySignature(pubKey, signableDigest, signature);
            Assert.True(isValid);

            // Tampered signature check
            byte[] tamperedSignature = (byte[])signature.Clone();
            tamperedSignature[10] ^= 0xFF;
            bool isTamperedValid = ECDsaValidator.VerifySignature(pubKey, signableDigest, tamperedSignature);
            Assert.False(isTamperedValid);

            // Tampered digest check
            byte[] tamperedDigest = (byte[])signableDigest.Clone();
            tamperedDigest[0] ^= 0x01;
            bool isDigestTamperedValid = ECDsaValidator.VerifySignature(pubKey, tamperedDigest, signature);
            Assert.False(isDigestTamperedValid);
        }

        [Fact]
        public void DpapiVault_EncryptsAndDecryptsCredentials_AndFailsWithWrongKey()
        {
            byte[] sharedKey = new byte[32];
            RandomNumberGenerator.Fill(sharedKey);

            string domain = "WORKGROUP";
            string user = "TestUser";
            string password = "ComplexP@ssw0rd!123";

            byte[] sealedBlob = DpapiVault.SealCredential(user, domain, password, sharedKey);
            Assert.NotNull(sealedBlob);
            Assert.NotEmpty(sealedBlob);

            // Successful decryption
            bool success = DpapiVault.UnsealCredential(sealedBlob, sharedKey, out var decDomain, out var decUser, out var decPass);
            Assert.True(success);
            Assert.Equal(domain, decDomain);
            Assert.Equal(user, decUser);
            Assert.Equal(password, decPass);

            // Decryption with wrong key must fail
            byte[] wrongKey = new byte[32];
            RandomNumberGenerator.Fill(wrongKey);
            bool failWrongKey = DpapiVault.UnsealCredential(sealedBlob, wrongKey, out _, out _, out _);
            Assert.False(failWrongKey);
        }

        [Fact]
        public void BinaryWireCodec_RoundTripsChallengeReq_AndChallengeResp()
        {
            var pcId = Guid.NewGuid();
            byte[] nonce = new byte[32];
            RandomNumberGenerator.Fill(nonce);

            var req = new AuthChallengeReq
            {
                PcId = pcId,
                NoncePc = nonce,
                TimestampUtcMs = 1700000000000,
                PolicyFlags = 0x01
            };

            byte[] encodedReq = BinaryWireCodec.EncodeChallengeReq(req);
            bool reqDecoded = BinaryWireCodec.TryDecodeChallengeReq(encodedReq, out var decodedReq);

            Assert.True(reqDecoded);
            Assert.NotNull(decodedReq);
            Assert.Equal(req.PcId, decodedReq.PcId);
            Assert.Equal(req.NoncePc, decodedReq.NoncePc);
            Assert.Equal(req.TimestampUtcMs, decodedReq.TimestampUtcMs);
            Assert.Equal(req.PolicyFlags, decodedReq.PolicyFlags);
        }

        [Fact]
        public void ProximityStateMachine_TransitionsToGracePeriod_AndTriggersLock()
        {
            var config = new ProximityConfig
            {
                UnlockThresholdRssi = -65,
                LockThresholdRssi = -85,
                GracePeriodSeconds = 1,
                AutoLockEnabled = true
            };

            var sm = new ProximityStateMachine(config);
            bool lockCalled = false;
            sm.LockTriggered += () => { lockCalled = true; };

            sm.NotifyConnected();
            Assert.Equal(ProximityState.Authenticating, sm.CurrentState);

            sm.NotifyAuthenticated();
            Assert.Equal(ProximityState.AuthenticatedOutOfRange, sm.CurrentState);

            // Strong RSSI brings it into proximity
            sm.ProcessRssi(-60);
            Assert.Equal(ProximityState.InProximity, sm.CurrentState);

            // Weak RSSI enters grace period
            sm.ProcessRssi(-90);
            Assert.Equal(ProximityState.GracePeriod, sm.CurrentState);

            // Tick past grace period triggers lock
            System.Threading.Thread.Sleep(1100);
            sm.ProcessTick();

            Assert.Equal(ProximityState.LockTriggered, sm.CurrentState);
            Assert.True(lockCalled);
        }
    }
}
