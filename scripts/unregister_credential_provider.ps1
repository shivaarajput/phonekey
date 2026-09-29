<#
.SYNOPSIS
    Unregisters PhoneKey Credential Provider and restores default Windows logon providers.
#>

[CmdletBinding()]
param()

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be executed in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "=== Unregistering PhoneKey Credential Provider ===" -ForegroundColor Yellow

$clsid = "{8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}"

# Remove Credential Provider registration
$cpRegPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$clsid"
if (Test-Path $cpRegPath) {
    Remove-Item -Path $cpRegPath -Force -Recurse
    Write-Host "[+] Removed LogonUI Credential Provider registration" -ForegroundColor Green
}

# Remove COM registration
$clsidPath = "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid"
if (Test-Path $clsidPath) {
    Remove-Item -Path $clsidPath -Force -Recurse
    Write-Host "[+] Removed COM CLSID registration" -ForegroundColor Green
}

# Stop and delete service
$service = Get-Service -Name "PhoneKeyService" -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name "PhoneKeyService" -Force -ErrorAction SilentlyContinue
    sc.exe delete "PhoneKeyService" | Out-Null
    Write-Host "[+] Removed PhoneKey Windows Service" -ForegroundColor Green
}

Write-Host "`n[SUCCESS] PhoneKey unregistered safely. Standard Windows PIN/Password remain fully operational." -ForegroundColor Green
