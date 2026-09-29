# PhoneKey Threat Model & Trust Boundaries Specification

## 1. System Overview & Assets
PhoneKey is a hybrid proximity and cryptographic authentication system binding a physical Android device to a Windows 10/11 workstation.

### Protected Assets
1. **Windows User Logon Credentials:** Plaintext Windows passwords, Kerberos TGTs, NTLM hashes, and user session tokens.
2. **Workstation Access (Active Session):** The unlocked interactive desktop of the user.
3. **Android Keystore Private Keys:** Hardware-bound NIST P-256 signing keys isolated inside the phone's TEE/StrongBox.
4. **PhoneKey Service Configuration & Vault:** Enrolled device identities, public keys, machine-bound DPAPI encrypted credential blobs.
5. **BLE Communication Channel:** Over-the-air challenge, response, and heartbeat telemetry.

---

## 2. Trust Boundaries & Execution Contexts

```
[ Android Device (Untrusted RF Environment) ]
   |
   | (Boundary 1: Over-The-Air BLE Radio - Untrusted / Eavesdroppable / Injectable)
   v
[ Windows Session 0: PhoneKeyService.exe (NT AUTHORITY\SYSTEM) ]
   |
   | (Boundary 2: Local Inter-Process Communication - Named Pipe with DACL)
   v
[ Windows Logon Desktop: LogonUI.exe -> PhoneKeyCP.dll (SYSTEM) ]
   |
   | (Boundary 3: Local Security Authority Subsystem Service - lsass.exe)
   v
[ Windows Kernel / Interactive User Session (Session 1+) ]
   |
   | (Boundary 4: User-mode UI to Service Pipe - Limited Commands Only)
   v
[ PhoneKeyUI.exe (Standard User Token) ]
```

### Trust Boundary Analysis
* **Boundary 1 (Air / BLE):** Adversaries can sniff, inject, replay, or jam 2.4 GHz packets. All authentication traffic must be cryptographically protected (ephemeral nonces, digital signatures, RTT validation). Device identity must never rely on MAC addresses or BLE advertised names.
* **Boundary 2 (Service to Credential Provider Pipe):** `PhoneKeyCP.dll` executes inside `LogonUI.exe` as `SYSTEM`. The named pipe `\\.\pipe\PhoneKeyAuthPipe` must enforce a Discretionary Access Control List (DACL) permitting only `NT AUTHORITY\SYSTEM` to query sensitive serialized logon credentials.
* **Boundary 3 (LSA Interface):** Only authenticated Windows subsystems can submit logon tokens via `LsaLogonUser`. Credential structures must strictly conform to `KERB_INTERACTIVE_LOGON`.
* **Boundary 4 (Desktop UI to Service Pipe):** The tray/settings UI runs under the logged-in user's identity. It must be able to view status and initiate pairing, but must **never** be permitted to request user passwords or bypass authentication.

---

## 3. Threat Matrix & Detailed Mitigations

