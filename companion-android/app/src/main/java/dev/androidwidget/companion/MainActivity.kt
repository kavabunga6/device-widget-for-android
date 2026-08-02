package dev.androidwidget.companion

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.androidwidget.companion.connection.CompanionConnectionService
import dev.androidwidget.companion.protocol.PairingConfig
import dev.androidwidget.companion.security.SecurePairingStore

class MainActivity : Activity() {
    private lateinit var pairingInput: EditText
    private lateinit var statusText: TextView
    private lateinit var notificationAccessText: TextView
    private lateinit var store: SecurePairingStore

    private val stateReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            statusText.text = intent?.getStringExtra(CompanionConnectionService.EXTRA_STATE).orEmpty()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = SecurePairingStore(this)
        setContentView(createContent())
        handlePairingIntent(intent)
        requestNotificationPermission()
        if (store.loadAuthenticated() != null) {
            statusText.text = "Найдено сохранённое сопряжение"
            CompanionConnectionService.startSaved(this)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handlePairingIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        notificationAccessText.text = if (hasNotificationAccess())
            "Доступ к уведомлениям включён"
        else
            "Доступ к уведомлениям выключен"
    }

    override fun onStart() {
        super.onStart()
        val filter = IntentFilter(CompanionConnectionService.ACTION_STATE_CHANGED)
        if (Build.VERSION.SDK_INT >= 33)
            registerReceiver(stateReceiver, filter, RECEIVER_NOT_EXPORTED)
        else
            @Suppress("UnspecifiedRegisterReceiverFlag") registerReceiver(stateReceiver, filter)
    }

    override fun onStop() {
        unregisterReceiver(stateReceiver)
        super.onStop()
    }

    private fun createContent(): ScrollView {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(24), dp(28), dp(24), dp(28))
            setBackgroundColor(Color.rgb(32, 33, 36))
        }
        content.addView(text("Device Widget Companion", 26f, Color.WHITE, true))
        content.addView(text(
            "Локальная защищённая связь с Windows, macOS и Linux",
            14f,
            Color.rgb(197, 199, 206),
        ).withTopMargin(6))

        content.addView(text("Сопряжение", 18f, Color.WHITE, true).withTopMargin(30))
        content.addView(text(
            "В desktop-приложении нажмите «Новый код», скопируйте ссылку и вставьте её сюда.",
            13f,
            Color.rgb(197, 199, 206),
        ).withTopMargin(6))
        pairingInput = EditText(this).apply {
            hint = "awidget://pair?host=…"
            setHintTextColor(Color.rgb(120, 124, 134))
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.rgb(43, 45, 49))
            minLines = 3
            setPadding(dp(14), dp(12), dp(14), dp(12))
        }
        content.addView(pairingInput.withTopMargin(12))

        content.addView(Button(this).apply {
            text = "Подключить"
            setOnClickListener { connect() }
        }.withTopMargin(12))

        statusText = text("Ожидаю ссылку сопряжения", 13f, Color.rgb(139, 124, 255))
        content.addView(statusText.withTopMargin(12))

        content.addView(text("Уведомления", 18f, Color.WHITE, true).withTopMargin(30))
        notificationAccessText = text("Проверяю доступ…", 13f, Color.rgb(197, 199, 206))
        content.addView(notificationAccessText.withTopMargin(7))
        content.addView(Button(this).apply {
            text = "Открыть доступ к уведомлениям"
            setOnClickListener {
                runCatching { startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) }
            }
        }.withTopMargin(10))

        content.addView(Button(this).apply {
            text = "Лицензии и сторонние компоненты"
            setOnClickListener { showOpenSourceLicenses() }
        }.withTopMargin(10))

        content.addView(text(
            "Приложение не запрашивает READ_SMS, журнал звонков или контакты. На компьютер передаются только уведомления, доступ к которым вы явно разрешили.",
            12f,
            Color.rgb(150, 154, 164),
        ).withTopMargin(28))

        return ScrollView(this).apply {
            addView(content, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    private fun connect() {
        val value = pairingInput.text.toString()
        runCatching { PairingConfig.parse(value) }
            .onSuccess {
                statusText.text = "Запускаю защищённое подключение…"
                CompanionConnectionService.startPairing(this, value)
            }
            .onFailure { statusText.text = it.message ?: "Некорректная ссылка" }
    }

    private fun handlePairingIntent(intent: Intent?) {
        val pairingUri = intent?.data?.toString().orEmpty()
        if (pairingUri.isBlank())
            return
        pairingInput.setText(pairingUri)
        connect()
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) !=
            android.content.pm.PackageManager.PERMISSION_GRANTED
        ) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 100)
    }

    private fun hasNotificationAccess(): Boolean {
        val component = ComponentName(
            this,
            dev.androidwidget.companion.notifications.NotificationRelayService::class.java,
        )
        if (Build.VERSION.SDK_INT >= 27)
            return getSystemService(NotificationManager::class.java)
                .isNotificationListenerAccessGranted(component)
        val enabled = Settings.Secure.getString(contentResolver, "enabled_notification_listeners").orEmpty()
        return enabled.split(':').any { ComponentName.unflattenFromString(it) == component }
    }

    private fun showOpenSourceLicenses() {
        val notice = resources.openRawResource(R.raw.third_party_notices)
            .bufferedReader(Charsets.UTF_8)
            .use { it.readText() }
        AlertDialog.Builder(this)
            .setTitle("Лицензии")
            .setMessage(notice)
            .setPositiveButton("Готово", null)
            .show()
    }

    private fun text(value: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = value
        textSize = size
        setTextColor(color)
        if (bold) setTypeface(typeface, android.graphics.Typeface.BOLD)
    }

    private fun <T : android.view.View> T.withTopMargin(value: Int): T = apply {
        layoutParams = LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
        ).also { it.topMargin = dp(value) }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
}
