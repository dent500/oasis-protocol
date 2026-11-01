$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$project = 'c:\Users\Bugs_\Documents\GitHub\My project'
$log = 'c:\Users\Bugs_\Documents\GitHub\My project\build.log'

Write-Host "Starting Unity Standalone Build via Start-Process -Wait..."
$p = Start-Process -FilePath $unity -ArgumentList "-quit -batchmode -projectPath `"$project`" -executeMethod OasisBuildTrigger.BuildStandalone -logFile `"$log`"" -Wait -PassThru -NoNewWindow
Write-Host "Unity exited with code: $($p.ExitCode)"

if (Test-Path 'Temp\build_result.txt') {
    Write-Host "Build Result:"
    Get-Content 'Temp\build_result.txt'
}

$releaseTarget = 'Builds\Release\OasisProtocol-v1.1.0-Win64'
Write-Host "Syncing full standalone build to $releaseTarget..."
if (-not (Test-Path $releaseTarget)) {
    New-Item -ItemType Directory -Path $releaseTarget -Force | Out-Null
}

# Copy executable and core runtime files
$coreFiles = @(
    'Oasis Protocol.exe',
    'UnityPlayer.dll',
    'UnityCrashHandler64.exe',
    'dstorage.dll',
    'dstoragecore.dll'
)
foreach ($f in $coreFiles) {
    $src = Join-Path 'Builds' $f
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination (Join-Path $releaseTarget $f) -Force
    }
}

# Copy directories: MonoBleedingEdge, D3D12, Oasis Protocol_Data
$dirs = @('MonoBleedingEdge', 'D3D12', 'Oasis Protocol_Data')
foreach ($d in $dirs) {
    $srcDir = Join-Path 'Builds' $d
    $dstDir = Join-Path $releaseTarget $d
    if (Test-Path $srcDir) {
        Copy-Item -Path $srcDir -Destination $releaseTarget -Recurse -Force
        Write-Host "Synced directory: $d"
    }
}

Write-Host "Complete standalone release sync finished!"
