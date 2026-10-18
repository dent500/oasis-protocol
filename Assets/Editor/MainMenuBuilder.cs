using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public class MainMenuBuilder : Editor
{
    private const string SceneFolder = "Assets/Scenes/MainMenu";
    private const string ScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
    private const string InputActionsPath = "Assets/SharedAssets/FirstPersonController/InputSystem/InputSystem_Actions.inputactions";

    static MainMenuBuilder()
    {
        EditorApplication.delayCall += CheckAndAutoBuild;
    }

    private static void CheckAndAutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        BuildHomeMenu();
    }

    [MenuItem("GameHub/Build & Open Home Menu Scene", priority = 1)]
    public static void BuildHomeMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[GameHub] Cannot rebuild menu scene during Play Mode. Stop play mode first.");
            return;
        }

        if (!Directory.Exists(SceneFolder))
        {
            Directory.CreateDirectory(SceneFolder);
            AssetDatabase.Refresh();
        }

        // Create new empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Camera
        GameObject camObj = new GameObject("Main Camera");
        Camera cam = camObj.AddComponent<Camera>();
        camObj.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.025f, 0.04f, 0.065f, 1f);
        cam.orthographic = false;
        camObj.transform.position = new Vector3(0, 1, -10);
        camObj.AddComponent<UniversalAdditionalCameraData>();

        // 2. Event System configured with project's InputSystem_Actions
        GameObject eventObj = new GameObject("EventSystem");
        eventObj.AddComponent<EventSystem>();
        InputSystemUIInputModule uiModule = eventObj.AddComponent<InputSystemUIInputModule>();
        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions != null)
        {
            uiModule.actionsAsset = inputActions;
        }
        else
        {
            uiModule.AssignDefaultActions();
        }

        // 3. UI Root Canvas
        GameObject canvasObj = new GameObject("MainMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        MainMenuManager menuManager = canvasObj.AddComponent<MainMenuManager>();
        menuManager.oasisSceneName = "OasisScene";
        menuManager.gardenSceneName = "GardenScene";
        menuManager.cockpitSceneName = "CockpitScene";

        // Build complete styled UI (faded background artwork, styled header, Host & Join cards, Settings & Credits modals)
        menuManager.BuildOrUpgradeHomeMenuUI();

        // Save Scene
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        // Update EditorBuildSettings
        UpdateBuildSettings();

        Debug.Log("<color=cyan><b>[GameHub]</b> Home Menu scene rebuilt with styled header, faded background, Host/Join cards, Settings & Credits at: " + ScenePath + "</color>");
    }

    public static void UpdateBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[2];
        scenes[0] = new EditorBuildSettingsScene(ScenePath, true);
        scenes[1] = new EditorBuildSettingsScene("Assets/Scenes/Oasis/OasisScene.unity", true);

        EditorBuildSettings.scenes = scenes;
        AssetDatabase.SaveAssets();
    }

    [MenuItem("GameHub/Build & Package Game (ZIP for Friends)", priority = 2)]
    public static void BuildStandalonePlayer()
    {
        string buildDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "OasisProtocol");
        if (!Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        string exePath = Path.Combine(buildDir, "OasisProtocol.exe");

        string[] scenes = new string[]
        {
            "Assets/Scenes/MainMenu/MainMenu.unity",
            "Assets/Scenes/Oasis/OasisScene.unity"
        };

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log("<color=cyan><b>[GameHub]</b> Starting build of Oasis Protocol...</color>");
        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        var summary = report.summary;

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"<color=green><b>[GameHub] Build Succeeded!</b> Size: {summary.totalSize / (1024 * 1024)} MB at: {exePath}</color>");

            string zipPath = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "OasisProtocol.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(buildDir, zipPath);
                Debug.Log($"<color=lime><b>[GameHub] Successfully created shareable ZIP package at: {zipPath}</b></color>");
                EditorUtility.RevealInFinder(zipPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[GameHub] Could not auto-zip (" + ex.Message + "). Build folder is ready at: " + buildDir);
                EditorUtility.RevealInFinder(buildDir);
            }
        }
        else
        {
            Debug.LogError($"<color=red><b>[GameHub] Build failed:</b> {summary.totalErrors} errors.</color>");
        }
    }
}
