<#
.SYNOPSIS
    Builds the PhoneKey solution and runs unit tests.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

Write-Host "=== Building PhoneKey Windows Projects ($Configuration) ===" -ForegroundColor Cyan

# 1. Build .NET Core Library, Service, and WPF UI
$slnPath = Join-Path $PSScriptRoot "..\windows\PhoneKey.sln"
dotnet build $slnPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build failed!"
    exit $LASTEXITCODE
}

Write-Host "`n=== Running PhoneKey Core Security & Cryptographic Tests ===" -ForegroundColor Cyan
$testProj = Join-Path $PSScriptRoot "..\tests\PhoneKey.Core.Tests\PhoneKey.Core.Tests.csproj"
dotnet test $testProj -c $Configuration --logger "console;verbosity=normal"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit tests failed!"
    exit $LASTEXITCODE
}

Write-Host "`n[SUCCESS] All builds and cryptographic test validations passed." -ForegroundColor Green
