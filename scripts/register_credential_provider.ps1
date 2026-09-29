<#
.SYNOPSIS
    Installs and registers PhoneKey Windows Service and Credential Provider.
.DESCRIPTION
    Requires elevated Administrator privileges.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "C:\Program Files\PhoneKey"
)

# Enforce Administrator privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be executed in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "=== PhoneKey Installation & Registration ===" -ForegroundColor Cyan

# 1. Create Target Directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# 2. Locate and Copy PhoneKeyCP.dll
$dllCandidates = @(
    (Join-Path $PSScriptRoot "PhoneKeyCP.dll"),
    (Join-Path $PSScriptRoot "..\windows\src\PhoneKey.CredentialProvider\x64\Release\PhoneKeyCP.dll"),
    (Join-Path $PSScriptRoot "..\windows\src\PhoneKey.CredentialProvider\x64\Release\PhoneKey.CredentialProvider.dll")
)

$dllSource = $dllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$dllTarget = Join-Path $InstallDir "PhoneKeyCP.dll"

if ($dllSource) {
    Copy-Item -Path $dllSource -Destination $dllTarget -Force
    Write-Host "[+] Copied PhoneKeyCP.dll to $InstallDir" -ForegroundColor Green
} else {
    Write-Error "PhoneKeyCP.dll not found in $PSScriptRoot. Please ensure PhoneKeyCP.dll is in the same directory as this script."
    exit 1
}

# 3. Register COM InprocServer32
$clsid = "{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}"
$clsidPath = "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid"
$inprocPath = "$clsidPath\InprocServer32"

New-Item -Path $clsidPath -Force | Out-Null
Set-ItemProperty -Path $clsidPath -Name "(default)" -Value "PhoneKey Credential Provider"

New-Item -Path $inprocPath -Force | Out-Null
Set-ItemProperty -Path $inprocPath -Name "(default)" -Value $dllTarget
Set-ItemProperty -Path $inprocPath -Name "ThreadingModel" -Value "Apartment"
Write-Host "[+] Registered COM CLSID $clsid" -ForegroundColor Green

# 4. Register in Windows Authentication Credential Providers
$cpRegPath = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$clsid"
New-Item -Path $cpRegPath -Force | Out-Null
Set-ItemProperty -Path $cpRegPath -Name "(default)" -Value "PhoneKeyCredentialProvider"
Write-Host "[+] Registered Credential Provider in Windows LogonUI" -ForegroundColor Green

# 5. Copy and Register PhoneKey Windows Service
$serviceSource = Join-Path $PSScriptRoot "Service"
$serviceTargetDir = Join-Path $InstallDir "Service"

if (Test-Path $serviceSource) {
    if (-not (Test-Path $serviceTargetDir)) {
        New-Item -ItemType Directory -Path $serviceTargetDir -Force | Out-Null
    }
    Copy-Item -Path "$serviceSource\*" -Destination $serviceTargetDir -Recurse -Force
    Write-Host "[+] Installed PhoneKey Windows Service binaries to $serviceTargetDir" -ForegroundColor Green
}

$serviceExeCandidates = @(
    (Join-Path $serviceTargetDir "PhoneKey.Service.exe"),
    (Join-Path $InstallDir "PhoneKeyService.exe"),
    (Join-Path $InstallDir "PhoneKey.Service.exe")
)
$serviceExe = $serviceExeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($serviceExe) {
    $existing = Get-Service -Name "PhoneKeyService" -ErrorAction SilentlyContinue
    if (-not $existing) {
        New-Service -Name "PhoneKeyService" -BinaryPathName "`"$serviceExe`"" -DisplayName "PhoneKey Proximity Auth Service" -StartupType Automatic | Out-Null
        Write-Host "[+] Created PhoneKey Windows Service" -ForegroundColor Green
    } else {
        sc.exe config "PhoneKeyService" binPath= "`"$serviceExe`"" | Out-Null
    }
    Start-Service -Name "PhoneKeyService" -ErrorAction SilentlyContinue
    Write-Host "[+] Started PhoneKeyService" -ForegroundColor Green
} else {
    Write-Warning "PhoneKey.Service.exe not found in $serviceTargetDir."
}

# 6. Copy UI Dashboard if present
$uiSource = Join-Path $PSScriptRoot "UI"
$uiTargetDir = Join-Path $InstallDir "UI"
if (Test-Path $uiSource) {
    if (-not (Test-Path $uiTargetDir)) {
        New-Item -ItemType Directory -Path $uiTargetDir -Force | Out-Null
    }
    Copy-Item -Path "$uiSource\*" -Destination $uiTargetDir -Recurse -Force
    Write-Host "[+] Installed PhoneKey Desktop UI to $uiTargetDir" -ForegroundColor Green
}

Write-Host "`n[SUCCESS] PhoneKey registered successfully. Standard PIN and Password options remain active." -ForegroundColor Green
