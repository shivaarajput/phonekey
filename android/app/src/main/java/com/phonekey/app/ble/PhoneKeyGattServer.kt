package com.phonekey.app.ble

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothGattServer
import android.bluetooth.BluetoothGattServerCallback
import android.bluetooth.BluetoothGattService
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.util.Log
import com.phonekey.app.crypto.ChallengeProcessor

class PhoneKeyGattServer(
    private val context: Context,
    private val onAuthSuccess: (String) -> Unit
) {
    companion object {
        private const val TAG = "PhoneKeyGattServer"
    }

    private var bluetoothGattServer: BluetoothGattServer? = null
    private var connectedDevice: BluetoothDevice? = null
    private var challengeCharacteristic: BluetoothGattCharacteristic? = null
    private var lastResponsePayload: ByteArray? = null

    private val gattServerCallback = object : BluetoothGattServerCallback() {
        @SuppressLint("MissingPermission")
        override fun onConnectionStateChange(device: BluetoothDevice, status: Int, newState: Int) {
            super.onConnectionStateChange(device, status, newState)
            Log.d(TAG, "Device ${device.address} connection state changed: $newState")
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                connectedDevice = device
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                if (connectedDevice?.address == device.address) {
                    connectedDevice = null
                }
            }
        }

        @SuppressLint("MissingPermission")
        override fun onCharacteristicReadRequest(
            device: BluetoothDevice,
            requestId: Int,
            offset: Int,
            characteristic: BluetoothGattCharacteristic
        ) {
            super.onCharacteristicReadRequest(device, requestId, offset, characteristic)
            when (characteristic.uuid) {
                GattUuids.VERSION_CHAR_UUID -> {
                    // Return Protocol Version 1.0 (Major=1, Minor=0)
                    val versionData = byteArrayOf(0x01, 0x00)
                    bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, versionData)
                }
                GattUuids.HEARTBEAT_CHAR_UUID -> {
                    val hbData = byteArrayOf(0x01, 100) // Screen On, Battery 100%
                    bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, hbData)
                }
                GattUuids.PUBLIC_KEY_CHAR_UUID -> {
                    val repository = com.phonekey.app.data.PairedPcRepository(context)
                    val pcs = repository.getPairedPcs()
                    val activePc = pcs.firstOrNull { !it.isRevoked }
                    val keyBytes = if (activePc != null) {
                        com.phonekey.app.crypto.KeyStoreManager.getPublicKeyBytes(activePc.pcId) ?: byteArrayOf()
                    } else byteArrayOf()
                    bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, keyBytes)
                }
                GattUuids.CHALLENGE_CHAR_UUID -> {
                    val resp = lastResponsePayload ?: byteArrayOf()
                    bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, resp)
                }
                else -> {
                    bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, null)
                }
            }
        }

        @SuppressLint("MissingPermission")
        override fun onCharacteristicWriteRequest(
            device: BluetoothDevice,
            requestId: Int,
            characteristic: BluetoothGattCharacteristic,
            preparedWrite: Boolean,
            responseNeeded: Boolean,
            offset: Int,
            value: ByteArray
        ) {
            super.onCharacteristicWriteRequest(device, requestId, characteristic, preparedWrite, responseNeeded, offset, value)

            if (responseNeeded) {
                bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, null)
            }

            if (characteristic.uuid == GattUuids.CHALLENGE_CHAR_UUID) {
                Log.d(TAG, "Received Challenge Write request (${value.size} bytes). Processing...")
                val result = ChallengeProcessor.processChallenge(value)

                if (result.responsePayload.isNotEmpty()) {
                    lastResponsePayload = result.responsePayload
                    // Send indication with response packet
                    characteristic.value = result.responsePayload
                    bluetoothGattServer?.notifyCharacteristicChanged(device, characteristic, true)
                    Log.d(TAG, "Sent signed challenge response indication.")

                    if (result.success && result.pcId != null) {
                        onAuthSuccess(result.pcId.toString())
                    }
                }
            }
        }

        @SuppressLint("MissingPermission")
        override fun onDescriptorWriteRequest(
            device: BluetoothDevice,
            requestId: Int,
            descriptor: BluetoothGattDescriptor,
            preparedWrite: Boolean,
            responseNeeded: Boolean,
            offset: Int,
            value: ByteArray
        ) {
            super.onDescriptorWriteRequest(device, requestId, descriptor, preparedWrite, responseNeeded, offset, value)
            if (responseNeeded) {
                bluetoothGattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, null)
            }
        }
    }

    @SuppressLint("MissingPermission")
    fun start() {
        val manager = context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
        bluetoothGattServer = manager?.openGattServer(context, gattServerCallback)

        val service = BluetoothGattService(GattUuids.SERVICE_UUID, BluetoothGattService.SERVICE_TYPE_PRIMARY)

        // Version Characteristic
        val versionChar = BluetoothGattCharacteristic(
            GattUuids.VERSION_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_READ,
            BluetoothGattCharacteristic.PERMISSION_READ
        )

        // Challenge Characteristic (Write + Indicate)
        val challengeChar = BluetoothGattCharacteristic(
            GattUuids.CHALLENGE_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_WRITE or BluetoothGattCharacteristic.PROPERTY_INDICATE,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )
        val cccd = BluetoothGattDescriptor(
            GattUuids.CCCD_UUID,
            BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE
        )
        challengeChar.addDescriptor(cccd)
        challengeCharacteristic = challengeChar

        // Heartbeat Characteristic
        val heartbeatChar = BluetoothGattCharacteristic(
            GattUuids.HEARTBEAT_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_READ,
            BluetoothGattCharacteristic.PERMISSION_READ
        )

        // Public Key Characteristic (NIST P-256 Public Key)
        val publicKeyChar = BluetoothGattCharacteristic(
            GattUuids.PUBLIC_KEY_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_READ,
            BluetoothGattCharacteristic.PERMISSION_READ
        )

        service.addCharacteristic(versionChar)
        service.addCharacteristic(challengeChar)
        service.addCharacteristic(heartbeatChar)
        service.addCharacteristic(publicKeyChar)

        bluetoothGattServer?.addService(service)
        Log.i(TAG, "PhoneKey GATT Server started successfully.")
    }

    @SuppressLint("MissingPermission")
    fun stop() {
        bluetoothGattServer?.close()
        bluetoothGattServer = null
        connectedDevice = null
        Log.i(TAG, "PhoneKey GATT Server stopped.")
    }
}
