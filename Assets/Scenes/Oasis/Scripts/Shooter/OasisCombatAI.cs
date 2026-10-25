using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Autonomous Humanoid AI Combatant for Oasis Team Battles (2v2, 3v3, 4v4).
/// Features random operative chassis selection, grounded dune locomotion,
/// intelligent target acquisition across opposing team members (AI & players),
/// spine tracking, rifle fire, colored laser tracers, overhead health bars, and respawns.
/// </summary>
public class OasisCombatAI : MonoBehaviour
{
    [Header("Team & Identity")]
    public int teamId = 1; // 0 = Team Alpha (Blue), 1 = Team Bravo (Red)
    public string botName = "Operative";
    public string operativeClassId = "heavy_warrior";
    public Color teamColor;

    [Header("Combat Stats")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float maxShield = 50f;
    public float currentShield = 50f;
    public float attackRange = 36f;
    public float fireRate = 0.22f;
    public float damagePerShot = 12f;
    public float moveSpeed = 4.2f;

    [Header("Match Performance")]
    public int kills = 0;
    public int score = 0;

    // Components
    private CharacterController _characterController;
    private Animator _animator;
    private Transform _spineBone;
    private Transform _gunArmBone;
    private Transform _visualRoot;
    private AudioSource _audioSource;

    // Overhead UI & 3D Team Beacon
    private GameObject _overheadCanvasObj;
    private Slider _overheadHealthSlider;
    private Slider _overheadShieldSlider;
    private Text _overheadNameText;
    private GameObject _overheadBeaconObj;
    private Material _overheadBeaconMat;

    // Targeting & Navigation
    private Transform _currentTarget;
    private float _retargetTimer = 0f;
    private float _nextFireTime = 0f;
    private float _burstTimer = 0f;
    private int _burstShotsRemaining = 0;
    private float _strafeDir = 1f;
    private float _verticalVelocity = 0f;
    private bool _isDead = false;
    public bool isDead => _isDead;

    [Header("Downed & Revival System")]
    public bool isDowned = false;
    public float downedTimer = 10.0f;
    public float reviveProgress = 0.0f;
    private int _lastAttackerId = 0;
    private int _lastAttackerTeamId = -1;

    private Transform _downedAllyTarget = null;
    private OasisCombatAI _downedAllyBot = null;
    private bool _targetIsDownedPlayer = false;

    [Header("Weapon Magazine & Tactical Reload")]
    public int maxAmmo = 16;
    public int currentAmmo = 16;
    public bool isReloading = false;
    public float reloadDuration = 2.0f;
    private float _reloadTimer = 0f;

    // Dynamic Locomotion, Anti-Stuck Tracking & Obstacle Vaulting
    [Header("Anti-Stuck & Obstacle Jumping")]
    public bool enableObstacleJump = true;
    public float obstacleJumpForce = 6.2f;
    private float _jumpCooldownTimer = 0f;
    private Vector3 _lastSampledPos;
    private float _stuckTimer = 0f;
    private Vector3 _evasiveJitter = Vector3.zero;
    private float _maneuverTimer = 0f;
    private int _currentManeuver = 0; // 0 = Aggressive Advance, 1 = Lateral Flank, 2 = Tactical Reposition
    private float _intendedMoveSpeed = 0f;

    // Blaster Muzzle
    private Transform _muzzleTransform;
    public float stunTimer = 0f;
    public void ApplyEMPStun(float duration)
    {
        stunTimer = Mathf.Max(stunTimer, duration);
        currentShield = Mathf.Max(0f, currentShield - 50f);
        if (_overheadShieldSlider != null) _overheadShieldSlider.value = currentShield;
        BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, Vector3.up, false);
    }

