$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "1. Building Unity Standalone Windows 64..."
Write-Host "========================================"

powershell -ExecutionPolicy Bypass -File .\build_game.ps1

$sourceDir = (Resolve-Path "Builds\Release\OasisProtocol-v1.1.0-Win64").Path
$version = "v1.1.5"
$zipName = "Oasis-Protocol-$version-Windows.zip"
$destZip = Join-Path (Resolve-Path "Builds\Release").Path $zipName

Write-Host "========================================"
Write-Host "2. Packaging $destZip with POSIX forward slashes..."
Write-Host "========================================"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path $destZip) {
    Remove-Item $destZip -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($destZip, [System.IO.Compression.ZipArchiveMode]::Create)

$allFiles = Get-ChildItem -Path $sourceDir -Recurse | Where-Object { -not $_.PSIsContainer }
foreach ($file in $allFiles) {
    $relPath = $file.FullName.Substring($sourceDir.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relPath, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}

$zip.Dispose()

$zipItem = Get-Item $destZip
$sizeMB = [math]::Round($zipItem.Length / 1MB, 2)
Write-Host "Standard forward-slash zip successfully created at: $destZip ($sizeMB MB)"

Write-Host "========================================"
Write-Host "3. Verifying Zip Entries..."
Write-Host "========================================"
$check = [System.IO.Compression.ZipFile]::OpenRead($destZip)
Write-Host "Total entries in zip: $($check.Entries.Count)"
$check.Entries | Select-Object -First 10 | ForEach-Object { Write-Host " - $($_.FullName)" }
$check.Dispose()
