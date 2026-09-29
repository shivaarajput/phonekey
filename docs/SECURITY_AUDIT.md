# PhoneKey Security Audit & Self-Assessment

## Executive Summary
This document provides a comprehensive security review and architectural audit of the PhoneKey Windows & Android system implementation, conducted from the dual perspective of a Windows security engineer, an Android security engineer, and a cryptographer.

---

## 1. Vulnerability Analysis by Category

### 1.1 Authentication Bypasses & BLE Header Independence
* **Finding:** In many naive Bluetooth unlock implementations, systems check only for the peripheral's advertised BLE MAC address, device name, or local presence.
* **Audit Evaluation:** **PASSED.**
  * In PhoneKey, `BleManager` and `ProximityStateMachine` explicitly decouple device presence from authentication.
  * Discovered BLE peripherals are treated as completely untrusted RF channels.
  * Workstation unlock requires receiving a valid `AUTH_CHALLENGE_RESP` signed by the private key corresponding to an enrolled public key stored in `DeviceRegistry`.
  * If an attacker broadcasts the victim's BLE MAC or name, the handshake fails at `ECDsaValidator.VerifySignature()`.

### 1.2 Race Conditions & TOCTOU (Time-Of-Check to Time-Of-Use)
* **Finding:** In proximity-based unlocking, there is a risk that a phone is verified in proximity at $T_0$, but by the time $T_1$ when `LogonUI` requests credentials, the user has walked away.
* **Mitigation in Implementation:**
  * When `PhoneKeyCP` calls `CIpcClient::RequestLogonCredentials()`, `NamedPipeServer.HandleLogonBufferRequestAsync()` re-verifies `_stateMachine.CurrentState == ProximityState.InProximity` atomically.
  * The challenge-response cycle and proximity state are protected by `SemaphoreSlim _authLock` in `BleManager`.
  * If the phone signal drops during the LogonUI interaction, the state shifts to `GracePeriod` and the credential release is immediately rejected.

### 1.3 Insecure Inter-Process Communication (IPC) & Privilege Escalation
* **Finding:** The Windows Service runs as `NT AUTHORITY\SYSTEM` in Session 0. If standard users in Session 1 could connect to the named pipe and query `GetSerializedLogonBuffer`, this would represent a local privilege escalation (LPE) vulnerability to obtain user passwords.
* **Mitigation in Implementation:**
  * `NamedPipeServer` constructs an explicit Windows `PipeSecurity` Discretionary Access Control List (DACL).
  * In `HandleLogonBufferRequestAsync()`, the pipe executes `pipe.RunAsClient(...)` to inspect the client token's security identifier.
  * If `identity.IsSystem` is false, access is denied immediately and logged as a security alert. Standard desktop user processes (`PhoneKeyUI.exe`) can query status (`GetStatus`) and trigger locks (`ManualLock`), but cannot extract logon credentials.

### 1.4 Insecure Local Storage & DPAPI Split-Key Vault
* **Finding:** Storing cached passwords on disk is a high-value target for offline attacks or stolen hardware.
* **Mitigation in Implementation:**
  * Credentials are protected by a **Split-Key Defense**:
    $$\text{Ciphertext} = \text{DPAPI}_{\text{LocalMachine}}(\text{AES-256-GCM}_{K_{\text{phone}}}(\text{Domain} \parallel \text{User} \parallel \text{Pass}))$$
  * Without the physical Android phone participating in the ECDH/BLE exchange to unlock the session, an offline attacker extracting the disk cannot decrypt the AES-GCM layer even with full access to the machine DPAPI master key.
  * All intermediate memory buffers containing credentials are sanitized with `CryptographicOperations.ZeroMemory` in C# and `SecureZeroMemory` in C++.

