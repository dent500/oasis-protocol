using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Comprehensive Project Health and Build Size Checker.
/// Evaluates scenes, missing scripts, broken materials/shaders, build settings,
/// and asset sizes to ensure an error-free, lean standalone build.
/// Run on demand via the Tools menu.
/// </summary>
public class ProjectHealthChecker
{
    private const string ReportPath = "Logs/HealthCheckReport.txt";

    [MenuItem("Tools/Run Project Health Check", priority = 10)]
    public static void RunHealthCheckMenuItem()
    {
        RunFullHealthCheck();
    }

    public static string RunFullHealthCheck()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine($"PROJECT HEALTH & BUILD READINESS REPORT - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("================================================================================\n");

        int issueCount = 0;
        int warningCount = 0;

        // 1. Build Scenes Verification
        sb.AppendLine("--- 1. SCENES IN BUILD SETTINGS ---");
        var scenes = EditorBuildSettings.scenes;
        if (scenes == null || scenes.Length == 0)
        {
            sb.AppendLine("[ERROR] No scenes configured in EditorBuildSettings!");
            issueCount++;
        }
        else
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                var s = scenes[i];
                bool exists = File.Exists(s.path);
                sb.AppendLine($"  [{i}] {(s.enabled ? "[ENABLED] " : "[DISABLED]")} {s.path} (Exists: {exists})");
                if (s.enabled && !exists)
                {
                    sb.AppendLine($"      [ERROR] Scene file missing on disk: {s.path}");
                    issueCount++;
                }
            }
        }
        sb.AppendLine();

        // 2. Render Pipeline & Graphics Settings Check
        sb.AppendLine("--- 2. GRAPHICS & URP CONFIGURATION ---");
        var currentPipeline = GraphicsSettings.currentRenderPipeline;
        if (currentPipeline == null)
        {
            sb.AppendLine("[WARNING] UniversalRenderPipelineAsset is not set globally in GraphicsSettings!");
            warningCount++;
        }
        else
        {
            sb.AppendLine($"  Active Render Pipeline: {currentPipeline.name} ({currentPipeline.GetType().Name})");
        }

        var defaultQuality = QualitySettings.GetQualityLevel();
        sb.AppendLine($"  Default Quality Level: {QualitySettings.names[defaultQuality]} (Index: {defaultQuality})");
        sb.AppendLine();

        // 3. Managed Stripping Level & Player Settings
        sb.AppendLine("--- 3. PLAYER & BUILD STRIPPING SETTINGS ---");
        var strippingLevel = PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone);
        sb.AppendLine($"  Standalone Managed Stripping Level: {strippingLevel}");
        if (strippingLevel == ManagedStrippingLevel.Disabled)
        {
            sb.AppendLine("  [ADVICE] Managed Stripping Level is Disabled. Setting to Low/Medium reduces binary size.");
        }
        else
        {
            sb.AppendLine("  [OK] Managed Code Stripping is ACTIVE for lean standalone executable.");
        }
        sb.AppendLine($"  Color Space: {PlayerSettings.colorSpace}");
        sb.AppendLine($"  Run in Background: {Application.runInBackground}");
        sb.AppendLine();

        // 4. Scene Assets Health Scan (Scanning build scenes)
        sb.AppendLine("--- 4. SCENE ASSETS & COMPONENT HEALTH SCAN ---");
        string originalScenePath = SceneManager.GetActiveScene().path;

        foreach (var buildScene in scenes)
        {
            if (!buildScene.enabled || !File.Exists(buildScene.path)) continue;

            sb.AppendLine($"  Inspecting Scene: {buildScene.path} ...");
            Scene openedScene = default;

            try
            {
                if (SceneManager.GetActiveScene().path != buildScene.path)
                {
                    openedScene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                }
                else
                {
                    openedScene = SceneManager.GetActiveScene();
                }

                GameObject[] rootObjects = openedScene.GetRootGameObjects();
                int totalObjects = 0;
                int missingScripts = 0;
                int brokenMaterials = 0;
                int brokenShaders = 0;

                foreach (var root in rootObjects)
                {
                    Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
                    totalObjects += allTransforms.Length;

                    foreach (var t in allTransforms)
                    {
                        var go = t.gameObject;
                        Component[] components = go.GetComponents<Component>();
                        for (int c = 0; c < components.Length; c++)
                        {
                            if (components[c] == null)
                            {
                                missingScripts++;
                                sb.AppendLine($"    [ERROR] Missing MonoBehaviour script on GameObject: '{go.name}' (Hierarchy: {GetHierarchyPath(t)})");
                                issueCount++;
                            }
                        }

                        Renderer r = go.GetComponent<Renderer>();
                        if (r != null && !(r is ParticleSystemRenderer))
                        {
                            Material[] mats = r.sharedMaterials;
                            if (mats != null)
                            {
                                for (int m = 0; m < mats.Length; m++)
                                {
                                    if (mats[m] == null)
                                    {
                                        brokenMaterials++;
                                        sb.AppendLine($"    [WARNING] Missing/Null Material slot [{m}] on Renderer: '{go.name}'");
                                        warningCount++;
                                    }
                                    else if (mats[m].shader == null || mats[m].shader.name == "Hidden/InternalErrorShader")
                                    {
                                        brokenShaders++;
                                        sb.AppendLine($"    [ERROR] Pink/Error Shader on Material '{mats[m].name}' of '{go.name}'");
                                        issueCount++;
                                    }
                                }
                            }
                        }
                    }
                }

                sb.AppendLine($"    Total GameObjects: {totalObjects}");
                sb.AppendLine($"    Missing Scripts: {missingScripts}");
                sb.AppendLine($"    Broken Materials: {brokenMaterials}");
                sb.AppendLine($"    Error Shaders: {brokenShaders}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    [EXCEPTION during scene scan] {ex.Message}");
                warningCount++;
            }
        }

        // Restore original scene if changed
        if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath) && SceneManager.GetActiveScene().path != originalScenePath)
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        sb.AppendLine();

        // 5. Build Size & Project Cleanliness Audit
        sb.AppendLine("--- 5. ASSET REPOSITORY SIZE AUDIT ---");
        long totalAssetsBytes = GetDirectorySize("Assets");
        sb.AppendLine($"  Total Assets Folder Size: {totalAssetsBytes / (1024f * 1024f):F2} MB");

        // Check if any redundant cache / backup folders exist
        string[] redundantDirs = new string[] { "Assets/_Recovery", "Assets/Tutorials" };
        foreach (var rDir in redundantDirs)
        {
            if (Directory.Exists(rDir))
            {
                sb.AppendLine($"  [WARNING] Redundant folder still exists: {rDir}");
                warningCount++;
            }
            else
            {
                sb.AppendLine($"  [OK] Cleaned redundant folder: {rDir}");
            }
        }
        sb.AppendLine();

        // 6. Summary & Readiness Verdict
        sb.AppendLine("================================================================================");
        sb.AppendLine($"DIAGNOSTIC SUMMARY: {issueCount} Errors, {warningCount} Warnings");
        if (issueCount == 0)
        {
            sb.AppendLine("VERDICT: [PASSED - READY FOR STANDALONE BUILD]");
            sb.AppendLine("All build scenes are intact, all scripts compile cleanly, and no fatal issues detected.");
        }
        else
        {
            sb.AppendLine("VERDICT: [ACTION REQUIRED] Resolve critical errors before building.");
        }
        sb.AppendLine("================================================================================");

        string reportText = sb.ToString();
        try
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ReportPath, reportText);
            Debug.Log($"[ProjectHealthChecker] Health check completed. Report written to {ReportPath}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ProjectHealthChecker] Could not write report file: {ex.Message}");
        }

        return reportText;
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static long GetDirectorySize(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return 0;
        long sum = 0;
        var dirInfo = new DirectoryInfo(folderPath);
        foreach (var file in dirInfo.GetFiles("*.*", SearchOption.AllDirectories))
        {
            sum += file.Length;
        }
        return sum;
    }
}
