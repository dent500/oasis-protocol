using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Autonomous Humanoid AI Combatant for Oasis Team Battles (2v2, 3v3, 4v4).
/// Features class-specific tactical archetypes, context-aware state machine maneuvers,
/// out-of-combat shield regeneration, line-of-sight target acquisition, 3-ray whisker
/// obstacle avoidance, coordinated squad revives with covering fire, and relative
/// ally (blue) vs. enemy (red) tactical identification.
/// </summary>
public class OasisCombatAI : MonoBehaviour
{
    [Header("Team & Identity")]
    public int teamId = 1; // 0 = Team Alpha, 1 = Team Bravo, 2 = Team Charlie, 3 = Team Delta
    public int squadSlot = 0;
    public string botName = "Operative";
    public string operativeClassId = "heavy_warrior";
    public Color teamColor;

    [Header("Combat Stats")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float maxShield = 50f;
    public float currentShield = 50f;
    public float attackRange = 38f;
    public float fireRate = 0.22f;
    public float damagePerShot = 12f;
    public float moveSpeed = 4.3f;

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
    private Image _overheadHealthFillImg;
    private Text _overheadNameText;
    private GameObject _overheadBeaconObj;
    private Material _overheadBeaconMat;
    private Material _torsoRingMat;
    private Material _customBodyMat;
    private int _cachedLocalPlayerTeam = -99;

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

    // Shield Regeneration
    private float _shieldRegenDelayTimer = 0f;

    [Header("Downed & Revival System")]
    public bool isDowned = false;
    public float downedTimer = 10.0f;
    public float reviveProgress = 0.0f;
    private float _downedGraceTimer = 0.0f;
    private int _lastAttackerId = 0;
    private int _lastAttackerTeamId = -1;
    private float _lastHitDistance = 0f;

    private Transform _downedAllyTarget = null;
    private OasisCombatAI _downedAllyBot = null;
    private bool _targetIsDownedPlayer = false;
    private Transform _lastReviveLoggedTarget = null;

    [Header("Weapon Magazine & Tactical Reload")]
    public int maxAmmo = 16;
    public int currentAmmo = 16;
    public bool isReloading = false;
    public float reloadDuration = 1.9f;
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
    private int _currentManeuver = 0; // 0 = Aggressive Push, 1 = Lateral Flank, 2 = Tactical Fallback
    private float _intendedMoveSpeed = 0f;

    // Blaster Muzzle
    private Transform _muzzleTransform;
    public float stunTimer = 0f;

    public void ApplyEMPStun(float duration)
    {
        stunTimer = Mathf.Max(stunTimer, duration);
        currentShield = Mathf.Max(0f, currentShield - 50f);
        _shieldRegenDelayTimer = 4.5f;
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

    private void ConfigureClassArchetypeStats()
    {
        switch (operativeClassId)
        {
            case "heavy_warrior":
                maxHealth = 100f;
                maxShield = 70f;
                moveSpeed = 4.15f;
                damagePerShot = 13f;
                attackRange = 36f;
                break;

            case "combat_medic":
                maxHealth = 100f;
                maxShield = 55f;
                moveSpeed = 4.65f;
                damagePerShot = 12f;
                attackRange = 36f;
                break;

            case "cyborg_specops":
            case "cyber_outlaw":
                maxHealth = 100f;
                maxShield = 50f;
                moveSpeed = 4.80f;
                damagePerShot = 12f;
                attackRange = 38f;
                break;

            case "wasteland_nomad":
            case "desert_commando":
            case "unit07_android":
            default:
                maxHealth = 100f;
                maxShield = 55f;
                moveSpeed = 4.45f;
                damagePerShot = 12f;
                attackRange = 40f;
                break;
        }
    }

    public void InitializeBot(int team, string name, string classId, Texture2D skinTex, Color skinAccent, int assignedSlot = 0)
    {
        this.teamId = team;
        this.squadSlot = assignedSlot;
        this.botName = name;
        this.operativeClassId = classId;
        this.teamColor = OasisShooterManager.GetTeamColor(team);

        ConfigureClassArchetypeStats();

        currentHealth = maxHealth;
        currentShield = maxShield;
        _isDead = false;
        _cachedLocalPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

        BuildHumanoidVisual(skinTex, skinAccent);
        BuildOverheadHealthBar();
        BuildOverheadBeacon();

        // Choose an initial class-appropriate tactical maneuver without logging at 00s before movement
        _currentManeuver = (operativeClassId == "cyborg_specops" || operativeClassId == "cyber_outlaw") ? 1 : 0;
        _maneuverTimer = Random.Range(3.5f, 5.2f);
        _strafeDir = (assignedSlot % 2 == 0) ? 1f : -1f;
    }

    private void BuildHumanoidVisual(Texture2D skinTex, Color skinAccent)
    {
        Color tacCol = GetTacticalColor();

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

            // Strip colliders from visual child hierarchy immediately (disable first so same-frame raycasts ignore them)
            var cols = visual.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                c.enabled = false;
                Destroy(c);
            }

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

            // 4. Apply Material with selected operative skin and tactical ally/enemy emission
            Material baseMat = Resources.Load<Material>("RobotHero_URP");
#if UNITY_EDITOR
            if (baseMat == null) baseMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/SciFiWarriorPBRHPPolyart/Materials/RobotHero_URP.mat");
#endif
            if (baseMat == null) baseMat = new Material(OasisShaderHelper.GetLitShader());
            _customBodyMat = new Material(baseMat);
            _customBodyMat.name = $"{botName}_Mat";

            if (skinTex != null)
            {
                if (_customBodyMat.HasProperty("_BaseMap")) _customBodyMat.SetTexture("_BaseMap", skinTex);
                if (_customBodyMat.HasProperty("_MainTex")) _customBodyMat.SetTexture("_MainTex", skinTex);
                _customBodyMat.color = Color.white;
            }
            else
            {
                _customBodyMat.color = tacCol;
            }

            // Give tactical ally (blue) / enemy (red) glow on emissions
            if (_customBodyMat.HasProperty("_EmissionColor"))
            {
                _customBodyMat.EnableKeyword("_EMISSION");
                _customBodyMat.SetColor("_EmissionColor", tacCol * 1.4f);
            }

            var rends = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                r.sharedMaterial = _customBodyMat;
            }

            // 4b. Attach glowing Holographic Tactical Beacon Ring to torso for instantaneous friend-or-foe identification
            GameObject teamRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            teamRing.name = "Team_HoloBeacon";
            teamRing.transform.SetParent(visual.transform, false);
            teamRing.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            teamRing.transform.localScale = new Vector3(0.65f, 0.035f, 0.65f);
            var ringCol = teamRing.GetComponent<Collider>();
            if (ringCol != null)
            {
                ringCol.enabled = false;
                Destroy(ringCol);
            }
            _torsoRingMat = new Material(OasisShaderHelper.GetLitShader());
            _torsoRingMat.color = tacCol;
            if (_torsoRingMat.HasProperty("_BaseColor")) _torsoRingMat.SetColor("_BaseColor", tacCol);
            if (_torsoRingMat.HasProperty("_EmissionColor"))
            {
                _torsoRingMat.EnableKeyword("_EMISSION");
                _torsoRingMat.SetColor("_EmissionColor", tacCol * 2.5f);
            }
            teamRing.GetComponent<Renderer>().sharedMaterial = _torsoRingMat;

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

            _customBodyMat = new Material(OasisShaderHelper.GetLitShader());
            _customBodyMat.color = tacCol;
            if (_customBodyMat.HasProperty("_EmissionColor"))
            {
                _customBodyMat.EnableKeyword("_EMISSION");
                _customBodyMat.SetColor("_EmissionColor", tacCol * 1.2f);
            }

            // Torso
            GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.transform.SetParent(visual.transform, false);
            torso.transform.localPosition = new Vector3(0, 1.1f, 0);
            torso.transform.localScale = new Vector3(0.55f, 0.75f, 0.35f);
            var tCol = torso.GetComponent<Collider>();
            if (tCol != null) { tCol.enabled = false; Destroy(tCol); }
            torso.GetComponent<Renderer>().sharedMaterial = _customBodyMat;

            // Head / Helmet
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(visual.transform, false);
            head.transform.localPosition = new Vector3(0, 1.7f, 0);
            head.transform.localScale = new Vector3(0.35f, 0.38f, 0.35f);
            var hCol = head.GetComponent<Collider>();
            if (hCol != null) { hCol.enabled = false; Destroy(hCol); }
            head.GetComponent<Renderer>().sharedMaterial = _customBodyMat;

            // Legs
            GameObject leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftLeg.transform.SetParent(visual.transform, false);
            leftLeg.transform.localPosition = new Vector3(-0.16f, 0.45f, 0);
            leftLeg.transform.localScale = new Vector3(0.16f, 0.45f, 0.16f);
            var llCol = leftLeg.GetComponent<Collider>();
            if (llCol != null) { llCol.enabled = false; Destroy(llCol); }
            leftLeg.GetComponent<Renderer>().sharedMaterial = _customBodyMat;

            GameObject rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightLeg.transform.SetParent(visual.transform, false);
            rightLeg.transform.localPosition = new Vector3(0.16f, 0.45f, 0);
            rightLeg.transform.localScale = new Vector3(0.16f, 0.45f, 0.16f);
            var rlCol = rightLeg.GetComponent<Collider>();
            if (rlCol != null) { rlCol.enabled = false; Destroy(rlCol); }
            rightLeg.GetComponent<Renderer>().sharedMaterial = _customBodyMat;

            // Rifle
            GameObject rifle = BuildProceduralRifle(visual.transform);
            rifle.transform.localPosition = new Vector3(0.32f, 1.15f, 0.35f);
            rifle.transform.localRotation = Quaternion.identity;
            _muzzleTransform = rifle.transform.Find("Muzzle");
        }
    }

    private GameObject BuildProceduralRifle(Transform parent)
    {
        GameObject gunRoot = new GameObject("AI_BlasterRifle");
        gunRoot.transform.SetParent(parent, false);
        gunRoot.transform.localPosition = new Vector3(-0.02f, 0.08f, 0.12f);
        gunRoot.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Material darkMetal = new Material(OasisShaderHelper.GetLitShader());
        darkMetal.color = new Color(0.15f, 0.17f, 0.2f);

        Material glowMat = new Material(OasisShaderHelper.GetLitShader());
        Color tacCol = GetTacticalColor();
        glowMat.color = tacCol;
        if (glowMat.HasProperty("_EmissionColor"))
        {
            glowMat.EnableKeyword("_EMISSION");
            glowMat.SetColor("_EmissionColor", tacCol * 2f);
        }

        // Receiver body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(gunRoot.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        body.transform.localScale = new Vector3(0.06f, 0.10f, 0.42f);
        var bCol = body.GetComponent<Collider>();
        if (bCol != null) { bCol.enabled = false; Destroy(bCol); }
        body.GetComponent<Renderer>().sharedMaterial = darkMetal;

        // Barrel
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.transform.SetParent(gunRoot.transform, false);
        barrel.transform.localPosition = new Vector3(0f, 0.02f, 0.42f);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        barrel.transform.localScale = new Vector3(0.025f, 0.18f, 0.025f);
        var brCol = barrel.GetComponent<Collider>();
        if (brCol != null) { brCol.enabled = false; Destroy(brCol); }
        barrel.GetComponent<Renderer>().sharedMaterial = glowMat;

        // Muzzle Point
        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(gunRoot.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.02f, 0.65f);

        return gunRoot;
    }

    /// <summary>
    /// Builds a spinning 3D holographic diamond beacon above the operative's head
    /// so teammates (blue) and enemies (red) are unmistakably identifiable at any distance.
    /// </summary>
    private void BuildOverheadBeacon()
    {
        if (_overheadBeaconObj != null) Destroy(_overheadBeaconObj);

        _overheadBeaconObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _overheadBeaconObj.name = "Team_3D_DiamondBeacon";
        _overheadBeaconObj.transform.SetParent(transform, false);
        _overheadBeaconObj.transform.localPosition = new Vector3(0f, 2.85f, 0f);
        _overheadBeaconObj.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        _overheadBeaconObj.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);

        var col = _overheadBeaconObj.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
            Destroy(col);
        }

        Color beaconColor = GetTacticalColor();
        _overheadBeaconMat = OasisShaderHelper.CreateUnlitMaterial(beaconColor);
        var rend = _overheadBeaconObj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = _overheadBeaconMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }
    }

    /// <summary>
    /// Returns true if this operative is on the local human player's team (Friendly Ally).
    /// </summary>
    public bool IsFriendlyToLocalPlayer()
    {
        int localTeam = (OasisShooterManager.Instance != null)
            ? OasisShooterManager.Instance.localPlayerTeam
            : PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        return this.teamId == localTeam;
    }

    /// <summary>
    /// Returns high-visibility Cyan-Blue for friendly allies and Crimson-Red for opposing enemies
    /// so the player can instantaneously distinguish friend from foe regardless of which of the 4 squads they belong to.
    /// </summary>
    public Color GetTacticalColor()
    {
        return IsFriendlyToLocalPlayer()
            ? new Color(0.15f, 0.85f, 1.0f)  // Friendly Ally: Bright Tactical Blue
            : new Color(1.0f, 0.22f, 0.18f); // Hostile Enemy: Bright Crimson Red
    }

    public string GetFullDisplayName()
    {
        string teamShort = OasisShooterManager.GetTeamShortName(this.teamId);
        string roleTag = IsFriendlyToLocalPlayer() ? "ALLY" : "ENEMY";
        return $"[{teamShort} • {roleTag}] {botName}";
    }

    public void RefreshTacticalColors()
    {
        Color tacCol = GetTacticalColor();
        if (_overheadNameText != null && !isDowned && !isReloading)
        {
            _overheadNameText.text = GetFullDisplayName();
            _overheadNameText.color = tacCol;
        }
        if (_overheadHealthFillImg != null)
        {
            _overheadHealthFillImg.color = tacCol;
        }
        if (_overheadBeaconMat != null && !isDowned)
        {
            _overheadBeaconMat.color = tacCol;
            if (_overheadBeaconMat.HasProperty("_BaseColor")) _overheadBeaconMat.SetColor("_BaseColor", tacCol);
        }
        if (_torsoRingMat != null)
        {
            _torsoRingMat.color = tacCol;
            if (_torsoRingMat.HasProperty("_BaseColor")) _torsoRingMat.SetColor("_BaseColor", tacCol);
            if (_torsoRingMat.HasProperty("_EmissionColor")) _torsoRingMat.SetColor("_EmissionColor", tacCol * 2.5f);
        }
        if (_customBodyMat != null && _customBodyMat.HasProperty("_EmissionColor"))
        {
            _customBodyMat.SetColor("_EmissionColor", tacCol * 1.4f);
        }
    }

    private void BuildOverheadHealthBar()
    {
        if (_overheadCanvasObj != null) Destroy(_overheadCanvasObj);

        _overheadCanvasObj = new GameObject("AI_OverheadHealth");
        _overheadCanvasObj.transform.SetParent(transform, false);
        _overheadCanvasObj.transform.localPosition = new Vector3(0f, 3.42f, 0f);
        _overheadCanvasObj.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = _overheadCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = _overheadCanvasObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180, 56);

        // 0. Dark Semi-Transparent Backdrop Plate for crisp contrast against desert sky & sand
        GameObject plateObj = new GameObject("BackdropPlate");
        plateObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform plateRT = plateObj.AddComponent<RectTransform>();
        plateRT.anchorMin = new Vector2(0.5f, 0.5f);
        plateRT.anchorMax = new Vector2(0.5f, 0.5f);
        plateRT.sizeDelta = new Vector2(145, 42);
        plateRT.anchoredPosition = new Vector2(0f, 1f);
        Image plateImg = plateObj.AddComponent<Image>();
        plateImg.color = new Color(0.02f, 0.04f, 0.08f, 0.72f);

        // 1. Callsign + Team Tag Label
        GameObject nameObj = new GameObject("NameLabel");
        nameObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform nameRT = nameObj.AddComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0.5f, 0.5f);
        nameRT.anchorMax = new Vector2(0.5f, 0.5f);
        nameRT.sizeDelta = new Vector2(220, 26);
        nameRT.anchoredPosition = new Vector2(0f, 9f);

        _overheadNameText = nameObj.AddComponent<Text>();
        _overheadNameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _overheadNameText.text = GetFullDisplayName();
        _overheadNameText.fontSize = 15;
        _overheadNameText.fontStyle = FontStyle.Bold;
        _overheadNameText.alignment = TextAnchor.MiddleCenter;
        _overheadNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _overheadNameText.color = GetTacticalColor();

        Outline outline = nameObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        // 2. Shield Bar Slider (Top thin cyan bar)
        GameObject sSliderObj = new GameObject("ShieldSlider");
        sSliderObj.transform.SetParent(_overheadCanvasObj.transform, false);
        RectTransform ssRT = sSliderObj.AddComponent<RectTransform>();
        ssRT.anchorMin = new Vector2(0.5f, 0.5f);
        ssRT.anchorMax = new Vector2(0.5f, 0.5f);
        ssRT.sizeDelta = new Vector2(90, 5);
        ssRT.anchoredPosition = new Vector2(0f, -5f);

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
        _overheadHealthFillImg = fill.AddComponent<Image>();
        _overheadHealthFillImg.color = GetTacticalColor();

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

        // Keep ally (blue) vs enemy (red) colors dynamically synced if local player team changes
        int currentLocalTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        if (currentLocalTeam != _cachedLocalPlayerTeam)
        {
            _cachedLocalPlayerTeam = currentLocalTeam;
            RefreshTacticalColors();
        }

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
            if (_downedGraceTimer > 0f)
            {
                _downedGraceTimer -= Time.deltaTime;
            }

            // Pause bleedout timer while a squadmate is actively channeling revive on us
            if (reviveProgress <= 0.02f)
            {
                downedTimer -= Time.deltaTime;
            }

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

            // Downed collapse / prone posture handling
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                if (_animator.HasState(0, Animator.StringToHash("Die")))
                {
                    var st = _animator.GetCurrentAnimatorStateInfo(0);
                    if (st.IsName("Die"))
                    {
                        if (st.normalizedTime >= 0.95f)
                        {
                            _animator.speed = 0f; // Freeze fallen flat on ground
                        }
                    }
                    else
                    {
                        _animator.Play("Die");
                    }
                    if (_visualRoot != null)
                    {
                        _visualRoot.localPosition = Vector3.zero;
                        _visualRoot.localRotation = Quaternion.identity;
                    }
                }
                else
                {
                    _animator.speed = (reviveProgress <= 0.01f) ? 0.35f : 0f;
                    if (_visualRoot != null)
                    {
                        float crawlSway = (reviveProgress <= 0.01f) ? Mathf.Sin(Time.time * 4.8f) * 5.5f : 0f;
                        _visualRoot.localPosition = Vector3.Lerp(_visualRoot.localPosition, new Vector3(0f, 0.22f, -0.40f), Time.deltaTime * 10f);
                        _visualRoot.localRotation = Quaternion.Slerp(_visualRoot.localRotation, Quaternion.Euler(82f, 0f, crawlSway), Time.deltaTime * 10f);
                    }
                }
            }

            // Slowly crawl toward nearest standing squadmate when not actively being revived
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

                Vector3 crawlVel = crawlDir * (moveSpeed * 0.25f);
                crawlVel.y = _characterController.isGrounded ? -2f : -9.81f;
                _characterController.Move(crawlVel * Time.deltaTime);
            }

            if (downedTimer <= 0f)
            {
                Die(_lastAttackerId);
            }
            return;
        }

        // 2. Out-of-Combat Shield Regeneration (recharges after 4.2s without taking damage)
        if (_shieldRegenDelayTimer > 0f)
        {
            _shieldRegenDelayTimer -= Time.deltaTime;
        }
        else if (currentShield < maxShield)
        {
            currentShield = Mathf.Min(maxShield, currentShield + 14f * Time.deltaTime);
            if (_overheadShieldSlider != null)
            {
                _overheadShieldSlider.value = currentShield;
            }
        }

        // 3. Face overhead canvas toward Camera.main
        if (_overheadCanvasObj != null && Camera.main != null)
        {
            _overheadCanvasObj.transform.rotation = Camera.main.transform.rotation;
        }

        // 4. Handle Active Weapon Reload
        if (isReloading)
        {
            _reloadTimer -= Time.deltaTime;
            if (_reloadTimer <= 0f)
            {
                isReloading = false;
                currentAmmo = maxAmmo;
                if (_overheadNameText != null)
                {
                    _overheadNameText.text = GetFullDisplayName();
                    _overheadNameText.color = GetTacticalColor();
                }
            }
        }

        // 5. Scan for downed friendly allies (Player or friendly bots) to prioritize resurrecting them
        _downedAllyTarget = null;
        _downedAllyBot = null;
        _targetIsDownedPlayer = false;

        // Medics brave closer threats (3.0m) than standard classes (4.2m) to rescue fallen teammates
        float dangerThreshold = (operativeClassId == "combat_medic") ? 3.0f : 4.2f;
        bool inImmediateDanger = (_currentTarget != null && Vector3.Distance(transform.position, _currentTarget.position) < dangerThreshold);

        if (!inImmediateDanger)
        {
            int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

            // Priority A: Check if human player is on our team and downed (ignore player during simulation mode)
            if (localPlayerTeam == this.teamId && OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isSimulationMode && OasisShooterManager.Instance.isPlayerDowned)
            {
                Transform player = OasisPlayerHelper.FindPlayerTransform();
                if (player != null)
                {
                    _downedAllyTarget = player;
                    _targetIsDownedPlayer = true;
                }
            }

            // Priority B: If player is not downed, check if any friendly AI bot is downed
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

        // Log REVIVE_START telemetry once when acquiring a new downed ally target
        if (_downedAllyTarget != null)
        {
            if (_lastReviveLoggedTarget != _downedAllyTarget)
            {
                _lastReviveLoggedTarget = _downedAllyTarget;
                float revDist = Vector3.Distance(transform.position, _downedAllyTarget.position);
                if (_targetIsDownedPlayer)
                {
                    OasisAITelemetryLogger.Instance?.RecordReviveStartOnPlayer(this, revDist);
                }
                else if (_downedAllyBot != null)
                {
                    OasisAITelemetryLogger.Instance?.RecordReviveStart(this, _downedAllyBot, revDist);
                }
            }
        }
        else
        {
            _lastReviveLoggedTarget = null;
        }

        // 6. Scan and evaluate combat targets (immediately retarget if current target is downed or dead)
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f || _currentTarget == null || !IsTargetAliveAndEnemy(_currentTarget))
        {
            if (_currentTarget != null && !IsTargetAliveAndEnemy(_currentTarget))
            {
                _burstShotsRemaining = 0;
            }
            _retargetTimer = Random.Range(0.85f, 1.55f);
            Transform prev = _currentTarget;
            _currentTarget = FindBestEnemyTarget();
            if (_currentTarget != null && _currentTarget != prev)
            {
                // Crisp reaction time before firing at newly acquired enemy
                _nextFireTime = Time.time + Random.Range(0.22f, 0.48f);
            }
        }

        // 7. Locomotion & Combat / Revive Positioning
        HandleMovementAndPositioning();

        // 8. Weapons & Firing (bots can fire defensive bursts while advancing to a downed ally, only pausing when kneeling)
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

    /// <summary>
    /// Context-aware tactical maneuver evaluator:
    /// - Chooses Tactical Fallback (2) when shields are broken and health is low, or while reloading.
    /// - Chooses Aggressive Push (0) when healthy with shields or finishing off a weakened enemy (<45 HP).
    /// - Chooses Lateral Flank (1) for flanker classes or when coordinating pincer fire with squadmates.
    /// </summary>
    private int SelectTacticalManeuver(float distToTarget)
    {
        if (isReloading || (currentShield <= 5f && currentHealth < 60f))
        {
            return 2; // Tactical Fallback to recharge shields / finish reload
        }

        var targetAI = _currentTarget != null ? _currentTarget.GetComponent<OasisCombatAI>() : null;
        bool targetWeak = (targetAI != null && (targetAI.currentHealth + targetAI.currentShield) < 48f);

        if (targetWeak || distToTarget > 22f)
        {
            return 0; // Aggressive Push to close distance or finish weak target
        }

        if (operativeClassId == "cyborg_specops" || operativeClassId == "cyber_outlaw")
        {
            return (Random.value < 0.7f) ? 1 : 0; // High-speed flankers prefer Lateral Flank
        }

        if (operativeClassId == "heavy_warrior")
        {
            return (currentShield > 20f) ? 0 : 1; // Heavy Warrior pushes frontline while shields hold
        }

        if (operativeClassId == "combat_medic")
        {
            return (distToTarget < 15f) ? 2 : 1; // Medic stays at mid-range support distance
        }

        // Balanced classes: mix Lateral Flank and Aggressive Push while healthy
        return (Random.value < 0.55f) ? 1 : 0;
    }

    /// <summary>
    /// 3-Ray Whisker Obstacle Avoidance + Squadmate Separation so bots glide smoothly around canyon boulders.
    /// </summary>
    private Vector3 ApplyWhiskerAvoidanceAndSeparation(Vector3 desiredDir)
    {
        if (desiredDir.sqrMagnitude < 0.01f) return desiredDir;
        desiredDir.y = 0f;
        desiredDir.Normalize();

        Vector3 origin = transform.position + Vector3.up * 0.85f;
        Vector3 leftWhisker = Quaternion.Euler(0f, -35f, 0f) * desiredDir;
        Vector3 rightWhisker = Quaternion.Euler(0f, 35f, 0f) * desiredDir;

        bool centerBlocked = IsRayBlockedByEnvironment(origin, desiredDir, 2.8f, out RaycastHit centerHit);
        bool leftBlocked = IsRayBlockedByEnvironment(origin, leftWhisker, 2.3f, out _);
        bool rightBlocked = IsRayBlockedByEnvironment(origin, rightWhisker, 2.3f, out _);

        Vector3 steered = desiredDir;
        if (centerBlocked)
        {
            if (!leftBlocked && rightBlocked)
            {
                steered = (desiredDir + Quaternion.Euler(0f, -65f, 0f) * desiredDir * 1.8f).normalized;
            }
            else if (!rightBlocked && leftBlocked)
            {
                steered = (desiredDir + Quaternion.Euler(0f, 65f, 0f) * desiredDir * 1.8f).normalized;
            }
            else
            {
                Vector3 slide = Vector3.Cross(Vector3.up, centerHit.normal) * _strafeDir;
                steered = (desiredDir * 0.3f + slide * 1.9f).normalized;
            }
        }
        else if (leftBlocked && !rightBlocked)
        {
            steered = (desiredDir + rightWhisker * 0.65f).normalized;
        }
        else if (rightBlocked && !leftBlocked)
        {
            steered = (desiredDir + leftWhisker * 0.65f).normalized;
        }

        // Gentle squadmate separation so friendly bots don't stack on the exact same firing position
        Vector3 separation = Vector3.zero;
        foreach (var b in ActiveBots)
        {
            if (b == null || b == this || b._isDead || b.isDowned) continue;
            if (b.teamId == this.teamId)
            {
                Vector3 diff = transform.position - b.transform.position;
                diff.y = 0f;
                float d = diff.magnitude;
                if (d > 0.05f && d < 2.4f)
                {
                    separation += diff.normalized * ((2.4f - d) * 0.55f);
                }
            }
        }

        Vector3 finalDir = steered + separation;
        finalDir.y = 0f;
        return finalDir.sqrMagnitude > 0.01f ? finalDir.normalized : desiredDir;
    }

    private bool IsRayBlockedByEnvironment(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit envHit)
    {
        envHit = default;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.isTrigger &&
                hit.collider.GetComponentInParent<OasisCombatAI>() == null &&
                hit.collider.GetComponentInParent<CharacterController>() == null &&
                !hit.collider.CompareTag("Player"))
            {
                envHit = hit;
                return true;
            }
        }
        return false;
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
            transform.position = OasisSpawnSystem.Instance.GetTeamSpawnPosition(this.teamId, this.squadSlot);
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
            if (IsRayBlockedByEnvironment(kneeLevel, transform.forward, 1.4f, out _))
            {
                // Check if head/chest level is clear (ensuring it's a vaultable rock/step, not a high cliff)
                Vector3 headLevel = transform.position + Vector3.up * 1.5f;
                if (!IsRayBlockedByEnvironment(headLevel, transform.forward, 1.6f, out _))
                {
                    _verticalVelocity = obstacleJumpForce;
                    _jumpCooldownTimer = 1.6f;
                    _evasiveJitter = transform.forward * 3.5f;
                    if (_animator != null) _animator.Play("Run_guard_AR");
                }
            }
        }

        // Anti-Stuck Detection: ONLY trigger if bot was actively trying to move for >1.4s but made no progress
        bool isKneelingRevive = (_downedAllyTarget != null && Vector3.Distance(transform.position, _downedAllyTarget.position) <= 2.4f);
        if (_intendedMoveSpeed > 0.8f && !isDowned && !isKneelingRevive)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= 1.4f)
            {
                if ((transform.position - _lastSampledPos).sqrMagnitude < 0.22f)
                {
                    bool lowBlocked = IsRayBlockedByEnvironment(transform.position + Vector3.up * 0.38f, transform.forward, 1.5f, out _);

                    if (enableObstacleJump && (_characterController.isGrounded || _verticalVelocity <= 0f) && (lowBlocked || Random.value < 0.7f))
                    {
                        _verticalVelocity = obstacleJumpForce;
                        _jumpCooldownTimer = 1.8f;
                        Vector3 jumpDir = transform.forward * 4.2f + (Random.value > 0.5f ? transform.right : -transform.right) * 2.0f;
                        _evasiveJitter = jumpDir;
                        _strafeDir = -_strafeDir;
                        if (_animator != null) _animator.Play("Run_guard_AR");

                        OasisAITelemetryLogger.Instance?.RecordStuckIncident(this, transform.position, "Obstacle Jump");
                    }
                    else
                    {
                        _strafeDir = -_strafeDir;
                        Vector3 awayDir = (transform.right * _strafeDir) - transform.forward * 0.65f;
                        _evasiveJitter = awayDir.normalized * 3.6f;

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

            if (dist > 2.4f)
            {
                // Sprint toward fallen ally using 3-ray whisker obstacle avoidance, while still facing enemy threat if one is nearby
                Vector3 moveDir = ApplyWhiskerAvoidanceAndSeparation(toAlly.normalized);
                if (_currentTarget != null)
                {
                    Vector3 toEnemy = _currentTarget.position - transform.position;
                    toEnemy.y = 0f;
                    if (toEnemy.sqrMagnitude > 0.1f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toEnemy.normalized), Time.deltaTime * 9f);
                    }
                }
                else if (moveDir.sqrMagnitude > 0.05f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * 10f);
                }

                float sprintMultiplier = (operativeClassId == "combat_medic") ? 1.55f : 1.42f;
                moveVelocity = moveDir * (moveSpeed * sprintMultiplier);
                _intendedMoveSpeed = moveVelocity.magnitude;
                if (_animator != null) _animator.Play("Run_guard_AR");
            }
            else
            {
                // In range! Face ally, kneel, and channel resurrection (Combat Medic revives in 2.0s, standard in 3.0s)
                moveVelocity = Vector3.zero;
                _intendedMoveSpeed = 0f;
                if (toAlly.sqrMagnitude > 0.05f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toAlly.normalized), Time.deltaTime * 10f);
                }
                if (_animator != null)
                {
                    _animator.speed = 1.0f;
                    _animator.Play("Idle_Ducking_AR");
                }

                float reviveDuration = (operativeClassId == "combat_medic") ? 2.0f : 3.0f;

                if (_targetIsDownedPlayer && OasisShooterManager.Instance != null)
                {
                    bool wasDowned = OasisShooterManager.Instance.isPlayerDowned;
                    OasisShooterManager.Instance.ChannelPlayerRevive(Time.deltaTime / reviveDuration, this.botName);
                    if (wasDowned && !OasisShooterManager.Instance.isPlayerDowned)
                    {
                        this.score += 75;
                        OasisAITelemetryLogger.Instance?.RecordReviveSuccessOnPlayer(this);
                        _lastReviveLoggedTarget = null;
                    }
                }
                else if (_downedAllyBot != null)
                {
                    _downedAllyBot.reviveProgress += Time.deltaTime / reviveDuration;
                    if (_downedAllyBot.reviveProgress >= 1.0f)
                    {
                        _downedAllyBot.Revive();
                        this.score += 75;
                        OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>{botName} RESURRECTED {_downedAllyBot.botName}!</color>", 3.0f);
                        OasisAITelemetryLogger.Instance?.RecordReviveSuccess(this, _downedAllyBot);
                        _lastReviveLoggedTarget = null;
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
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 9.5f);
            }

            // Immediate tactical override if shields break under low health or reload begins
            bool needsEmergencyFallback = isReloading || (currentShield <= 2f && currentHealth < 55f);
            if (needsEmergencyFallback && _currentManeuver != 2)
            {
                _currentManeuver = 2;
                _maneuverTimer = Random.Range(3.2f, 4.5f);
                _strafeDir = -_strafeDir;
                OasisAITelemetryLogger.Instance?.RecordManeuverChange(this, _currentManeuver);
            }

            // Cycle context-aware combat maneuvers
            _maneuverTimer -= Time.deltaTime;
            if (_maneuverTimer <= 0f)
            {
                _maneuverTimer = Random.Range(3.4f, 5.2f);
                int nextManeuver = SelectTacticalManeuver(dist);
                _strafeDir = (Random.value > 0.35f) ? -_strafeDir : _strafeDir;
                if (nextManeuver != _currentManeuver)
                {
                    _currentManeuver = nextManeuver;
                    OasisAITelemetryLogger.Instance?.RecordManeuverChange(this, _currentManeuver);
                }
            }

            Vector3 strafeVector = Vector3.Cross(Vector3.up, toTarget.normalized) * _strafeDir;
            Vector3 moveDir;

            if (isReloading)
            {
                // When reloading: actively backpedal and strafe wide to break enemy tracking
                moveDir = (-toTarget.normalized * 0.70f + strafeVector * 0.75f).normalized;
                moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                moveVelocity = moveDir * (moveSpeed * 1.30f);
            }
            else if (dist < 6.5f)
            {
                // DANGER ZONE (Too close!): Immediate tactical backpedal to restore stand-off distance
                moveDir = (-toTarget.normalized * 0.85f + strafeVector * 0.55f).normalized;
                moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                moveVelocity = moveDir * (moveSpeed * 1.25f);
            }
            else if (dist < 11.5f)
            {
                // CLOSE QUARTERS: Create spacing while strafing
                moveDir = (-toTarget.normalized * 0.40f + strafeVector * 0.88f).normalized;
                moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                moveVelocity = moveDir * (moveSpeed * 1.15f);
            }
            else if (dist > 25.0f)
            {
                // LONG RANGE: Close into combat range with angled flank advance
                moveDir = (toTarget.normalized * 0.78f + strafeVector * 0.52f).normalized;
                moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                moveVelocity = moveDir * (moveSpeed * 1.25f);
            }
            else
            {
                // IDEAL WEAPON COMBAT ZONE (11.5m - 25m): Context-driven dueling
                switch (_currentManeuver)
                {
                    case 0: // Aggressive Push: angled advance with strong lateral strafe
                        moveDir = (toTarget.normalized * 0.38f + strafeVector * 0.85f).normalized;
                        moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                        moveVelocity = moveDir * (moveSpeed * 1.16f);
                        break;

                    case 1: // Lateral Flank: wide high-speed circling around target
                        moveDir = (strafeVector * 0.96f + toTarget.normalized * 0.14f).normalized;
                        moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                        moveVelocity = moveDir * (moveSpeed * 1.14f);
                        break;

                    case 2: // Tactical Fallback: retreat toward cover while strafing
                    default:
                        moveDir = (-toTarget.normalized * 0.55f + strafeVector * 0.80f).normalized;
                        moveDir = ApplyWhiskerAvoidanceAndSeparation(moveDir);
                        moveVelocity = moveDir * (moveSpeed * 1.12f);
                        break;
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
                Vector3 moveDir = ApplyWhiskerAvoidanceAndSeparation(toCenter.normalized);
                Quaternion lookRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
                moveVelocity = moveDir * (moveSpeed * 0.98f);
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

        // Stop firing only when actively kneeling at a downed ally (< 2.4m); allow covering fire while sprinting to them!
        if (_downedAllyTarget != null && Vector3.Distance(transform.position, _downedAllyTarget.position) <= 2.4f)
        {
            _burstShotsRemaining = 0;
            return;
        }

        // Never fire at a target that has already been knocked down or eliminated
        if (!IsTargetAliveAndEnemy(_currentTarget))
        {
            _burstShotsRemaining = 0;
            _retargetTimer = 0f;
            return;
        }

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
        if (!HasClearLineOfSight(_currentTarget))
        {
            _burstShotsRemaining = 0;
            return;
        }

        // Handle active burst fire sequence
        if (_burstShotsRemaining > 0)
        {
            _burstTimer -= Time.deltaTime;
            if (_burstTimer <= 0f)
            {
                _burstTimer = 0.21f;
                _burstShotsRemaining--;
                ExecuteSingleBlasterShot();
            }
            return;
        }

        // Trigger next burst fire
        if (Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + Random.Range(1.15f, 2.25f);
            _burstShotsRemaining = Random.Range(2, 5);
            _burstTimer = 0.04f;
        }
    }

    private bool HasClearLineOfSight(Transform target)
    {
        if (target == null) return false;
        Vector3 muzzle = _muzzleTransform != null ? _muzzleTransform.position : transform.position + Vector3.up * 1.35f;
        Vector3 targetAim = target.position + Vector3.up * 1.15f;
        Vector3 toAim = targetAim - muzzle;
        float dist = toAim.magnitude;
        if (dist <= 0.6f) return true;

        RaycastHit[] hits = Physics.RaycastAll(muzzle, toAim.normalized, dist - 0.35f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i].collider;
            if (c == null || c.isTrigger) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (c.transform == target || c.transform.IsChildOf(target)) continue;
            if (c.GetComponentInParent<OasisCombatAI>() != null) continue;
            if (c.GetComponentInParent<CharacterController>() != null) continue;
            return false; // Environment obstacle blocks line of sight
        }
        return true;
    }

    private void ExecuteSingleBlasterShot()
    {
        if (_currentTarget != null && !IsTargetAliveAndEnemy(_currentTarget))
        {
            _burstShotsRemaining = 0;
            _retargetTimer = 0f;
            return;
        }

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
            float spreadRadius = Mathf.Clamp(targetDist * 0.030f, 0.16f, 0.78f);
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

        // Raycast against scene (filter out triggers, shooter, friendly teammates, and already-downed operatives)
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
                if (localPlayerTeam == this.teamId || (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned))
                {
                    // Friendly teammate or already downed player: laser passes through cleanly
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
                if (hitAI.teamId == this.teamId || hitAI.isDowned || hitAI.isDead)
                {
                    // Friendly AI operative or already downed operative: laser passes through so downed operatives can be revived
                    continue;
                }
                else
                {
                    // Hostile standing AI enemy hit — record hit first so chronological log shows HIT before KNOCKDOWN
                    laserEnd = h.point;
                    this.score += 15;
                    float hitDist = Vector3.Distance(muzzle, h.point);
                    OasisAITelemetryLogger.Instance?.RecordHitLanded(this, hitAI, damagePerShot, hitDist);
                    hitAI.TakeDamage(damagePerShot, h.point, h.normal, this.GetHashCode(), this.teamId, hitDist);
                    BlasterVFX.SpawnImpact(h.point, h.normal, false);
                    if (hitAI.isDowned)
                    {
                        _burstShotsRemaining = 0;
                        _retargetTimer = 0f;
                    }
                    break;
                }
            }

            // Hit environment (dune, rock, wall)
            laserEnd = h.point;
            BlasterVFX.SpawnImpact(h.point, h.normal, false);
            break;
        }

        // Colored laser beam using tactical color (blue for ally, red for enemy)
        BlasterVFX.SpawnLaserBeam(muzzle, laserEnd, GetTacticalColor());
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, int attackerId = 0, int attackerTeamId = -1, float hitDistance = 0f)
    {
        if (_isDead) return;
        if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isTeamBattleActive) return;

        if (attackerTeamId >= 0)
        {
            _lastAttackerTeamId = attackerTeamId;
        }
        if (hitDistance > 0.01f)
        {
            _lastHitDistance = hitDistance;
        }
        else if (hitPoint != Vector3.zero)
        {
            _lastHitDistance = Vector3.Distance(transform.position, hitPoint);
        }

        if (isDowned)
        {
            // Newly downed operatives have a 0.85s invulnerability grace period so stray pellets/bursts don't instant-execute them
            if (_downedGraceTimer > 0f) return;

            downedTimer -= 1.2f;
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

        _shieldRegenDelayTimer = 4.2f;
        _lastAttackerId = attackerId;

        // Slight revive setback when taking fire while reviving (0.08f instead of 0.25f so medics can power through)
        if (_downedAllyBot != null && _downedAllyBot.reviveProgress > 0f)
        {
            _downedAllyBot.reviveProgress = Mathf.Max(0f, _downedAllyBot.reviveProgress - 0.08f);
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
        _downedGraceTimer = 0.85f;
        reviveProgress = 0f;
        _burstShotsRemaining = 0;
        _lastAttackerId = attackerId;

        // Transition visual root and play Die collapse animation (or prone posture)
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            if (_animator.HasState(0, Animator.StringToHash("Die")))
            {
                _animator.speed = 1.0f;
                _animator.Play("Die");
                if (_visualRoot != null)
                {
                    _visualRoot.localPosition = Vector3.zero;
                    _visualRoot.localRotation = Quaternion.identity;
                }
            }
            else
            {
                _animator.speed = 0.35f;
                if (_animator.HasState(0, Animator.StringToHash("Idle_Ducking_AR")))
                {
                    _animator.Play("Idle_Ducking_AR");
                }
                else
                {
                    _animator.Play("WalkFront_Shoot_AR");
                }
                if (_visualRoot != null)
                {
                    _visualRoot.localPosition = new Vector3(0f, 0.22f, -0.40f);
                    _visualRoot.localRotation = Quaternion.Euler(82f, 0f, 0f);
                }
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
                if (_lastHitDistance <= 0.01f)
                {
                    _lastHitDistance = Vector3.Distance(attackerBot.transform.position, transform.position);
                }
            }
        }

        // Immediately award knockdown score, update team kills, and refresh HUD
        OasisShooterManager.Instance?.OnCombatAIDowned(this, isPlayerDownedThis, _lastAttackerTeamId);
        OasisAITelemetryLogger.Instance?.RecordKnockdown(this, attackerId, _lastHitDistance);

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
        currentHealth = maxHealth * 0.65f;
        currentShield = maxShield * 0.65f;
        _shieldRegenDelayTimer = 1.5f;

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
                _overheadNameText.text = GetFullDisplayName();
                _overheadNameText.color = GetTacticalColor();
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
            if (_overheadBeaconMat != null)
            {
                Color bc = GetTacticalColor();
                _overheadBeaconMat.color = bc;
                if (_overheadBeaconMat.HasProperty("_BaseColor")) _overheadBeaconMat.SetColor("_BaseColor", bc);
            }
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

        // When respawning from bleedout death, combatant respawns back inside starting base outpost in its assigned squad slot
        OasisSpawnSystem.Instance.RespawnCombatant(transform, this.teamId, this.squadSlot);

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
        _shieldRegenDelayTimer = 0f;

        if (_characterController != null) _characterController.enabled = true;
        if (_overheadCanvasObj != null)
        {
            _overheadCanvasObj.transform.localPosition = new Vector3(0f, 3.42f, 0f);
            _overheadCanvasObj.SetActive(true);
            if (_overheadNameText != null)
            {
                _overheadNameText.text = GetFullDisplayName();
                _overheadNameText.color = GetTacticalColor();
            }
            if (_overheadShieldSlider != null) _overheadShieldSlider.value = maxShield;
            if (_overheadHealthSlider != null) _overheadHealthSlider.value = maxHealth;
        }

        if (_overheadBeaconObj != null)
        {
            _overheadBeaconObj.transform.localPosition = new Vector3(0f, 2.85f, 0f);
            _overheadBeaconObj.SetActive(true);
            if (_overheadBeaconMat != null)
            {
                Color bc = GetTacticalColor();
                _overheadBeaconMat.color = bc;
                if (_overheadBeaconMat.HasProperty("_BaseColor")) _overheadBeaconMat.SetColor("_BaseColor", bc);
            }
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
                    if (HasClearLineOfSight(player))
                    {
                        score -= 14.0f; // Strong preference for targets with unobstructed line of sight
                    }
                    if (score < lowestThreatScore)
                    {
                        lowestThreatScore = score;
                        bestTarget = player;
                    }
                }
            }
        }

        // 2. Check other AI bots on opposing team (prioritize visible standing enemies with threat, focus-fire & revenge weighting)
        foreach (var bot in ActiveBots)
        {
            if (bot == null || bot == this || bot._isDead || bot.isDowned) continue;
            if (bot.teamId != this.teamId)
            {
                float d = Vector3.Distance(transform.position, bot.transform.position);
                float threatScore = d;

                // Line-of-Sight bonus: prioritize enemies we can immediately hit without rock obstruction
                if (HasClearLineOfSight(bot.transform))
                {
                    threatScore -= 14.0f;
                }

                // Retaliation priority for the enemy actively shooting us
                if (bot.GetHashCode() == _lastAttackerId)
                {
                    threatScore -= 9.0f;
                }

                // Focus-fire elimination priority for vulnerable opponents (< 45 HP)
                if (bot.currentHealth < 45f)
                {
                    threatScore -= 6.0f;
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

        // If target is the local player, verify player is on opposing team and not downed
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