| Threat ID | Threat Class | Attack Description | Mitigation Mechanism | Residual Risk & Detection |
| :--- | :--- | :--- | :--- | :--- |
| **TH-01** | **Spoofing** | Attacker spoofs phone BLE MAC address and broadcast name to trigger unlock. | Identity is decoupled from BLE headers. Windows requires ECDSA-SHA256 signature over fresh 256-bit nonce using public key registered during out-of-band enrollment. | None. Unsigned challenges immediately terminate connection. |
| **TH-02** | **Replay Attack** | Attacker sniffs a valid signed challenge over BLE and replays it after the victim walks away. | 1. Windows generates a fresh cryptographically secure 256-bit random challenge nonce $N_{\text{pc}}$ per attempt.<br>2. Nonce is single-use and invalidates after 5 seconds or upon first consumption.<br>3. Timestamp verification ensures mutual clock sanity. | None. Replayed nonces are rejected by the challenge cache. |
| **TH-03** | **Relay / Wormhole Attack** | Attacker near PC tunnels BLE traffic over Wi-Fi/cellular to an accomplice near the victim's phone 200m away. | 1. **Round-Trip Time (RTT) Enforcement:** Challenge transmission to signature verification must execute in $\le 350\text{ ms}$. Internet relay hops exceed this threshold.<br>2. **Android Presence Verification:** Android Keystore key requires device unlock state or biometric verification. | Physical microsecond RF relay within immediate vicinity (<5m) if phone is left unlocked. |
| **TH-04** | **Man-In-The-Middle (MITM)** | Attacker intercepts initial enrollment to substitute their own public key. | **Out-Of-Band (OOB) Pairing via Visual QR Code:** Windows generates an ephemeral ECDH keypair and high-entropy pairing token displayed on screen. Android scans this via physical camera, completing mutual authentication without trusting unauthenticated BLE. | Attacker with physical compromise of PC display or phone camera. |
| **TH-05** | **Private Key Extraction** | Malware on Android attempts to dump the signing key from memory or storage. | Keys are generated inside the **Android Keystore** with `PURPOSE_SIGN`, backed by hardware TEE (ARM TrustZone) or StrongBox (dedicated EAL5+ SE chip). Key material is non-exportable even under root exploits. | Zero-day physical chip decap or micro-probing of hardware security element. |
| **TH-06** | **Credential Stash Theft** | Attacker steals PC SSD or reads registry to obtain the cached Windows password. | The cached credential is encrypted using **Dual-Key Defense**: Windows DPAPI (machine-bound + SYSTEM ACL) + an AES-256-GCM key derived from the phone's enrollment secret. Disk offline extraction yields only high-entropy ciphertext. | Memory dumping of `PhoneKeyService` while it is actively handling an unlock event. Buffer is wiped via `SecureZeroMemory` immediately. |
| **TH-07** | **Denial of Service (Jamming)** | RF noise or pocket obstruction drops BLE packets, locking the PC repeatedly. | **Proximity Hysteresis & Grace Period:** Dual thresholds (Unlock: $\ge -65\text{ dBm}$, Lock: $\le -85\text{ dBm}$). Transient signal drops trigger a 15-second grace period with audible/visual warning before invoking `LockWorkStation()`. | Deliberate continuous broadband RF jamming will keep workstation locked (fail-safe to secure state). |
| **TH-08** | **Local Privilege Escalation** | Low-privileged local Windows user queries service pipe to steal the password. | Named pipe enforces strict Windows Security Descriptor allowing write access only to `SYSTEM` and `LogonUI.exe` tokens. Client PID and process image are validated before responding. | Pre-existing kernel compromise on Windows. |
| **TH-09** | **Phone Theft (Physical)** | Attacker steals victim's phone and walks up to victim's PC. | 1. PhoneKey app checks `KeyguardManager.isDeviceLocked()`. Key signing is rejected if phone is locked.<br>2. High-security mode requires biometric verification (fingerprint/face) for each unlock. | User disables biometric enforcement and leaves phone unlocked in attacker's possession. |
| **TH-10** | **Offline Revocation** | User revokes lost phone on PC while the phone is offline or powered off. | Revocation occurs locally on Windows by wiping the stored public key and zeroing the DPAPI credential stash. Even if the phone is later powered on, its signatures are rejected. | None. Local revocation is immediate and permanent. |

---

## 4. Fundamental Distinctions
* **Cryptographic Authentication:** Confirms that the entity holding the private key corresponding to the enrolled public key has signed the fresh challenge.
* **Possession:** Confirms the physical device containing the TEE key is available.
* **Proximity:** Confirms the physical device is emitting BLE signals within acceptable RSSI range and round-trip latency limits.
* **User Presence:** Confirms human intent (phone is unlocked or biometric was supplied).
* **Local Windows Authorization:** Windows LSA verifies user permissions and creates the interactive token.
