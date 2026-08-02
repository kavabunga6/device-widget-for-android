package dev.androidwidget.companion.notifications

import android.app.Notification
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import dev.androidwidget.companion.connection.CompanionBus
import org.json.JSONObject

class NotificationRelayService : NotificationListenerService() {
    override fun onNotificationPosted(statusBarNotification: StatusBarNotification) {
        if (statusBarNotification.packageName == packageName ||
            statusBarNotification.notification.flags and Notification.FLAG_GROUP_SUMMARY != 0
        ) return

        val extras = statusBarNotification.notification.extras
        val title = normalize(
            extras.getCharSequence(Notification.EXTRA_CONVERSATION_TITLE)?.toString()
                ?: extras.getCharSequence(Notification.EXTRA_TITLE)?.toString().orEmpty(),
            100,
        )
        val preview = normalize(
            extras.getCharSequence(Notification.EXTRA_BIG_TEXT)?.toString()
                ?: extras.getCharSequence(Notification.EXTRA_TEXT)?.toString().orEmpty(),
            300,
        )
        if (title.isBlank() && preview.isBlank()) return

        val appName = runCatching {
            val info = packageManager.getApplicationInfo(statusBarNotification.packageName, 0)
            packageManager.getApplicationLabel(info).toString()
        }.getOrDefault(statusBarNotification.packageName)

        CompanionBus.send(
            JSONObject()
                .put("type", "notification")
                .put("notificationId", statusBarNotification.key)
                .put("packageName", statusBarNotification.packageName)
                .put("appName", normalize(appName, 80))
                .put("title", title)
                .put("preview", preview)
                .put("postedAtUnixMilliseconds", statusBarNotification.postTime)
                .put("isConversation", statusBarNotification.notification.category == Notification.CATEGORY_MESSAGE)
                .toString(),
        )
    }

    private fun normalize(value: String, maximumLength: Int): String {
        val normalized = value.replace(Regex("\\s+"), " ").trim()
        return if (normalized.length <= maximumLength) normalized else normalized.take(maximumLength - 1) + "…"
    }
}
