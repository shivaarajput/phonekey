# Windows Credential Provider Architecture & Setup Guide

## 1. What is a Windows Credential Provider?
Starting with Windows Vista and continuing through Windows 10 and 11, the Windows logon UI (`LogonUI.exe`) uses **Credential Providers** (in-process COM DLLs) to acquire credentials from users.

Credential Providers replace the legacy GINA (`msgina.dll`).

### Core COM Interfaces:
* **`ICredentialProvider`**:
  * `SetUsageScenario`: Informs provider whether it is being invoked for `CPUS_LOGON`, `CPUS_UNLOCK_WORKSTATION`, or `CPUS_CHANGE_PASSWORD`.
  * `SetSerialization`: Provides previous credentials if available.
  * `Advise` / `UnAdvise`: Registers the `ICredentialProviderEvents` callback interface.
  * `GetCredentialCount`: Reports how many credential tiles this provider exposes.
  * `GetCredentialAt`: Returns an `ICredentialProviderCredential` pointer for each tile.
* **`ICredentialProviderCredential2`**:
  * `GetFieldState`: Configures which controls (user image, user name, status text, submit button) are visible.
  * `GetStringValue`: Supplies the localized UI strings (e.g. "PhoneKey: Phone in proximity, authenticating...").
  * `GetSerialization`: Returns a packed `KERB_INTERACTIVE_LOGON` or `MSV1_0_INTERACTIVE_LOGON` authentication buffer back to Winlogon/LSA when authentication succeeds.

---

## 2. PhoneKey Credential Provider Design

`PhoneKeyCP.dll` runs in the context of `LogonUI.exe` as `NT AUTHORITY\SYSTEM`.
It does **not** handle BLE directly (since `LogonUI.exe` can be spawned and destroyed at any time).
Instead, it establishes a fast local connection via a **Named Pipe** (`\\.\pipe\PhoneKeyAuthPipe`) to the long-running background service `PhoneKeyService.exe`.

```
 LogonUI.exe (Secure Desktop)
 +---------------------------------------------+
 |  PhoneKeyCP.dll                             |
 |  [ICredentialProvider]                      |
 |  [ICredentialProviderCredential2]           |
 |         |                                   |
 |         | Named Pipe Client                 |
 +---------|-----------------------------------+
           |
           | \\.\pipe\PhoneKeyAuthPipe (Local, DACL restricted to SYSTEM)
           v
 PhoneKeyService.exe (Session 0, SYSTEM)
 +---------------------------------------------+
 |  - BLE Central connection to Android        |
 |  - Continuous RSSI tracking                 |
 |  - Validated Challenge/Response Status      |
 |  - Secure DPAPI Credential Vault            |
 +---------------------------------------------+
```

When the phone is nearby and authenticated:
1. `PhoneKeyService` sends an `AUTH_STATUS_READY` message across the named pipe.
2. `PhoneKeyCP` updates its tile text to: **"PhoneKey: Verified. Tap Unlock or Press Enter"** (or auto-submits if walk-up mode is enabled).
3. Upon submission, `PhoneKeyCP` requests the serialized logon buffer from the service.
4. The service decrypts the DPAPI vault in memory, formats the `KERB_INTERACTIVE_LOGON` struct, transmits it to the Credential Provider, and wipes the plaintext.
5. `PhoneKeyCP` returns `S_OK` from `GetSerialization()`, and Windows LSA logs the user in.

---

## 3. Windows Registry Registration

To register `PhoneKeyCP.dll` in Windows, register its COM CLSID and add it to the active Credential Providers list:

### COM Registration:
```reg
HKEY_CLASSES_ROOT\CLSID\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}
    @ = "PhoneKey Credential Provider"

HKEY_CLASSES_ROOT\CLSID\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}\InprocServer32
    @ = "C:\Program Files\PhoneKey\PhoneKeyCP.dll"
    ThreadingModel = "Apartment"
```

### Windows Authentication Registration:
```reg
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}
    @ = "PhoneKeyCredentialProvider"
```

---

## 4. Safety & Recovery Notice
* PhoneKey is an **additive** Credential Provider.
* The standard Windows Password and Windows Hello PIN providers remain enabled.
* If PhoneKey is uninstalled, unregistered, or the phone battery dies, clicking **"Sign-in options"** on the Windows lock screen displays the standard Windows PIN or Password tile.
