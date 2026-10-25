using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Dedicated Launch-Time Character Selection Screen for Oasis.
/// Displays an exclusive full-page briefing screen prior to entering gameplay.
/// Features interactive operative chassis selection on the left and a live, 
/// real-time 3D rotating character preview with full tactical dossier on the right.
/// </summary>
public class OasisCharacterCustomizer : MonoBehaviour
{
    public static OasisCharacterCustomizer Instance { get; private set; }

    [System.Serializable]
    public class OperativeProfile
    {
        public string id;
        public string name;
        public string role;
        public string description;
        public string textureResourcePath;
        public Color themeColor;

        // Tactical Dossier Metrics (0.0 to 1.0)
        public float armor;
        public float mobility;
        public float firepower;
        public float shieldTech;

        public string primaryWeapon;
        public string passiveTrait;
        public string passiveDescription;

        public OperativeProfile(
            string id, string name, string role, string desc, string texPath, Color color,
            float armor, float mobility, float firepower, float shieldTech,
            string primaryWeapon, string passiveTrait, string passiveDescription)
        {
            this.id = id;
            this.name = name;
            this.role = role;
            this.description = desc;
            this.textureResourcePath = texPath;
            this.themeColor = color;
            this.armor = armor;
            this.mobility = mobility;
            this.firepower = firepower;
            this.shieldTech = shieldTech;
            this.primaryWeapon = primaryWeapon;
            this.passiveTrait = passiveTrait;
            this.passiveDescription = passiveDescription;
        }
    }

    public List<OperativeProfile> operatives = new List<OperativeProfile>()
    {
        new OperativeProfile(
            "heavy_warrior", "Titanium Heavy Warrior", "ASSAULT / FRONTLINE", 
            "Reinforced titanium chassis engineered for intense frontline breaches. High kinetic resistance with balanced assault firepower.", 
            "RobotHero_URP", new Color(0.25f, 0.75f, 1.0f),
            0.95f, 0.60f, 0.85f, 0.75f,
            "Micro-Bomb Flak Mortar", "[F] TITANIUM SLAM", "Discharges kinetic shockwave (45 AoE dmg) & grants +60 HP temporary overshield."),

        new OperativeProfile(
            "desert_commando", "Desert Tactical Commando", "DEMOLITIONS / INFANTRY", 
            "Specialized desert warfare rig with high-traction dune boots and blast-resistant ballistic weave armor.", 
            "Characters/Textures/skin_soldier", new Color(0.95f, 0.72f, 0.28f),
            0.80f, 0.80f, 0.90f, 0.65f,
            "Plasma Scatter Shotgun", "[F] CLUSTER MORTAR", "Launches a 4-fragment high-explosive incendiary cluster barrage."),

        new OperativeProfile(
            "cyborg_specops", "Cyborg Special Operative", "INFILTRATOR / AGILITY", 
            "Cybernetically augmented operative equipped with nanotech fiber suit for rapid slide evasion and precision targeting.", 
            "Characters/Textures/cyborgFemaleA", new Color(0.20f, 0.95f, 0.90f),
            0.65f, 0.95f, 0.85f, 0.80f,
            "Continuous Phase Laser Beam", "[F] TACTICAL CLOAK", "Active camouflage (breaks enemy AI target lock), +35% speed & instant weapon recharge."),

        new OperativeProfile(
            "unit07_android", "Unit-07 Combat Android", "HEAVY MECH / VANGUARD", 
            "Autonomous heavy military android with reinforced chassis, internal auto-repair units, and recoil dampeners.", 
            "Characters/Textures/skin_robot", new Color(0.35f, 1.0f, 0.55f),
            0.90f, 0.68f, 0.85f, 0.90f,
            "Continuous Phase Laser Beam", "[F] CORE EMP PULSE", "14m electromagnetic wave stuns hostiles (3.5s), strips shields & maxes beam ammo."),

        new OperativeProfile(
            "wasteland_nomad", "Wasteland Nomad Adventurer", "RECON / SURVIVAL", 
            "Seasoned desert scout wearing temperature-regulating dune silks, moisture seals, and lightweight tactical armor.", 
            "Characters/Textures/skin_adventurer", new Color(1.0f, 0.60f, 0.25f),
            0.70f, 0.90f, 0.80f, 0.85f,
            "Long-Range Scout Carbine", "[F] ORBITAL RECON", "Exposes all enemy locations through dunes/cover (8s) with +25% blaster damage buff."),

        new OperativeProfile(
            "combat_medic", "Oasis Combat Medic", "SUPPORT / BIO-SHIELD", 
            "Field operative in sterile hazardous-environment armor equipped with rapid regenerative trauma patches.", 
            "Characters/Textures/survivorFemaleA", new Color(0.98f, 0.40f, 0.45f),
            0.75f, 0.85f, 0.70f, 0.95f,
            "Tactical Defense Carbine", "[F] NANO-MENDER FIELD", "Deployable nanotech aura regenerates 18 HP/s for 6s and instantly revives nearby squadmates."),

        new OperativeProfile(
            "cyber_outlaw", "Cyber Outlaw Specialist", "GUNSLINGER / SHARPSHOOTER", 
            "Rogue gunslinger in modified cyberwear with overclocked neural fire-rate boosters and critical reflex wiring.", 
            "Characters/Textures/criminalMaleA", new Color(0.92f, 0.35f, 0.95f),
            0.70f, 0.90f, 0.95f, 0.70f,
            "High-Caliber Dual Pistols & AR", "Deadshot Overclock", "Weakspot critical hits deal 50% extra kinetic damage.")
    };

    public int selectedOperativeIndex = 0;
    public bool isSelectionActive = true;

    // UI References
    private GameObject _selectionCanvas;
    private List<Image> _cardBackgrounds = new List<Image>();
    private List<Image> _cardBorders = new List<Image>();
    private List<Text> _cardButtonTexts = new List<Text>();
    private List<Image> _cardAccents = new List<Image>();

    // Right-side Dossier UI elements
    private Text _dossierNameText;
    private Text _dossierRoleText;
    private Text _dossierDescText;
    private Image _dossierRoleBadge;
    private RectTransform _statBarArmor;
    private RectTransform _statBarMobility;
    private RectTransform _statBarFirepower;
    private RectTransform _statBarShield;
    private Text _statValArmor;
    private Text _statValMobility;
    private Text _statValFirepower;
    private Text _statValShield;
    private Text _dossierWeaponText;
    private Text _dossierPassiveText;
    private Text _confirmButtonText;
    private Image _confirmButtonBg;
    private Button _confirmButton;
    private Image _roleHeaderBtnBg;
    private Text _roleHeaderBtnText;

    [Header("Match Settings")]
    public int selectedTeam = 0; // 0 = Alpha, 1 = Bravo, 2 = Charlie, 3 = Delta
    public int selectedNumTeams = 2; // 2, 3, or 4 Teams
    public int selectedPlayersPerTeam = 2; // 1, 2, 3, or 4 Players per Team
    public string selectedMatchFormat = "2v2"; // Derived summary string (e.g. "2 TEAMS • 2/TEAM (2v2)")
    public bool isSpectatorMode = false; // True = AI Simulation / Spectator mode

    // Match Config UI References
    private readonly Image[] _teamBtnBgs = new Image[4];
    private readonly Text[] _teamBtnTexts = new Text[4];
    private Image _btnSpectatorBg;
    private Text _btnSpectatorText;
    private readonly Dictionary<int, Image> _numTeamsBtnBgs = new Dictionary<int, Image>();
    private readonly Dictionary<int, Text> _numTeamsBtnTexts = new Dictionary<int, Text>();
    private readonly Dictionary<int, Image> _playersPerTeamBtnBgs = new Dictionary<int, Image>();
    private readonly Dictionary<int, Text> _playersPerTeamBtnTexts = new Dictionary<int, Text>();

    // Dynamic Team & Player Roster UI References (Supports 2, 3, or 4 Teams)
    private readonly GameObject[] _teamRosterBoxes = new GameObject[4];
    private readonly RectTransform[] _teamRosterBoxRTs = new RectTransform[4];
    private readonly Text[] _teamRosterHeaders = new Text[4];
    private readonly Text[] _teamRosterTexts = new Text[4];

    // 10s Countdown Overlay References
    private GameObject _countdownModalObj;
    private Text _countdownSecondsText;
    private Text _countdownSubheaderText;
    private Text _countdownMatchupText;
    private Image _countdownProgressBarFill;
    private Coroutine _countdownCoroutine;
    private bool _isCountingDown = false;
    private bool _isSimCountdown = false;
    private AudioSource _audioSource;

    // 3D Staging Viewport References
    private GameObject _stagingRoot;
    private Camera _previewCamera;
    private RenderTexture _previewRenderTexture;
    private GameObject _previewCharInstance;
    private Renderer[] _previewRenderers;
    private Renderer _glowRingRenderer;
    private Light _rimLight;
    private float _previewYaw = 0f;
    private bool _isDraggingPreview = false;
    private Vector2 _lastMousePos;

    private StarterAssets.StarterAssetsInputs _starterInputs;

    public bool IsLocalPlayerClient()
    {
        return OasisNetworkManager.Instance != null &&
               OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client;
    }

    public bool CanLocalPlayerStartGame()
    {
        return !IsLocalPlayerClient();
    }

    public void OnRoleHeaderButtonClicked()
    {
        if (OasisNetworkManager.Instance != null && !string.IsNullOrEmpty(OasisNetworkManager.Instance.activeRelayJoinCode))
        {
            GUIUtility.systemCopyBuffer = OasisNetworkManager.Instance.activeRelayJoinCode;
            OasisShooterManager.Instance?.ShowNotificationBanner($"RELAY JOIN CODE {OasisNetworkManager.Instance.activeRelayJoinCode} COPIED TO CLIPBOARD!", 3.5f);
            return;
        }

        if (IsLocalPlayerClient())
        {
            SwitchToHostRoleInStaging();
        }
    }

    public void UpdateRoleHeaderBadge()
    {
        if (_roleHeaderBtnBg == null || _roleHeaderBtnText == null) return;

        bool canStart = CanLocalPlayerStartGame();
        if (canStart)
        {
            _roleHeaderBtnBg.color = new Color(0.08f, 0.45f, 0.28f, 0.96f);
            string relayCode = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.activeRelayJoinCode : "";
            if (!string.IsNullOrEmpty(relayCode))
            {
                _roleHeaderBtnText.text = $"<color=#38EF7D>★ HOST // JOIN CODE: <color=#FFFFFF><b>{relayCode}</b></color> (CLICK TO COPY)</color>";
            }
            else if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.IsConnectingOrHostingInProgress)
            {
                _roleHeaderBtnText.text = "<color=#38EF7D>★ HOST // ALLOCATING RELAY CODE...</color>";
            }
            else
            {
                _roleHeaderBtnText.text = "<color=#38EF7D>★ ROLE: SQUAD HOST (HOST STARTS GAME)</color>";
            }
        }
        else
        {
            _roleHeaderBtnBg.color = new Color(0.52f, 0.32f, 0.08f, 0.96f);
            _roleHeaderBtnText.text = "<color=#FFEA00>● ROLE: CLIENT — WAITING FOR HOST  [CLICK FOR HOST]</color>";
        }
    }

    public void SwitchToHostRoleInStaging()
    {
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.EnsureHostSessionForStaging();
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    public void ReturnToMainMenu()
    {
        isSelectionActive = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        var player = OasisPlayerHelper.FindPlayerTransform();
        if (player != null && player.GetComponent<OasisCharacterCustomizer>() == null)
        {
            player.gameObject.AddComponent<OasisCharacterCustomizer>();
            return;
        }

        if (player == null)
        {
            GameObject runner = new GameObject("OasisCharacterCustomizer_Attacher");
            var attacher = runner.AddComponent<AttacherRunner>();
            attacher.StartCoroutine(attacher.WaitForPlayerRoutine());
        }
    }

    private class AttacherRunner : MonoBehaviour
    {
        public IEnumerator WaitForPlayerRoutine()
        {
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForSeconds(0.1f);
                var player = OasisPlayerHelper.FindPlayerTransform();
                if (player != null)
                {
                    if (player.GetComponent<OasisCharacterCustomizer>() == null)
                    {
                        player.gameObject.AddComponent<OasisCharacterCustomizer>();
                    }
                    Destroy(gameObject);
                    yield break;
                }
            }
            Destroy(gameObject);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _starterInputs = GetComponent<StarterAssets.StarterAssetsInputs>();
        selectedOperativeIndex = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
        if (selectedOperativeIndex < 0 || selectedOperativeIndex >= operatives.Count)
        {
            selectedOperativeIndex = 0;
        }

        selectedNumTeams = Mathf.Clamp(PlayerPrefs.GetInt("OasisNumTeams", 2), 2, 4);
        selectedPlayersPerTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayersPerTeam", 2), 1, 4);
        selectedTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayerTeam", 0), 0, selectedNumTeams - 1);
        UpdateMatchFormatString();

        // 1. Hide in-game HUD & Radar while on the character selection page
        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.SetHUDActive(false);
        }
        if (OasisMapSystem.Instance != null)
        {
            OasisMapSystem.Instance.SetMapCanvasActive(false);
        }

        // 2. Setup isolated 3D character preview staging
        Setup3DPreviewStage();

        // 3. Build the full-screen selection page
        BuildLaunchSelectionUI();

        // 4. Update preview and dossier to initial selected operative
        UpdatePreviewAndDossier();
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }

        // If Host already launched the match while this Client was loading OasisScene, deploy immediately!
        if (OasisNetworkManager.AutoDeployOnSceneLoad)
        {
            bool sim = OasisNetworkManager.AutoDeployIsSim;
            OasisNetworkManager.AutoDeployOnSceneLoad = false;
            OasisNetworkManager.AutoDeployIsSim = false;
            ExecuteHostLaunchCommand(selectedNumTeams, selectedPlayersPerTeam, sim);
            return;
        }

        // 5. Enforce cursor & input lock
        StartCoroutine(EnforceLaunchSelectionState());
    }

    private void Update()
    {
        if (!isSelectionActive) return;

        // Handle active 10s countdown shortcuts (ONLY the Host can skip or cancel the countdown)
        if (_isCountingDown && CanLocalPlayerStartGame())
        {
            bool skipPressed = false;
            bool cancelPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                skipPressed = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                cancelPressed = kb.escapeKey.wasPressedThisFrame;
            }
#else
            skipPressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            cancelPressed = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (skipPressed)
            {
                SkipCountdown();
                return;
            }
            if (cancelPressed)
            {
                CancelCountdown();
                return;
            }
        }

        // Rotate the 3D character model continuously if not dragging
        if (!_isDraggingPreview && _previewCharInstance != null)
        {
            _previewYaw += Time.unscaledDeltaTime * 22f;
            _previewCharInstance.transform.localRotation = Quaternion.Euler(0, 180f + _previewYaw, 0);
        }

        // Handle mouse drag over the preview viewport
        HandlePreviewViewportDrag();
    }

    private void HandlePreviewViewportDrag()
    {
        bool down = false;
        bool held = false;
        bool up = false;
        Vector2 mousePos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            down = mouse.leftButton.wasPressedThisFrame;
            held = mouse.leftButton.isPressed;
            up = mouse.leftButton.wasReleasedThisFrame;
            mousePos = mouse.position.ReadValue();
        }
