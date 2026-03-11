param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "..\\App1\\ShellCommandManager.csproj"
$iss = Join-Path $PSScriptRoot "ShellCommandManager.iss"

Write-Host "Building app ($Configuration|$Platform)..."
dotnet build $project -c $Configuration -p:Platform=$Platform

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    throw "Inno Setup compiler (iscc) not found. Install Inno Setup first, then run this script again."
}

Write-Host "Compiling installer..."
& $iscc.Source $iss

Write-Host "Done. Output: $root\\out\\ShellCommandManager-setup-x64.exe"
