#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OasisBuildTrigger
{
    private const string TriggerFile = "Temp/trigger_build.txt";
    private const string OpenOasisTrigger = "Temp/open_oasis_scene.txt";

    static OasisBuildTrigger()
    {
        EditorApplication.update += PollTrigger;
    }

    [MenuItem("Oasis/Scenes/Open Oasis Scene (Combat Map)", priority = 10)]
    public static void OpenOasisScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Oasis/OasisScene.unity");
            FocusSceneCameraOnArena();
            Debug.Log("<color=cyan><b>[Oasis]</b> OasisScene successfully opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Scenes/Open Subterra Facility Scene (Indoor Map)", priority = 11)]
    public static void OpenFacilityScene()
    {
        string path = "Assets/Scenes/Facility/FacilityScene.unity";
        if (!File.Exists(path))
        {
            BakeOrUpdateFacilitySceneAsset();
        }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(path);
            var sceneView = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? SceneView.sceneViews[0] as SceneView : null);
            if (sceneView != null)
            {
                sceneView.pivot = new Vector3(0f, 6.0f, 0f);
                sceneView.rotation = Quaternion.Euler(30f, 45f, 0f);
                sceneView.size = 35f;
                sceneView.Repaint();
            }
            Debug.Log("<color=cyan><b>[Oasis]</b> Subterra Facility Scene opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Maps/Bake or Update Facility Scene Asset", priority = 3)]
    public static void BakeOrUpdateFacilitySceneAsset()
    {
        string facilityScenePath = "Assets/Scenes/Facility/FacilityScene.unity";
        string facilityDir = Path.GetDirectoryName(facilityScenePath);
        if (!Directory.Exists(facilityDir)) Directory.CreateDirectory(facilityDir);

        Scene scene;
        bool isNew = !File.Exists(facilityScenePath);
        if (isNew)
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.OpenScene(facilityScenePath, OpenSceneMode.Single);
        }

        // 1. Directional / Ambient light
        GameObject dirLightObj = GameObject.Find("Directional Light");
        if (dirLightObj == null) dirLightObj = new GameObject("Directional Light");
        Light l = dirLightObj.GetComponent<Light>();
        if (l == null) l = dirLightObj.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 0.08f;
        l.color = new Color(0.15f, 0.20f, 0.28f);
        dirLightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 2. Instantiate or find FPS Controller Prefab
        var player = OasisPlayerHelper.FindPlayerTransform();
        if (player == null)
        {
            GameObject fpsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SharedAssets/Prefabs/FPS_Controller_Prefab.prefab");
            if (fpsPrefab != null)
            {
                var instantiatedPlayer = PrefabUtility.InstantiatePrefab(fpsPrefab) as GameObject;
                instantiatedPlayer.name = "FPS_Controller";
                instantiatedPlayer.transform.position = new Vector3(0f, 0.5f, -34f); // South Armory base
                instantiatedPlayer.transform.rotation = Quaternion.identity;
            }
        }

        // 3. Indoor Facility Builder
        var facBuilder = Object.FindAnyObjectByType<IndoorFacilityBuilder>();
        if (facBuilder == null)
        {
            GameObject fbObj = new GameObject("IndoorFacilityBuilder");
            facBuilder = fbObj.AddComponent<IndoorFacilityBuilder>();
        }
        facBuilder.EnsureFacilityBuilt();

        // 4. Set RenderSettings
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.14f, 0.18f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        // 5. Save Scene
        EditorSceneManager.SaveScene(scene, facilityScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // 6. Update EditorBuildSettings
        UpdateBuildSettingsWithScene(facilityScenePath);

        Debug.Log("<color=cyan><b>[Oasis]</b> Subterra Facility Scene asset successfully baked and saved at: " + facilityScenePath + "</color>");
    }

    public static void UpdateBuildSettingsWithScene(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool exists = false;
        foreach (var s in scenes)
        {
            if (s.path.Equals(path, System.StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                s.enabled = true;
                break;
            }
        }
        if (!exists)
        {
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    [MenuItem("Oasis/Reimport All Shaders and Materials", priority = 2)]
    public static void ReimportShadersAndMaterials()
    {
        AssetDatabase.ImportAsset("Assets/Scenes/Oasis/Shaders", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scenes/Oasis/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var sceneView = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? SceneView.sceneViews[0] as SceneView : null);
        if (sceneView != null) sceneView.Repaint();
        Debug.Log("<color=cyan><b>[Oasis]</b> Shaders and materials forcefully reimported.</color>");
    }

    [MenuItem("Oasis/Focus Scene Camera on Combat Arena", priority = 1)]
    public static void FocusSceneCameraOnArena()
    {
        var sceneView = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? SceneView.sceneViews[0] as SceneView : null);
        if (sceneView != null)
        {
            sceneView.pivot = new Vector3(0f, 2.5f, -145f);
            sceneView.rotation = Quaternion.Euler(8f, 180f, 0f);
            sceneView.size = 28f;
            sceneView.Repaint();
            Debug.Log("<color=cyan><b>[Oasis]</b> Scene view camera focused on combat arena.</color>");
        }

        var player = GameObject.FindWithTag("Player") ?? GameObject.Find("FPS_Controller");
        if (player != null)
        {
            Selection.activeGameObject = player;
        }
    }

    [MenuItem("Oasis/Scenes/Open Main Menu", priority = 12)]
    public static void OpenMainMenuScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu/MainMenu.unity");
            Debug.Log("<color=cyan><b>[Oasis]</b> MainMenu successfully opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Build Windows Standalone", priority = 20)]
    public static void BuildStandalone()
    {
        Debug.Log("[OasisBuildTrigger] Starting Windows Standalone build...");

        string[] scenes = new string[]
        {
            "Assets/Scenes/MainMenu/MainMenu.unity",
            "Assets/Scenes/Oasis/OasisScene.unity",
            "Assets/Scenes/Facility/FacilityScene.unity"
        };

        string buildPath = "Builds/Oasis Protocol.exe";
        string buildDir = Path.GetDirectoryName(buildPath);
        if (!string.IsNullOrEmpty(buildDir) && !Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        string resultMsg = $"Result: {report.summary.result}, Errors: {report.summary.totalErrors}, Warnings: {report.summary.totalWarnings}, Time: {report.summary.totalTime.TotalSeconds:F1}s";
        Debug.Log($"[OasisBuildTrigger] Windows build completed with {resultMsg}");
        try
        {
            using (var sw = new StreamWriter("Temp/build_result.txt"))
            {
                sw.WriteLine(resultMsg);
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception || msg.type == LogType.Warning)
                        {
                            sw.WriteLine($"[{msg.type}] {msg.content}");
                        }
                    }
                }
            }
        }
        catch {}
    }

    private static void PollTrigger()
    {
        if (File.Exists(OpenOasisTrigger))
        {
            try { File.Delete(OpenOasisTrigger); } catch {}
            OpenOasisScene();
        }
        else if (File.Exists(TriggerFile))
        {
            try { File.Delete(TriggerFile); } catch {}
            BuildStandalone();
        }
    }
}
#endif