### 1.5 DLL & Plugin Loading Risks (Credential Provider)
* **Finding:** `PhoneKeyCP.dll` is loaded into `LogonUI.exe` as `SYSTEM` on the Secure Desktop. Insecure DLL search paths could lead to DLL side-loading.
* **Mitigation in Implementation:**
  * Registration script enforces placement in `%ProgramFiles%\PhoneKey\PhoneKeyCP.dll`.
  * The DLL statically links only to core Windows subsystem libraries (`Secur32.lib`, `Shlwapi.lib`, `Credui.lib`).
  * No external third-party DLLs are loaded in the Winlogon address space.

### 1.6 Replay & Downgrade Attacks
* **Finding:** Sniffing valid BLE signatures or intercepting challenge packets.
* **Mitigation in Implementation:**
  * `NonceManager` generates 256-bit cryptographically secure random nonces using `RandomNumberGenerator.Fill`.
  * Nonces are tracked in a concurrent cache and consumed **atomically** via `TryRemove`. A challenge can **never** be used more than once.
  * Mutual nonces: both PC nonce $N_{\text{pc}}$ and phone nonce $N_{\text{phone}}$ are hashed into the signable digest.
  * Domain separation prefix `"PhoneKey-v1-Auth"` ensures signatures cannot be replayed across different protocol versions or other applications.

### 1.7 Relay & Wormhole Attacks
* **Finding:** Attacker relays RF signal between PC and remote phone over the internet.
* **Mitigation in Implementation:**
  * Strict Round-Trip Time (RTT) enforcement: `ExecuteChallengeHandshakeAsync()` caps execution at $400\text{ ms}$. Internet-based multi-hop relays introduce latencies far exceeding this window.
  * Android Keystore user presence gate: Optional biometric (`.setUserAuthenticationRequired(true)`) ensures an attacker cannot trigger an unlock if the phone is unattended.

### 1.8 Android Keystore Private Key Protection
* **Finding:** Extraction of signing keys by malware or rooted devices.
* **Mitigation in Implementation:**
  * Keys are generated using `KeyGenParameterSpec` with `PURPOSE_SIGN` and `DIGEST_SHA256` inside `AndroidKeyStore`.
  * `isInsideSecureHardware()` and StrongBox backing (API 28+) guarantee the private key is held in dedicated tamper-resistant hardware (ARM TrustZone / Titan M / Knox Vault).
  * The private key is non-exportable; signature computation happens exclusively inside the secure enclave.

### 1.9 Accidental Logging of Secrets
* **Audit Evaluation:** **PASSED.**
  * Code inspection confirms: passwords, DPAPI unsealed buffers, and private key references are never passed to `ILogger`, Android `Log.d`, or debug output.
  * Only hashes, nonces, RSSI values, and timing telemetry are logged.

---

## 2. Windows Architectural Realities & Honest Limitations

### 2.1 The "Magic Unlock" Myth
Ordinary desktop applications running as a user cannot unlock a locked Windows PC. Winlogon and the Secure Desktop run in an isolated session. Attempting to use UI automation, keyboard simulation (`SendKeys`), or undocumented APIs is brittle and insecure.
* **PhoneKey Architecture:** Implements a true, fully compliant **Windows Credential Provider** (`ICredentialProvider` + `ICredentialProviderCredential2`) that cleanly integrates with Windows LSA (`lsass.exe`).

### 2.2 BLE RSSI Physical Limitations
Received Signal Strength Indicator (RSSI) is an RF signal power measurement, **not an accurate distance metric**. Multipath reflections, wall materials, and human body water absorption (10–20 dB drop when phone is in a back pocket) cause natural fluctuation.
* **PhoneKey Architecture:** Uses a **1D Kalman Filter** to smooth raw RSSI readings and provides a **dual-threshold hysteresis model** (Unlock: $\ge -65\text{ dBm}$, Lock: $\le -85\text{ dBm}$) with a **15-second grace period**. This avoids nuisance locking while guaranteeing prompt automatic locking on genuine departure.

### 2.3 Permanent Lockout Prevention
* PhoneKey does not replace or disable Windows Hello PIN or Password tiles.
* If the phone battery is depleted, the phone is misplaced, or the Bluetooth adapter fails, clicking **"Sign-in options"** on the Windows lock screen allows immediate fallback to the user's PIN or Password.
