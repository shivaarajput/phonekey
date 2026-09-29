# PhoneKey BLE Protocol Specification v1.0

## 1. BLE GATT Architecture

### 1.1 Service & Characteristic UUIDs
* **PhoneKey Service UUID:** `0000FEE0-7068-6F6E-656B-657900000001` (128-bit custom service)
* **Characteristics:**
  * **Protocol Version & Capabilities:** `0000FEE1-7068-6F6E-656B-657900000001`
    * Properties: `READ`
    * Permissions: Open
  * **Challenge / Response Exchange:** `0000FEE2-7068-6F6E-656B-657900000001`
    * Properties: `WRITE`, `INDICATE`
    * Permissions: Authenticated / Encrypted
  * **Heartbeat & Telemetry:** `0000FEE3-7068-6F6E-656B-657900000001`
    * Properties: `READ`, `NOTIFY`
    * Permissions: Open
  * **Enrollment & Provisioning:** `0000FEE4-7068-6F6E-656B-657900000001`
    * Properties: `WRITE`
    * Permissions: Encrypted via OOB Shared Secret

---

## 2. Wire Packet Definitions

All multi-byte numeric fields are encoded in **Big-Endian** (Network Byte Order).

### 2.1 AUTH_CHALLENGE_REQ (Windows -> Android)
Sent by Windows Central to trigger an authentication cycle.

| Field Name | Type / Length | Description |
| :--- | :--- | :--- |
| `Magic` | `uint16` (2 bytes) | Protocol Identifier: `0x504B` ('PK') |
| `Version` | `uint8` (1 byte) | Protocol Version: `0x01` |
| `OpCode` | `uint8` (1 byte) | Operation Code: `0x10` (`AUTH_CHALLENGE_REQ`) |
| `PcId` | `uint8[16]` (16 bytes) | Unique persistent GUID of the Windows PC |
| `NoncePc` | `uint8[32]` (32 bytes) | Cryptographically secure random 256-bit challenge |
| `TimestampUtc` | `uint64` (8 bytes) | Unix Epoch timestamp in milliseconds |
| `PolicyFlags` | `uint8` (1 byte) | Bit 0: Require Biometrics; Bit 1: Require Unlocked Screen |
| `Hmac` | `uint8[32]` (32 bytes) | HMAC-SHA256 over above fields using pairing-derived session key |

**Total Size:** 92 bytes (fits in single BLE MTU after MTU request to 247/512 bytes).

### 2.2 AUTH_CHALLENGE_RESP (Android -> Windows)
Indicated by Android Peripheral upon processing the challenge.

| Field Name | Type / Length | Description |
| :--- | :--- | :--- |
| `Magic` | `uint16` (2 bytes) | Protocol Identifier: `0x504B` ('PK') |
| `Version` | `uint8` (1 byte) | Protocol Version: `0x01` |
| `OpCode` | `uint8` (1 byte) | Operation Code: `0x11` (`AUTH_CHALLENGE_RESP`) |
| `StatusCode` | `uint8` (1 byte) | `0x00`: Success; `0x01`: Device Locked; `0x02`: User Cancelled; `0x03`: Hardware Error |
| `NoncePhone` | `uint8[32]` (32 bytes) | Phone-generated 256-bit entropy |
| `HardwareFlag` | `uint8` (1 byte) | `0x01`: Software Key; `0x02`: TEE Backed; `0x03`: StrongBox Backed |
| `BatteryLevel` | `uint8` (1 byte) | Battery percentage (0-100) |
| `SignatureLength`| `uint16` (2 bytes) | Length of following ECDSA signature ($N \approx 64$ to $72$) |
| `Signature` | `uint8[N]` (Variable) | IEEE P1363 ($r \| s$) or DER-encoded ECDSA-SHA256 signature |

### 2.3 Signable Payload Construction
The phone signs the SHA-256 digest of:
```
SignableBuffer = "PhoneKey-v1-Auth" || PcId || NoncePc || TimestampUtc || NoncePhone
```

