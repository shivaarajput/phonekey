package com.phonekey.app.crypto

import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID

object ChallengeProcessor {
    private const val MAGIC_HEADER: Short = 0x504B // 'PK'
    private const val PROTOCOL_VERSION: Byte = 0x01
    private const val OP_CHALLENGE_REQ: Byte = 0x10
    private const val OP_CHALLENGE_RESP: Byte = 0x11
    private const val DOMAIN_SEPARATOR = "PhoneKey-v1-Auth"

    data class ChallengeRequest(
        val pcId: UUID,
        val noncePc: ByteArray,
        val timestampUtcMs: Long,
        val policyFlags: Byte
    )

    data class ChallengeResult(
        val success: Boolean,
        val responsePayload: ByteArray,
        val pcId: UUID? = null
    )

    fun processChallenge(rawBytes: ByteArray, batteryLevel: Int = 100): ChallengeResult {
        if (rawBytes.size < 61) {
            return ChallengeResult(false, byteArrayOf())
        }

        val buffer = ByteBuffer.wrap(rawBytes).order(ByteOrder.BIG_ENDIAN)
        val magic = buffer.short
        val version = buffer.get()
        val opCode = buffer.get()

        if (magic != MAGIC_HEADER || version != PROTOCOL_VERSION || opCode != OP_CHALLENGE_REQ) {
            return ChallengeResult(false, byteArrayOf())
        }

        val pcIdMost = buffer.long
        val pcIdLeast = buffer.long
        val pcId = UUID(pcIdMost, pcIdLeast)

        val noncePc = ByteArray(32)
        buffer.get(noncePc)

        val timestampUtcMs = buffer.long
        val policyFlags = buffer.get()

        // Verify key exists for this PC ID, auto-generate if first time connecting
        val pcIdStr = pcId.toString()
        if (!KeyStoreManager.hasKey(pcIdStr)) {
            try {
                KeyStoreManager.generateKeyPair(pcIdStr)
            } catch (e: Exception) {
                val errResp = buildResponsePacket(
                    statusCode = 0x03, // Hardware / Key Error
                    noncePhone = ByteArray(32),
                    hwLevel = 0x01,
                    batteryLevel = batteryLevel.toByte(),
                    signature = byteArrayOf()
                )
                return ChallengeResult(false, errResp, pcId)
            }
        }

        // Generate Phone Entropy
        val noncePhone = ByteArray(32)
        SecureRandom().nextBytes(noncePhone)

        // Construct Canonical Signable Buffer
        val signableDigest = buildCanonicalDigest(pcId, noncePc, timestampUtcMs, noncePhone)

        // Sign inside Android Keystore TEE / StrongBox
        val signature = try {
            KeyStoreManager.signPayload(pcIdStr, signableDigest)
        } catch (e: Exception) {
            val errResp = buildResponsePacket(0x03, noncePhone, 0x01, batteryLevel.toByte(), byteArrayOf())
            return ChallengeResult(false, errResp, pcId)
        }

        val responsePayload = buildResponsePacket(
            statusCode = 0x00, // Success
            noncePhone = noncePhone,
            hwLevel = 0x02, // TEE
            batteryLevel = batteryLevel.toByte(),
            signature = signature
        )

        return ChallengeResult(true, responsePayload, pcId)
    }

    private fun buildCanonicalDigest(
        pcId: UUID,
        noncePc: ByteArray,
        timestampUtcMs: Long,
        noncePhone: ByteArray
    ): ByteArray {
        val baos = ByteArrayOutputStream()
        val dos = DataOutputStream(baos)

        dos.write(DOMAIN_SEPARATOR.toByteArray(Charsets.UTF_8))
        dos.writeLong(pcId.mostSignificantBits)
        dos.writeLong(pcId.leastSignificantBits)
        dos.write(noncePc)
        dos.writeLong(timestampUtcMs)
        dos.write(noncePhone)
        dos.flush()

        val md = MessageDigest.getInstance("SHA-256")
        return md.digest(baos.toByteArray())
    }

    private fun buildResponsePacket(
        statusCode: Byte,
        noncePhone: ByteArray,
        hwLevel: Byte,
        batteryLevel: Byte,
        signature: ByteArray
    ): ByteArray {
        val totalSize = 2 + 1 + 1 + 1 + 32 + 1 + 1 + 2 + signature.size
        val buffer = ByteBuffer.allocate(totalSize).order(ByteOrder.BIG_ENDIAN)

        buffer.putShort(MAGIC_HEADER)
        buffer.put(PROTOCOL_VERSION)
        buffer.put(OP_CHALLENGE_RESP)
        buffer.put(statusCode)
        buffer.put(noncePhone)
        buffer.put(hwLevel)
        buffer.put(batteryLevel)
        buffer.putShort(signature.size.toShort())
        buffer.put(signature)

        return buffer.array()
    }
}
