using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Prepares and configures the Robot Hero (Sci-Fi Warrior) asset for Universal Render Pipeline (URP):
/// 1. Configures normal map texture import settings.
/// 2. Creates high-quality URP Lit material with Albedo, Normal, Metallic, Emission, and AO.
/// 3. Generates a modern native Unity 6 AnimatorController from imported FBX animation clips.
/// 4. Builds and stores the ready-to-spawn prefab in Assets/Resources/RobotHero.prefab using PBR_HP_Mesh.fbx.
/// </summary>
[InitializeOnLoad]
public class RobotHeroSetup
{
    private const string TextureFolder = "Assets/SciFiWarriorPBRHPPolyart/Textures";
    private const string AnimationFolder = "Assets/SciFiWarriorPBRHPPolyart/Animations";
    private const string MaterialPath = "Assets/Resources/RobotHero_URP.mat";
    private const string MeshPath = "Assets/SciFiWarriorPBRHPPolyart/Mesh/PBR_HP_Mesh.fbx";
    private const string ControllerPath = "Assets/Resources/RobotHero.controller";
    private const string TargetPrefabPath = "Assets/Resources/RobotHero.prefab";

    static RobotHeroSetup()
    {
        EditorApplication.delayCall += CheckAndSetup;
    }

    private static void CheckAndSetup()
    {
        try
        {
            bool needsSetup = !File.Exists(TargetPrefabPath) || !File.Exists(ControllerPath);
            if (!needsSetup)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (ctrl == null || ctrl.layers.Length == 0)
                {
                    needsSetup = true;
                }
                else
                {
                    bool hasDie = false;
                    foreach (var s in ctrl.layers[0].stateMachine.states)
                    {
                        if (s.state.name == "Die") { hasDie = true; break; }
                    }
                    if (!hasDie) needsSetup = true;
                }
            }

            if (needsSetup)
            {
                SetupCharacter();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[RobotHeroSetup] Setup deferred: {ex.Message}");
        }
    }

    [MenuItem("Tools/Setup Robot Hero Character", priority = 1)]
    public static void SetupCharacterMenuItem()
    {
        SetupCharacter();
    }

    public static void SetupCharacter()
    {
        Debug.Log("[RobotHeroSetup] Starting Robot Hero URP & Unity 6 setup...");

        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
        }

        // 1. Configure Normal Map import settings
        string normalMapPath = $"{TextureFolder}/PBR_Free_NM.png";
        if (File.Exists(normalMapPath))
        {
            TextureImporter importer = AssetImporter.GetAtPath(normalMapPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        // 2. Ensure URP Lit Material exists
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (urpLit == null) urpLit = Shader.Find("Standard");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(urpLit);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else
        {
            mat.shader = urpLit;
        }

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/PBR_Albedo.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalMapPath);
        Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/PBR_Free_MS.png");
        Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/PBR_Free_EM.png");
        Texture2D occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/PBR_Free_AO.png");

        if (albedo != null)
        {
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
        }
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (metallic != null)
        {
            mat.SetTexture("_MetallicGlossMap", metallic);
            mat.SetFloat("_Metallic", 1.0f);
            mat.SetFloat("_Smoothness", 0.85f);
        }
        if (emission != null)
        {
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", Color.white * 1.5f);
            mat.EnableKeyword("_EMISSION");
        }
        if (occlusion != null)
        {
            mat.SetTexture("_OcclusionMap", occlusion);
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        // 3. Create or update Unity 6 native AnimatorController
        AnimatorController controller = CreateOrGetAnimatorController();

        // 4. Instantiate Mesh FBX
        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath);
        if (fbxAsset == null)
        {
            Debug.LogError($"[RobotHeroSetup] Mesh FBX not found at {MeshPath}!");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
        instance.name = "RobotHero";

        // Assign URP material
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.sharedMaterial = mat;
        }

        // Attach Animator and Avatar
        Animator anim = instance.GetComponent<Animator>();
        if (anim == null) anim = instance.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        // Ensure Avatar is mapped (Humanoid Avatar from Polyart_Mesh.fbx or PBR_HP_Mesh.fbx)
        Avatar avatar = null;
        Animator fbxAnim = fbxAsset.GetComponent<Animator>();
        if (fbxAnim != null && fbxAnim.avatar != null)
        {
            avatar = fbxAnim.avatar;
        }
        if (avatar == null)
        {
            // Try loading avatar from Polyart_Mesh.fbx (the primary humanoid rig source)
            var polyAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/SciFiWarriorPBRHPPolyart/Mesh/Polyart_Mesh.fbx");
            foreach (var a in polyAssets)
            {
                if (a is Avatar av) { avatar = av; break; }
            }
        }
        if (avatar == null)
        {
            var pbrAssets = AssetDatabase.LoadAllAssetsAtPath(MeshPath);
            foreach (var a in pbrAssets)
            {
                if (a is Avatar av) { avatar = av; break; }
            }
        }
        if (avatar != null)
        {
            anim.avatar = avatar;
        }

        // Save as clean Unity 6 prefab in Assets/Resources/RobotHero.prefab
        PrefabUtility.SaveAsPrefabAsset(instance, TargetPrefabPath);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RobotHeroSetup] Clean Unity 6 RobotHero prefab successfully created at {TargetPrefabPath}!");
    }

    private static AnimatorController CreateOrGetAnimatorController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        var rootStateMachine = controller.layers[0].stateMachine;

        // Map required state names to their FBX files
        var states = new (string stateName, string fbxFile)[]
        {
            ("Idle_Shoot_Ar", "Idle_Shoot_ar.fbx"),
            ("WalkFront_Shoot_AR", "WalkFront_Shoot_ar.fbx"),
            ("Run_guard_AR", "Run_guard_AR.fbx"),
            ("Idle_Ducking_AR", "Idle_Ducking_ar.fbx"),
            ("Shoot_Autoshot_AR", "Shoot_AutoShot_AR.fbx"),
            ("Jump", "Jump.fbx"),
            ("Reload", "Reload.fbx"),
            ("Die", "Die.fbx")
        };

        AnimatorState defaultState = null;

        foreach (var (stateName, fbxFile) in states)
        {
            AnimationClip clip = LoadAnimationClip($"{AnimationFolder}/{fbxFile}");
            if (clip == null) continue;

            // Find existing state or add new
            AnimatorState state = null;
            foreach (var childState in rootStateMachine.states)
            {
                if (childState.state.name == stateName)
                {
                    state = childState.state;
                    break;
                }
            }

            if (state == null)
            {
                state = rootStateMachine.AddState(stateName);
            }

            state.motion = clip;

            if (stateName == "Idle_Shoot_Ar")
            {
                defaultState = state;
            }
        }

        if (defaultState != null)
        {
            rootStateMachine.defaultState = defaultState;
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip LoadAnimationClip(string fbxPath)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var obj in assets)
        {
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }
}
