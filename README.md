# PhoneKey: Production-Grade Windows & Android Proximity Authentication System

PhoneKey turns your Android smartphone into a hardware-backed cryptographic proximity key for Windows 10/11 logon and workstation locking.

---

## Architecture Summary

```
                      +---------------------------------------+
                      |             ANDROID PHONE             |
                      | - Jetpack Compose UI                  |
                      | - Low-Power BLE Peripheral Service    |
                      | - Android Keystore (TEE / StrongBox)  |
                      | - Non-exportable NIST P-256 Keypair   |
                      +-------------------+-------------------+
                                          |
                                          | BLE GATT Protocol
                                          | (NIST P-256 ECDSA, Mutual Nonces, RTT <= 400ms)
                                          v
+-----------------------------------------------------------------------------------------+
|                                      WINDOWS PC                                         |
|                                                                                         |
|  [SESSION 0: PhoneKeyService.exe (SYSTEM)]                                              |
|  - Continuous BLE Central Scanner & GATT Client                                         |
|  - 1D Kalman-filtered RSSI tracking with dual-threshold hysteresis                      |
|  - Challenge-Response Engine (256-bit CSPRNG nonces, single-use anti-replay)            |
|  - Split-Key DPAPI Vault (Machine DPAPI + Phone-bound AES-256-GCM)                      |
|  - Named Pipe Server (\\.\pipe\PhoneKeyAuthPipe with SYSTEM DACL)                       |
|  - Auto-lock Controller (Win32 LockWorkStation)                                         |
|                                         |                                               |
|                                         | Named Pipe IPC                                |
|                                         v                                               |
|  [LOGON SECURE DESKTOP: PhoneKeyCP.dll in LogonUI.exe]                                  |
|  - Native C++ Credential Provider implementing ICredentialProvider & Credential2       |
|  - Queries PhoneKeyService for verified proximity and cryptographic auth               |
|  - Serializes KERB_INTERACTIVE_LOGON authentication package directly into LSA           |
|  - Seamless coexistence with Windows Hello / PIN / Password (permanent fallback)        |
|                                                                                         |
|  [USER SESSION: PhoneKey.UI.exe (WPF Desktop App)]                                      |
|  - Glassmorphic dark dashboard: live connection, RSSI signal meter, auth status         |
|  - Pairing Wizard with optical QR Code generation                                       |
|  - Lock Now, Remove Phone, Settings, and Security Diagnostics                           |
+-----------------------------------------------------------------------------------------+
```

---

## Project Structure

```
phonekey/
├── docs/
│   ├── THREAT_MODEL.md                  # Comprehensive STRIDE & residual risk analysis
│   ├── PROTOCOL_SPEC.md                 # Binary packet wire formats, opcodes, state machine
│   ├── CREDENTIAL_PROVIDER_SETUP.md     # COM registration & LogonUI architecture guide
│   └── SECURITY_AUDIT.md                # 14-category security review & self-audit
├── windows/
│   ├── PhoneKey.sln                     # Visual Studio Solution
│   └── src/
│       ├── PhoneKey.Core/               # Shared .NET 8 Library
│       │   ├── Crypto/                  # NonceManager, ECDsaValidator, DpapiVault
│       │   ├── Protocol/                # BinaryWireCodec, ProtocolModels
│       │   ├── Proximity/               # KalmanFilter, ProximityStateMachine
│       │   └── Storage/                 # DeviceRegistry (DPAPI protected)
│       ├── PhoneKey.Service/            # Windows Service in Session 0 (SYSTEM)
│       │   ├── Ble/                     # BleManager (WinRT GATT client & watcher)
│       │   ├── Ipc/                     # NamedPipeServer (DACL restricted)
│       │   └── AutoLock/                # LockController (Win32 LockWorkStation)
│       ├── PhoneKey.CredentialProvider/ # Native C++ COM DLL for LogonUI.exe
│       │   ├── PhoneKeyCredentialProvider.cpp / .h
│       │   ├── PhoneKeyCredential.cpp / .h
│       │   ├── IpcClient.cpp / .h
│       │   ├── helpers.cpp / .h
│       │   ├── dllmain.cpp
│       │   └── PhoneKeyCP.def
│       └── PhoneKey.UI/                 # Modern WPF Desktop & Settings Dashboard
│           ├── MainWindow.xaml / .cs
│           └── Views/
│               ├── PairingWizardWindow.xaml / .cs
│               └── DiagnosticsDialog.xaml / .cs
├── android/
│   └── app/
│       ├── build.gradle.kts
│       └── src/main/
│           ├── AndroidManifest.xml
│           └── java/com/phonekey/app/
│               ├── crypto/              # KeyStoreManager (TEE/StrongBox), ChallengeProcessor
│               ├── ble/                 # PhoneKeyGattServer, BleAdvertiserManager
│               ├── service/             # PhoneKeyForegroundService
│               ├── data/                # PairedPcRepository
│               └── ui/                  # MainActivity (Jetpack Compose)
├── tests/
│   └── PhoneKey.Core.Tests/             # Unit tests for crypto, anti-replay, wire codec, and proximity
└── scripts/
    ├── register_credential_provider.ps1 # Registers COM CLSID, Credential Provider, and Service
    ├── unregister_credential_provider.ps1 # Safely unregisters PhoneKey and restores defaults
    └── build_all.ps1                    # Automated build and test runner
```

---

## Key Security Features

1. **Decoupled from Bluetooth Headers:** Never relies on phone BLE MAC address, Bluetooth name, or Wi-Fi SSID for identity.
2. **Hardware-Backed Protection:** Android private key is generated inside Android Keystore with `PURPOSE_SIGN` and isolated in TEE / StrongBox.
3. **Anti-Replay:** 256-bit CSPRNG challenge nonces, single-use atomic consumption, and a 5-second freshness window.
4. **Relay Mitigation:** Cryptographic round-trip time (RTT) is strictly enforced ($\le 400\text{ ms}$).
5. **Split-Key DPAPI Vault:** Windows logon credentials stored at rest are dual-encrypted with machine DPAPI and sealed with an AES-256-GCM key derived from phone pairing.
6. **No Permanent Lockout:** Standard Windows PIN, Windows Hello, and Password remain available at all times via "Sign-in options".
7. **Proximity Hysteresis:** 1D Kalman-filtered RSSI with dual thresholds (Unlock: $\ge -65\text{ dBm}$, Lock: $\le -85\text{ dBm}$) and a 15-second grace period prevent false locks.
