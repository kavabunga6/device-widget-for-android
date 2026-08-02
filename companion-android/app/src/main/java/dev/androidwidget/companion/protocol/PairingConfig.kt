package dev.androidwidget.companion.protocol

import android.net.Uri

data class PairingConfig(
    val host: String,
    val port: Int,
    val fingerprint: String,
    val pairingCode: String? = null,
    val authToken: String? = null,
) {
    val socketUrl: String get() = "wss://$host:$port/companion"

    companion object {
        fun parse(value: String): PairingConfig {
            val uri = Uri.parse(value.trim())
            require(uri.scheme == "awidget" && uri.host == "pair") { "Некорректная ссылка сопряжения" }
            val host = uri.getQueryParameter("host").orEmpty()
            val port = uri.getQueryParameter("port")?.toIntOrNull() ?: 39817
            val fingerprint = uri.getQueryParameter("fingerprint").orEmpty().lowercase()
            val code = uri.getQueryParameter("code").orEmpty()
            require(host.isNotBlank() && port in 1..65535) { "В ссылке отсутствует адрес компьютера" }
            require(fingerprint.matches(Regex("[0-9a-f]{64}"))) { "Некорректный fingerprint компьютера" }
            require(code.matches(Regex("[0-9]{6}"))) { "Некорректный код сопряжения" }
            return PairingConfig(host, port, fingerprint, pairingCode = code)
        }
    }
}
