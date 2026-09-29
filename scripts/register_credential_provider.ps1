<#
.SYNOPSIS
    Installs and registers PhoneKey Windows Service and Credential Provider.
.DESCRIPTION
    Requires elevated Administrator privileges.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "C:\Program Files\PhoneKey",
    [string]$BuildOutputDir = "..\windows\src\PhoneKey.CredentialProvider\x64\Release"
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

# 2. Copy Binaries
$dllSource = Join-Path $PSScriptRoot "$BuildOutputDir\PhoneKeyCP.dll"
$dllTarget = Join-Path $InstallDir "PhoneKeyCP.dll"

if (Test-Path $dllSource) {
    Copy-Item -Path $dllSource -Destination $dllTarget -Force
    Write-Host "[+] Copied PhoneKeyCP.dll to $InstallDir" -ForegroundColor Green
} else {
    Write-Warning "PhoneKeyCP.dll not found in $dllSource. Make sure to build the C++ project in Release|x64."
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

# 5. Register and Start PhoneKey Windows Service
$serviceExe = Join-Path $InstallDir "PhoneKeyService.exe"
if (Test-Path $serviceExe) {
    $existing = Get-Service -Name "PhoneKeyService" -ErrorAction SilentlyContinue
    if (-not $existing) {
        New-Service -Name "PhoneKeyService" -BinaryPathName $serviceExe -DisplayName "PhoneKey Proximity Auth Service" -StartupType Automatic | Out-Null
        Write-Host "[+] Created PhoneKey Windows Service" -ForegroundColor Green
    }
    Start-Service -Name "PhoneKeyService" -ErrorAction SilentlyContinue
    Write-Host "[+] Started PhoneKeyService" -ForegroundColor Green
}

Write-Host "`n[SUCCESS] PhoneKey registered successfully. Standard PIN and Password options remain active." -ForegroundColor Green