#else
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up = Input.GetMouseButtonUp(0);
        mousePos = Input.mousePosition;
#endif

        if (down)
        {
            // Check if mouse is on the right half of the screen (the 3D viewport region)
            if (mousePos.x > Screen.width * 0.45f && mousePos.y > Screen.height * 0.35f)
            {
                _isDraggingPreview = true;
                _lastMousePos = mousePos;
            }
        }
        else if (held && _isDraggingPreview)
        {
            float deltaX = mousePos.x - _lastMousePos.x;
            _lastMousePos = mousePos;

            _previewYaw -= deltaX * 0.8f;
            if (_previewCharInstance != null)
            {
                _previewCharInstance.transform.localRotation = Quaternion.Euler(0, 180f + _previewYaw, 0);
            }
        }
        else if (up)
        {
            _isDraggingPreview = false;
        }
    }

    private IEnumerator EnforceLaunchSelectionState()
    {
        while (isSelectionActive)
        {
            if (_starterInputs != null)
            {
                _starterInputs.cursorInputForLook = false;
                _starterInputs.cursorLocked = false;
                _starterInputs.move = Vector2.zero;
                _starterInputs.look = Vector2.zero;
                _starterInputs.sprint = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
        }
    }

    private void Setup3DPreviewStage()
    {
        // Place staging stage far below ground away from playable desert world
        Vector3 stagePos = new Vector3(850f, -320f, 850f);
        _stagingRoot = new GameObject("Oasis_CharacterPreview_Stage");
        _stagingRoot.transform.position = stagePos;

        // 1. Create Staging Pedestal Platform
        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Staging_Pedestal";
        pedestal.transform.SetParent(_stagingRoot.transform, false);
        pedestal.transform.localPosition = new Vector3(0f, -0.06f, 0f);
        pedestal.transform.localScale = new Vector3(2.4f, 0.12f, 2.4f);
        var pCol = pedestal.GetComponent<Collider>();
        if (pCol != null) Destroy(pCol);

        Material pedMat = new Material(OasisShaderHelper.GetLitShader());
        pedMat.color = new Color(0.08f, 0.10f, 0.15f);
        pedestal.GetComponent<Renderer>().sharedMaterial = pedMat;

        // 2. Glowing Neon Platform Ring
        GameObject glowRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        glowRing.name = "Staging_GlowRing";
        glowRing.transform.SetParent(_stagingRoot.transform, false);
        glowRing.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        glowRing.transform.localScale = new Vector3(2.55f, 0.04f, 2.55f);
        var rCol = glowRing.GetComponent<Collider>();
        if (rCol != null) Destroy(rCol);

        Material ringMat = new Material(OasisShaderHelper.GetLitShader());
        var op = operatives[selectedOperativeIndex];
        ringMat.color = op.themeColor;
        if (ringMat.HasProperty("_EmissionColor"))
        {
            ringMat.EnableKeyword("_EMISSION");
            ringMat.SetColor("_EmissionColor", op.themeColor * 1.5f);
        }
        _glowRingRenderer = glowRing.GetComponent<Renderer>();
        _glowRingRenderer.sharedMaterial = ringMat;

        // 3. Instantiate Character 3D Model
        GameObject prefab = Resources.Load<GameObject>("RobotHero");
        if (prefab != null)
        {
            _previewCharInstance = Instantiate(prefab, _stagingRoot.transform, false);
            _previewCharInstance.name = "Preview_RobotHero";
            _previewCharInstance.transform.localPosition = Vector3.zero;
            _previewCharInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _previewCharInstance.transform.localScale = Vector3.one;

            var cols = _previewCharInstance.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols) Destroy(c);

            RuntimeAnimatorController animCtrl = Resources.Load<RuntimeAnimatorController>("RobotHero");
            if (animCtrl == null) animCtrl = Resources.Load<RuntimeAnimatorController>("SciFiWarrior");
            var anim = _previewCharInstance.GetComponent<Animator>();
            if (anim == null) anim = _previewCharInstance.AddComponent<Animator>();
            if (anim != null && animCtrl != null)
            {
                anim.runtimeAnimatorController = animCtrl;
                anim.applyRootMotion = false;
            }

            _previewRenderers = _previewCharInstance.GetComponentsInChildren<Renderer>(true);
        }

        // 4. Dedicated Studio Lighting
        // Key Light
        GameObject keyLightObj = new GameObject("Staging_KeyLight");
        keyLightObj.transform.SetParent(_stagingRoot.transform, false);
        keyLightObj.transform.localPosition = new Vector3(1.5f, 3.2f, 3.5f);
        keyLightObj.transform.localRotation = Quaternion.Euler(30f, 205f, 0f);
        var keyLight = keyLightObj.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1.0f, 0.96f, 0.90f);
        keyLight.intensity = 1.4f;

        // Rim Light (Signature operative accent)
        GameObject rimLightObj = new GameObject("Staging_RimLight");
        rimLightObj.transform.SetParent(_stagingRoot.transform, false);
        rimLightObj.transform.localPosition = new Vector3(-1.8f, 2.5f, -2.5f);
        rimLightObj.transform.localRotation = Quaternion.Euler(20f, 35f, 0f);
        _rimLight = rimLightObj.AddComponent<Light>();
        _rimLight.type = LightType.Directional;
        _rimLight.color = op.themeColor;
        _rimLight.intensity = 2.2f;

        // Fill Light
        GameObject fillLightObj = new GameObject("Staging_FillLight");
        fillLightObj.transform.SetParent(_stagingRoot.transform, false);
        fillLightObj.transform.localPosition = new Vector3(-2.5f, 1.5f, 3.0f);
        fillLightObj.transform.localRotation = Quaternion.Euler(15f, 140f, 0f);
        var fillLight = fillLightObj.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(0.45f, 0.60f, 0.85f);
        fillLight.intensity = 0.65f;

        // 5. Dedicated Camera & High-Res 1:1 Portrait/Square RenderTexture
        _previewRenderTexture = new RenderTexture(960, 960, 24, RenderTextureFormat.ARGB32);
        _previewRenderTexture.name = "OperativePreview_RenderTexture";
        _previewRenderTexture.antiAliasing = 4;
        _previewRenderTexture.Create();

        GameObject camObj = new GameObject("Staging_Camera");
        camObj.transform.SetParent(_stagingRoot.transform, false);
        // Backed up to 4.50m and centered at y=1.05m to frame full character from head to toe with headroom
        camObj.transform.localPosition = new Vector3(0f, 1.05f, 4.50f);
        camObj.transform.localRotation = Quaternion.Euler(2.0f, 180f, 0f);

        _previewCamera = camObj.AddComponent<Camera>();
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.backgroundColor = new Color(0.045f, 0.060f, 0.090f, 1f);
        _previewCamera.fieldOfView = 30f;
        _previewCamera.aspect = 1.0f;
        _previewCamera.nearClipPlane = 0.1f;
        _previewCamera.farClipPlane = 30f;
        _previewCamera.targetTexture = _previewRenderTexture;
    }

    public void SelectCard(int index)
    {
        selectedOperativeIndex = Mathf.Clamp(index, 0, operatives.Count - 1);
        PlayerPrefs.SetInt("OasisSelectedOperative", selectedOperativeIndex);
        PlayerPrefs.Save();
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }
        UpdatePreviewAndDossier();
    }

    private void UpdatePreviewAndDossier()
    {
        if (selectedOperativeIndex < 0 || selectedOperativeIndex >= operatives.Count) return;
        var op = operatives[selectedOperativeIndex];

        // 1. Update 3D Preview Model Materials & Lighting
        Texture2D skinTex = null;
        if (!string.IsNullOrEmpty(op.textureResourcePath))
        {
            skinTex = Resources.Load<Texture2D>(op.textureResourcePath);
        }

        Material baseMat = Resources.Load<Material>("RobotHero_URP");
        if (baseMat == null) baseMat = new Material(OasisShaderHelper.GetLitShader());
        Material customMat = new Material(baseMat);
        customMat.name = $"{op.id}_PreviewMat";

        if (skinTex != null)
        {
            if (customMat.HasProperty("_BaseMap")) customMat.SetTexture("_BaseMap", skinTex);
            if (customMat.HasProperty("_MainTex")) customMat.SetTexture("_MainTex", skinTex);
            customMat.color = Color.white;
        }
        else
        {
            customMat.color = op.themeColor;
        }

        if (_previewRenderers != null)
        {
            foreach (var r in _previewRenderers)
            {
                if (r != null) r.sharedMaterial = customMat;
            }
        }

        if (_glowRingRenderer != null && _glowRingRenderer.sharedMaterial != null)
        {
            _glowRingRenderer.sharedMaterial.color = op.themeColor;
            if (_glowRingRenderer.sharedMaterial.HasProperty("_EmissionColor"))
            {
                _glowRingRenderer.sharedMaterial.SetColor("_EmissionColor", op.themeColor * 1.5f);
            }
        }

        if (_rimLight != null)
        {
            _rimLight.color = op.themeColor;
        }

        // 2. Refresh Left Roster Selection Cards
        for (int i = 0; i < operatives.Count; i++)
        {
            bool isSel = (i == selectedOperativeIndex);
            Color theme = operatives[i].themeColor;

            if (i < _cardBackgrounds.Count && _cardBackgrounds[i] != null)
            {
                _cardBackgrounds[i].color = isSel 
                    ? new Color(0.12f, 0.22f, 0.35f, 0.98f) 
                    : new Color(0.07f, 0.09f, 0.13f, 0.92f);
            }

            if (i < _cardBorders.Count && _cardBorders[i] != null)
            {
                _cardBorders[i].color = isSel ? theme : new Color(0.15f, 0.18f, 0.24f, 0.8f);
            }

            if (i < _cardButtonTexts.Count && _cardButtonTexts[i] != null)
            {
                _cardButtonTexts[i].text = isSel 
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(theme)}>● ACTIVE</color>" 
                    : "<color=#8FA0B5>SELECT</color>";
                _cardButtonTexts[i].fontStyle = isSel ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // 3. Update Right Tactical Dossier
        if (_dossierNameText != null) _dossierNameText.text = op.name.ToUpper();
        if (_dossierRoleText != null)
        {
            _dossierRoleText.text = op.role;
            _dossierRoleText.color = op.themeColor;
        }
        if (_dossierRoleBadge != null) _dossierRoleBadge.color = op.themeColor;
        if (_dossierDescText != null) _dossierDescText.text = op.description;

        // Stat Bars
        SetStatBar(_statBarArmor, _statValArmor, op.armor);
        SetStatBar(_statBarMobility, _statValMobility, op.mobility);
        SetStatBar(_statBarFirepower, _statValFirepower, op.firepower);
        SetStatBar(_statBarShield, _statValShield, op.shieldTech);

        if (_dossierWeaponText != null) _dossierWeaponText.text = $"PRIMARY: <color=#FFFFFF>{op.primaryWeapon}</color>";
        if (_dossierPassiveText != null)
        {
            _dossierPassiveText.text = $"PASSIVE [<color=#{ColorUtility.ToHtmlStringRGB(op.themeColor)}>{op.passiveTrait}</color>]: {op.passiveDescription}";
        }

        // 4. Unified Launch Game Mode Button (ONLY the Host can start the game!)
        UpdateMatchFormatString();
        bool canStart = CanLocalPlayerStartGame();
        if (_confirmButton != null)
        {
            _confirmButton.interactable = canStart;
        }
        if (_confirmButtonText != null)
        {
            if (!canStart)
            {
                string teamLabel = OasisShooterManager.GetTeamFullName(selectedTeam);
                _confirmButtonText.text = $"WAITING FOR HOST TO START GAME...   //   ONLY THE HOST CAN START THE GAME  ({teamLabel})";
            }
            else if (isSpectatorMode)
            {
                _confirmButtonText.text = $"HOST: LAUNCH GAME MODE (START FOR ALL)   //   {selectedNumTeams} TEAMS × {selectedPlayersPerTeam}/TEAM  •  SPECTATOR AI SIMULATION";
            }
            else
            {
                string teamLabel = OasisShooterManager.GetTeamFullName(selectedTeam);
                _confirmButtonText.text = $"HOST: LAUNCH GAME MODE (START FOR ALL)   //   {selectedNumTeams} TEAMS × {selectedPlayersPerTeam}/TEAM  •  {teamLabel}";
            }
        }
        if (_confirmButtonBg != null)
        {
            if (!canStart)
            {
                _confirmButtonBg.color = new Color(0.16f, 0.20f, 0.28f, 0.96f);
            }
            else
            {
                _confirmButtonBg.color = isSpectatorMode
                    ? new Color(0.12f, 0.48f, 0.72f, 1.0f)
                    : new Color(0.08f, 0.65f, 0.38f, 1.0f);
            }
        }

        if (_roleHeaderBtnBg != null && _roleHeaderBtnText != null)
        {
            if (canStart)
            {
                _roleHeaderBtnBg.color = new Color(0.08f, 0.45f, 0.28f, 0.96f);
                _roleHeaderBtnText.text = "<color=#38EF7D>★ ROLE: SQUAD HOST (HOST STARTS GAME)</color>";
            }
            else
            {
                _roleHeaderBtnBg.color = new Color(0.52f, 0.32f, 0.08f, 0.96f);
                _roleHeaderBtnText.text = "<color=#FFEA00>● ROLE: CLIENT — [CLICK TO SWITCH TO HOST]</color>";
            }
        }

        UpdateRosterDisplay();
    }

    private void UpdateMatchFormatString()
    {
        selectedNumTeams = Mathf.Clamp(selectedNumTeams, 2, 4);
        selectedPlayersPerTeam = Mathf.Clamp(selectedPlayersPerTeam, 1, 4);
        selectedMatchFormat = $"{selectedNumTeams} TEAMS • {selectedPlayersPerTeam}/TEAM";
    }

    public void SetPlayerTeam(int team)
    {
        if (IsLocalPlayerClient())
        {
            selectedTeam = Mathf.Clamp(team, 0, selectedNumTeams - 1);
        }
        else
        {
            selectedTeam = Mathf.Clamp(team, 0, 3);
            if (selectedTeam >= selectedNumTeams)
            {
                selectedNumTeams = selectedTeam + 1;
                PlayerPrefs.SetInt("OasisNumTeams", selectedNumTeams);
            }
        }
        isSpectatorMode = false;
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisPlayerTeam", selectedTeam);
        PlayerPrefs.Save();
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    public void ToggleSpectatorMode()
    {
        if (IsLocalPlayerClient()) return; // Only Host controls match mode
        isSpectatorMode = !isSpectatorMode;
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    public void SetNumTeams(int numTeams)
    {
        if (IsLocalPlayerClient()) return; // Only Host controls number of teams
        selectedNumTeams = Mathf.Clamp(numTeams, 2, 4);
        if (selectedTeam >= selectedNumTeams)
        {
            selectedTeam = selectedNumTeams - 1;
            PlayerPrefs.SetInt("OasisPlayerTeam", selectedTeam);
        }
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisNumTeams", selectedNumTeams);
        PlayerPrefs.Save();
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    public void SetPlayersPerTeam(int playersPerTeam)
    {
        if (IsLocalPlayerClient()) return; // Only Host controls players per team
        selectedPlayersPerTeam = Mathf.Clamp(playersPerTeam, 1, 4);
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisPlayersPerTeam", selectedPlayersPerTeam);
        PlayerPrefs.Save();
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
        }
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    public void SyncMatchConfigFromHost(int numTeams, int playersPerTeam)
    {
        selectedNumTeams = Mathf.Clamp(numTeams, 2, 4);
        selectedPlayersPerTeam = Mathf.Clamp(playersPerTeam, 1, 4);
        if (selectedTeam >= selectedNumTeams)
        {
            selectedTeam = selectedNumTeams - 1;
            PlayerPrefs.SetInt("OasisPlayerTeam", selectedTeam);
        }
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisNumTeams", selectedNumTeams);
        PlayerPrefs.SetInt("OasisPlayersPerTeam", selectedPlayersPerTeam);
        PlayerPrefs.Save();
        RefreshMatchSettingsUI();
        UpdatePreviewAndDossier();
    }

    private void RefreshMatchSettingsUI()
    {
        // 1. Squad Side Buttons (Alpha, Bravo, Charlie, Delta)
        Color[] teamActiveBgs = new Color[]
        {
            new Color(0.12f, 0.45f, 0.75f, 0.98f), // Alpha Blue
            new Color(0.72f, 0.20f, 0.20f, 0.98f), // Bravo Red
            new Color(0.12f, 0.56f, 0.34f, 0.98f), // Charlie Green
            new Color(0.68f, 0.44f, 0.08f, 0.98f)  // Delta Amber
        };

        for (int t = 0; t < 4; t++)
        {
            bool isTeamActiveInMatch = (t < selectedNumTeams);
            bool isTeamSelected = (!isSpectatorMode && selectedTeam == t);
            string shortName = OasisShooterManager.GetTeamShortName(t);
            string colorHex = OasisShooterManager.GetTeamColorHex(t);

            if (_teamBtnBgs[t] != null)
            {
                if (isTeamSelected)
                    _teamBtnBgs[t].color = teamActiveBgs[t];
                else if (isTeamActiveInMatch)
                    _teamBtnBgs[t].color = new Color(0.09f, 0.14f, 0.21f, 0.90f);
                else
                    _teamBtnBgs[t].color = new Color(0.05f, 0.07f, 0.10f, 0.55f);
            }

            if (_teamBtnTexts[t] != null)
            {
                if (isTeamSelected)
                    _teamBtnTexts[t].text = $"<color={colorHex}>● {shortName}</color>";
                else if (isTeamActiveInMatch)
                    _teamBtnTexts[t].text = $"<color=#A8BCD0>{shortName}</color>";
                else
                    _teamBtnTexts[t].text = $"<color=#526274>{shortName} (+)</color>";

                _teamBtnTexts[t].fontStyle = isTeamSelected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // 2. Compact Spectator Button Styling
        if (_btnSpectatorBg != null)
        {
            _btnSpectatorBg.color = isSpectatorMode
                ? new Color(0.55f, 0.35f, 0.08f, 0.98f)
                : new Color(0.08f, 0.12f, 0.18f, 0.85f);
        }
        if (_btnSpectatorText != null)
        {
            _btnSpectatorText.text = isSpectatorMode
                ? "<color=#FFEA00>● SPECTATE: ON</color>"
                : "<color=#8FA0B5>SPECTATE: OFF</color>";
            _btnSpectatorText.fontStyle = isSpectatorMode ? FontStyle.Bold : FontStyle.Normal;
        }

        // 3. Number of Teams Buttons (2, 3, 4)
        for (int n = 2; n <= 4; n++)
        {
            bool isSel = (selectedNumTeams == n);
            if (_numTeamsBtnBgs.TryGetValue(n, out Image bg) && bg != null)
            {
                bg.color = isSel
                    ? new Color(0.12f, 0.48f, 0.68f, 0.98f)
                    : new Color(0.08f, 0.12f, 0.18f, 0.85f);
            }
            if (_numTeamsBtnTexts.TryGetValue(n, out Text txt) && txt != null)
            {
                txt.text = isSel ? $"<color=#26D9FF>● {n}</color>" : $"<color=#8FA0B5>{n}</color>";
                txt.fontStyle = isSel ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // 4. Players Per Team Buttons (1, 2, 3, 4)
        for (int p = 1; p <= 4; p++)
        {
            bool isSel = (selectedPlayersPerTeam == p);
            if (_playersPerTeamBtnBgs.TryGetValue(p, out Image bg) && bg != null)
            {
                bg.color = isSel
                    ? new Color(0.12f, 0.52f, 0.42f, 0.98f)
                    : new Color(0.08f, 0.12f, 0.18f, 0.85f);
            }
            if (_playersPerTeamBtnTexts.TryGetValue(p, out Text txt) && txt != null)
            {
                txt.text = isSel ? $"<color=#38EF7D>● {p}</color>" : $"<color=#8FA0B5>{p}</color>";
                txt.fontStyle = isSel ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        UpdateRosterDisplay();
    }

    /// <summary>
    /// Computes and renders live team roster breakdowns showing real players (Host, You, & connected players)
    /// swapping out for CPU bots on whichever team they select across all active teams (2, 3, or 4 teams)
    /// and squad sizes (1 to 4 players per team).
    /// </summary>
    public void UpdateRosterDisplay()
    {
        if (_teamRosterTexts[0] == null) return;

        int numTeams = Mathf.Clamp(selectedNumTeams, 2, 4);
        int teamCapacity = Mathf.Clamp(selectedPlayersPerTeam, 1, 4);
        var chosenOp = (selectedOperativeIndex >= 0 && selectedOperativeIndex < operatives.Count)
            ? operatives[selectedOperativeIndex]
            : operatives[0];

        string[][] teamCallsigns = new string[][]
        {
            new string[] { "VANCE", "MERCER", "VALKYRIE", "KAI", "ROOK" },
            new string[] { "VIPER", "RAZOR", "GHOST", "HAVOC", "KORP" },
            new string[] { "FALCON", "TITAN", "NOVA", "LYNX", "APEX" },
            new string[] { "BLAZE", "REAPER", "ECHO", "DRAKE", "VOLT" }
        };
        string[][] teamClasses = new string[][]
        {
            new string[] { "cyborg_specops", "combat_medic", "heavy_warrior", "wasteland_nomad", "unit07_android" },
            new string[] { "desert_commando", "heavy_warrior", "cyber_outlaw", "unit07_android", "combat_medic" },
            new string[] { "wasteland_nomad", "cyborg_specops", "combat_medic", "desert_commando", "heavy_warrior" },
            new string[] { "cyber_outlaw", "unit07_android", "heavy_warrior", "cyborg_specops", "combat_medic" }
        };
        string[][] teamRoles = new string[][]
        {
            new string[] { "Infiltrator / SpecOps", "Support / Medic", "Assault / Frontline", "Recon / Survival", "Heavy Mech" },
            new string[] { "Demolitions / Commando", "Assault / Frontline", "Gunslinger / Outlaw", "Heavy Mech", "Support / Medic" },
            new string[] { "Recon / Survival", "Infiltrator / SpecOps", "Support / Medic", "Demolitions", "Assault / Frontline" },
            new string[] { "Gunslinger / Outlaw", "Heavy Mech", "Assault / Frontline", "Infiltrator / SpecOps", "Support / Medic" }
        };

        List<OasisNetworkManager.ConnectedOperativeInfo> remoteOps = (OasisNetworkManager.Instance != null)
            ? OasisNetworkManager.Instance.GetRemoteOperatives()
            : new List<OasisNetworkManager.ConnectedOperativeInfo>();

        bool isLocalHost = (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host);

        // Dynamically layout active team boxes side-by-side (2, 3, or 4 columns)
        float totalMargin = 0.03f;
        float gap = 0.012f;
        float availableWidth = 1.0f - totalMargin - (gap * (numTeams - 1));
        float colWidth = availableWidth / numTeams;

        for (int t = 0; t < 4; t++)
        {
            bool isActive = (t < numTeams);
            if (_teamRosterBoxes[t] != null)
            {
                _teamRosterBoxes[t].SetActive(isActive);
            }
            if (!isActive) continue;

            if (_teamRosterBoxRTs[t] != null)
            {
                float minX = 0.015f + t * (colWidth + gap);
                float maxX = minX + colWidth;
                _teamRosterBoxRTs[t].anchorMin = new Vector2(minX, 0.04f);
                _teamRosterBoxRTs[t].anchorMax = new Vector2(maxX, 0.83f);
                _teamRosterBoxRTs[t].offsetMin = Vector2.zero;
                _teamRosterBoxRTs[t].offsetMax = Vector2.zero;
            }

            string shortName = OasisShooterManager.GetTeamShortName(t);
            string colorHex = OasisShooterManager.GetTeamColorHex(t);

            var sb = new System.Text.StringBuilder();
            int totalAssigned = 0;
            int realPlayersOnTeam = 0;

            // 1. Connected Remote Operatives (e.g., Squad Host if we joined as Client, or joined Clients if we are Host)
            for (int r = 0; r < remoteOps.Count; r++)
            {
                var rop = remoteOps[r];
                int rTeam = Mathf.Clamp(rop.teamId, 0, numTeams - 1);
                // In 1/TEAM (1v1) mode, if a local-test Host placeholder is on the same team as the local player, shift them to the opposing team so each 1-person team has 1 real player
                if (!isSpectatorMode && teamCapacity == 1 && rop.isLocalPlaceholder && rTeam == selectedTeam)
                {
                    rTeam = (selectedTeam + 1) % numTeams;
                }

                if (rTeam == t && totalAssigned < teamCapacity)
                {
                    totalAssigned++;
                    realPlayersOnTeam++;
                    int opIdx = Mathf.Clamp(rop.operativeIndex, 0, operatives.Count - 1);
                    var rProfile = operatives[opIdx];
                    string roleBadge = rop.isHost ? "SQUAD HOST" : rop.playerName.ToUpper();
                    if (numTeams >= 3)
                    {
                        sb.AppendLine($"<color=#38EF7D>● {roleBadge} (Real)</color>\n  <b>{rProfile.name}</b> <color={colorHex}>[{rProfile.id}]</color>");
                    }
                    else
                    {
                        sb.AppendLine($"<color=#38EF7D>● {roleBadge} (Real Player)</color> — <b>{rProfile.name}</b> <color={colorHex}>[{rProfile.id}]</color>\n    <color=#94A8BE>{rProfile.role}</color>");
                    }
                }
            }

            // 2. Local Player ("YOU") — swaps out 1 AI bot on whichever team you select
            if (!isSpectatorMode && selectedTeam == t && totalAssigned < teamCapacity)
            {
                totalAssigned++;
                realPlayersOnTeam++;
                string youLabel = isLocalHost ? "YOU (Squad Host)" : "YOU (Real Player)";
                if (numTeams >= 3)
                {
                    sb.AppendLine($"<color=#FFEA00>★ {youLabel}</color>\n  <b>{chosenOp.name}</b> <color={colorHex}>[{chosenOp.id}]</color>");
                }
                else
                {
                    sb.AppendLine($"<color=#FFEA00>★ {youLabel}</color> — <b>{chosenOp.name}</b> <color={colorHex}>[{chosenOp.id}]</color>\n    <color=#94A8BE>{chosenOp.role}</color>");
                }
            }

            // 3. Remaining slots filled with AI Bots (automatically swapped out as real players join/select this team)
            int cpuBotsOnTeam = Mathf.Max(0, teamCapacity - totalAssigned);
            int cpuIdx = 0;
            while (totalAssigned < teamCapacity)
            {
                totalAssigned++;
                string botName = teamCallsigns[t][cpuIdx % teamCallsigns[t].Length];
                string botClass = teamClasses[t][cpuIdx % teamClasses[t].Length];
                string botRole = teamRoles[t][cpuIdx % teamRoles[t].Length];
                if (numTeams >= 3)
                {
                    sb.AppendLine($"<color=#8FA0B5>[CPU]</color> <color={colorHex}><b>{botName}</b></color> <color=#A0B4C8>({botClass})</color>");
                }
                else
                {
                    sb.AppendLine($"<color=#8FA0B5><b>[CPU BOT]</b></color> <color={colorHex}><b>[{shortName}] {botName}</b></color> — <color=#B8C8D8>{botClass}</color>\n    <color=#687E96>{botRole}</color>");
                }
                cpuIdx++;
            }

            if (_teamRosterHeaders[t] != null)
            {
                _teamRosterHeaders[t].fontSize = (numTeams >= 4) ? 14 : 15;
                string splitTag = $"{realPlayersOnTeam} REAL / {cpuBotsOnTeam} AI";
                if (isSpectatorMode)
                    _teamRosterHeaders[t].text = $"<color={colorHex}><b>{shortName} [{splitTag}]</b></color>";
                else if (selectedTeam == t)
                    _teamRosterHeaders[t].text = $"<color={colorHex}><b>★ {shortName} (YOUR SQUAD • {splitTag})</b></color>";
                else
                    _teamRosterHeaders[t].text = $"<color={colorHex}><b>{shortName} ({splitTag})</b></color>";
            }

            if (_teamRosterTexts[t] != null)
            {
                _teamRosterTexts[t].fontSize = (numTeams >= 4 || teamCapacity >= 4) ? 12 : 14;
                _teamRosterTexts[t].text = sb.ToString().TrimEnd();
            }
        }
    }

    // ==========================================
    // 10-SECOND TACTICAL DEPLOYMENT COUNTDOWN
    // ==========================================

    public void StartDeploymentCountdown(bool isSimulation)
    {
        // ONLY the Host can start the game!
        if (!CanLocalPlayerStartGame())
        {
            return;
        }
        if (_isCountingDown) return;
        _isCountingDown = true;
        _isSimCountdown = isSimulation;

        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
            OasisNetworkManager.Instance.BroadcastCountdownStart(selectedNumTeams, selectedPlayersPerTeam, isSimulation);
        }

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
        }

        BuildCountdownModal();
        _countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    public void StartDeploymentCountdownFromHost(bool isSimulation)
    {
        if (!isSelectionActive) return;
        if (_isCountingDown) return;
        _isCountingDown = true;
        _isSimCountdown = isSimulation;

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
        }

        BuildCountdownModal();
        _countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    private void BuildCountdownModal()
    {
        if (_selectionCanvas == null) return;
        if (_countdownModalObj != null) Destroy(_countdownModalObj);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var op = (selectedOperativeIndex >= 0 && selectedOperativeIndex < operatives.Count) ? operatives[selectedOperativeIndex] : operatives[0];

        // Sleek Non-Blocking Top Deployment Bar (Does NOT cover the screen or block 3D character viewport!)
        _countdownModalObj = new GameObject("Deployment_Countdown_Bar");
        _countdownModalObj.transform.SetParent(_selectionCanvas.transform, false);
        var barRT = _countdownModalObj.AddComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0.22f, 0.885f);
        barRT.anchorMax = new Vector2(0.78f, 0.985f);
        barRT.offsetMin = Vector2.zero;
        barRT.offsetMax = Vector2.zero;

        Color borderAccent = _isSimCountdown ? new Color(0.12f, 0.75f, 0.95f) : OasisShooterManager.GetTeamColor(selectedTeam);
        var barBorder = _countdownModalObj.AddComponent<Image>();
        barBorder.color = borderAccent;

        GameObject barInner = new GameObject("Bar_Inner");
        barInner.transform.SetParent(_countdownModalObj.transform, false);
        var biRT = barInner.AddComponent<RectTransform>();
        biRT.anchorMin = Vector2.zero;
        biRT.anchorMax = Vector2.one;
        biRT.offsetMin = new Vector2(2, 2);
        biRT.offsetMax = new Vector2(-2, -2);
        var biImg = barInner.AddComponent<Image>();
        biImg.color = new Color(0.04f, 0.07f, 0.12f, 0.96f);

        // 1. Big Countdown Number Box (Left Side)
        GameObject cntBox = new GameObject("Countdown_Box");
        cntBox.transform.SetParent(barInner.transform, false);
        var cntRT = cntBox.AddComponent<RectTransform>();
        cntRT.anchorMin = new Vector2(0.015f, 0.08f);
        cntRT.anchorMax = new Vector2(0.11f, 0.92f);
        cntRT.offsetMin = Vector2.zero;
        cntRT.offsetMax = Vector2.zero;
        var cntBg = cntBox.AddComponent<Image>();
        cntBg.color = new Color(0.08f, 0.14f, 0.22f, 0.9f);

        GameObject cntNum = new GameObject("Number");
        cntNum.transform.SetParent(cntBox.transform, false);
        var cnRT = cntNum.AddComponent<RectTransform>();
        cnRT.anchorMin = Vector2.zero;
        cnRT.anchorMax = Vector2.one;
        cnRT.offsetMin = Vector2.zero;
        cnRT.offsetMax = Vector2.zero;
        _countdownSecondsText = cntNum.AddComponent<Text>();
        _countdownSecondsText.font = font;
        _countdownSecondsText.fontSize = 38;
        _countdownSecondsText.fontStyle = FontStyle.Bold;
        _countdownSecondsText.color = _isSimCountdown ? new Color(0.2f, 0.85f, 1f) : new Color(0.22f, 0.95f, 0.50f);
        _countdownSecondsText.alignment = TextAnchor.MiddleCenter;
        _countdownSecondsText.text = "10";

        // 2. Middle Section: Deployment Title, Subheader, and Progress Bar
        GameObject midObj = new GameObject("Mid_Section");
        midObj.transform.SetParent(barInner.transform, false);
        var midRT = midObj.AddComponent<RectTransform>();
        midRT.anchorMin = new Vector2(0.125f, 0.08f);
        midRT.anchorMax = new Vector2(0.66f, 0.92f);
        midRT.offsetMin = Vector2.zero;
        midRT.offsetMax = Vector2.zero;

        // Title text
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(midObj.transform, false);
        var tRT = titleObj.AddComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0f, 0.54f);
        tRT.anchorMax = new Vector2(1f, 0.96f);
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;
        var tTxt = titleObj.AddComponent<Text>();
        tTxt.font = font;
        tTxt.fontSize = 15;
        tTxt.fontStyle = FontStyle.Bold;
        tTxt.alignment = TextAnchor.MiddleLeft;
        tTxt.text = _isSimCountdown
            ? $"<color=#26D9FF>AI COMBAT SIMULATION INITIALIZING ({selectedNumTeams} TEAMS × {selectedPlayersPerTeam}/TEAM)</color>"
            : $"<color=#38EF7D>TACTICAL SQUAD DEPLOYMENT INITIALIZING</color> — FORMAT: {selectedMatchFormat}";

        // Subheader text
        GameObject subObj = new GameObject("Subheader");
        subObj.transform.SetParent(midObj.transform, false);
        var sRT = subObj.AddComponent<RectTransform>();
        sRT.anchorMin = new Vector2(0f, 0.22f);
        sRT.anchorMax = new Vector2(1f, 0.52f);
        sRT.offsetMin = Vector2.zero;
        sRT.offsetMax = Vector2.zero;
        _countdownSubheaderText = subObj.AddComponent<Text>();
        _countdownSubheaderText.font = font;
        _countdownSubheaderText.fontSize = 12;
        _countdownSubheaderText.color = new Color(0.80f, 0.90f, 1.0f);
        _countdownSubheaderText.alignment = TextAnchor.MiddleLeft;
        _countdownSubheaderText.text = "DEPLOYMENT IN T - 10 SECONDS";

        // Progress Bar Track
        GameObject pbTrack = new GameObject("Progress_Bar_Track");
        pbTrack.transform.SetParent(midObj.transform, false);
        var pbtRT = pbTrack.AddComponent<RectTransform>();
        pbtRT.anchorMin = new Vector2(0f, 0.04f);
        pbtRT.anchorMax = new Vector2(1f, 0.16f);
        pbtRT.offsetMin = Vector2.zero;
        pbtRT.offsetMax = Vector2.zero;
        var pbtImg = pbTrack.AddComponent<Image>();
        pbtImg.color = new Color(0.10f, 0.14f, 0.20f, 1f);

        // Progress Bar Fill
        GameObject pbFill = new GameObject("Fill");
        pbFill.transform.SetParent(pbTrack.transform, false);
        var pbfRT = pbFill.AddComponent<RectTransform>();
        pbfRT.anchorMin = Vector2.zero;
        pbfRT.anchorMax = Vector2.one;
        pbfRT.offsetMin = Vector2.zero;
        pbfRT.offsetMax = Vector2.zero;
        _countdownProgressBarFill = pbFill.AddComponent<Image>();
        _countdownProgressBarFill.color = _isSimCountdown ? new Color(0.20f, 0.85f, 1.0f) : new Color(0.22f, 0.95f, 0.50f);

        // 3. Right Action Buttons: Skip & Cancel on Host; Standby message on Client
        if (CanLocalPlayerStartGame())
        {
            // Deploy Now / Skip button
            GameObject skipBtnObj = new GameObject("Btn_Skip_DeployNow");
            skipBtnObj.transform.SetParent(barInner.transform, false);
            var skRT = skipBtnObj.AddComponent<RectTransform>();
            skRT.anchorMin = new Vector2(0.675f, 0.14f);
            skRT.anchorMax = new Vector2(0.845f, 0.86f);
            skRT.offsetMin = Vector2.zero;
            skRT.offsetMax = Vector2.zero;
            var skImg = skipBtnObj.AddComponent<Image>();
            skImg.color = new Color(0.08f, 0.65f, 0.38f, 1f);
            var skBtn = skipBtnObj.AddComponent<Button>();
            skBtn.targetGraphic = skImg;
            skBtn.onClick.AddListener(SkipCountdown);

            GameObject skTextObj = new GameObject("Text");
            skTextObj.transform.SetParent(skipBtnObj.transform, false);
            var sktRT = skTextObj.AddComponent<RectTransform>();
            sktRT.anchorMin = Vector2.zero;
            sktRT.anchorMax = Vector2.one;
            sktRT.offsetMin = Vector2.zero;
            sktRT.offsetMax = Vector2.zero;
            var skTxt = skTextObj.AddComponent<Text>();
            skTxt.font = font;
            skTxt.fontSize = 13;
            skTxt.fontStyle = FontStyle.Bold;
            skTxt.color = Color.white;
            skTxt.text = "DEPLOY NOW [SPACE]";
            skTxt.alignment = TextAnchor.MiddleCenter;

            // Cancel / Abort button
            GameObject cancelBtnObj = new GameObject("Btn_Cancel_Abort");
            cancelBtnObj.transform.SetParent(barInner.transform, false);
            var cRT = cancelBtnObj.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.855f, 0.14f);
            cRT.anchorMax = new Vector2(0.985f, 0.86f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;
            var cImg = cancelBtnObj.AddComponent<Image>();
            cImg.color = new Color(0.72f, 0.20f, 0.20f, 1f);
            var cBtn = cancelBtnObj.AddComponent<Button>();
            cBtn.targetGraphic = cImg;
            cBtn.onClick.AddListener(CancelCountdown);

            GameObject cTextObj = new GameObject("Text");
            cTextObj.transform.SetParent(cancelBtnObj.transform, false);
            var ctRT = cTextObj.AddComponent<RectTransform>();
            ctRT.anchorMin = Vector2.zero;
            ctRT.anchorMax = Vector2.one;
            ctRT.offsetMin = Vector2.zero;
            ctRT.offsetMax = Vector2.zero;
            var cTxt = cTextObj.AddComponent<Text>();
            cTxt.font = font;
            cTxt.fontSize = 13;
            cTxt.fontStyle = FontStyle.Bold;
            cTxt.color = Color.white;
            cTxt.text = "CANCEL [ESC]";
            cTxt.alignment = TextAnchor.MiddleCenter;
        }
        else
        {
            GameObject clientWaitObj = new GameObject("Client_Wait_Banner");
            clientWaitObj.transform.SetParent(barInner.transform, false);
            var cwRT = clientWaitObj.AddComponent<RectTransform>();
            cwRT.anchorMin = new Vector2(0.675f, 0.14f);
            cwRT.anchorMax = new Vector2(0.985f, 0.86f);
            cwRT.offsetMin = Vector2.zero;
            cwRT.offsetMax = Vector2.zero;
            var cwImg = clientWaitObj.AddComponent<Image>();
            cwImg.color = new Color(0.12f, 0.18f, 0.28f, 0.96f);

            GameObject cwTxtObj = new GameObject("Text");
            cwTxtObj.transform.SetParent(clientWaitObj.transform, false);
            var cwtRT = cwTxtObj.AddComponent<RectTransform>();
            cwtRT.anchorMin = Vector2.zero;
            cwtRT.anchorMax = Vector2.one;
            cwtRT.offsetMin = Vector2.zero;
            cwtRT.offsetMax = Vector2.zero;
            var cwTxt = cwTxtObj.AddComponent<Text>();
            cwTxt.font = font;
            cwTxt.fontSize = 13;
            cwTxt.fontStyle = FontStyle.Bold;
            cwTxt.color = new Color(0.22f, 0.95f, 0.55f);
            cwTxt.text = "HOST STARTED MISSION // PREPARING DEPLOYMENT";
            cwTxt.alignment = TextAnchor.MiddleCenter;
        }
    }

    private IEnumerator CountdownRoutine()
    {
        float totalTime = 10.0f;
        float remaining = totalTime;
        int lastSecondInt = 10;

        PlayCountdownSound(false);

        while (remaining > 0f)
        {
            if (!_isCountingDown) yield break;

            remaining -= Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(remaining / totalTime);

            if (_countdownProgressBarFill != null)
            {
                _countdownProgressBarFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            }

            int currentSecondInt = Mathf.CeilToInt(remaining);
            if (currentSecondInt != lastSecondInt && currentSecondInt > 0)
            {
                lastSecondInt = currentSecondInt;
                PlayCountdownSound(false);
            }

            if (_countdownSecondsText != null)
            {
                _countdownSecondsText.text = currentSecondInt.ToString("00");
            }
            if (_countdownSubheaderText != null)
            {
                _countdownSubheaderText.text = $"DEPLOYMENT IN T - {currentSecondInt}s";
            }

            yield return null;
        }

        PlayCountdownSound(true);
        if (_countdownSecondsText != null) _countdownSecondsText.text = "DEPLOYING!";
        if (_countdownProgressBarFill != null) _countdownProgressBarFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        yield return new WaitForSecondsRealtime(0.35f);

        ExecuteDeployment();
    }

    private void PlayCountdownSound(bool isFinal)
    {
        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }
        var clip = ProceduralAudio.GetCountdownBeepClip(isFinal);
        if (clip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clip, isFinal ? 0.7f : 0.45f);
        }
    }

    public void SkipCountdown()
    {
        if (!CanLocalPlayerStartGame()) return;
        if (!_isCountingDown) return;
        PlayCountdownSound(true);
        ExecuteDeployment();
    }

    public void CancelCountdown()
    {
        if (!CanLocalPlayerStartGame()) return;
        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastCountdownCancel();
        }
        CancelCountdownFromHost();
    }

    public void CancelCountdownFromHost()
    {
        if (!_isCountingDown) return;
        _isCountingDown = false;
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        if (_countdownModalObj != null)
        {
            Destroy(_countdownModalObj);
            _countdownModalObj = null;
        }
    }

    private void ExecuteDeployment()
    {
        _isCountingDown = false;
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        if (_countdownModalObj != null)
        {
            Destroy(_countdownModalObj);
            _countdownModalObj = null;
        }

        if (_isSimCountdown)
        {
            Launch2v2SimulationInternal();
        }
        else
        {
            ConfirmAndEnterGameInternal();
        }
    }

    public void ExecuteHostLaunchCommand(int numTeams, int playersPerTeam, bool isSim)
    {
        _isCountingDown = false;
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        if (_countdownModalObj != null)
        {
            Destroy(_countdownModalObj);
            _countdownModalObj = null;
        }

        SyncMatchConfigFromHost(numTeams, playersPerTeam);
        if (isSim)
        {
            Launch2v2SimulationInternal();
        }
        else
        {
            ConfirmAndEnterGameInternal();
        }
    }

    private void SetStatBar(RectTransform bar, Text valText, float value)
    {
        if (bar != null)
        {
            bar.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }
        if (valText != null)
        {
            valText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }

    public void ConfirmAndEnterGame()
    {
        if (!CanLocalPlayerStartGame()) return;
        ConfirmAndEnterGameInternal();
    }

    private void ConfirmAndEnterGameInternal()
    {
        isSelectionActive = false;
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisSelectedOperative", selectedOperativeIndex);
        PlayerPrefs.SetInt("OasisPlayerTeam", selectedTeam);
        PlayerPrefs.SetInt("OasisNumTeams", selectedNumTeams);
        PlayerPrefs.SetInt("OasisPlayersPerTeam", selectedPlayersPerTeam);
        PlayerPrefs.SetString("OasisMatchFormat", selectedMatchFormat);
        PlayerPrefs.Save();

        if (OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.BroadcastLobbySelection(selectedTeam, selectedOperativeIndex, selectedNumTeams, selectedPlayersPerTeam);
            if (CanLocalPlayerStartGame())
            {
                OasisNetworkManager.Instance.BroadcastMatchLaunch(selectedNumTeams, selectedPlayersPerTeam, false);
            }
        }

        var chosen = operatives[selectedOperativeIndex];

        // 1. Lock in the chosen operative skin to the player model
        Texture2D skinTex = null;
        if (!string.IsNullOrEmpty(chosen.textureResourcePath))
        {
            skinTex = Resources.Load<Texture2D>(chosen.textureResourcePath);
        }
        Color teamAccent = OasisShooterManager.GetTeamColor(selectedTeam);
        if (OasisCombatMovement.Instance != null)
        {
            OasisCombatMovement.Instance.ApplyCharacterSkin(skinTex, teamAccent, chosen.id);
            OasisCombatMovement.Instance.ConfigureOperativeAbility(selectedOperativeIndex);
            OasisCombatMovement.Instance.SetThirdPersonMode(false);
            var pWeapon = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
            if (pWeapon == null) pWeapon = FindAnyObjectByType<BlasterWeapon>();
            if (pWeapon != null) pWeapon.ApplyOperativeWeapon(selectedOperativeIndex);
        }

        // 2. Teleport player to a random grounded spawn point for their selected team
        Transform pTransform = (OasisCombatMovement.Instance != null) 
            ? OasisCombatMovement.Instance.transform 
            : OasisPlayerHelper.FindPlayerTransform();
        if (pTransform != null)
        {
            OasisSpawnSystem.Instance.RespawnCombatant(pTransform, selectedTeam);
        }

        // 3. Clean up 3D staging preview objects
        if (_previewRenderTexture != null)
        {
            _previewRenderTexture.Release();
            Destroy(_previewRenderTexture);
            _previewRenderTexture = null;
        }
        if (_stagingRoot != null)
        {
            Destroy(_stagingRoot);
            _stagingRoot = null;
        }

        // 4. Remove the full-page selection canvas
        if (_selectionCanvas != null)
        {
            Destroy(_selectionCanvas);
            _selectionCanvas = null;
        }

        // 5. Restore gameplay controls and cursor lock
        if (_starterInputs != null)
        {
            _starterInputs.look = Vector2.zero;
            _starterInputs.SetCursorState(true);
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var pFpc = pTransform != null ? pTransform.GetComponent<StarterAssets.FirstPersonController>() : null;
        if (pFpc != null)
        {
            pFpc.ResetCameraPitch(0f);
        }
        if (Camera.main != null && pTransform != null)
        {
            Camera.main.transform.rotation = pTransform.rotation;
        }

        // 6. Restore In-game Combat HUD and Map
        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.LaunchCombatFromSelection();
            string teamName = OasisShooterManager.GetTeamFullName(selectedTeam);
            OasisShooterManager.Instance.ShowNotificationBanner(
                $"OPERATIVE DEPLOYED: {chosen.name.ToUpper()}\n<size=15>Side: {teamName}  //  Format: {selectedMatchFormat}</size>", 4.0f);
        }
        if (OasisMapSystem.Instance != null)
        {
            OasisMapSystem.Instance.SetMapCanvasActive(true);
        }

        Debug.Log($"<color=cyan>[OasisCharacterCustomizer]</color> Operative deployed: {chosen.name} ({chosen.role}) on Team {selectedTeam} ({selectedMatchFormat})");
    }

    public void Launch2v2Simulation()
    {
        if (!CanLocalPlayerStartGame()) return;
        Launch2v2SimulationInternal();
    }

    private void Launch2v2SimulationInternal()
    {
        UpdateMatchFormatString();
        PlayerPrefs.SetInt("OasisNumTeams", selectedNumTeams);
        PlayerPrefs.SetInt("OasisPlayersPerTeam", selectedPlayersPerTeam);
        PlayerPrefs.SetString("OasisMatchFormat", selectedMatchFormat);
        PlayerPrefs.Save();

        if (OasisNetworkManager.Instance != null && CanLocalPlayerStartGame())
        {
            OasisNetworkManager.Instance.BroadcastMatchLaunch(selectedNumTeams, selectedPlayersPerTeam, true);
        }

        CloseCustomizerForSimulation();
        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.StartSimulationMode();
        }
    }

    public void CloseCustomizerForSimulation()
    {
        isSelectionActive = false;

        if (_previewRenderTexture != null)
        {
            _previewRenderTexture.Release();
            Destroy(_previewRenderTexture);
            _previewRenderTexture = null;
        }
        if (_stagingRoot != null)
        {
            Destroy(_stagingRoot);
            _stagingRoot = null;
        }

        if (_selectionCanvas != null)
        {
            Destroy(_selectionCanvas);
            _selectionCanvas = null;
        }

        if (_starterInputs != null)
        {
            _starterInputs.cursorInputForLook = false;
            _starterInputs.cursorLocked = false;
            _starterInputs.enabled = false;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (OasisMapSystem.Instance != null)
        {
            OasisMapSystem.Instance.SetMapCanvasActive(true);
        }
    }

    private void BuildLaunchSelectionUI()
    {
        _selectionCanvas = new GameObject("Oasis_OperativeSelection_Page");
        Canvas canvas = _selectionCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // Above all HUD & game UI elements

        CanvasScaler scaler = _selectionCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _selectionCanvas.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 1. Dedicated Solid Opaque Background Page
        GameObject bgObj = new GameObject("Selection_Page_Backdrop");
        bgObj.transform.SetParent(_selectionCanvas.transform, false);
        RectTransform bgRT = bgObj.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.035f, 0.045f, 0.070f, 1.0f); // 100% Solid dark military background

        // Subtle tech grid lines texture or accent vignette
        GameObject borderFrame = new GameObject("Tech_Frame_Accent");
        borderFrame.transform.SetParent(bgObj.transform, false);
        RectTransform bfRT = borderFrame.AddComponent<RectTransform>();
        bfRT.anchorMin = new Vector2(0.015f, 0.02f);
        bfRT.anchorMax = new Vector2(0.985f, 0.98f);
        bfRT.offsetMin = Vector2.zero;
        bfRT.offsetMax = Vector2.zero;
        Image bfImg = borderFrame.AddComponent<Image>();
        bfImg.color = new Color(0.12f, 0.18f, 0.28f, 0.35f);

        // 2. Top Header Bar
        GameObject headerObj = new GameObject("Top_Header_Bar");
        headerObj.transform.SetParent(bgObj.transform, false);
        RectTransform hRT = headerObj.AddComponent<RectTransform>();
        hRT.anchorMin = new Vector2(0.03f, 0.90f);
        hRT.anchorMax = new Vector2(0.97f, 0.98f);
        hRT.offsetMin = Vector2.zero;
        hRT.offsetMax = Vector2.zero;

        // Protocol Title
        GameObject titleObj = new GameObject("Protocol_Title");
        titleObj.transform.SetParent(headerObj.transform, false);
        RectTransform tRT = titleObj.AddComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0, 0.45f);
        tRT.anchorMax = new Vector2(1, 1.0f);
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;
        Text tText = titleObj.AddComponent<Text>();
        tText.font = font;
        tText.text = "OASIS PROTOCOL // TACTICAL OPERATIVE SELECTION";
        tText.fontSize = 28;
        tText.fontStyle = FontStyle.Bold;
        tText.color = new Color(0.25f, 0.85f, 1.0f);
        tText.alignment = TextAnchor.MiddleLeft;

        // Subtitle
        GameObject subObj = new GameObject("Protocol_Subtitle");
        subObj.transform.SetParent(headerObj.transform, false);
        RectTransform subRT = subObj.AddComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0, 0);
        subRT.anchorMax = new Vector2(0.62f, 0.45f);
        subRT.offsetMin = Vector2.zero;
        subRT.offsetMax = Vector2.zero;
        Text subText = subObj.AddComponent<Text>();
        subText.font = font;
        subText.text = "SELECT YOUR COMBAT CHASSIS & SPEC BEFORE DEPLOYING INTO THE COMBAT SECTOR";
        subText.fontSize = 14;
        subText.color = new Color(0.60f, 0.72f, 0.85f);
        subText.alignment = TextAnchor.MiddleLeft;

        // Session Role / Host Control Badge Button (Top Right)
        GameObject roleBtnObj = new GameObject("Role_HostToggle_Button");
        roleBtnObj.transform.SetParent(headerObj.transform, false);
        RectTransform roleRT = roleBtnObj.AddComponent<RectTransform>();
        roleRT.anchorMin = new Vector2(0.63f, 0.14f);
        roleRT.anchorMax = new Vector2(0.875f, 0.86f);
        roleRT.offsetMin = Vector2.zero;
        roleRT.offsetMax = Vector2.zero;
        _roleHeaderBtnBg = roleBtnObj.AddComponent<Image>();
        _roleHeaderBtnBg.color = new Color(0.08f, 0.48f, 0.30f, 0.95f);
        Button roleBtn = roleBtnObj.AddComponent<Button>();
        roleBtn.targetGraphic = _roleHeaderBtnBg;
        roleBtn.onClick.AddListener(SwitchToHostRoleInStaging);

        GameObject roleTxtObj = new GameObject("Label");
        roleTxtObj.transform.SetParent(roleBtnObj.transform, false);
        RectTransform roleTxtRT = roleTxtObj.AddComponent<RectTransform>();
        roleTxtRT.anchorMin = Vector2.zero;
        roleTxtRT.anchorMax = Vector2.one;
        roleTxtRT.offsetMin = new Vector2(6f, 0f);
        roleTxtRT.offsetMax = new Vector2(-6f, 0f);
        _roleHeaderBtnText = roleTxtObj.AddComponent<Text>();
        _roleHeaderBtnText.font = font;
        _roleHeaderBtnText.fontSize = 11;
        _roleHeaderBtnText.fontStyle = FontStyle.Bold;
        _roleHeaderBtnText.color = Color.white;
        _roleHeaderBtnText.alignment = TextAnchor.MiddleCenter;
        _roleHeaderBtnText.text = "SESSION ROLE: HOST (CAN LAUNCH)";

        // Return to Main Menu Button (Top Far Right)
        GameObject menuBtnObj = new GameObject("Return_MainMenu_Button");
        menuBtnObj.transform.SetParent(headerObj.transform, false);
        RectTransform menuRT = menuBtnObj.AddComponent<RectTransform>();
        menuRT.anchorMin = new Vector2(0.885f, 0.14f);
        menuRT.anchorMax = new Vector2(1.0f, 0.86f);
        menuRT.offsetMin = Vector2.zero;
        menuRT.offsetMax = Vector2.zero;
        Image menuBg = menuBtnObj.AddComponent<Image>();
        menuBg.color = new Color(0.14f, 0.20f, 0.30f, 0.95f);
        Button menuBtn = menuBtnObj.AddComponent<Button>();
        menuBtn.targetGraphic = menuBg;
        menuBtn.onClick.AddListener(ReturnToMainMenu);

        GameObject menuTxtObj = new GameObject("Label");
        menuTxtObj.transform.SetParent(menuBtnObj.transform, false);
        RectTransform menuTxtRT = menuTxtObj.AddComponent<RectTransform>();
        menuTxtRT.anchorMin = Vector2.zero;
        menuTxtRT.anchorMax = Vector2.one;
        menuTxtRT.offsetMin = Vector2.zero;
        menuTxtRT.offsetMax = Vector2.zero;
        Text menuTxt = menuTxtObj.AddComponent<Text>();
        menuTxt.font = font;
        menuTxt.fontSize = 11;
        menuTxt.fontStyle = FontStyle.Bold;
        menuTxt.color = new Color(0.80f, 0.92f, 1.0f);
        menuTxt.alignment = TextAnchor.MiddleCenter;
        menuTxt.text = "← MAIN MENU";

        // Horizontal divider line
        GameObject divObj = new GameObject("Header_Divider");
        divObj.transform.SetParent(bgObj.transform, false);
        RectTransform dRT = divObj.AddComponent<RectTransform>();
        dRT.anchorMin = new Vector2(0.03f, 0.895f);
        dRT.anchorMax = new Vector2(0.97f, 0.897f);
        dRT.offsetMin = Vector2.zero;
        dRT.offsetMax = Vector2.zero;
        Image dImg = divObj.AddComponent<Image>();
        dImg.color = new Color(0.20f, 0.35f, 0.50f, 0.70f);

        // ==========================================
        // 3. LEFT PANEL: Pre-Match Squad Settings (Top) + Compact Operative Selection (Bottom)
        // ==========================================
        GameObject leftPanel = new GameObject("Left_Operatives_Panel");
        leftPanel.transform.SetParent(bgObj.transform, false);
        RectTransform lpRT = leftPanel.AddComponent<RectTransform>();
        lpRT.anchorMin = new Vector2(0.025f, 0.04f);
        lpRT.anchorMax = new Vector2(0.455f, 0.885f);
        lpRT.offsetMin = Vector2.zero;
        lpRT.offsetMax = Vector2.zero;

        // --- 3A. TOP OF LEFT PANEL: Match, Squad & Spectator Settings Bar ---
        GameObject matchConfigObj = new GameObject("Top_MatchSettings_Box");
        matchConfigObj.transform.SetParent(leftPanel.transform, false);
        RectTransform mcRT = matchConfigObj.AddComponent<RectTransform>();
        mcRT.anchorMin = new Vector2(0f, 0.78f);
        mcRT.anchorMax = new Vector2(1f, 1.0f);
        mcRT.offsetMin = Vector2.zero;
        mcRT.offsetMax = Vector2.zero;

        Image mcBoxBorder = matchConfigObj.AddComponent<Image>();
        mcBoxBorder.color = new Color(0.18f, 0.32f, 0.48f, 0.85f);

        GameObject mcInner = new GameObject("Inner");
        mcInner.transform.SetParent(matchConfigObj.transform, false);
        RectTransform mciRT = mcInner.AddComponent<RectTransform>();
        mciRT.anchorMin = Vector2.zero;
        mciRT.anchorMax = Vector2.one;
        mciRT.offsetMin = new Vector2(2, 2);
        mciRT.offsetMax = new Vector2(-2, -2);
        Image mcBoxBg = mcInner.AddComponent<Image>();
        mcBoxBg.color = new Color(0.05f, 0.075f, 0.115f, 0.98f);

        // Settings Header Title
        GameObject mcHdrObj = new GameObject("Settings_Header");
        mcHdrObj.transform.SetParent(mcInner.transform, false);
        RectTransform mchRT = mcHdrObj.AddComponent<RectTransform>();
        mchRT.anchorMin = new Vector2(0.025f, 0.78f);
        mchRT.anchorMax = new Vector2(0.975f, 0.97f);
        mchRT.offsetMin = Vector2.zero;
        mchRT.offsetMax = Vector2.zero;
        Text mchText = mcHdrObj.AddComponent<Text>();
        mchText.font = font;
        mchText.text = "MATCH CONFIGURATION // SQUAD SIDE, TEAMS, PLAYERS/TEAM & SPECTATOR";
        mchText.fontSize = 11;
        mchText.fontStyle = FontStyle.Bold;
        mchText.color = new Color(0.35f, 0.85f, 1.0f);
        mchText.alignment = TextAnchor.MiddleLeft;

        // ROW 1: Squad Side (Alpha / Bravo / Charlie / Delta) + Compact Spectator Toggle
        GameObject sideLabelObj = new GameObject("Side_Label");
        sideLabelObj.transform.SetParent(mcInner.transform, false);
        RectTransform slRT = sideLabelObj.AddComponent<RectTransform>();
        slRT.anchorMin = new Vector2(0.02f, 0.42f);
        slRT.anchorMax = new Vector2(0.135f, 0.75f);
        slRT.offsetMin = Vector2.zero;
        slRT.offsetMax = Vector2.zero;
        Text slText = sideLabelObj.AddComponent<Text>();
        slText.font = font;
        slText.text = "SQUAD:";
        slText.fontSize = 11;
        slText.fontStyle = FontStyle.Bold;
        slText.color = new Color(0.80f, 0.88f, 0.96f);
        slText.alignment = TextAnchor.MiddleLeft;

        // 4 Squad Side Buttons (Alpha, Bravo, Charlie, Delta)
        float[] sqMinX = new float[] { 0.14f, 0.295f, 0.45f, 0.605f };
        float[] sqMaxX = new float[] { 0.288f, 0.443f, 0.598f, 0.753f };
        for (int t = 0; t < 4; t++)
        {
            int teamIdx = t;
            string shortName = OasisShooterManager.GetTeamShortName(t);
            GameObject sqBtnObj = new GameObject($"Btn_Team_{shortName}");
            sqBtnObj.transform.SetParent(mcInner.transform, false);
            RectTransform sqRT = sqBtnObj.AddComponent<RectTransform>();
            sqRT.anchorMin = new Vector2(sqMinX[t], 0.43f);
            sqRT.anchorMax = new Vector2(sqMaxX[t], 0.74f);
            sqRT.offsetMin = Vector2.zero;
            sqRT.offsetMax = Vector2.zero;

            _teamBtnBgs[t] = sqBtnObj.AddComponent<Image>();
            _teamBtnBgs[t].color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            Button sqBtn = sqBtnObj.AddComponent<Button>();
            sqBtn.targetGraphic = _teamBtnBgs[t];
            sqBtn.onClick.AddListener(() => SetPlayerTeam(teamIdx));

            GameObject sqTxtObj = new GameObject("Text");
            sqTxtObj.transform.SetParent(sqBtnObj.transform, false);
            RectTransform sqtRT = sqTxtObj.AddComponent<RectTransform>();
            sqtRT.anchorMin = Vector2.zero;
            sqtRT.anchorMax = Vector2.one;
            sqtRT.offsetMin = Vector2.zero;
            sqtRT.offsetMax = Vector2.zero;
            _teamBtnTexts[t] = sqTxtObj.AddComponent<Text>();
            _teamBtnTexts[t].font = font;
            _teamBtnTexts[t].fontSize = 11;
            _teamBtnTexts[t].text = shortName;
            _teamBtnTexts[t].alignment = TextAnchor.MiddleCenter;
        }

        // Button: Compact Spectator Setting
        GameObject specBtnObj = new GameObject("Btn_SpectatorToggle");
        specBtnObj.transform.SetParent(mcInner.transform, false);
        RectTransform spRT = specBtnObj.AddComponent<RectTransform>();
        spRT.anchorMin = new Vector2(0.765f, 0.43f);
        spRT.anchorMax = new Vector2(0.98f, 0.74f);
        spRT.offsetMin = Vector2.zero;
        spRT.offsetMax = Vector2.zero;
        _btnSpectatorBg = specBtnObj.AddComponent<Image>();
        _btnSpectatorBg.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
        Button specBtn = specBtnObj.AddComponent<Button>();
        specBtn.targetGraphic = _btnSpectatorBg;
        specBtn.onClick.AddListener(ToggleSpectatorMode);

        GameObject specTxtObj = new GameObject("Text");
        specTxtObj.transform.SetParent(specBtnObj.transform, false);
        RectTransform sptRT = specTxtObj.AddComponent<RectTransform>();
        sptRT.anchorMin = Vector2.zero;
        sptRT.anchorMax = Vector2.one;
        sptRT.offsetMin = Vector2.zero;
        sptRT.offsetMax = Vector2.zero;
        _btnSpectatorText = specTxtObj.AddComponent<Text>();
        _btnSpectatorText.font = font;
        _btnSpectatorText.fontSize = 11;
        _btnSpectatorText.text = "SPECTATE: OFF";
        _btnSpectatorText.alignment = TextAnchor.MiddleCenter;

        // ROW 2: Number of Teams (2, 3, 4) + Players Per Team (1, 2, 3, 4)
        GameObject teamsLabelObj = new GameObject("Teams_Label");
        teamsLabelObj.transform.SetParent(mcInner.transform, false);
        RectTransform tlRT = teamsLabelObj.AddComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.02f, 0.06f);
        tlRT.anchorMax = new Vector2(0.135f, 0.37f);
        tlRT.offsetMin = Vector2.zero;
        tlRT.offsetMax = Vector2.zero;
        Text tlText = teamsLabelObj.AddComponent<Text>();
        tlText.font = font;
        tlText.text = "TEAMS:";
        tlText.fontSize = 11;
        tlText.fontStyle = FontStyle.Bold;
        tlText.color = new Color(0.80f, 0.88f, 0.96f);
        tlText.alignment = TextAnchor.MiddleLeft;

        _numTeamsBtnBgs.Clear();
        _numTeamsBtnTexts.Clear();
        int[] teamCounts = new int[] { 2, 3, 4 };
        float[] tcMinX = new float[] { 0.14f, 0.24f, 0.34f };
        float[] tcMaxX = new float[] { 0.232f, 0.332f, 0.432f };
        for (int i = 0; i < teamCounts.Length; i++)
        {
            int countVal = teamCounts[i];
            GameObject tBtnObj = new GameObject($"Btn_NumTeams_{countVal}");
            tBtnObj.transform.SetParent(mcInner.transform, false);
            RectTransform tbRT = tBtnObj.AddComponent<RectTransform>();
            tbRT.anchorMin = new Vector2(tcMinX[i], 0.06f);
            tbRT.anchorMax = new Vector2(tcMaxX[i], 0.37f);
            tbRT.offsetMin = Vector2.zero;
            tbRT.offsetMax = Vector2.zero;

            Image tBg = tBtnObj.AddComponent<Image>();
            tBg.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            _numTeamsBtnBgs[countVal] = tBg;

            Button tBtn = tBtnObj.AddComponent<Button>();
            tBtn.targetGraphic = tBg;
            tBtn.onClick.AddListener(() => SetNumTeams(countVal));

            GameObject tTxtObj = new GameObject("Text");
            tTxtObj.transform.SetParent(tBtnObj.transform, false);
            RectTransform tTxtRT = tTxtObj.AddComponent<RectTransform>();
            tTxtRT.anchorMin = Vector2.zero;
            tTxtRT.anchorMax = Vector2.one;
            tTxtRT.offsetMin = Vector2.zero;
            tTxtRT.offsetMax = Vector2.zero;

            Text tTxt = tTxtObj.AddComponent<Text>();
            tTxt.font = font;
            tTxt.fontSize = 12;
            tTxt.text = countVal.ToString();
            tTxt.alignment = TextAnchor.MiddleCenter;
            _numTeamsBtnTexts[countVal] = tTxt;
        }

        // Players Per Team Label & Buttons (1, 2, 3, 4)
        GameObject pptLabelObj = new GameObject("PlayersPerTeam_Label");
        pptLabelObj.transform.SetParent(mcInner.transform, false);
        RectTransform pptRT = pptLabelObj.AddComponent<RectTransform>();
        pptRT.anchorMin = new Vector2(0.455f, 0.06f);
        pptRT.anchorMax = new Vector2(0.625f, 0.37f);
        pptRT.offsetMin = Vector2.zero;
        pptRT.offsetMax = Vector2.zero;
        Text pptText = pptLabelObj.AddComponent<Text>();
        pptText.font = font;
        pptText.text = "PLAYERS/TEAM:";
        pptText.fontSize = 11;
        pptText.fontStyle = FontStyle.Bold;
        pptText.color = new Color(0.80f, 0.88f, 0.96f);
        pptText.alignment = TextAnchor.MiddleLeft;

        _playersPerTeamBtnBgs.Clear();
        _playersPerTeamBtnTexts.Clear();
        int[] playerCounts = new int[] { 1, 2, 3, 4 };
        float[] pcMinX = new float[] { 0.63f, 0.72f, 0.81f, 0.90f };
        float[] pcMaxX = new float[] { 0.712f, 0.802f, 0.892f, 0.98f };
        for (int i = 0; i < playerCounts.Length; i++)
        {
            int pVal = playerCounts[i];
            GameObject pBtnObj = new GameObject($"Btn_PlayersPerTeam_{pVal}");
            pBtnObj.transform.SetParent(mcInner.transform, false);
            RectTransform pbRT = pBtnObj.AddComponent<RectTransform>();
            pbRT.anchorMin = new Vector2(pcMinX[i], 0.06f);
            pbRT.anchorMax = new Vector2(pcMaxX[i], 0.37f);
            pbRT.offsetMin = Vector2.zero;
            pbRT.offsetMax = Vector2.zero;

            Image pBg = pBtnObj.AddComponent<Image>();
            pBg.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            _playersPerTeamBtnBgs[pVal] = pBg;

            Button pBtn = pBtnObj.AddComponent<Button>();
            pBtn.targetGraphic = pBg;
            pBtn.onClick.AddListener(() => SetPlayersPerTeam(pVal));

            GameObject pTxtObj = new GameObject("Text");
            pTxtObj.transform.SetParent(pBtnObj.transform, false);
            RectTransform pTxtRT = pTxtObj.AddComponent<RectTransform>();
            pTxtRT.anchorMin = Vector2.zero;
            pTxtRT.anchorMax = Vector2.one;
            pTxtRT.offsetMin = Vector2.zero;
            pTxtRT.offsetMax = Vector2.zero;

            Text pTxt = pTxtObj.AddComponent<Text>();
            pTxt.font = font;
            pTxt.fontSize = 12;
            pTxt.text = pVal.ToString();
            pTxt.alignment = TextAnchor.MiddleCenter;
            _playersPerTeamBtnTexts[pVal] = pTxt;
        }

        // --- 3B. BOTTOM OF LEFT PANEL: Compact Operative Selection Cards (7 Cards) ---
        GameObject lpTitle = new GameObject("Roster_Header");
        lpTitle.transform.SetParent(leftPanel.transform, false);
        RectTransform lptRT = lpTitle.AddComponent<RectTransform>();
        lptRT.anchorMin = new Vector2(0f, 0.715f);
        lptRT.anchorMax = new Vector2(1f, 0.765f);
        lptRT.offsetMin = Vector2.zero;
        lptRT.offsetMax = Vector2.zero;
        Text lptText = lpTitle.AddComponent<Text>();
        lptText.font = font;
        lptText.text = "SELECT OPERATIVE CHASSIS [7]";
        lptText.fontSize = 15;
        lptText.fontStyle = FontStyle.Bold;
        lptText.color = new Color(0.85f, 0.92f, 1.0f);
        lptText.alignment = TextAnchor.MiddleLeft;

        GameObject rosterContainer = new GameObject("Roster_Container");
        rosterContainer.transform.SetParent(leftPanel.transform, false);
        RectTransform rcRT = rosterContainer.AddComponent<RectTransform>();
        rcRT.anchorMin = new Vector2(0f, 0f);
        rcRT.anchorMax = new Vector2(1f, 0.71f);
        rcRT.offsetMin = Vector2.zero;
        rcRT.offsetMax = Vector2.zero;

        // Proportional layout so all 7 cards fit 100% cleanly with zero overflow
        float slotStep = 1.0f / operatives.Count;
        float slotHeight = slotStep * 0.88f;

        for (int i = 0; i < operatives.Count; i++)
        {
            int cardIndex = i;
            var op = operatives[i];

            float maxY = 1.0f - (i * slotStep);
            float minY = maxY - slotHeight;

            GameObject card = new GameObject($"Card_{i}_{op.id}");
            card.transform.SetParent(rosterContainer.transform, false);
            RectTransform cRT = card.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0f, minY);
            cRT.anchorMax = new Vector2(1f, maxY);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            // Card Border Outline
            Image borderImg = card.AddComponent<Image>();
            borderImg.color = (i == selectedOperativeIndex) ? op.themeColor : new Color(0.15f, 0.20f, 0.28f, 0.8f);
            _cardBorders.Add(borderImg);

            // Card Inner Body
            GameObject bodyObj = new GameObject("Body");
            bodyObj.transform.SetParent(card.transform, false);
            RectTransform bRT = bodyObj.AddComponent<RectTransform>();
            bRT.anchorMin = Vector2.zero;
            bRT.anchorMax = Vector2.one;
            bRT.offsetMin = new Vector2(2, 2);
            bRT.offsetMax = new Vector2(-2, -2);
            Image bodyImg = bodyObj.AddComponent<Image>();
            bodyImg.color = (i == selectedOperativeIndex) 
                ? new Color(0.12f, 0.22f, 0.35f, 0.98f) 
                : new Color(0.07f, 0.09f, 0.13f, 0.92f);
            _cardBackgrounds.Add(bodyImg);

            // Left Theme Color Stripe
            GameObject stripe = new GameObject("AccentStripe");
            stripe.transform.SetParent(bodyObj.transform, false);
            RectTransform sRT = stripe.AddComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0, 0);
            sRT.anchorMax = new Vector2(0, 1);
            sRT.pivot = new Vector2(0, 0.5f);
            sRT.anchoredPosition = Vector2.zero;
            sRT.sizeDelta = new Vector2(6, 0);
            Image sImg = stripe.AddComponent<Image>();
            sImg.color = op.themeColor;
            _cardAccents.Add(sImg);

            // Operative Name + Role on Upper Row
            GameObject nameObj = new GameObject("Name");
            nameObj.transform.SetParent(bodyObj.transform, false);
            RectTransform nRT = nameObj.AddComponent<RectTransform>();
            nRT.anchorMin = new Vector2(0.03f, 0.48f);
            nRT.anchorMax = new Vector2(0.48f, 0.94f);
            nRT.offsetMin = Vector2.zero;
            nRT.offsetMax = Vector2.zero;
            Text nText = nameObj.AddComponent<Text>();
            nText.font = font;
            nText.text = op.name;
            nText.fontSize = 15;
            nText.fontStyle = FontStyle.Bold;
            nText.color = Color.white;
            nText.alignment = TextAnchor.MiddleLeft;

            // Operative Role Badge
            GameObject roleObj = new GameObject("Role");
            roleObj.transform.SetParent(bodyObj.transform, false);
            RectTransform rRT = roleObj.AddComponent<RectTransform>();
            rRT.anchorMin = new Vector2(0.49f, 0.48f);
            rRT.anchorMax = new Vector2(0.76f, 0.94f);
            rRT.offsetMin = Vector2.zero;
            rRT.offsetMax = Vector2.zero;
            Text rText = roleObj.AddComponent<Text>();
            rText.font = font;
            rText.text = op.role;
            rText.fontSize = 11;
            rText.fontStyle = FontStyle.Bold;
            rText.color = op.themeColor;
            rText.alignment = TextAnchor.MiddleRight;

            // Weapon & Passive preview line on Lower Row
            GameObject loadoutObj = new GameObject("Loadout");
            loadoutObj.transform.SetParent(bodyObj.transform, false);
            RectTransform lRT = loadoutObj.AddComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0.03f, 0.06f);
            lRT.anchorMax = new Vector2(0.76f, 0.46f);
            lRT.offsetMin = Vector2.zero;
            lRT.offsetMax = Vector2.zero;
            Text lText = loadoutObj.AddComponent<Text>();
            lText.font = font;
            lText.text = $"{op.primaryWeapon}  •  {op.passiveTrait}";
            lText.fontSize = 11;
            lText.color = new Color(0.62f, 0.74f, 0.86f);
            lText.alignment = TextAnchor.MiddleLeft;

            // Right Select State Pill
            GameObject btnObj = new GameObject("SelectBtn");
            btnObj.transform.SetParent(bodyObj.transform, false);
            RectTransform btnRT = btnObj.AddComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.78f, 0.18f);
            btnRT.anchorMax = new Vector2(0.98f, 0.82f);
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;
            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = new Color(0.12f, 0.16f, 0.22f, 0.9f);

            GameObject btnTextObj = new GameObject("Text");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            RectTransform btRT = btnTextObj.AddComponent<RectTransform>();
            btRT.anchorMin = Vector2.zero;
            btRT.anchorMax = Vector2.one;
            btRT.offsetMin = Vector2.zero;
            btRT.offsetMax = Vector2.zero;
            Text btnText = btnTextObj.AddComponent<Text>();
            btnText.font = font;
            btnText.text = (i == selectedOperativeIndex) 
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(op.themeColor)}>● ACTIVE</color>" 
                : "<color=#8FA0B5>SELECT</color>";
            btnText.fontSize = 12;
            btnText.fontStyle = (i == selectedOperativeIndex) ? FontStyle.Bold : FontStyle.Normal;
            btnText.alignment = TextAnchor.MiddleCenter;
            _cardButtonTexts.Add(btnText);

            Button fullCardBtn = card.AddComponent<Button>();
            fullCardBtn.targetGraphic = bodyImg;
            fullCardBtn.onClick.AddListener(() => SelectCard(cardIndex));
        }

        // ==========================================
        // 4. RIGHT PANEL: Top Split (3D Viewport + Dossier), Middle (Large Team Rosters), Bottom (Launch Button)
        // ==========================================
        GameObject rightPanel = new GameObject("Right_Visual_Dossier_Panel");
        rightPanel.transform.SetParent(bgObj.transform, false);
        RectTransform rpRT = rightPanel.AddComponent<RectTransform>();
        rpRT.anchorMin = new Vector2(0.475f, 0.04f);
        rpRT.anchorMax = new Vector2(0.975f, 0.885f);
        rpRT.offsetMin = Vector2.zero;
        rpRT.offsetMax = Vector2.zero;

        // --- 4A. UPPER LEFT: 3D Visual Preview Viewport (Compact Square Framing) ---
        GameObject viewportObj = new GameObject("3D_Visual_Viewport");
        viewportObj.transform.SetParent(rightPanel.transform, false);
        RectTransform vpRT = viewportObj.AddComponent<RectTransform>();
        vpRT.anchorMin = new Vector2(0f, 0.485f);
        vpRT.anchorMax = new Vector2(0.45f, 1.0f);
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;

        Image vpBorder = viewportObj.AddComponent<Image>();
        vpBorder.color = new Color(0.20f, 0.35f, 0.50f, 0.85f);

        GameObject vpInner = new GameObject("Viewport_Screen");
        vpInner.transform.SetParent(viewportObj.transform, false);
        RectTransform vpiRT = vpInner.AddComponent<RectTransform>();
        vpiRT.anchorMin = Vector2.zero;
        vpiRT.anchorMax = Vector2.one;
        vpiRT.offsetMin = new Vector2(2, 2);
        vpiRT.offsetMax = new Vector2(-2, -2);
        Image vpInnerBg = vpInner.AddComponent<Image>();
        vpInnerBg.color = new Color(0.045f, 0.060f, 0.090f, 1f);

        GameObject rawImgObj = new GameObject("RenderTexture_RawImage");
        rawImgObj.transform.SetParent(vpInner.transform, false);
        RectTransform rawRT = rawImgObj.AddComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = Vector2.zero;
        rawRT.offsetMax = Vector2.zero;
        RawImage rawImg = rawImgObj.AddComponent<RawImage>();
        rawImg.texture = _previewRenderTexture;

        GameObject vpHeader = new GameObject("Telemetry_Header");
        vpHeader.transform.SetParent(vpInner.transform, false);
        RectTransform vphRT = vpHeader.AddComponent<RectTransform>();
        vphRT.anchorMin = new Vector2(0.04f, 0.90f);
        vphRT.anchorMax = new Vector2(0.96f, 0.98f);
        vphRT.offsetMin = Vector2.zero;
        vphRT.offsetMax = Vector2.zero;
        Text vphText = vpHeader.AddComponent<Text>();
        vphText.font = font;
        vphText.text = "3D CHASSIS STAGING";
        vphText.fontSize = 11;
        vphText.fontStyle = FontStyle.Bold;
        vphText.color = new Color(0.35f, 0.85f, 1.0f, 0.85f);
        vphText.alignment = TextAnchor.MiddleLeft;

        GameObject dragHint = new GameObject("Drag_Hint");
        dragHint.transform.SetParent(vpInner.transform, false);
        RectTransform dhRT = dragHint.AddComponent<RectTransform>();
        dhRT.anchorMin = new Vector2(0.04f, 0.02f);
        dhRT.anchorMax = new Vector2(0.96f, 0.09f);
        dhRT.offsetMin = Vector2.zero;
        dhRT.offsetMax = Vector2.zero;
        Text dhText = dragHint.AddComponent<Text>();
        dhText.font = font;
        dhText.text = "< DRAG TO ROTATE 360° >";
        dhText.fontSize = 11;
        dhText.color = new Color(0.55f, 0.70f, 0.85f, 0.75f);
        dhText.alignment = TextAnchor.MiddleCenter;

        // --- 4B. UPPER RIGHT: Tactical Dossier & Stat Breakdown ---
        GameObject dossierObj = new GameObject("Tactical_Dossier_Box");
        dossierObj.transform.SetParent(rightPanel.transform, false);
        RectTransform dRT2 = dossierObj.AddComponent<RectTransform>();
        dRT2.anchorMin = new Vector2(0.47f, 0.485f);
        dRT2.anchorMax = new Vector2(1.0f, 1.0f);
        dRT2.offsetMin = Vector2.zero;
        dRT2.offsetMax = Vector2.zero;

        Image dBoxBorder = dossierObj.AddComponent<Image>();
        dBoxBorder.color = new Color(0.18f, 0.30f, 0.45f, 0.75f);

        GameObject dInner = new GameObject("Inner");
        dInner.transform.SetParent(dossierObj.transform, false);
        RectTransform diRT = dInner.AddComponent<RectTransform>();
        diRT.anchorMin = Vector2.zero;
        diRT.anchorMax = Vector2.one;
        diRT.offsetMin = new Vector2(2, 2);
        diRT.offsetMax = new Vector2(-2, -2);
        Image dBoxBg = dInner.AddComponent<Image>();
        dBoxBg.color = new Color(0.055f, 0.075f, 0.115f, 0.96f);

        // Operative Title
        GameObject dName = new GameObject("Dossier_Name");
        dName.transform.SetParent(dInner.transform, false);
        RectTransform dnRT = dName.AddComponent<RectTransform>();
        dnRT.anchorMin = new Vector2(0.04f, 0.86f);
        dnRT.anchorMax = new Vector2(0.96f, 0.97f);
        dnRT.offsetMin = Vector2.zero;
        dnRT.offsetMax = Vector2.zero;
        _dossierNameText = dName.AddComponent<Text>();
        _dossierNameText.font = font;
        _dossierNameText.fontSize = 19;
        _dossierNameText.fontStyle = FontStyle.Bold;
        _dossierNameText.color = Color.white;
        _dossierNameText.alignment = TextAnchor.MiddleLeft;

        // Role Badge
        GameObject dRole = new GameObject("Dossier_Role");
        dRole.transform.SetParent(dInner.transform, false);
        RectTransform drRT = dRole.AddComponent<RectTransform>();
        drRT.anchorMin = new Vector2(0.04f, 0.77f);
        drRT.anchorMax = new Vector2(0.96f, 0.86f);
        drRT.offsetMin = Vector2.zero;
        drRT.offsetMax = Vector2.zero;
        _dossierRoleText = dRole.AddComponent<Text>();
        _dossierRoleText.font = font;
        _dossierRoleText.fontSize = 12;
        _dossierRoleText.fontStyle = FontStyle.Bold;
        _dossierRoleText.alignment = TextAnchor.MiddleLeft;

        // Description / Tactical Lore
        GameObject dDesc = new GameObject("Dossier_Desc");
        dDesc.transform.SetParent(dInner.transform, false);
        RectTransform ddRT = dDesc.AddComponent<RectTransform>();
        ddRT.anchorMin = new Vector2(0.04f, 0.58f);
        ddRT.anchorMax = new Vector2(0.96f, 0.76f);
        ddRT.offsetMin = Vector2.zero;
        ddRT.offsetMax = Vector2.zero;
        _dossierDescText = dDesc.AddComponent<Text>();
        _dossierDescText.font = font;
        _dossierDescText.fontSize = 12;
        _dossierDescText.color = new Color(0.72f, 0.82f, 0.92f);
        _dossierDescText.alignment = TextAnchor.UpperLeft;

        // Stat Bars Container (4 stacked rows for clean readability)
        GameObject statsContainer = new GameObject("Stats_Grid");
        statsContainer.transform.SetParent(dInner.transform, false);
        RectTransform sgRT = statsContainer.AddComponent<RectTransform>();
        sgRT.anchorMin = new Vector2(0.04f, 0.22f);
        sgRT.anchorMax = new Vector2(0.96f, 0.56f);
        sgRT.offsetMin = Vector2.zero;
        sgRT.offsetMax = Vector2.zero;

        CreateStatBarRow(statsContainer.transform, font, "ARMOR / DEFENSE", 0f, 0.54f, 0.48f, 1f, out _statBarArmor, out _statValArmor);
        CreateStatBarRow(statsContainer.transform, font, "MOBILITY / SPEED", 0.52f, 0.54f, 1.0f, 1f, out _statBarMobility, out _statValMobility);
        CreateStatBarRow(statsContainer.transform, font, "FIREPOWER / ACCURACY", 0f, 0f, 0.48f, 0.46f, out _statBarFirepower, out _statValFirepower);
        CreateStatBarRow(statsContainer.transform, font, "SHIELD / TECH CAPACITY", 0.52f, 0f, 1.0f, 0.46f, out _statBarShield, out _statValShield);

        // Primary Weapon Line
        GameObject dWeapon = new GameObject("Dossier_Weapon");
        dWeapon.transform.SetParent(dInner.transform, false);
        RectTransform dwRT = dWeapon.AddComponent<RectTransform>();
        dwRT.anchorMin = new Vector2(0.04f, 0.12f);
        dwRT.anchorMax = new Vector2(0.96f, 0.21f);
        dwRT.offsetMin = Vector2.zero;
        dwRT.offsetMax = Vector2.zero;
        _dossierWeaponText = dWeapon.AddComponent<Text>();
        _dossierWeaponText.font = font;
        _dossierWeaponText.fontSize = 12;
        _dossierWeaponText.color = new Color(0.75f, 0.88f, 1.0f);
        _dossierWeaponText.alignment = TextAnchor.MiddleLeft;

        // Passive Trait Line
        GameObject dPassive = new GameObject("Dossier_Passive");
        dPassive.transform.SetParent(dInner.transform, false);
        RectTransform dpRT = dPassive.AddComponent<RectTransform>();
        dpRT.anchorMin = new Vector2(0.04f, 0.02f);
        dpRT.anchorMax = new Vector2(0.96f, 0.12f);
        dpRT.offsetMin = Vector2.zero;
        dpRT.offsetMax = Vector2.zero;
        _dossierPassiveText = dPassive.AddComponent<Text>();
        _dossierPassiveText.font = font;
        _dossierPassiveText.fontSize = 11;
        _dossierPassiveText.color = new Color(0.85f, 0.92f, 1.0f);
        _dossierPassiveText.alignment = TextAnchor.MiddleLeft;

        // --- 4C. MIDDLE-LOWER: Expanded Squad & Team Rosters Box (35.5% of Right Panel Height!) ---
        GameObject teamsSectionObj = new GameObject("Expanded_TeamRosters_Box");
        teamsSectionObj.transform.SetParent(rightPanel.transform, false);
        RectTransform tsRT = teamsSectionObj.AddComponent<RectTransform>();
        tsRT.anchorMin = new Vector2(0f, 0.11f);
        tsRT.anchorMax = new Vector2(1f, 0.465f);
        tsRT.offsetMin = Vector2.zero;
        tsRT.offsetMax = Vector2.zero;

        Image tsBorder = teamsSectionObj.AddComponent<Image>();
        tsBorder.color = new Color(0.18f, 0.32f, 0.48f, 0.80f);

        GameObject tsInner = new GameObject("Inner");
        tsInner.transform.SetParent(teamsSectionObj.transform, false);
        RectTransform tsiRT = tsInner.AddComponent<RectTransform>();
        tsiRT.anchorMin = Vector2.zero;
        tsiRT.anchorMax = Vector2.one;
        tsiRT.offsetMin = new Vector2(2, 2);
        tsiRT.offsetMax = new Vector2(-2, -2);
        Image tsBg = tsInner.AddComponent<Image>();
        tsBg.color = new Color(0.045f, 0.065f, 0.10f, 0.98f);

        // Teams Section Title Bar
        GameObject tsTitleObj = new GameObject("Teams_Section_Title");
        tsTitleObj.transform.SetParent(tsInner.transform, false);
        RectTransform tstRT = tsTitleObj.AddComponent<RectTransform>();
        tstRT.anchorMin = new Vector2(0.02f, 0.85f);
        tstRT.anchorMax = new Vector2(0.98f, 0.98f);
        tstRT.offsetMin = Vector2.zero;
        tstRT.offsetMax = Vector2.zero;
        Text tstText = tsTitleObj.AddComponent<Text>();
        tstText.font = font;
        tstText.text = "ACTIVE MATCHUP & SQUAD ROSTERS // REAL PLAYERS & CPU OPERATIVES";
        tstText.fontSize = 13;
        tstText.fontStyle = FontStyle.Bold;
        tstText.color = new Color(0.85f, 0.94f, 1.0f);
        tstText.alignment = TextAnchor.MiddleLeft;

        // Dynamic Team Roster Cards (Supports 2, 3, or 4 Teams side-by-side)
        Color[] cardBorders = new Color[]
        {
            new Color(0.15f, 0.65f, 0.95f, 0.65f), // Alpha Blue
            new Color(0.95f, 0.28f, 0.25f, 0.65f), // Bravo Red
            new Color(0.20f, 0.85f, 0.48f, 0.65f), // Charlie Green
            new Color(0.95f, 0.72f, 0.18f, 0.65f)  // Delta Amber
        };
        Color[] cardBgs = new Color[]
        {
            new Color(0.04f, 0.10f, 0.17f, 0.96f),
            new Color(0.16f, 0.05f, 0.06f, 0.96f),
            new Color(0.04f, 0.14f, 0.09f, 0.96f),
            new Color(0.15f, 0.11f, 0.04f, 0.96f)
        };

        for (int t = 0; t < 4; t++)
        {
            string shortName = OasisShooterManager.GetTeamShortName(t);
            GameObject tBox = new GameObject($"Roster_{shortName}_Box");
            tBox.transform.SetParent(tsInner.transform, false);
            RectTransform tbxRT = tBox.AddComponent<RectTransform>();
            tbxRT.anchorMin = new Vector2(0.015f + t * 0.24f, 0.04f);
            tbxRT.anchorMax = new Vector2(0.015f + (t + 1) * 0.24f - 0.01f, 0.83f);
            tbxRT.offsetMin = Vector2.zero;
            tbxRT.offsetMax = Vector2.zero;
            Image tbxBorder = tBox.AddComponent<Image>();
            tbxBorder.color = cardBorders[t];

            _teamRosterBoxes[t] = tBox;
            _teamRosterBoxRTs[t] = tbxRT;

            GameObject tInner = new GameObject($"{shortName}_Inner");
            tInner.transform.SetParent(tBox.transform, false);
            RectTransform tiRT = tInner.AddComponent<RectTransform>();
            tiRT.anchorMin = Vector2.zero;
            tiRT.anchorMax = Vector2.one;
            tiRT.offsetMin = new Vector2(2, 2);
            tiRT.offsetMax = new Vector2(-2, -2);
            Image tbxImg = tInner.AddComponent<Image>();
            tbxImg.color = cardBgs[t];

            GameObject tHdrObj = new GameObject("Header");
            tHdrObj.transform.SetParent(tInner.transform, false);
            RectTransform thRT = tHdrObj.AddComponent<RectTransform>();
            thRT.anchorMin = new Vector2(0.035f, 0.80f);
            thRT.anchorMax = new Vector2(0.965f, 0.97f);
            thRT.offsetMin = Vector2.zero;
            thRT.offsetMax = Vector2.zero;
            Text hdrTxt = tHdrObj.AddComponent<Text>();
            hdrTxt.font = font;
            hdrTxt.fontSize = 14;
            hdrTxt.fontStyle = FontStyle.Bold;
            hdrTxt.color = OasisShooterManager.GetTeamColor(t);
            hdrTxt.alignment = TextAnchor.MiddleLeft;
            hdrTxt.text = OasisShooterManager.GetTeamFullName(t);
            _teamRosterHeaders[t] = hdrTxt;

            GameObject tTxtObj = new GameObject("RosterList");
            tTxtObj.transform.SetParent(tInner.transform, false);
            RectTransform ttxRT = tTxtObj.AddComponent<RectTransform>();
            ttxRT.anchorMin = new Vector2(0.035f, 0.04f);
            ttxRT.anchorMax = new Vector2(0.965f, 0.78f);
            ttxRT.offsetMin = Vector2.zero;
            ttxRT.offsetMax = Vector2.zero;
            Text rTxt = tTxtObj.AddComponent<Text>();
            rTxt.font = font;
            rTxt.fontSize = 13;
            rTxt.lineSpacing = 1.08f;
            rTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            rTxt.verticalOverflow = VerticalWrapMode.Overflow;
            rTxt.color = Color.white;
            rTxt.alignment = TextAnchor.UpperLeft;
            rTxt.supportRichText = true;
            _teamRosterTexts[t] = rTxt;
        }

        RefreshMatchSettingsUI();

        // --- 4D. BOTTOM: Unified Full-Width "LAUNCH GAME MODE" Button ---
        GameObject deployBtnObj = new GameObject("Launch_GameMode_Button");
        deployBtnObj.transform.SetParent(rightPanel.transform, false);
        RectTransform depRT = deployBtnObj.AddComponent<RectTransform>();
        depRT.anchorMin = new Vector2(0f, 0f);
        depRT.anchorMax = new Vector2(1.0f, 0.092f);
        depRT.offsetMin = Vector2.zero;
        depRT.offsetMax = Vector2.zero;

        _confirmButtonBg = deployBtnObj.AddComponent<Image>();
        _confirmButtonBg.color = new Color(0.08f, 0.65f, 0.38f, 1.0f);

        Button deployBtn = deployBtnObj.AddComponent<Button>();
        deployBtn.targetGraphic = _confirmButtonBg;
        deployBtn.interactable = CanLocalPlayerStartGame();
        deployBtn.onClick.AddListener(() => StartDeploymentCountdown(isSpectatorMode));
        _confirmButton = deployBtn;

        GameObject depTextObj = new GameObject("Text");
        depTextObj.transform.SetParent(deployBtnObj.transform, false);
        RectTransform deptRT = depTextObj.AddComponent<RectTransform>();
        deptRT.anchorMin = Vector2.zero;
        deptRT.anchorMax = Vector2.one;
        deptRT.offsetMin = Vector2.zero;
        deptRT.offsetMax = Vector2.zero;
        _confirmButtonText = depTextObj.AddComponent<Text>();
        _confirmButtonText.font = font;
        _confirmButtonText.fontSize = 19;
        _confirmButtonText.fontStyle = FontStyle.Bold;
        _confirmButtonText.color = Color.white;
        _confirmButtonText.text = "LAUNCH GAME MODE";
        _confirmButtonText.alignment = TextAnchor.MiddleCenter;
    }

    private void CreateStatBarRow(
        Transform parent, Font font, string label, 
        float minX, float minY, float maxX, float maxY, 
        out RectTransform barFill, out Text valText)
    {
        GameObject row = new GameObject($"Stat_{label}");
        row.transform.SetParent(parent, false);
        RectTransform rRT = row.AddComponent<RectTransform>();
        rRT.anchorMin = new Vector2(minX, minY);
        rRT.anchorMax = new Vector2(maxX, maxY);
        rRT.offsetMin = Vector2.zero;
        rRT.offsetMax = Vector2.zero;

        // Label
        GameObject lblObj = new GameObject("Label");
        lblObj.transform.SetParent(row.transform, false);
        RectTransform lblRT = lblObj.AddComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0f, 0.5f);
        lblRT.anchorMax = new Vector2(0.70f, 1f);
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
        Text lbl = lblObj.AddComponent<Text>();
        lbl.font = font;
        lbl.text = label;
        lbl.fontSize = 11;
        lbl.color = new Color(0.60f, 0.72f, 0.85f);
        lbl.alignment = TextAnchor.MiddleLeft;

        // Percent Value
        GameObject valObj = new GameObject("Value");
        valObj.transform.SetParent(row.transform, false);
        RectTransform vRT = valObj.AddComponent<RectTransform>();
        vRT.anchorMin = new Vector2(0.70f, 0.5f);
        vRT.anchorMax = new Vector2(1f, 1f);
        vRT.offsetMin = Vector2.zero;
        vRT.offsetMax = Vector2.zero;
        valText = valObj.AddComponent<Text>();
        valText.font = font;
        valText.text = "80%";
        valText.fontSize = 11;
        valText.fontStyle = FontStyle.Bold;
        valText.color = Color.white;
        valText.alignment = TextAnchor.MiddleRight;

        // Track Background
        GameObject track = new GameObject("Track");
        track.transform.SetParent(row.transform, false);
        RectTransform trkRT = track.AddComponent<RectTransform>();
        trkRT.anchorMin = new Vector2(0f, 0.05f);
        trkRT.anchorMax = new Vector2(1f, 0.40f);
        trkRT.offsetMin = Vector2.zero;
        trkRT.offsetMax = Vector2.zero;
        Image trkImg = track.AddComponent<Image>();
        trkImg.color = new Color(0.12f, 0.16f, 0.22f, 1.0f);

        // Fill Bar
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(track.transform, false);
        barFill = fill.AddComponent<RectTransform>();
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0.8f, 1f);
        barFill.offsetMin = Vector2.zero;
        barFill.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.25f, 0.85f, 1.0f);
    }
}
