package dev.androidwidget.companion.connection

import android.annotation.SuppressLint
import android.app.KeyguardManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.ComponentName
import android.content.pm.ServiceInfo
import android.os.BatteryManager
import android.os.Build
import android.os.IBinder
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import dev.androidwidget.companion.MainActivity
import dev.androidwidget.companion.R
import dev.androidwidget.companion.notifications.NotificationRelayService
import dev.androidwidget.companion.protocol.PairingConfig
import dev.androidwidget.companion.security.SecurePairingStore
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import org.json.JSONObject
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

class CompanionConnectionService : Service() {
    private lateinit var store: SecurePairingStore
    private var client: OkHttpClient? = null
    private var socket: WebSocket? = null
    private var activeConfig: PairingConfig? = null
    private var authenticated = false
    private val mainHandler = Handler(Looper.getMainLooper())
    private val statusUpdate = object : Runnable {
        override fun run() {
            if (!authenticated)
                return
            sendStatus()
            mainHandler.postDelayed(this, STATUS_UPDATE_INTERVAL_MILLIS)
        }
    }

    override fun onCreate() {
        super.onCreate()
        store = SecurePairingStore(this)
        createNotificationChannel()
        promoteToForeground("Ожидаю подключение")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val pairingUri = intent?.getStringExtra(EXTRA_PAIRING_URI)
        val config = if (pairingUri.isNullOrBlank()) store.loadAuthenticated() else runCatching {
            PairingConfig.parse(pairingUri)
        }.getOrElse {
            publishState("Ошибка ссылки: ${it.message}")
            null
        }
        if (config != null)
            connect(config)
        else
            publishState("Создайте код на компьютере и вставьте ссылку")
        return START_STICKY
    }

