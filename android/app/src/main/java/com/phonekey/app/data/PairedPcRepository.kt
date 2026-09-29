package com.phonekey.app.data

import android.content.Context
import android.content.SharedPreferences
import com.phonekey.app.crypto.KeyStoreManager
import org.json.JSONArray
import org.json.JSONObject

data class PairedPc(
    val pcId: String,
    val hostname: String,
    val enrolledAt: Long,
    var lastAuthenticated: Long = 0L,
    var isRevoked: Boolean = false
)

class PairedPcRepository(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences("phonekey_paired_pcs", Context.MODE_PRIVATE)

    fun getPairedPcs(): List<PairedPc> {
        val jsonStr = prefs.getString("pcs_list", "[]") ?: "[]"
        val list = mutableListOf<PairedPc>()
        val array = JSONArray(jsonStr)
        for (i in 0 until array.length()) {
            val obj = array.getJSONObject(i)
            list.add(
                PairedPc(
                    pcId = obj.getString("pcId"),
                    hostname = obj.getString("hostname"),
                    enrolledAt = obj.getLong("enrolledAt"),
                    lastAuthenticated = obj.optLong("lastAuthenticated", 0L),
                    isRevoked = obj.optBoolean("isRevoked", false)
                )
            )
        }
        return list
    }

    fun savePc(pc: PairedPc) {
        val list = getPairedPcs().toMutableList()
        val index = list.indexOfFirst { it.pcId == pc.pcId }
        if (index >= 0) {
            list[index] = pc
        } else {
            list.add(pc)
        }
        persistList(list)
    }

    fun updateLastAuthenticated(pcId: String) {
        val list = getPairedPcs().toMutableList()
        val index = list.indexOfFirst { it.pcId == pcId }
        if (index >= 0) {
            list[index].lastAuthenticated = System.currentTimeMillis()
            persistList(list)
        }
    }

    fun revokePc(pcId: String) {
        val list = getPairedPcs().toMutableList()
        val index = list.indexOfFirst { it.pcId == pcId }
        if (index >= 0) {
            list[index].isRevoked = true
            persistList(list)
            KeyStoreManager.deleteKey(pcId)
        }
    }

    fun deletePc(pcId: String) {
        val list = getPairedPcs().filter { it.pcId != pcId }
        persistList(list)
        KeyStoreManager.deleteKey(pcId)
    }

    private fun persistList(list: List<PairedPc>) {
        val array = JSONArray()
        for (pc in list) {
            val obj = JSONObject().apply {
                put("pcId", pc.pcId)
                put("hostname", pc.hostname)
                put("enrolledAt", pc.enrolledAt)
                put("lastAuthenticated", pc.lastAuthenticated)
                put("isRevoked", pc.isRevoked)
            }
            array.put(obj)
        }
        prefs.edit().putString("pcs_list", array.toString()).apply()
    }
}
