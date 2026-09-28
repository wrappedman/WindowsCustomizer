# Windows build script: build + test + publish single-file EXE.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Configuration Release] [-SelfContained]
param(
    [string]$Configuration = "Release",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "dist\win-x64"
if ($SelfContained) { $Out = Join-Path $Root "dist\win-x64-sc" }

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore (Join-Path $Root "WindowsCustomizer.sln")

Write-Host "==> Build" -ForegroundColor Cyan
dotnet build (Join-Path $Root "WindowsCustomizer.sln") -c $Configuration --no-restore

Write-Host "==> Test" -ForegroundColor Cyan
dotnet test (Join-Path $Root "tests\WindowsCustomizer.Core.Tests\WindowsCustomizer.Core.Tests.csproj") -c $Configuration --no-build

Write-Host "==> Publish (single-file, win-x64)" -ForegroundColor Cyan
$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish (Join-Path $Root "src\WindowsCustomizer.App\WindowsCustomizer.App.csproj") `
    -c $Configuration -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $Out

Write-Host ""
Write-Host "Done. Output: $Out\WindowsCustomizer.exe" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host "(Framework-dependent: target machine needs .NET 8 Desktop Runtime)"
}
