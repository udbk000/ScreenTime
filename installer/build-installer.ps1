$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDir = Join-Path $repoRoot "src\ScreenTime.App\bin\Release\net8.0-windows10.0.19041.0\win-x64"
$outputDir = Join-Path $PSScriptRoot "Output"
$payloadZip = Join-Path $outputDir "payload.zip"
$setupExe = Join-Path $outputDir "ScreenTime-Setup.exe"
$iconPath = Join-Path $repoRoot "src\ScreenTime.App\Assets\app.ico"
$installerCs = Join-Path $PSScriptRoot "ScreenTimeInstaller.cs"
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

Write-Host "1. Erstelle komprimiertes App-Archiv (payload.zip) aus $releaseDir ..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($releaseDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "2. Kompiliere eigenständigen Einzeldatei-Installer ($setupExe) ..."
& $cscPath /target:winexe /out:$setupExe /optimize+ /win32icon:$iconPath /resource:"$payloadZip,payload.zip" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $installerCs

Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
Write-Host "Fertig! Installer erstellt unter: $setupExe"
