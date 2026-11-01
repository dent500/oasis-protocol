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

$sourceDll = 'Builds\Oasis Protocol_Data\Managed\Assembly-CSharp.dll'
if (Test-Path $sourceDll) {
    $time = (Get-Item $sourceDll).LastWriteTime
    Write-Host "Source Assembly-CSharp.dll timestamp: $time"
    $targets = Get-ChildItem -Path 'Builds\Release' -Recurse -Filter 'Assembly-CSharp.dll'
    foreach ($t in $targets) {
        Copy-Item -Path $sourceDll -Destination $t.FullName -Force
        Write-Host "Synced to $($t.FullName)"
    }
}
