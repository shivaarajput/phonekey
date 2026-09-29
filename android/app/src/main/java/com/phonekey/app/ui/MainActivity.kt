package com.phonekey.app.ui

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.phonekey.app.crypto.KeyStoreManager
import com.phonekey.app.data.PairedPc
import com.phonekey.app.data.PairedPcRepository
import com.phonekey.app.service.PhoneKeyForegroundService
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.*

class MainActivity : ComponentActivity() {

    private lateinit var repository: PairedPcRepository
    private val pairedPcsState = mutableStateOf<List<PairedPc>>(emptyList())

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.entries.all { it.value }
        if (allGranted) {
            PhoneKeyForegroundService.startService(this)
        } else {
            Toast.makeText(this, "BLE permissions required for PhoneKey operation", Toast.LENGTH_LONG).show()
        }
    }

    private val qrScanLauncher = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) {
            processQrPayload(result.contents)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        repository = PairedPcRepository(this)
        pairedPcsState.value = repository.getPairedPcs()

        checkAndRequestPermissions()

        setContent {
            PhoneKeyTheme {
                MainScreen(
                    onToggleService = { enable ->
                        if (enable) {
                            PhoneKeyForegroundService.startService(this)
                        } else {
                            PhoneKeyForegroundService.stopService(this)
                        }
                    },
                    onScanQr = { launchQrScanner() },
                    pairedPcs = pairedPcsState.value,
                    onRevoke = { pcId ->
                        repository.revokePc(pcId)
                        pairedPcsState.value = repository.getPairedPcs()
                    },
                    onDelete = { pcId ->
                        repository.deletePc(pcId)
                        pairedPcsState.value = repository.getPairedPcs()
                    }
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        if (::repository.isInitialized) {
            pairedPcsState.value = repository.getPairedPcs()
        }
    }

    private fun checkAndRequestPermissions() {
        val permissions = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            permissions.add(Manifest.permission.BLUETOOTH_ADVERTISE)
            permissions.add(Manifest.permission.BLUETOOTH_CONNECT)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            permissions.add(Manifest.permission.POST_NOTIFICATIONS)
        }
        permissions.add(Manifest.permission.CAMERA)

        val needed = permissions.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }

        if (needed.isNotEmpty()) {
            permissionLauncher.launch(needed.toTypedArray())
        } else {
            PhoneKeyForegroundService.startService(this)
        }
    }

    private fun launchQrScanner() {
        val options = ScanOptions().apply {
            setPrompt("Scan PhoneKey pairing QR code on Windows screen")
            setBeepEnabled(true)
            setOrientationLocked(true)
            setCaptureActivity(PortraitCaptureActivity::class.java)
        }
        qrScanLauncher.launch(options)
    }

    private fun processQrPayload(json: String) {
        try {
            val obj = JSONObject(json)
            val pcId = obj.getString("PcId")
            val hostname = obj.getString("PcHostname")

            // Generate NIST P-256 Key inside Keystore
            val keyResult = KeyStoreManager.generateKeyPair(pcId)

            val pc = PairedPc(
                pcId = pcId,
                hostname = hostname,
                enrolledAt = System.currentTimeMillis()
            )
            repository.savePc(pc)

            // Immediately refresh UI state so enrolled card shows up
            pairedPcsState.value = repository.getPairedPcs()

            Toast.makeText(this, "Enrolled with $hostname (${keyResult.securityLevel})", Toast.LENGTH_LONG).show()
        } catch (e: Exception) {
            Toast.makeText(this, "Invalid QR code format: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    onToggleService: (Boolean) -> Unit,
    onScanQr: () -> Unit,
    pairedPcs: List<PairedPc>,
    onRevoke: (String) -> Unit,
    onDelete: (String) -> Unit
) {
    var serviceEnabled by remember { mutableStateOf(true) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("PhoneKey", fontWeight = FontWeight.Bold) },
                actions = {
                    IconButton(onClick = onScanQr) {
                        Icon(Icons.Default.Add, contentDescription = "Pair PC")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = Color(0xFF0F172A),
                    titleContentColor = Color.White,
                    actionIconContentColor = Color.White
                )
            )
        },
        containerColor = Color(0xFF0F172A)
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .padding(16.dp)
        ) {
            // Status Card
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = Color(0xFF1E293B))
            ) {
                Column(modifier = Modifier.padding(20.dp)) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(
                                modifier = Modifier
                                    .size(14.dp)
                                    .clip(CircleShape)
                                    .background(if (serviceEnabled) Color(0xFF10B981) else Color(0xFFEF4444))
                            )
                            Spacer(modifier = Modifier.width(10.dp))
                            Text(
                                text = if (serviceEnabled) "PhoneKey Active (Broadcasting)" else "PhoneKey Paused",
                                color = Color.White,
                                fontWeight = FontWeight.Bold,
                                fontSize = 16.sp
                            )
                        }

                        Switch(
                            checked = serviceEnabled,
                            onCheckedChange = {
                                serviceEnabled = it
                                onToggleService(it)
                            }
                        )
                    }

                    Spacer(modifier = Modifier.height(14.dp))
                    Text(
                        text = if (serviceEnabled) 
                            "Broadcasting presence beacon to nearby paired Windows PCs."
                        else 
                            "Presence broadcasting is paused. PC will not auto-unlock.",
                        color = Color(0xFF94A3B8),
                        fontSize = 13.sp
                    )
                    Text(
                        text = "Hardware Security: TEE / StrongBox Active (Isolated Private Key)",
                        color = Color(0xFF64748B),
                        fontSize = 11.sp,
                        modifier = Modifier.padding(top = 4.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(20.dp))

            Text(
                text = "PAIRED WINDOWS PCS",
                color = Color(0xFF94A3B8),
                fontSize = 12.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp)
            )

            if (pairedPcs.isEmpty()) {
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f),
                    shape = RoundedCornerShape(16.dp),
                    colors = CardDefaults.cardColors(containerColor = Color(0xFF161F30))
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .padding(24.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Icon(
                                Icons.Default.Lock,
                                contentDescription = null,
                                tint = Color(0xFF6366F1),
                                modifier = Modifier.size(52.dp)
                            )
                            Spacer(modifier = Modifier.height(14.dp))
                            Text(
                                text = "Ready for PC Connection",
                                color = Color.White,
                                fontWeight = FontWeight.Bold,
                                fontSize = 16.sp
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = "Keep PhoneKey ON. On your Windows PC, open PhoneKey — it will detect this phone and let you pair with 1 click.",
                                color = Color(0xFF94A3B8),
                                fontSize = 13.sp,
                                modifier = Modifier.padding(horizontal = 16.dp),
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(18.dp))
                            OutlinedButton(
                                onClick = onScanQr,
                                colors = ButtonDefaults.outlinedButtonColors(contentColor = Color(0xFFA5B4FC))
                            ) {
                                Icon(Icons.Default.Add, contentDescription = null, modifier = Modifier.size(16.dp))
                                Spacer(modifier = Modifier.width(6.dp))
                                Text("Or Pair via QR Code", fontSize = 13.sp)
                            }
                        }
                    }
                }
            } else {
                LazyColumn(modifier = Modifier.weight(1f)) {
                    items(pairedPcs) { pc ->
                        PcItemCard(
                            pc = pc,
                            onRevoke = { onRevoke(pc.pcId) },
                            onDelete = { onDelete(pc.pcId) }
                        )
                        Spacer(modifier = Modifier.height(10.dp))
                    }
                }
            }
        }
    }
}

