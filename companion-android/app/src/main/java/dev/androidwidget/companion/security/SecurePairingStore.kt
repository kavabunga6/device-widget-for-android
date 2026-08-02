package dev.androidwidget.companion.security

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import dev.androidwidget.companion.protocol.PairingConfig
import java.security.KeyStore
import java.util.Base64
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class SecurePairingStore(context: Context) {
    private val preferences = context.getSharedPreferences("companion_pairing", Context.MODE_PRIVATE)

    val installationId: String
        get() = preferences.getString("installation_id", null) ?: UUID.randomUUID().toString().also {
            preferences.edit().putString("installation_id", it).apply()
        }

    fun saveAuthenticated(config: PairingConfig, token: String) {
        val encrypted = encrypt(token)
        preferences.edit()
            .putString("host", config.host)
            .putInt("port", config.port)
            .putString("fingerprint", config.fingerprint)
            .putString("token", encrypted)
            .apply()
    }

    fun loadAuthenticated(): PairingConfig? {
        val host = preferences.getString("host", null) ?: return null
        val fingerprint = preferences.getString("fingerprint", null) ?: return null
        val encrypted = preferences.getString("token", null) ?: return null
        return runCatching {
            PairingConfig(host, preferences.getInt("port", 39817), fingerprint, authToken = decrypt(encrypted))
        }.getOrNull()
    }

    fun clear() = preferences.edit().remove("host").remove("port").remove("fingerprint").remove("token").apply()

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        val payload = cipher.iv + cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        return Base64.getEncoder().encodeToString(payload)
    }

    private fun decrypt(value: String): String {
        val payload = Base64.getDecoder().decode(value)
        require(payload.size > IV_LENGTH)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), GCMParameterSpec(128, payload.copyOfRange(0, IV_LENGTH)))
        return cipher.doFinal(payload.copyOfRange(IV_LENGTH, payload.size)).toString(Charsets.UTF_8)
    }

    private fun getOrCreateKey(): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(
                KeyGenParameterSpec.Builder(
                    KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build(),
            )
            generateKey()
        }
    }

    private companion object {
        const val KEY_ALIAS = "android_widget_companion_auth"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_LENGTH = 12
    }
}
