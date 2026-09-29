package com.phonekey.app.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.phonekey.app.ble.BleAdvertiserManager
import com.phonekey.app.ble.PhoneKeyGattServer
import com.phonekey.app.data.PairedPcRepository

class PhoneKeyForegroundService : Service() {
    companion object {
        const val CHANNEL_ID = "phonekey_service_channel"
        const val NOTIFICATION_ID = 1001

        fun startService(context: Context) {
            val intent = Intent(context, PhoneKeyForegroundService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stopService(context: Context) {
            val intent = Intent(context, PhoneKeyForegroundService::class.java)
            context.stopService(intent)
        }
    }

    private var gattServer: PhoneKeyGattServer? = null
    private var advertiserManager: BleAdvertiserManager? = null
    private lateinit var repository: PairedPcRepository

    override fun onCreate() {
        super.onCreate()
        repository = PairedPcRepository(this)
        createNotificationChannel()

        val notification = buildNotification("PhoneKey Active - Listening for paired Windows PCs")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID, 
                notification, 
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        // Initialize BLE GATT Server & Advertiser
        gattServer = PhoneKeyGattServer(this) { pcId ->
            repository.updateLastAuthenticated(pcId)
            updateNotification("Authenticated with PC ($pcId)")
        }
        gattServer?.start()

        advertiserManager = BleAdvertiserManager(this)
        advertiserManager?.startAdvertising()
    }

    override fun onDestroy() {
        super.onDestroy()
        advertiserManager?.stopAdvertising()
        gattServer?.stop()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "PhoneKey Proximity Service",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Monitors BLE proximity and authenticates Windows PC logins."
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(text: String): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("PhoneKey Security Engine")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_lock_lock)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun updateNotification(text: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.notify(NOTIFICATION_ID, buildNotification(text))
    }
}