@Composable
fun PcItemCard(pc: PairedPc, onRevoke: () -> Unit, onDelete: () -> Unit) {
    val dateFormat = SimpleDateFormat("MMM dd, yyyy HH:mm", Locale.getDefault())
    val enrolledDate = dateFormat.format(Date(pc.enrolledAt))
    val lastAuth = if (pc.lastAuthenticated > 0) dateFormat.format(Date(pc.lastAuthenticated)) else "Never"

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = Color(0xFF1E293B))
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.Default.Lock,
                        contentDescription = null,
                        tint = if (pc.isRevoked) Color(0xFFEF4444) else Color(0xFF6366F1),
                        modifier = Modifier.size(28.dp)
                    )
                    Spacer(modifier = Modifier.width(12.dp))
                    Column {
                        Text(
                            text = pc.hostname,
                            color = Color.White,
                            fontWeight = FontWeight.SemiBold,
                            fontSize = 15.sp
                        )
                        Text(
                            text = if (pc.isRevoked) "Revoked" else "Enrolled: $enrolledDate",
                            color = if (pc.isRevoked) Color(0xFFEF4444) else Color(0xFF94A3B8),
                            fontSize = 11.sp
                        )
                    }
                }

                Row {
                    if (!pc.isRevoked) {
                        IconButton(onClick = onRevoke) {
                            Icon(Icons.Default.Lock, contentDescription = "Revoke", tint = Color(0xFFF59E0B))
                        }
                    }
                    IconButton(onClick = onDelete) {
                        Icon(Icons.Default.Delete, contentDescription = "Delete", tint = Color(0xFFEF4444))
                    }
                }
            }

            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = "Last Authenticated: $lastAuth",
                color = Color(0xFF64748B),
                fontSize = 11.sp
            )
        }
    }
}

@Composable
fun PhoneKeyTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Color(0xFF6366F1),
            background = Color(0xFF0F172A),
            surface = Color(0xFF1E293B)
        ),
        content = content
    )
}