### 2.4 HEARTBEAT_PACKET (Windows <-> Android)
Lightweight telemetry packet transmitted every 1.5 seconds to measure connection health and monitor RSSI.

| Field Name | Type / Length | Description |
| :--- | :--- | :--- |
| `Sequence` | `uint32` (4 bytes) | Incrementing packet sequence counter |
| `StatusFlags` | `uint8` (1 byte) | Bit 0: Screen On; Bit 1: User Present; Bit 2: Charging |
| `Reserved` | `uint8[3]` (3 bytes) | Alignment padding |

---

## 3. Out-Of-Band (OOB) Enrollment Protocol

```
    WINDOWS PC                                             ANDROID PHONE
+------------------+                                   +-------------------+
| 1. Generate      |                                   |                   |
|    - PcId (GUID) |                                   |                   |
|    - Ephemeral   |                                   |                   |
|      ECDH Key    |                                   |                   |
|    - Salt (32B)  |                                   |                   |
| 2. Display QR    |                                   |                   |
|    Code on screen| === [ Optical Camera Scan ] =====>| 3. Read QR Code   |
+------------------+                                   |    Parse PC info  |
                                                       | 4. Generate Phone |
                                                       |    NIST P-256 Key |
                                                       |    in Keystore    |
                                                       | 5. Derive Shared  |
                                                       |    Secret via     |
                                                       |    ECDH + HKDF    |
                                                       +-------------------+
                                                                 |
                                                                 | 6. ENROLL_REQ (BLE)
                                                                 |    - Phone Public Key
                                                                 |    - Device Name
                                                                 |    - Attestation cert
                                                                 |    - Encrypted with
                                                                 |      derived secret
                                                                 v
+------------------+
| 7. Complete      |
|    ECDH & HKDF   |
| 8. Verify Phone  |
|    Key & Cert    |
| 9. Save to Vault |
| 10. Show 6-digit |
|     Confirm Code | <==== [ Visual Verification by User ] ====> [ Match Code ]
+------------------+
```

---

## 4. Proximity State Machine

```
     +---------------------------------------------------------+
     |                      DISCONNECTED                       |
     +---------------------------------------------------------+
           |                                              ^
           | BLE Advertisement Detected                   | Timeout (15s)
           v                                              |
     +---------------------------------------------------------+
     |                       CONNECTING                        |
     +---------------------------------------------------------+
           |                                              |
           | Connected & MTU Exchanged                    | Handshake Fail
           v                                              |
     +---------------------------------------------------------+  |
     |                   AUTHENTICATING                        |  |
     |  (Challenge sent, RTT timer running, sig check)         |  |
     +---------------------------------------------------------+  |
           |                                              |       |
           | Valid Signature && RTT <= 350ms              | Fail  |
           v                                              v       |
     +---------------------------------------------------------+  |
     |                     AUTHENTICATED                       |  |
     +---------------------------------------------------------+  |
           |                                                      |
           +----------------------+                               |
           |                      |                               |
           v                      v                               |
+---------------------+ +----------------------+                  |
|     IN_PROXIMITY    | |     OUT_OF_RANGE     |                  |
| (RSSI >= -65 dBm)   | |  (RSSI <= -85 dBm)   |                  |
| Eligible for Unlock | | Start Grace Timer    |                  |
+---------------------+ +----------------------+                  |
           |                      |                               |
           |                      v                               |
           |             +---------------------+                  |
           |             |    GRACE_PERIOD     |                  |
           |             |   (e.g., 15s wait)  |                  |
           |             +---------------------+                  |
           |                      |                               |
           |                      | Timer Expired                 |
           |                      v                               |
           |             +---------------------+                  |
           +------------>|   LOCK_TRIGGERED    |------------------+
                         | (LockWorkStation()) |
                         +---------------------+
```
