@echo off
cd /d "C:\Users\Bugs_\Documents\GitHub\My project"
"C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" -batchmode -quit -acceptSoftwareTermsForThisRunOnly -projectPath "C:\Users\Bugs_\Documents\GitHub\My project" -executeMethod OasisBuildTrigger.BuildStandalone -logFile "Logs\BatchBuild.log"
echo ExitCode: %ERRORLEVEL%
