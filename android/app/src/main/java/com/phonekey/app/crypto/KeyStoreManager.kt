package com.phonekey.app.crypto

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.PublicKey
import java.security.Signature
import java.security.spec.ECGenParameterSpec

enum class SecurityLevel {
    SOFTWARE,
    TEE,
    STRONGBOX
}

object KeyStoreManager {
    private const val ANDROID_KEYSTORE = "AndroidKeyStore"
    private const val KEY_ALIAS_PREFIX = "PhoneKey_PC_"

    fun getAliasForPc(pcId: String): String = "$KEY_ALIAS_PREFIX$pcId"

    /**
     * Generates a hardware-backed NIST P-256 EC KeyPair inside the Android Keystore.
     * Attempts StrongBox backing first; falls back to TEE.
     */
    fun generateKeyPair(pcId: String, requireUserAuth: Boolean = false): KeyPairResult {
        val alias = getAliasForPc(pcId)
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }

        if (keyStore.containsAlias(alias)) {
            keyStore.deleteEntry(alias)
        }

        // Try StrongBox first if Android 9+ (API 28)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            try {
                val strongBoxGenerator = KeyPairGenerator.getInstance(
                    KeyProperties.KEY_ALGORITHM_EC, 
                    ANDROID_KEYSTORE
                )
                val spec = buildKeyGenSpec(alias, requireUserAuth, useStrongBox = true)
                strongBoxGenerator.initialize(spec)
                val keyPair = strongBoxGenerator.generateKeyPair()
                return KeyPairResult(keyPair.public, SecurityLevel.STRONGBOX)
            } catch (e: Exception) {
                // StrongBox not available on this chipset; fall through to standard TEE
            }
        }

        // Standard TEE Key Generation
        val generator = KeyPairGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_EC, 
            ANDROID_KEYSTORE
        )
        val spec = buildKeyGenSpec(alias, requireUserAuth, useStrongBox = false)
        generator.initialize(spec)
        val keyPair = generator.generateKeyPair()

        val securityLevel = determineSecurityLevel(keyPair.private)
        return KeyPairResult(keyPair.public, securityLevel)
    }

    private fun buildKeyGenSpec(alias: String, requireUserAuth: Boolean, useStrongBox: Boolean): KeyGenParameterSpec {
        val builder = KeyGenParameterSpec.Builder(
            alias,
            KeyProperties.PURPOSE_SIGN
        )
            .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setUserAuthenticationRequired(requireUserAuth)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P && useStrongBox) {
            builder.setIsStrongBoxBacked(true)
        }

        return builder.build()
    }

    /**
     * Signs the given payload with the private key inside the TEE/StrongBox.
     * The private key never leaves the hardware security module.
     */
    fun signPayload(pcId: String, payload: ByteArray): ByteArray {
        val alias = getAliasForPc(pcId)
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        val privateKey = keyStore.getKey(alias, null) as? PrivateKey
            ?: throw IllegalStateException("Key for PC $pcId not found in Keystore.")

        val signature = Signature.getInstance("SHA256withECDSA")
        signature.initSign(privateKey)
        signature.update(payload)
        return signature.sign()
    }

    fun hasKey(pcId: String): Boolean {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        return keyStore.containsAlias(getAliasForPc(pcId))
    }

    fun deleteKey(pcId: String) {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        val alias = getAliasForPc(pcId)
        if (keyStore.containsAlias(alias)) {
            keyStore.deleteEntry(alias)
        }
    }

    private fun determineSecurityLevel(privateKey: PrivateKey): SecurityLevel {
        return try {
            val factory = KeyFactory.getInstance(privateKey.algorithm, ANDROID_KEYSTORE)
            val keyInfo = factory.getKeySpec(privateKey, KeyInfo::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                when (keyInfo.securityLevel) {
                    KeyProperties.SECURITY_LEVEL_STRONGBOX -> SecurityLevel.STRONGBOX
                    KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT -> SecurityLevel.TEE
                    else -> SecurityLevel.SOFTWARE
                }
            } else {
                if (keyInfo.isInsideSecureHardware) SecurityLevel.TEE else SecurityLevel.SOFTWARE
            }
        } catch (e: Exception) {
            SecurityLevel.TEE
        }
    }

    data class KeyPairResult(
        val publicKey: PublicKey,
        val securityLevel: SecurityLevel
    )
}