    override fun onDestroy() {
        authenticated = false
        CompanionBus.detach()
        mainHandler.removeCallbacksAndMessages(null)
        val closingSocket = socket
        socket = null
        closingSocket?.close(1000, "Service stopped")
        client?.dispatcher?.executorService?.shutdown()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun connect(config: PairingConfig) {
        activeConfig = config
        authenticated = false
        CompanionBus.detach()
        socket?.cancel()
        client?.dispatcher?.executorService?.shutdown()
        publishState("Подключение к ${config.host}…")

        val trustManager = FingerprintTrustManager(config.fingerprint)
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<TrustManager>(trustManager), SecureRandom())
        }
        client = OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { _, _ -> true }
            .connectTimeout(12, TimeUnit.SECONDS)
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .pingInterval(25, TimeUnit.SECONDS)
            .build()
        val request = Request.Builder().url(config.socketUrl).build()
        socket = client!!.newWebSocket(request, Listener(config))
    }

    private inner class Listener(private val config: PairingConfig) : WebSocketListener() {
        override fun onOpen(webSocket: WebSocket, response: Response) {
            val credential = config.pairingCode ?: config.authToken.orEmpty()
            val mode = if (config.pairingCode != null) "pair" else "auth"
            webSocket.send(buildHello(mode, credential).toString())
        }

        override fun onMessage(webSocket: WebSocket, text: String) {
            if (authenticated)
                return
            val response = runCatching { JSONObject(text) }.getOrNull() ?: return
            if (!response.optBoolean("accepted")) {
                publishState(response.optString("error", "Компьютер отклонил подключение"))
                webSocket.close(1008, "Authentication rejected")
                return
            }

            response.optString("authToken").takeIf { it.isNotBlank() }?.let { token ->
                store.saveAuthenticated(config, token)
                activeConfig = config.copy(pairingCode = null, authToken = token)
            }
            authenticated = true
            CompanionBus.attach { payload -> webSocket.send(payload) }
            publishState("Подключено к ${config.host}")
            mainHandler.removeCallbacks(statusUpdate)
            mainHandler.post(statusUpdate)
        }

        override fun onMessage(webSocket: WebSocket, bytes: ByteString) = Unit

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(code, reason)
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
            handleDisconnect(webSocket, "Соединение закрыто")
        }

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
            handleDisconnect(webSocket, "Нет связи: ${t.message ?: "ошибка сети"}")
        }
    }

    private fun handleDisconnect(disconnectedSocket: WebSocket, message: String) {
        if (socket !== disconnectedSocket)
            return
        socket = null
        authenticated = false
        CompanionBus.detach()
        mainHandler.removeCallbacks(statusUpdate)
        publishState(message)
        val saved = store.loadAuthenticated() ?: return
        mainHandler.postDelayed({ if (socket == null) connect(saved) }, RECONNECT_DELAY_MILLIS)
    }

    private fun buildHello(mode: String, credential: String): JSONObject {
        val deviceName = runCatching { Settings.Global.getString(contentResolver, "device_name") }
            .getOrNull().orEmpty().ifBlank { Build.MODEL }
        val device = JSONObject()
            .put("installationId", store.installationId)
            .put("displayName", deviceName)
            .put("manufacturer", Build.MANUFACTURER)
            .put("model", Build.MODEL)
            .put("androidVersion", Build.VERSION.RELEASE)
            .put("apiLevel", Build.VERSION.SDK_INT)
        return JSONObject()
            .put("protocolVersion", 1)
            .put("mode", mode)
            .put("device", device)
            .put("credential", credential)
    }

    private fun sendStatus() {
        val batteryManager = getSystemService(BatteryManager::class.java)
        val battery = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY).takeIf { it >= 0 }
        val batteryIntent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val batteryStatus = batteryIntent?.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val charging = batteryStatus == BatteryManager.BATTERY_STATUS_CHARGING ||
            batteryStatus == BatteryManager.BATTERY_STATUS_FULL
        val power = getSystemService(PowerManager::class.java)
        val keyguard = getSystemService(KeyguardManager::class.java)
        CompanionBus.send(
            JSONObject()
                .put("type", "status")
                .put("batteryPercent", battery)
                .put("isCharging", charging)
                .put("isScreenOn", power.isInteractive)
                .put("isLocked", keyguard.isDeviceLocked)
                .put("sentAtUnixMilliseconds", System.currentTimeMillis())
                .put("hasNotificationAccess", hasNotificationAccess())
                .toString(),
        )
    }

    private fun hasNotificationAccess(): Boolean {
        val component = ComponentName(this, NotificationRelayService::class.java)
        if (Build.VERSION.SDK_INT >= 27)
            return getSystemService(NotificationManager::class.java)
                .isNotificationListenerAccessGranted(component)
        val enabled = Settings.Secure.getString(contentResolver, "enabled_notification_listeners").orEmpty()
        return enabled.split(':').any { ComponentName.unflattenFromString(it) == component }
    }

    private fun publishState(message: String) {
        promoteToForeground(message)
        sendBroadcast(Intent(ACTION_STATE_CHANGED).setPackage(packageName).putExtra(EXTRA_STATE, message))
    }

    private fun promoteToForeground(message: String) {
        val openIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val notification = android.app.Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_notify_sync)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(message)
            .setContentIntent(openIntent)
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= 34)
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_REMOTE_MESSAGING)
        else
            startForeground(NOTIFICATION_ID, notification)
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.connection_channel),
            NotificationManager.IMPORTANCE_LOW,
        )
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    @SuppressLint("CustomX509TrustManager")
    private class FingerprintTrustManager(expected: String) : X509TrustManager {
        private val expectedBytes = expected.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            val certificate = chain?.firstOrNull() ?: throw CertificateException("Сертификат отсутствует")
            val actual = MessageDigest.getInstance("SHA-256").digest(certificate.encoded)
            if (!MessageDigest.isEqual(actual, expectedBytes))
                throw CertificateException("Fingerprint компьютера не совпадает")
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    companion object {
        const val ACTION_STATE_CHANGED = "dev.androidwidget.companion.STATE_CHANGED"
        const val EXTRA_STATE = "state"
        private const val EXTRA_PAIRING_URI = "pairing_uri"
        private const val CHANNEL_ID = "companion_connection"
        private const val NOTIFICATION_ID = 4101
        private const val RECONNECT_DELAY_MILLIS = 5_000L
        private const val STATUS_UPDATE_INTERVAL_MILLIS = 15_000L

        fun startPairing(context: Context, pairingUri: String) {
            val intent = Intent(context, CompanionConnectionService::class.java)
                .putExtra(EXTRA_PAIRING_URI, pairingUri)
            context.startForegroundService(intent)
        }

        fun startSaved(context: Context) {
            context.startForegroundService(Intent(context, CompanionConnectionService::class.java))
        }
    }
}