    // List of active AI bots for cross-targeting
    public static readonly List<OasisCombatAI> ActiveBots = new List<OasisCombatAI>();

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            _characterController = gameObject.AddComponent<CharacterController>();
        }
        _characterController.height = 1.85f;
        _characterController.radius = 0.52f;
        _characterController.center = new Vector3(0f, 0.92f, 0f);
        _characterController.stepOffset = 0.65f;
        _characterController.slopeLimit = 65f;
        _characterController.skinWidth = 0.05f;
        _characterController.minMoveDistance = 0f;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 1.0f;
        _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        _audioSource.minDistance = 2.0f;
        _audioSource.maxDistance = 24.0f;
    }

    private void OnEnable()
    {
        if (!ActiveBots.Contains(this)) ActiveBots.Add(this);
    }

    private void OnDisable()
    {
        ActiveBots.Remove(this);
    }

    public void InitializeBot(int team, string name, string classId, Texture2D skinTex, Color skinAccent)
    {
        this.teamId = team;
        this.botName = name;
        this.operativeClassId = classId;
        this.teamColor = OasisShooterManager.GetTeamColor(team);

        currentHealth = maxHealth;
        currentShield = maxShield;
        _isDead = false;

        BuildHumanoidVisual(skinTex, skinAccent);
        BuildOverheadHealthBar();
        BuildOverheadBeacon();

        OasisAITelemetryLogger.Instance?.RecordBotSpawn(this);
    }

    private void BuildHumanoidVisual(Texture2D skinTex, Color skinAccent)
    {
        // 1. Instantiate 3D Robot Hero character model
        GameObject prefab = Resources.Load<GameObject>("RobotHero");
#if UNITY_EDITOR
        if (prefab == null)
        {
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab");
        }
#endif
        GameObject visual = null;
        if (prefab != null)
        {
            visual = Instantiate(prefab, transform, false);
            visual.name = "AI_VisualMesh";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            _visualRoot = visual.transform;

            // Strip colliders from visual child hierarchy so CharacterController handles collision
            var cols = visual.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols) Destroy(c);

            // 2. Setup Animator
            RuntimeAnimatorController animCtrl = Resources.Load<RuntimeAnimatorController>("RobotHero");
            if (animCtrl == null) animCtrl = Resources.Load<RuntimeAnimatorController>("SciFiWarrior");
#if UNITY_EDITOR
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/RobotHero.controller");
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/SciFiWarriorPBRHPPolyart/Animators/SciFiWarrior.controller");
#endif
            _animator = visual.GetComponent<Animator>();
            if (_animator == null) _animator = visual.AddComponent<Animator>();
            if (_animator != null && animCtrl != null)
            {
                _animator.runtimeAnimatorController = animCtrl;
                _animator.applyRootMotion = false;
            }

            // 3. Find spine & gun arm bones for aim pitch tracking
            var allBones = visual.GetComponentsInChildren<Transform>(true);
            foreach (var b in allBones)
            {
                if (b.name == "ArmPosition_Right") _gunArmBone = b;
                if (b.name == "Chest" && _spineBone == null) _spineBone = b;
                if (b.name == "Spine" && _spineBone == null) _spineBone = b;
            }

            // 4. Apply Material with selected operative skin and team accent
            Material baseMat = Resources.Load<Material>("RobotHero_URP");
#if UNITY_EDITOR
            if (baseMat == null) baseMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/SciFiWarriorPBRHPPolyart/Materials/RobotHero_URP.mat");
#endif
            if (baseMat == null) baseMat = new Material(OasisShaderHelper.GetLitShader());
            Material customMat = new Material(baseMat);
            customMat.name = $"{botName}_Mat";

            if (skinTex != null)
            {
                if (customMat.HasProperty("_BaseMap")) customMat.SetTexture("_BaseMap", skinTex);
                if (customMat.HasProperty("_MainTex")) customMat.SetTexture("_MainTex", skinTex);
                customMat.color = Color.white;
            }
            else
            {
                customMat.color = skinAccent;
            }

            // Give team color glow on emissions
            if (customMat.HasProperty("_EmissionColor"))
            {
                customMat.EnableKeyword("_EMISSION");
                customMat.SetColor("_EmissionColor", teamColor * 1.5f);
            }

            var rends = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                r.sharedMaterial = customMat;
            }

            // 5. Attach 3D Blaster Rifle to the right gun arm bone
            Transform attachParent = _gunArmBone != null ? _gunArmBone : visual.transform;
            GameObject rifle = BuildProceduralRifle(attachParent);
            _muzzleTransform = rifle.transform.Find("Muzzle");
        }
        else
        {
            // Procedural humanoid fallback mesh to guarantee 100% visibility in any runtime state
            visual = new GameObject("AI_ProceduralHumanoid");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;
            _visualRoot = visual.transform;

            Material bodyMat = new Material(OasisShaderHelper.GetLitShader());
            bodyMat.color = teamColor;
            if (bodyMat.HasProperty("_EmissionColor"))
            {
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", teamColor * 1.2f);
            }

            // Torso
            GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.transform.SetParent(visual.transform, false);
            torso.transform.localPosition = new Vector3(0, 1.1f, 0);
            torso.transform.localScale = new Vector3(0.55f, 0.75f, 0.35f);
            Destroy(torso.GetComponent<Collider>());
            torso.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // Head / Helmet
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(visual.transform, false);
            head.transform.localPosition = new Vector3(0, 1.7f, 0);
            head.transform.localScale = new Vector3(0.35f, 0.38f, 0.35f);
            Destroy(head.GetComponent<Collider>());
            head.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // Legs
            GameObject leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftLeg.transform.SetParent(visual.transform, false);
            leftLeg.transform.localPosition = new Vector3(-0.16f, 0.45f, 0);
            leftLeg.transform.localScale = new Vector3(0.16f, 0.45f, 0.16f);
            Destroy(leftLeg.GetComponent<Collider>());
            leftLeg.GetComponent<Renderer>().sharedMaterial = bodyMat;

            GameObject rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightLeg.transform.SetParent(visual.transform, false);
            rightLeg.transform.localPosition = new Vector3(0.16f, 0.45f, 0);
            rightLeg.transform.localScale = new Vector3(0.16f, 0.45f, 0.16f);
            Destroy(rightLeg.GetComponent<Collider>());
            rightLeg.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // Rifle
            GameObject rifle = BuildProceduralRifle(visual.transform);
            rifle.transform.localPosition = new Vector3(0.28f, 1.05f, 0.4f);
            _muzzleTransform = rifle.transform.Find("Muzzle");
        }
    }

    private GameObject BuildProceduralRifle(Transform parent)
    {
        GameObject gun = new GameObject("AI_BlasterRifle");
        gun.transform.SetParent(parent, false);
        gun.transform.localPosition = new Vector3(0.08f, -0.05f, 0.18f);
        gun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        gun.transform.localScale = Vector3.one * 0.85f;

        Material gunMat = new Material(OasisShaderHelper.GetLitShader());
        gunMat.color = new Color(0.12f, 0.15f, 0.20f);

        Material barrelMat = new Material(OasisShaderHelper.GetLitShader());
        barrelMat.color = teamColor;
        if (barrelMat.HasProperty("_EmissionColor"))
        {
            barrelMat.EnableKeyword("_EMISSION");
            barrelMat.SetColor("_EmissionColor", teamColor * 1.5f);
        }

        // Receiver Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(gun.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, 0f);
        body.transform.localScale = new Vector3(0.12f, 0.14f, 0.45f);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial = gunMat;

        // Barrel
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.transform.SetParent(gun.transform, false);
        barrel.transform.localPosition = new Vector3(0f, 0.02f, 0.32f);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        barrel.transform.localScale = new Vector3(0.06f, 0.22f, 0.06f);
        Destroy(barrel.GetComponent<Collider>());
        barrel.GetComponent<Renderer>().sharedMaterial = barrelMat;

        // Muzzle Tip
        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(gun.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.02f, 0.54f);

        return gun;
    }

    private static Sprite s_ArrowSprite;
    public static Sprite GetOverheadArrowSprite()
    {
        if (s_ArrowSprite != null) return s_ArrowSprite;

        int w = 64;
        int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0, 0, 0, 0);
        Color white = Color.white;
        Color border = new Color(0.05f, 0.05f, 0.05f, 1f);

        for (int y = 0; y < h; y++)
        {
            float ny = (float)y / (h - 1);
            float t = Mathf.Clamp01((ny - 0.06f) / 0.88f);
            float outerHalfWidth = Mathf.Lerp(0.04f, 0.44f, t);

            for (int x = 0; x < w; x++)
            {
                float nx = (float)x / (w - 1);
                float dx = Mathf.Abs(nx - 0.5f);

                float notchY = 0.40f + (dx / 0.44f) * 0.48f;

                if (ny >= 0.06f && ny <= 0.94f && dx <= outerHalfWidth && ny <= notchY)
                {
                    bool isBorder = (dx >= outerHalfWidth - 0.035f) || 
                                   (ny <= 0.10f) || 
                                   (ny >= notchY - 0.035f);

                    tex.SetPixel(x, y, isBorder ? border : white);
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        tex.Apply();
        s_ArrowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        return s_ArrowSprite;
    }

    private static Mesh s_ArrowMesh;
    private static Mesh GetOverheadArrowMesh()
    {
        if (s_ArrowMesh != null) return s_ArrowMesh;

        Mesh m = new Mesh();
        m.name = "ProceduralOverheadArrow";

        // Sleek downward pointing faceted diamond/arrow
        // Tip at bottom (0, 0, 0)
        // 4 corners of waist (height = 0.32m, radius = 0.18m)
        // Top crown point (height = 0.45m)
        Vector3 tip = Vector3.zero;
        Vector3 c0 = new Vector3(-0.18f, 0.32f, -0.18f);
        Vector3 c1 = new Vector3( 0.18f, 0.32f, -0.18f);
        Vector3 c2 = new Vector3( 0.18f, 0.32f,  0.18f);
        Vector3 c3 = new Vector3(-0.18f, 0.32f,  0.18f);
        Vector3 crown = new Vector3(0f, 0.45f, 0f);

        Vector3[] verts = new Vector3[]
        {
            // Bottom 4 faces (pointing down towards operative)
            tip, c1, c0,
            tip, c2, c1,
            tip, c3, c2,
            tip, c0, c3,
            // Top 4 faces (faceted diamond roof)
            crown, c0, c1,
            crown, c1, c2,
            crown, c2, c3,
            crown, c3, c0
        };

        int[] tris = new int[24];
        for (int i = 0; i < 24; i++) tris[i] = i;

        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        s_ArrowMesh = m;
        return s_ArrowMesh;
    }

    private void BuildOverheadBeacon()
    {
        if (_overheadBeaconObj != null) Destroy(_overheadBeaconObj);

        _overheadBeaconObj = new GameObject("Overhead_Team_Beacon");
        _overheadBeaconObj.transform.SetParent(transform, false);
        _overheadBeaconObj.transform.localPosition = new Vector3(0f, 2.85f, 0f);

        MeshFilter mf = _overheadBeaconObj.AddComponent<MeshFilter>();
        mf.sharedMesh = GetOverheadArrowMesh();

        MeshRenderer mr = _overheadBeaconObj.AddComponent<MeshRenderer>();
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                             ?? Shader.Find("Unlit/Color") 
                             ?? OasisShaderHelper.GetLitShader();
        _overheadBeaconMat = new Material(unlitShader);
        _overheadBeaconMat.color = teamColor;
        if (_overheadBeaconMat.HasProperty("_BaseColor"))
        {
            _overheadBeaconMat.SetColor("_BaseColor", teamColor);
        }
        if (_overheadBeaconMat.HasProperty("_EmissionColor"))
        {
            _overheadBeaconMat.EnableKeyword("_EMISSION");
            _overheadBeaconMat.SetColor("_EmissionColor", teamColor * 2.0f);
        }
        mr.sharedMaterial = _overheadBeaconMat;
    }

    private void BuildOverheadHealthBar()
    {
        if (_overheadCanvasObj != null) Destroy(_overheadCanvasObj);

        _overheadCanvasObj = new GameObject("Overhead_Health_Canvas", typeof(RectTransform), typeof(Canvas));
        _overheadCanvasObj.transform.SetParent(transform, false);
        _overheadCanvasObj.transform.localPosition = new Vector3(0f, 3.42f, 0f);

        Canvas canvas = _overheadCanvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform cRT = _overheadCanvasObj.GetComponent<RectTransform>();
        cRT.sizeDelta = new Vector2(160, 60);
        cRT.localScale = Vector3.one * 0.010f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 1. Overhead Call-Sign Name
        GameObject nameObj = new GameObject("NameText", typeof(RectTransform), typeof(Text));
        nameObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform nRT = nameObj.GetComponent<RectTransform>();
        nRT.anchorMin = new Vector2(0.5f, 0.5f);
        nRT.anchorMax = new Vector2(0.5f, 0.5f);
        nRT.sizeDelta = new Vector2(160, 22);
        nRT.anchoredPosition = new Vector2(0f, 10f);

        _overheadNameText = nameObj.GetComponent<Text>();
        _overheadNameText.font = font;
        _overheadNameText.text = botName;
        _overheadNameText.fontSize = 14;
        _overheadNameText.fontStyle = FontStyle.Bold;
        _overheadNameText.alignment = TextAnchor.MiddleCenter;
        _overheadNameText.color = teamColor;
        Outline nOutline = nameObj.AddComponent<Outline>();
        nOutline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        nOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // 2. Shield Bar Slider (Top thin bar so shield hits are immediately visible!)
        GameObject sSliderObj = new GameObject("ShieldSlider");
        sSliderObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform ssRT = sSliderObj.AddComponent<RectTransform>();
        ssRT.anchorMin = new Vector2(0.5f, 0.5f);
        ssRT.anchorMax = new Vector2(0.5f, 0.5f);
        ssRT.sizeDelta = new Vector2(90, 6);
        ssRT.anchoredPosition = new Vector2(0f, -4f);

        _overheadShieldSlider = sSliderObj.AddComponent<Slider>();
        _overheadShieldSlider.minValue = 0f;
        _overheadShieldSlider.maxValue = maxShield;
        _overheadShieldSlider.value = currentShield;

        GameObject sBg = new GameObject("Background");
        sBg.transform.SetParent(sSliderObj.transform, false);
        RectTransform sBgRT = sBg.AddComponent<RectTransform>();
        sBgRT.anchorMin = Vector2.zero;
        sBgRT.anchorMax = Vector2.one;
        sBgRT.offsetMin = Vector2.zero;
        sBgRT.offsetMax = Vector2.zero;
        Image sBgImg = sBg.AddComponent<Image>();
        sBgImg.color = new Color(0.04f, 0.08f, 0.14f, 0.85f);

        GameObject sFillArea = new GameObject("Fill Area");
        sFillArea.transform.SetParent(sSliderObj.transform, false);
        RectTransform sFaRT = sFillArea.AddComponent<RectTransform>();
        sFaRT.anchorMin = Vector2.zero;
        sFaRT.anchorMax = Vector2.one;
        sFaRT.offsetMin = Vector2.zero;
        sFaRT.offsetMax = Vector2.zero;

        GameObject sFill = new GameObject("Fill");
        sFill.transform.SetParent(sFillArea.transform, false);
        RectTransform sFRT = sFill.AddComponent<RectTransform>();
        sFRT.anchorMin = Vector2.zero;
        sFRT.anchorMax = Vector2.one;
        sFRT.offsetMin = Vector2.zero;
        sFRT.offsetMax = Vector2.zero;
        Image sFillImg = sFill.AddComponent<Image>();
        sFillImg.color = new Color(0.28f, 0.92f, 1.0f, 0.95f);
        _overheadShieldSlider.fillRect = sFRT;

        // 3. Health Bar Slider (Bottom main bar)
        GameObject hSliderObj = new GameObject("HealthSlider");
        hSliderObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform hsRT = hSliderObj.AddComponent<RectTransform>();
        hsRT.anchorMin = new Vector2(0.5f, 0.5f);
        hsRT.anchorMax = new Vector2(0.5f, 0.5f);
        hsRT.sizeDelta = new Vector2(90, 8);
        hsRT.anchoredPosition = new Vector2(0f, -14f);

        _overheadHealthSlider = hSliderObj.AddComponent<Slider>();
        _overheadHealthSlider.minValue = 0f;
        _overheadHealthSlider.maxValue = maxHealth;
        _overheadHealthSlider.value = currentHealth;

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(hSliderObj.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.06f, 0.08f, 0.85f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(hSliderObj.transform, false);
        RectTransform faRT = fillArea.AddComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero;
        faRT.anchorMax = Vector2.one;
        faRT.offsetMin = Vector2.zero;
        faRT.offsetMax = Vector2.zero;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fRT = fill.AddComponent<RectTransform>();
        fRT.anchorMin = Vector2.zero;
        fRT.anchorMax = Vector2.one;
        fRT.offsetMin = Vector2.zero;
        fRT.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = teamColor;

        _overheadHealthSlider.fillRect = fRT;
    }

    public void HaltCombat()
    {
        _currentTarget = null;
        _downedAllyTarget = null;
        _downedAllyBot = null;
        isReloading = false;
        _burstShotsRemaining = 0;

        if (!isDowned && !_isDead && _animator != null)
        {
            _animator.speed = 1.0f;
            _animator.Play("Idle_Shoot_Ar");
        }
    }

    private void Update()
    {
        if (_isDead) return;

        // If team battle is inactive (match finished, countdown expired, or paused), halt all combat processing
        if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isTeamBattleActive)
        {
            if (_characterController != null && _characterController.enabled && !_characterController.isGrounded)
            {
                _characterController.Move(Vector3.down * (9.81f * Time.deltaTime));
            }
            if (!isDowned && _animator != null && !_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle_Shoot_Ar"))
            {
                _animator.speed = 1.0f;
                _animator.Play("Idle_Shoot_Ar");
            }
            if (_overheadCanvasObj != null && Camera.main != null)
            {
                _overheadCanvasObj.transform.rotation = Camera.main.transform.rotation;
            }
            return;
        }

        if (stunTimer > 0f)
        {
            stunTimer -= Time.deltaTime;
            if (_animator != null) _animator.speed = 0.2f;
            if (_overheadCanvasObj != null && Camera.main != null) _overheadCanvasObj.transform.rotation = Camera.main.transform.rotation;
            return;
        }

        // 1. If Downed: prone crawl animation along the ground, manage bleedout timer, update overhead canvas, and check expiry
        if (isDowned)
        {
            downedTimer -= Time.deltaTime;
            if (_overheadCanvasObj != null && Camera.main != null)
            {
                _overheadCanvasObj.transform.rotation = Camera.main.transform.rotation;
            }
            if (_overheadNameText != null)
            {
                if (reviveProgress > 0f)
                {
                    _overheadNameText.text = $"[REVIVING {Mathf.RoundToInt(reviveProgress * 100)}%] {botName}";
                    _overheadNameText.color = new Color(0.2f, 0.95f, 1f);
                }
                else
                {
                    _overheadNameText.text = $"[DOWNED ({Mathf.CeilToInt(downedTimer)}s)] {botName}";
                    _overheadNameText.color = new Color(1f, 0.75f, 0.15f);
                }
            }
            if (_overheadHealthSlider != null)
            {
                _overheadHealthSlider.value = (downedTimer / 8f) * maxHealth;
            }
            if (_overheadShieldSlider != null)
            {
                _overheadShieldSlider.value = reviveProgress * maxShield;
            }

            // Prone crawl posture & slow ground crawl toward nearest living squadmate (or away from danger)
            if (_visualRoot != null)
            {
                float crawlSway = (reviveProgress <= 0.01f) ? Mathf.Sin(Time.time * 4.8f) * 5.5f : 0f;
                _visualRoot.localPosition = Vector3.Lerp(_visualRoot.localPosition, new Vector3(0f, 0.22f, -0.40f), Time.deltaTime * 10f);
                _visualRoot.localRotation = Quaternion.Slerp(_visualRoot.localRotation, Quaternion.Euler(82f, 0f, crawlSway), Time.deltaTime * 10f);
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.speed = (reviveProgress <= 0.01f) ? 0.55f : 0.12f;
                if (_animator.HasState(0, Animator.StringToHash("WalkFront_Shoot_AR")))
                {
                    _animator.Play("WalkFront_Shoot_AR");
                }
            }

            // Slowly crawl along the ground when not actively being revived
            if (_characterController != null && _characterController.enabled && reviveProgress <= 0.01f)
            {
                Vector3 crawlDir = -transform.forward;
                float bestAllyDist = float.MaxValue;
                foreach (var b in ActiveBots)
                {
                    if (b == null || b == this || b._isDead || b.isDowned || b.teamId != this.teamId) continue;
                    float d = Vector3.Distance(transform.position, b.transform.position);
                    if (d < bestAllyDist)
                    {
                        bestAllyDist = d;
                        crawlDir = (b.transform.position - transform.position);
                    }
                }
                crawlDir.y = 0f;
                if (crawlDir.sqrMagnitude > 0.05f)
                {
                    crawlDir.Normalize();
                    Quaternion crawlRot = Quaternion.LookRotation(crawlDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, crawlRot, Time.deltaTime * 3.5f);
                }

                Vector3 crawlVel = crawlDir * (moveSpeed * 0.22f);
                crawlVel.y = _characterController.isGrounded ? -2f : -9.81f;
                _characterController.Move(crawlVel * Time.deltaTime);
            }

            if (downedTimer <= 0f)
            {
                Die(_lastAttackerId);
            }
            return;
        }

        // 2. Face overhead canvas toward Camera.main
        if (_overheadCanvasObj != null && Camera.main != null)
        {
            _overheadCanvasObj.transform.rotation = Camera.main.transform.rotation;
        }

        // 3. Handle Active Weapon Reload
        if (isReloading)
        {
            _reloadTimer -= Time.deltaTime;
            if (_reloadTimer <= 0f)
            {
                isReloading = false;
                currentAmmo = maxAmmo;
                if (_overheadNameText != null)
                {
                    _overheadNameText.text = botName;
                    _overheadNameText.color = teamColor;
                }
            }
        }

        // 4. Scan for downed friendly allies (Player or friendly bots) to prioritize resurrecting them
        _downedAllyTarget = null;
        _downedAllyBot = null;
        _targetIsDownedPlayer = false;

        // Only seek revive if not in point-blank dueling danger (< 7.0m from a standing enemy)
        bool inImmediateDanger = (_currentTarget != null && Vector3.Distance(transform.position, _currentTarget.position) < 7.0f);

        if (!inImmediateDanger)
        {
            int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

            // Check if player is on our team and downed (ignore player during simulation mode)
            if (localPlayerTeam == this.teamId && OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isSimulationMode && OasisShooterManager.Instance.isPlayerDowned)
            {
                Transform player = OasisPlayerHelper.FindPlayerTransform();
                if (player != null)
                {
                    _downedAllyTarget = player;
                    _targetIsDownedPlayer = true;
                }
            }

            // If player is not downed, check if any friendly AI bot is downed
            if (_downedAllyTarget == null)
            {
                float closestDownedDist = float.MaxValue;
                foreach (var b in ActiveBots)
                {
                    if (b == null || b == this || b._isDead || !b.isDowned) continue;
                    if (b.teamId == this.teamId)
                    {
                        float d = Vector3.Distance(transform.position, b.transform.position);
                        if (d < closestDownedDist)
                        {
                            closestDownedDist = d;
                            _downedAllyTarget = b.transform;
                            _downedAllyBot = b;
                            _targetIsDownedPlayer = false;
                        }
                    }
                }
            }
        }

        // 5. Scan and evaluate combat targets
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f || _currentTarget == null || !IsTargetAliveAndEnemy(_currentTarget))
        {
            _retargetTimer = Random.Range(1.2f, 2.2f);
            Transform prev = _currentTarget;
            _currentTarget = FindBestEnemyTarget();
            if (_currentTarget != null && _currentTarget != prev)
            {
                // Reaction delay before first firing at a newly acquired enemy
                _nextFireTime = Time.time + Random.Range(0.35f, 0.75f);
            }
        }

        // 6. Locomotion & Combat / Revive Positioning
        HandleMovementAndPositioning();

        // 7. Weapons & Firing
        HandleWeaponFiring();
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();

        // Keep overhead team indicator chevron and health bar perfectly billboarded and distance-scaled to the active camera
        if (_overheadCanvasObj != null && _overheadCanvasObj.activeSelf)
        {
            if (cam != null)
            {
                _overheadCanvasObj.transform.rotation = cam.transform.rotation;

                // Distance-scaled sizing so overhead nametag & health bar remain readable at combat ranges
                float dist = Vector3.Distance(transform.position, cam.transform.position);
                float dynamicScale = Mathf.Clamp(dist * 0.00075f, 0.009f, 0.026f);
                _overheadCanvasObj.transform.localScale = Vector3.one * dynamicScale;
            }
        }

        // Keep 3D overhead team beacon spinning and distance-scaled
        if (_overheadBeaconObj != null && _overheadBeaconObj.activeSelf)
        {
            _overheadBeaconObj.transform.Rotate(Vector3.up, 85f * Time.deltaTime, Space.World);

            if (cam != null)
            {
                float dist = Vector3.Distance(transform.position, cam.transform.position);
                float beaconScale = Mathf.Clamp(dist * 0.065f, 1.0f, 2.8f);
                _overheadBeaconObj.transform.localScale = Vector3.one * beaconScale;
            }
        }

        if (_isDead || isDowned || _currentTarget == null) return;

        // Rotate upper body spine & gun arm to aim vertically towards target
        Vector3 aimPoint = _currentTarget.position + Vector3.up * 1.2f;
        Vector3 dirToAim = aimPoint - transform.position;
        float pitch = Mathf.Asin(Mathf.Clamp(dirToAim.normalized.y, -0.85f, 0.85f)) * Mathf.Rad2Deg;

        if (_spineBone != null)
        {
            _spineBone.Rotate(Vector3.forward, -pitch * 0.7f, Space.Self);
        }
        if (_gunArmBone != null)
        {
            _gunArmBone.Rotate(Vector3.forward, pitch * 0.7f, Space.Self);
        }
    }

    private void HandleMovementAndPositioning()
    {
        if (_characterController == null) return;

        Vector3 anchor = (OasisSpawnSystem.Instance != null) 
            ? OasisSpawnSystem.Instance.GetPlayableCenter() 
            : new Vector3(0f, -2.0f, -112.0f);
        Vector3 arenaCenter = anchor + new Vector3(0f, 0f, -34f);
        float softBoundary = 42f;
        float hardBoundary = 56f;

        // Void catch / ground safeguard: trigger if bot falls below bedrock safety floor (Y < -12m)
        if (transform.position.y < (anchor.y - 12.0f) || transform.position.y < -12.0f)
        {
            _verticalVelocity = 0f;
            if (_characterController != null) _characterController.enabled = false;
            transform.position = OasisSpawnSystem.Instance.GetRandomSpawnPosition(this.teamId);
            if (_characterController != null) _characterController.enabled = true;
            return;
        }

        // Arena boundary containment: steer bots gently and smoothly back if they venture near canyon walls
        Vector2 deltaFromArena = new Vector2(transform.position.x - arenaCenter.x, transform.position.z - arenaCenter.z);
        Vector3 boundaryPush = Vector3.zero;
        if (deltaFromArena.magnitude > softBoundary)
        {
            Vector2 inward = -deltaFromArena.normalized;
            float pushFactor = Mathf.Clamp((deltaFromArena.magnitude - softBoundary) * 1.5f, 2.0f, 10.0f);
            boundaryPush = new Vector3(inward.x, 0f, inward.y) * pushFactor;
        }

        // Water No-Fire Zone Steering: if bot wades into the water pond, steer it back out toward dry land
        if (OasisWaterZone.IsInWater(transform.position))
        {
            boundaryPush += OasisWaterZone.GetPushOutDirection(transform.position) * 6.5f;
        }

        if (deltaFromArena.magnitude > hardBoundary)
        {
            // Extreme safety clamp: cleanly disable CC before translating to prevent physics jitter
            Vector2 clamped = deltaFromArena.normalized * (hardBoundary - 2.0f);
            if (_characterController != null) _characterController.enabled = false;
            transform.position = new Vector3(arenaCenter.x + clamped.x, transform.position.y, arenaCenter.z + clamped.y);
            if (_characterController != null) _characterController.enabled = true;
        }

        Vector3 moveVelocity = Vector3.zero;

        // Apply Earth Gravity (preserves upward velocity during jump ascent)
        if (_characterController.isGrounded && _verticalVelocity <= 0.0f)
        {
            _verticalVelocity = -2.0f;
        }
        else
        {
            _verticalVelocity -= 18.0f * Time.deltaTime;
        }

        // Manage jump cooldown
        if (_jumpCooldownTimer > 0f)
        {
            _jumpCooldownTimer -= Time.deltaTime;
        }

        // Proactive Obstacle Vault: Leap cleanly over low rocks, ledges, or dune ridges while advancing
        if (enableObstacleJump && _characterController.isGrounded && _jumpCooldownTimer <= 0f && _intendedMoveSpeed > 1.2f && !isDowned)
        {
            Vector3 kneeLevel = transform.position + Vector3.up * 0.38f;
            if (Physics.Raycast(kneeLevel, transform.forward, out RaycastHit hitObstacle, 1.4f))
            {
                if (!hitObstacle.collider.isTrigger && hitObstacle.collider.GetComponent<CharacterController>() == null)
                {
                    // Check if head/chest level is clear (ensuring it's a vaultable rock/step, not a high cliff)
                    Vector3 headLevel = transform.position + Vector3.up * 1.5f;
                    if (!Physics.Raycast(headLevel, transform.forward, 1.6f))
                    {
                        _verticalVelocity = obstacleJumpForce;
                        _jumpCooldownTimer = 1.6f;
                        _evasiveJitter = transform.forward * 3.5f;
                        if (_animator != null) _animator.Play("Run_guard_AR");
                    }
                }
            }
        }

        // Anti-Stuck Detection: ONLY trigger if bot was actively trying to move for >1.2s but made no progress
        if (_intendedMoveSpeed > 0.6f && !isDowned && _downedAllyTarget == null)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= 1.2f)
            {
                if ((transform.position - _lastSampledPos).sqrMagnitude < 0.25f)
                {
                    // Check if an obstacle is directly blocking movement
                    bool lowBlocked = Physics.Raycast(transform.position + Vector3.up * 0.38f, transform.forward, out RaycastHit hitObj, 1.5f);
                    bool highClear = !Physics.Raycast(transform.position + Vector3.up * 1.5f, transform.forward, 1.6f);

                    if (enableObstacleJump && (_characterController.isGrounded || _verticalVelocity <= 0f) && (lowBlocked || Random.value < 0.7f))
                    {
                        // OPTION 1: Obstacle Jump! Vault upward and boost forward over the obstacle
                        _verticalVelocity = obstacleJumpForce;
                        _jumpCooldownTimer = 1.8f;
                        Vector3 jumpDir = transform.forward * 4.2f + (Random.value > 0.5f ? transform.right : -transform.right) * 1.6f;
                        _evasiveJitter = jumpDir;
                        _currentManeuver = 2; // Switch to tactical reposition
                        _maneuverTimer = 2.0f;
                        if (_animator != null) _animator.Play("Run_guard_AR");

                        OasisAITelemetryLogger.Instance?.RecordStuckIncident(this, transform.position, "Obstacle Jump");
                    }
                    else
                    {
                        // OPTION 2: Tactical detour: step sideways/backward away from obstruction
                        Vector3 awayDir = (Random.value > 0.5f ? transform.right : -transform.right) - transform.forward * 0.6f;
                        _evasiveJitter = awayDir.normalized * 3.2f;
                        _currentManeuver = 2; // Switch to tactical reposition
                        _maneuverTimer = 2.5f;

                        OasisAITelemetryLogger.Instance?.RecordStuckIncident(this, transform.position, "Tactical Detour");
                    }
                }
                _lastSampledPos = transform.position;
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
            _lastSampledPos = transform.position;
        }
        _evasiveJitter = Vector3.Lerp(_evasiveJitter, Vector3.zero, Time.deltaTime * 4f);

        // PRIORITY SQUAD MISSION: Rush to downed ally and resurrect them!
        if (_downedAllyTarget != null)
        {
            Vector3 toAlly = _downedAllyTarget.position - transform.position;
            toAlly.y = 0f;
            float dist = toAlly.magnitude;

            if (toAlly.sqrMagnitude > 0.05f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toAlly.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            }

            if (dist > 2.2f)
            {
                // Sprint straight to the fallen ally
                Vector3 moveDir = toAlly.normalized;
                moveVelocity = moveDir * (moveSpeed * 1.45f);
                _intendedMoveSpeed = moveVelocity.magnitude;
                if (_animator != null) _animator.Play("Run_guard_AR");
            }
            else
            {
                // In range! Kneel and channel resurrection
                moveVelocity = Vector3.zero;
                _intendedMoveSpeed = 0f;
                if (_animator != null)
                {
                    _animator.speed = 1.0f;
                    _animator.Play("Idle_Ducking_AR");
                }

                if (_targetIsDownedPlayer && OasisShooterManager.Instance != null)
                {
                    bool wasDowned = OasisShooterManager.Instance.isPlayerDowned;
                    OasisShooterManager.Instance.ChannelPlayerRevive(Time.deltaTime / 3.5f, this.botName);
                    if (wasDowned && !OasisShooterManager.Instance.isPlayerDowned)
                    {
                        this.score += 75;
                    }
                }
                else if (_downedAllyBot != null)
                {
                    _downedAllyBot.reviveProgress += Time.deltaTime / 3.5f;
                    if (_downedAllyBot.reviveProgress >= 1.0f)
                    {
                        _downedAllyBot.Revive();
                        this.score += 75;
                        OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>{botName} RESURRECTED {_downedAllyBot.botName}!</color>", 3.0f);
                        OasisAITelemetryLogger.Instance?.RecordReviveSuccess(this, _downedAllyBot);
                    }
                }
            }

            moveVelocity += boundaryPush + _evasiveJitter;
            moveVelocity.y = _verticalVelocity;
            _characterController.Move(moveVelocity * Time.deltaTime);
            return;
        }

        float horizontalSpeed = 0f;

        // Combat Tactical Maneuvers: bots maintain tactical engagement distances (11m - 24m)
        if (_currentTarget != null)
        {
            Vector3 toTarget = _currentTarget.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            // Rotate smoothly to face target
            if (toTarget.sqrMagnitude > 0.1f)
            {
                Quaternion lookRot = Quaternion.LookRotation(toTarget.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 9f);
            }

            // Cycle dynamic combat maneuvers
            _maneuverTimer -= Time.deltaTime;
            if (_maneuverTimer <= 0f)
            {
                _maneuverTimer = Random.Range(2.0f, 3.8f);
                _currentManeuver = Random.Range(0, 3);
                _strafeDir = (Random.value > 0.5f) ? 1f : -1f;
                OasisAITelemetryLogger.Instance?.RecordManeuverChange(this, _currentManeuver);
            }

            Vector3 strafeVector = Vector3.Cross(Vector3.up, toTarget.normalized) * _strafeDir;
            Vector3 moveDir;

            if (isReloading)
            {
                // When reloading: actively backpedal and strafe defensively to avoid incoming shots
                moveDir = (-toTarget.normalized * 0.75f + strafeVector * 0.65f).normalized;
                moveVelocity = moveDir * (moveSpeed * 1.30f);
            }
            else if (dist < 6.0f)
            {
                // DANGER ZONE (Too close!): Immediate tactical backpedal to restore stand-off distance
                moveDir = (-toTarget.normalized * 0.90f + strafeVector * 0.45f).normalized;
                moveVelocity = moveDir * (moveSpeed * 1.25f);
            }
            else if (dist < 11.0f)
            {
                // CLOSE QUARTERS: Create spacing while strafing
                moveDir = (-toTarget.normalized * 0.45f + strafeVector * 0.85f).normalized;
                moveVelocity = moveDir * (moveSpeed * 1.15f);
            }
            else if (dist > 24.0f)
            {
                // LONG RANGE: Close into combat range with angled advance
                moveDir = (toTarget.normalized * 0.80f + strafeVector * 0.45f).normalized;
                moveVelocity = moveDir * (moveSpeed * 1.25f);
            }
            else
            {
                // IDEAL WEAPON COMBAT ZONE (11m - 24m): Lateral strafe and tactical dueling
                switch (_currentManeuver)
                {
                    case 0: // Aggressive dueling pressure: slight advance with heavy strafe
                        moveDir = (toTarget.normalized * 0.35f + strafeVector * 0.85f).normalized;
                        moveVelocity = moveDir * (moveSpeed * 1.15f);
                        break;

                    case 1: // Flanking circle strafe: pure lateral circling
                        moveDir = (strafeVector * 0.95f + toTarget.normalized * 0.10f).normalized;
                        moveVelocity = moveDir * (moveSpeed * 1.10f);
                        break;

                    case 2: // Defensive repositioning: fall back slightly while strafing
                    default:
                        moveDir = (-toTarget.normalized * 0.35f + strafeVector * 0.85f).normalized;
                        moveVelocity = moveDir * (moveSpeed * 1.05f);
                        break;
                }
            }

            // Obstacle Avoidance: raycast forward to smoothly slide around boulders and terrain
            if (Physics.Raycast(transform.position + Vector3.up * 0.8f, moveDir, out RaycastHit obsHit, 2.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (obsHit.collider != null && obsHit.collider.transform != _currentTarget)
                {
                    Vector3 detour = Vector3.Cross(Vector3.up, obsHit.normal);
                    moveDir = (moveDir + detour * 2.0f).normalized;
                    moveVelocity = moveDir * (moveSpeed * 1.1f);
                }
            }

            horizontalSpeed = moveVelocity.magnitude;
        }
        else
        {
            // No direct target visible: actively patrol forward towards central canyon battleground
            Vector3 toCenter = (arenaCenter - transform.position);
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 4f)
            {
                Vector3 moveDir = toCenter.normalized;
                Quaternion lookRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
                moveVelocity = moveDir * (moveSpeed * 0.95f);
                horizontalSpeed = moveVelocity.magnitude;
            }
        }

        _intendedMoveSpeed = horizontalSpeed;

        // Add boundary containment and anti-stuck jitter
        moveVelocity += boundaryPush + _evasiveJitter;

        moveVelocity.y = _verticalVelocity;
        _characterController.Move(moveVelocity * Time.deltaTime);

        // Ensure upright visual transform during normal movement
        if (_visualRoot != null)
        {
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
        }

        // Update Animation States (Walk / Run / Idle)
        if (_animator != null)
        {
            _animator.speed = 1.0f;
            if (horizontalSpeed > 4.2f)
            {
                _animator.Play("Run_guard_AR");
            }
            else if (horizontalSpeed > 0.2f)
            {
                _animator.Play("WalkFront_Shoot_AR");
            }
            else
            {
                _animator.Play("Idle_Shoot_Ar");
            }
        }
    }

    private void HandleWeaponFiring()
    {
        if (isDowned || isReloading || _currentTarget == null) return;

        // Water Sanctuary No-Fire Zone: Neither shooter in water nor firing at target in water
        if (OasisWaterZone.IsInWater(transform.position) || OasisWaterZone.IsInWater(_currentTarget.position))
        {
            _burstShotsRemaining = 0;
            return;
        }

        Vector3 toTarget = _currentTarget.position - transform.position;
        float dist = toTarget.magnitude;

        if (dist > attackRange + 6f) return;

        // Line of Sight Verification: do not shoot if obstructed by terrain, rocks, or tent walls
        Vector3 muzzle = _muzzleTransform != null ? _muzzleTransform.position : transform.position + Vector3.up * 1.3f;
        Vector3 targetAim = _currentTarget.position + Vector3.up * 1.1f;
        Vector3 toAim = targetAim - muzzle;
        if (Physics.Raycast(muzzle, toAim.normalized, out RaycastHit losHit, toAim.magnitude - 0.4f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (losHit.collider != null && !losHit.collider.transform.IsChildOf(_currentTarget) && losHit.collider.transform != _currentTarget)
            {
                // Obstructed by obstacle - reposition rather than wasting ammo into walls
                return;
            }
        }

        // Handle active burst fire sequence
        if (_burstShotsRemaining > 0)
        {
            _burstTimer -= Time.deltaTime;
            if (_burstTimer <= 0f)
            {
                _burstTimer = 0.24f;
                _burstShotsRemaining--;
                ExecuteSingleBlasterShot();
            }
            return;
        }

        // Trigger next burst fire
        if (Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + Random.Range(1.3f, 2.6f);
            _burstShotsRemaining = Random.Range(2, 4);
            _burstTimer = 0.05f;
        }
    }

    private void ExecuteSingleBlasterShot()
    {
        // Consume weapon ammo
        currentAmmo--;
        OasisAITelemetryLogger.Instance?.RecordShotFired(this);

        if (currentAmmo <= 0)
        {
            currentAmmo = 0;
            isReloading = true;
            _reloadTimer = reloadDuration;
            _burstShotsRemaining = 0;
            OasisAITelemetryLogger.Instance?.RecordReloadStart(this);

            if (_audioSource != null)
            {
                _audioSource.PlayOneShot(ProceduralAudio.GetReloadClip(), 0.35f);
            }
            if (_overheadNameText != null)
            {
                _overheadNameText.text = $"[RELOADING] {botName}";
                _overheadNameText.color = new Color(0.95f, 0.75f, 0.2f);
            }
        }
        Vector3 muzzle = _muzzleTransform != null ? _muzzleTransform.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.5f;

        Vector3 aimPoint;
        if (_currentTarget != null)
        {
            float targetDist = Vector3.Distance(muzzle, _currentTarget.position);
            Vector3 targetCenter = _currentTarget.position + Vector3.up * 1.15f;
            // Realistic, balanced distance-scaled spread: tight at close range, natural cone at distance
            float spreadRadius = Mathf.Clamp(targetDist * 0.032f, 0.18f, 0.85f);
            aimPoint = targetCenter + (Random.insideUnitSphere * spreadRadius);
        }
        else
        {
            aimPoint = muzzle + transform.forward * 20f;
        }

        Vector3 fireDir = (aimPoint - muzzle).normalized;

        // Audio: soft 3D audio shot
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.18f);
        }

        // Raycast against scene (filter out triggers, shooter, and friendly teammates)
        RaycastHit[] hits = Physics.RaycastAll(muzzle, fireDir, 60f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Vector3 laserEnd = muzzle + fireDir * 60f;
        int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        Transform localPlayerTrans = OasisPlayerHelper.FindPlayerTransform();

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.collider.isTrigger) continue;

            // Ignore self
            if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // Check if hit player
            bool isPlayer = h.collider.CompareTag("Player") ||
                            (localPlayerTrans != null && (h.collider.transform == localPlayerTrans || h.collider.transform.IsChildOf(localPlayerTrans))) ||
                            h.collider.GetComponentInParent<StarterAssets.FirstPersonController>();

            if (isPlayer)
            {
                if (localPlayerTeam == this.teamId)
                {
                    // Friendly teammate! Laser passes through teammates without hitting them
                    continue;
                }
                else
                {
                    // Hostile enemy player hit — award bot hit score and pass attacker reference
                    laserEnd = h.point;
                    this.score += 15;
                    OasisShooterManager.Instance?.TakeDamage(damagePerShot, this.teamId, this);
                    BlasterVFX.SpawnImpact(h.point, h.normal, false);
                    break;
                }
            }

            // Check if hit another AI Combatant
            OasisCombatAI hitAI = h.collider.GetComponentInParent<OasisCombatAI>();
            if (hitAI != null)
            {
                if (hitAI.teamId == this.teamId)
                {
                    // Friendly AI operative! Laser passes through without friendly fire
                    continue;
                }
                else
                {
                    // Hostile AI enemy hit — award bot hit score
                    laserEnd = h.point;
                    this.score += 15;
                    hitAI.TakeDamage(damagePerShot, h.point, h.normal, this.GetHashCode(), this.teamId);
                    BlasterVFX.SpawnImpact(h.point, h.normal, false);
                    OasisAITelemetryLogger.Instance?.RecordHitLanded(this, hitAI, damagePerShot, Vector3.Distance(muzzle, h.point));
                    break;
                }
            }

            // Hit environment (dune, rock, wall)
            laserEnd = h.point;
            BlasterVFX.SpawnImpact(h.point, h.normal, false);
            break;
        }

        // Colored laser beam
        BlasterVFX.SpawnLaserBeam(muzzle, laserEnd, teamColor);
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, int attackerId = 0, int attackerTeamId = -1)
    {
        if (_isDead) return;
        if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isTeamBattleActive) return;

        if (attackerTeamId >= 0)
        {
            _lastAttackerTeamId = attackerTeamId;
        }

        if (isDowned)
        {
            // Accelerated bleedout when damaged while downed (finishes off in 2-3 shots)
            downedTimer -= 3.5f;
            BlasterVFX.SpawnImpact(hitPoint, hitNormal, true);
            if (_overheadHealthSlider != null)
            {
                _overheadHealthSlider.value = Mathf.Max(0f, (downedTimer / 8f) * maxHealth);
            }
            if (downedTimer <= 0f)
            {
                Die(attackerId);
            }
            return;
        }

        // Interrupt revive channel when under fire
        if (_downedAllyBot != null && _downedAllyBot.reviveProgress > 0f)
        {
            _downedAllyBot.reviveProgress = Mathf.Max(0f, _downedAllyBot.reviveProgress - 0.25f);
        }

        // Shield absorbs first
        if (currentShield > 0f)
        {
            currentShield -= amount;
            if (currentShield < 0f)
            {
                currentHealth += currentShield; // Absorb remainder
                currentShield = 0f;
            }
        }
        else
        {
            currentHealth -= amount;
        }

        if (_overheadShieldSlider != null)
        {
            _overheadShieldSlider.value = Mathf.Max(0f, currentShield);
        }
        if (_overheadHealthSlider != null)
        {
            _overheadHealthSlider.value = Mathf.Max(0f, currentHealth);
        }

        // Visual flash & sound
        BlasterVFX.SpawnImpact(hitPoint, hitNormal, true);
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetHitWorldClip(), 0.20f);
        }

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            EnterDownedState(attackerId);
        }
    }

    public void EnterDownedState(int attackerId)
    {
        if (isDowned || _isDead) return;

        isDowned = true;
        downedTimer = 8.0f;
        reviveProgress = 0f;
        _lastAttackerId = attackerId;

        // Transition visual root into a prone ground crawl posture and play crawling animation
        if (_visualRoot != null)
        {
            _visualRoot.localPosition = new Vector3(0f, 0.22f, -0.40f);
            _visualRoot.localRotation = Quaternion.Euler(82f, 0f, 0f);
        }
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.speed = 0.55f;
            if (_animator.HasState(0, Animator.StringToHash("WalkFront_Shoot_AR")))
            {
                _animator.Play("WalkFront_Shoot_AR");
            }
            else if (_animator.HasState(0, Animator.StringToHash("Idle_Ducking_AR")))
            {
                _animator.Play("Idle_Ducking_AR");
            }
        }

        if (_overheadNameText != null)
        {
            _overheadNameText.text = $"[DOWNED (8s)] {botName}";
            _overheadNameText.color = new Color(1f, 0.75f, 0.15f);
        }
        if (_overheadBeaconMat != null)
        {
            _overheadBeaconMat.color = new Color(1f, 0.45f, 0.15f);
        }
        if (_overheadBeaconObj != null)
        {
            _overheadBeaconObj.transform.localPosition = new Vector3(0f, 1.30f, -0.35f);
        }
        if (_overheadCanvasObj != null)
        {
            _overheadCanvasObj.transform.localPosition = new Vector3(0f, 1.85f, -0.35f);
        }
        if (_overheadShieldSlider != null)
        {
            _overheadShieldSlider.value = 0f;
        }

        int myPlayerId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        bool isPlayerDownedThis = (attackerId == 0 || (myPlayerId != 0 && attackerId == myPlayerId));
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode)
        {
            isPlayerDownedThis = false;
        }

        // If knocked down by another CPU bot, credit that bot's kills and score!
        if (!isPlayerDownedThis)
        {
            OasisCombatAI attackerBot = null;
            foreach (var b in ActiveBots)
            {
                if (b != null && b != this && b.GetHashCode() == attackerId)
                {
                    attackerBot = b;
                    break;
                }
            }
            if (attackerBot == null && _lastAttackerTeamId >= 0)
            {
                foreach (var b in ActiveBots)
                {
                    if (b != null && b != this && b.teamId == _lastAttackerTeamId && !b.isDead)
                    {
                        attackerBot = b;
                        break;
                    }
                }
            }
            if (attackerBot != null)
            {
                attackerBot.kills++;
                attackerBot.score += 100;
            }
        }

        // Immediately award knockdown score, update team kills, and refresh HUD
        OasisShooterManager.Instance?.OnCombatAIDowned(this, isPlayerDownedThis, _lastAttackerTeamId);
        OasisAITelemetryLogger.Instance?.RecordKnockdown(this, attackerId, 0f);

        int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        if (!isPlayerDownedThis && this.teamId == localPlayerTeam)
        {
            OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#FF5544>SQUADMATE DOWNED: {botName}! HOLD E TO RESURRECT</color>", 3.0f);
        }
    }

    public void Revive()
    {
        if (!isDowned) return;

        isDowned = false;
        _isDead = false;
        downedTimer = 10.0f;
        reviveProgress = 0f;
        currentAmmo = maxAmmo;
        isReloading = false;
        currentHealth = maxHealth * 0.6f;
        currentShield = maxShield * 0.5f;

        if (_visualRoot != null)
        {
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
        }
        if (_characterController != null) _characterController.enabled = true;

        if (_overheadCanvasObj != null)
        {
            _overheadCanvasObj.transform.localPosition = new Vector3(0f, 3.42f, 0f);
            _overheadCanvasObj.SetActive(true);
            if (_overheadNameText != null)
            {
                _overheadNameText.text = botName;
                _overheadNameText.color = teamColor;
            }
            if (_overheadShieldSlider != null)
            {
                _overheadShieldSlider.value = currentShield;
            }
            if (_overheadHealthSlider != null)
            {
                _overheadHealthSlider.value = currentHealth;
            }
        }

        if (_overheadBeaconObj != null)
        {
            _overheadBeaconObj.transform.localPosition = new Vector3(0f, 2.85f, 0f);
            _overheadBeaconObj.SetActive(true);
            if (_overheadBeaconMat != null) _overheadBeaconMat.color = teamColor;
        }

        if (_animator != null)
        {
            _animator.speed = 1.0f;
            _animator.Play("Idle_Shoot_Ar");
        }

        BlasterVFX.SpawnWarpIn(transform.position);
    }

    private void Die(int killerId)
    {
        if (_isDead) return;
        _isDead = true;
        isDowned = false;

        OasisAITelemetryLogger.Instance?.RecordBleedoutDeath(this);

        // Immediately hide visual model and overhead UI upon death so corpse never stands upright on the battlefield
        if (_visualRoot != null)
        {
            _visualRoot.gameObject.SetActive(false);
        }
        if (_characterController != null) _characterController.enabled = false;
        if (_overheadCanvasObj != null) _overheadCanvasObj.SetActive(false);
        if (_overheadBeaconObj != null) _overheadBeaconObj.SetActive(false);

        // Spawn digital elimination impact effect
        BlasterVFX.SpawnImpact(transform.position + Vector3.up * 0.5f, Vector3.up, false);

        // Award score & update team match score (only when bleedout timer expires or executed!)
        int myPlayerId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        bool isPlayerKill = (killerId == 0 || (myPlayerId != 0 && killerId == myPlayerId));
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode)
        {
            isPlayerKill = false;
        }

        if (!isPlayerKill)
        {
            foreach (var b in ActiveBots)
            {
                if (b != null && b != this && b.GetHashCode() == killerId)
                {
                    b.score += 50;
                    break;
                }
            }
        }

        OasisShooterManager.Instance?.OnCombatAIDefeated(this, isPlayerKill);

        // Schedule respawn after 4 seconds back inside starting base
        StartCoroutine(RespawnRoutine(4.0f));
    }

    private IEnumerator RespawnRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        _verticalVelocity = 0f;

        // When respawning from bleedout death, combatant respawns back inside starting base outpost
        OasisSpawnSystem.Instance.RespawnCombatant(transform, this.teamId);

        if (_visualRoot != null)
        {
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.gameObject.SetActive(true);
        }

        currentHealth = maxHealth;
        currentShield = maxShield;
        currentAmmo = maxAmmo;
        isReloading = false;
        _isDead = false;
        isDowned = false;
        downedTimer = 10f;
        reviveProgress = 0f;

        if (_characterController != null) _characterController.enabled = true;
        if (_overheadCanvasObj != null)
        {
            _overheadCanvasObj.transform.localPosition = new Vector3(0f, 3.42f, 0f);
            _overheadCanvasObj.SetActive(true);
            if (_overheadNameText != null)
            {
                _overheadNameText.text = botName;
                _overheadNameText.color = teamColor;
            }
            if (_overheadShieldSlider != null) _overheadShieldSlider.value = maxShield;
            if (_overheadHealthSlider != null) _overheadHealthSlider.value = maxHealth;
        }

        if (_overheadBeaconObj != null)
        {
            _overheadBeaconObj.transform.localPosition = new Vector3(0f, 2.85f, 0f);
            _overheadBeaconObj.SetActive(true);
            if (_overheadBeaconMat != null) _overheadBeaconMat.color = teamColor;
        }

        if (_animator != null)
        {
            _animator.speed = 1.0f;
            _animator.Play("Idle_Shoot_Ar");
        }

        BlasterVFX.SpawnWarpIn(transform.position);
    }

    private Transform FindBestEnemyTarget()
    {
        Transform bestTarget = null;
        float lowestThreatScore = float.MaxValue;

        int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

        // 1. Check local player (ONLY IF OPPOSING ENEMY TEAM and NOT DOWNED, and NOT IN SIMULATION SPECTATOR MODE)
        if (localPlayerTeam != this.teamId && (OasisShooterManager.Instance == null || !OasisShooterManager.Instance.isSimulationMode))
        {
            bool playerDowned = OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned;
            bool playerCloaked = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.cloakTimeRemaining > 0f;
            if (!playerDowned && !playerCloaked)
            {
                Transform player = OasisPlayerHelper.FindPlayerTransform();
                if (player != null)
                {
                    float d = Vector3.Distance(transform.position, player.position);
                    float score = d;
                    if (score < lowestThreatScore)
                    {
                        lowestThreatScore = score;
                        bestTarget = player;
                    }
                }
            }
        }

        // 2. Check other AI bots on opposing team (prioritize standing enemies with threat & revenge weighting)
        foreach (var bot in ActiveBots)
        {
            if (bot == null || bot == this || bot._isDead || bot.isDowned) continue;
            if (bot.teamId != this.teamId)
            {
                float d = Vector3.Distance(transform.position, bot.transform.position);

                // Threat weighting:
                // - High retaliation priority for the enemy actively shooting us (_lastAttackerId)
                // - Elimination priority for critical low-health opponents (<35 HP)
                float threatScore = d;
                if (bot.GetHashCode() == _lastAttackerId)
                {
                    threatScore -= 8.0f; // Retaliate directly against attacker
                }
                if (bot.currentHealth < 35f)
                {
                    threatScore -= 4.0f; // Finish off vulnerable opponents
                }

                if (threatScore < lowestThreatScore)
                {
                    lowestThreatScore = threatScore;
                    bestTarget = bot.transform;
                }
            }
        }

        return bestTarget;
    }

    private bool IsTargetAliveAndEnemy(Transform target)
    {
        if (target == null) return false;

        // If target is an AI bot
        var ai = target.GetComponent<OasisCombatAI>();
        if (ai != null)
        {
            return !ai._isDead && !ai.isDowned && ai.teamId != this.teamId;
        }

        // If target is the local player, verify player is on opposing team
        int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        Transform localPlayer = OasisPlayerHelper.FindPlayerTransform();
        if (localPlayer != null && (target == localPlayer || target.IsChildOf(localPlayer)))
        {
            if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return false;
            bool playerDowned = OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned;
            return !playerDowned && localPlayerTeam != this.teamId;
        }

        return false;
    }
}
