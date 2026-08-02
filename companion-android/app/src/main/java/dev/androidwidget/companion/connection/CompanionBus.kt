package dev.androidwidget.companion.connection

object CompanionBus {
    @Volatile
    private var sender: ((String) -> Boolean)? = null

    fun attach(value: (String) -> Boolean) {
        sender = value
    }

    fun detach() {
        sender = null
    }

    fun send(json: String): Boolean = sender?.invoke(json) == true
}
