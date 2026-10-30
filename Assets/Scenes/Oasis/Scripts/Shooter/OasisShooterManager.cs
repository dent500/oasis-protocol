using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Cinemachine;

/// <summary>
/// Master Game & Wave Director for the Oasis FPS Shooter.
/// Handles drone spawning, wave progression, player health/damage, score, and shooter HUD.
/// </summary>
public class OasisShooterManager : MonoBehaviour
{
    public static OasisShooterManager Instance { get; private set; }

    [Header("Player Stats")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Wave Configuration")]
    public int currentWave = 1;
    public int totalDronesInWave = 5;
    public int dronesRemaining = 5;
    public int score = 0;
    public int kills = 0;

    [Header("Spawn Settings")]
    public Vector3 spawnCenter = new Vector3(0, 4f, 15f);
    public float spawnRadius = 25f;

    // HUD Elements
    private GameObject _hudCanvas;
    private Text _ammoText;
    private Text _waveText;
    private Text _dronesText;
    private Text _scoreText;
    private Slider _healthBar;
    private Image _healthFillImage;
    private Text _shieldPercentText;
    private Image _damageFlashImg;
    private Image _hitmarkerImg;
    private GameObject _bannerObj;
    private Text _bannerText;
    private GameObject _gameOverPanel;
    private Text _gameOverTitleText;
    private Text _gameOverDescText;

    private BlasterWeapon _weapon;
    private List<TargetDrone> _activeDrones = new List<TargetDrone>();
    private bool _isWaveTransition = false;
    private int _nextNetworkDroneId = 100;

    [Header("Team Deathmatch Combat")]
    public int alphaKills = 0;
    public int bravoKills = 0;
    public int charlieKills = 0;
    public int deltaKills = 0;
    public int[] teamKills = new int[4];
    public int activeNumTeams = 2;
    public int activePlayersPerTeam = 2;
    public int targetKills = 15;
    public bool isTeamBattleActive = false;
    public int localPlayerTeam => PlayerPrefs.GetInt("OasisPlayerTeam", 0);
    private readonly List<OasisCombatAI> _spawnedAIBots = new List<OasisCombatAI>();

    public static string GetTeamShortName(int teamId)
    {
        switch (teamId)
        {
            case 0: return "ALPHA";
            case 1: return "BRAVO";
            case 2: return "CHARLIE";
            case 3: return "DELTA";
            default: return $"TEAM {teamId + 1}";
        }
    }

    public static string GetTeamFullName(int teamId)
    {
        switch (teamId)
        {
            case 0: return "TEAM ALPHA (SENTINELS)";
            case 1: return "TEAM BRAVO (SYNDICATE)";
            case 2: return "TEAM CHARLIE (VANGUARD)";
            case 3: return "TEAM DELTA (OUTRIDERS)";
            default: return $"TEAM {teamId + 1}";
        }
    }

    public static string GetTeamColorHex(int teamId)
    {
        switch (teamId)
        {
            case 0: return "#26D9FF";
            case 1: return "#FF5544";
            case 2: return "#38EF7D";
            case 3: return "#FFBD2E";
            default: return "#FFFFFF";
        }
    }

    public static Color GetTeamColor(int teamId)
    {
        switch (teamId)
        {
            case 0: return new Color(0.15f, 0.85f, 1.0f);
            case 1: return new Color(1.0f, 0.35f, 0.25f);
            case 2: return new Color(0.22f, 0.94f, 0.49f);
            case 3: return new Color(1.0f, 0.74f, 0.18f);
            default: return Color.white;
        }
    }

    public void AddTeamKill(int teamId)
    {
        if (teamKills == null || teamKills.Length < 4) teamKills = new int[4];
        int clamped = Mathf.Clamp(teamId, 0, 3);
        teamKills[clamped]++;
        alphaKills = teamKills[0];
        bravoKills = teamKills[1];
        charlieKills = teamKills[2];
        deltaKills = teamKills[3];

        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastMatchSync(alphaKills, bravoKills, charlieKills, deltaKills, matchTimeRemaining);
        }
    }

    public void SyncTeamScoresFromHost(int aK, int bK, int cK, int dK, float remTime)
    {
        if (isMatchFinished) return;
        if (teamKills == null || teamKills.Length < 4) teamKills = new int[4];
        alphaKills = aK;
        bravoKills = bK;
        charlieKills = cK;
        deltaKills = dK;
        teamKills[0] = aK;
        teamKills[1] = bK;
        teamKills[2] = cK;
        teamKills[3] = dK;
        matchTimeRemaining = remTime;
        UpdateTeamBattleHUD();
    }

    public void ConcludeMatchFromHost(int winTeam, string reason, int aK, int bK, int cK, int dK)
    {
        if (isMatchFinished) return;
        if (teamKills == null || teamKills.Length < 4) teamKills = new int[4];
        alphaKills = aK;
        bravoKills = bK;
        charlieKills = cK;
        deltaKills = dK;
        teamKills[0] = aK;
        teamKills[1] = bK;
        teamKills[2] = cK;
        teamKills[3] = dK;
        ConcludeMatch(winTeam, reason);
    }

    [Header("Match Countdown Timer")]
    public float matchDuration = 180f; // 3 minutes standard match duration
    public float matchTimeRemaining = 180f;
    public bool isTimerRunning = false;

    [Header("Downed & Revival System")]
    public bool isPlayerDowned = false;
    public float playerDownedTimer = 10.0f;
    public float playerReviveProgress = 0.0f;

    public void AddPlayerScore(int amount)
    {
        score += amount;
        OasisNetworkManager.Instance?.BroadcastScore(score, kills);
        UpdateTeamBattleHUD();
    }

    public void ReviveLocalPlayer()
    {
        if (!isPlayerDowned) return;

        isPlayerDowned = false;
        playerReviveProgress = 0f;
        playerDownedTimer = 10.0f;
        currentHealth = maxHealth * 0.6f;
        UpdateHealthUI();

        if (_playerTransform != null)
        {
            var move = _playerTransform.GetComponent<OasisCombatMovement>();
            if (move != null) move.ResetMovementState();
            BlasterVFX.SpawnWarpIn(_playerTransform.position);
        }

        ShowBanner("<color=#26D9FF>SQUADMATE REVIVED YOU! RESUMING COMBAT AT POSITION</color>", 3.0f);
    }

    public void ChannelPlayerRevive(float amount, string reviverName)
    {
        if (!isPlayerDowned) return;

        playerReviveProgress += amount;
        ShowBanner($"<color=#26D9FF>{reviverName.ToUpper()} REVIVING SQUADMATE... {Mathf.RoundToInt(playerReviveProgress * 100)}%</color>", 0.25f);

        if (playerReviveProgress >= 1.0f)
        {
            ReviveLocalPlayer();
            if (OasisNetworkManager.Instance != null)
            {
                OasisNetworkManager.Instance.BroadcastPlayerRevived(OasisNetworkManager.Instance.myPlayerId, -1);
            }
            ShowBanner($"<color=#26D9FF>REVIVED BY {reviverName.ToUpper()}! RESUMING COMBAT AT POSITION</color>", 3.0f);
        }
    }

    public List<TargetDrone> GetActiveDrones() => _activeDrones;
    public int GetNextNetworkDroneId() => _nextNetworkDroneId++;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitializeInOasis()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name.IndexOf("Oasis", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (Instance == null)
                {
                    GameObject managerObj = new GameObject("OasisShooterManager");
                    Instance = managerObj.AddComponent<OasisShooterManager>();
                }
            }
        };

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene.IndexOf("Oasis", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (Instance == null)
            {
                GameObject managerObj = new GameObject("OasisShooterManager");
                Instance = managerObj.AddComponent<OasisShooterManager>();
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        currentHealth = maxHealth;
    }

    private Transform _playerTransform;

    [Header("Mission Launch State")]
    public bool isWaitingForOperatives = false;

    private void Start()
    {
        RemoveRestrictionsAndBarriers();
        SetupPlayerAndWeapon();
        InitializeWorldExpanderAndMap();
        BuildShooterHUD();

        // If character selection screen is currently present, hide HUD and wait for player deployment
        var customizer = FindAnyObjectByType<OasisCharacterCustomizer>();
        if (customizer != null && customizer.isSelectionActive)
        {
            SetHUDActive(false);
            return;
        }

        LaunchCombatFromSelection();
    }

    public void LaunchCombatFromSelection()
    {
        SetHUDActive(true);
        StartTeamBattle();
    }

    public void SetHUDActive(bool active)
    {
        if (_hudCanvas != null) _hudCanvas.SetActive(active);
    }

    private void InitializeWorldExpanderAndMap()
    {
        if (OasisSpawnSystem.Instance == null)
        {
            GameObject spawnSysObj = new GameObject("OasisSpawnSystem");
            spawnSysObj.AddComponent<OasisSpawnSystem>();
        }

        if (FindAnyObjectByType<OasisWorldExpander>() == null)
        {
            GameObject expanderObj = new GameObject("OasisWorldExpander");
            expanderObj.AddComponent<OasisWorldExpander>();
        }

        if (FindAnyObjectByType<OasisMapSystem>() == null)
        {
            GameObject mapObj = new GameObject("OasisMapSystem");
            mapObj.AddComponent<OasisMapSystem>();
        }

        if (FindAnyObjectByType<OasisNetworkManager>() == null)
        {
            GameObject netObj = new GameObject("OasisNetworkManager");
            netObj.AddComponent<OasisNetworkManager>();
        }

        if (FindAnyObjectByType<OasisCharacterCustomizer>() == null)
        {
            var player = OasisPlayerHelper.FindPlayerTransform();
            if (player != null && player.GetComponent<OasisCharacterCustomizer>() == null)
            {
                player.gameObject.AddComponent<OasisCharacterCustomizer>();
            }
        }
    }

    public void ShowNotificationBanner(string message, float duration = 2.5f)
    {
        ShowBanner(message, duration);
    }

    public Transform GetPlayerTransform() => _playerTransform;

    // Safe Spawn Shelter
    [Header("Safe Spawn Shelter")]
    public Vector3 SafeShelterCenter { get; set; } = new Vector3(0f, -2.0f, -112.0f);
    public float SafeShelterRadius { get; set; } = 12.0f;
    public bool HasSafeShelter { get; set; } = true;
    private Vector3 _initialSpawnPosition;
    private bool _wasInShelter = true;

    [Header("2v2 AI Simulation & Spectator")]
    public bool isSimulationMode = false;
    public int spectatedBotIndex = 0;
    public bool isFreeFlySpectator = false;

    [Header("Match Concluded State")]
    public bool isMatchFinished = false;
    public string matchEndTitle = "";
    public string matchEndDetails = "";
    public int matchWinningTeam = -1; // 0 = Alpha, 1 = Bravo, -1 = Draw

    private float _regenDelayTimer = 0f;
    private float _hostMatchSyncTimer = 0.5f;
    private AudioSource _audioSource;

    private void Update()
    {
        if (isMatchFinished)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // Host periodic synchronization of team kills and timer to all connected clients
        if (isTeamBattleActive && OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            _hostMatchSyncTimer -= Time.deltaTime;
            if (_hostMatchSyncTimer <= 0f)
            {
                _hostMatchSyncTimer = 0.5f;
                OasisNetworkManager.Instance.BroadcastMatchSync(alphaKills, bravoKills, charlieKills, deltaKills, matchTimeRemaining);
            }
        }

        // Handle Spectator Controls when in 2v2 AI Simulation Mode
        if (isSimulationMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Match Countdown Timer
            if (isTeamBattleActive && isTimerRunning)
            {
                if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
                {
                    matchTimeRemaining = Mathf.Max(0f, matchTimeRemaining - Time.deltaTime);
                    UpdateTeamBattleHUD();
                }
                else
                {
                    matchTimeRemaining -= Time.deltaTime;
                    if (matchTimeRemaining <= 0f)
                    {
                        matchTimeRemaining = 0f;
                        EndMatchByTime();
                    }
                    UpdateTeamBattleHUD();
                }
            }
            return;
        }

        // Match Countdown Timer
        if (isTeamBattleActive && isTimerRunning)
        {
            if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
            {
                matchTimeRemaining = Mathf.Max(0f, matchTimeRemaining - Time.deltaTime);
                UpdateTeamBattleHUD();
            }
            else
            {
                matchTimeRemaining -= Time.deltaTime;
                if (matchTimeRemaining <= 0f)
                {
                    matchTimeRemaining = 0f;
                    EndMatchByTime();
                }
                UpdateTeamBattleHUD();
            }
        }

        // Handle Downed Player State in Team Combat
        if (isTeamBattleActive && isPlayerDowned)
        {
            if (playerReviveProgress <= 0.02f)
            {
                playerDownedTimer -= Time.deltaTime;
            }
            if (playerDownedTimer <= 0f)
            {
                // Bleedout expired! Re-deploy at base
                OasisAITelemetryLogger.Instance?.RecordPlayerBleedout();
                isPlayerDowned = false;
                playerDownedTimer = 0f;
                int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

                UpdateTeamBattleHUD();
                ShowBanner("<color=#FF4444>BLEEDOUT COMPLETE // RE-DEPLOYING AT BASE...</color>", 3.0f);

                currentHealth = maxHealth;
                UpdateHealthUI();

                if (_playerTransform != null)
                {
                    var move = _playerTransform.GetComponent<OasisCombatMovement>();
                    if (move != null) move.ResetMovementState();
                }

                if (OasisSpawnSystem.Instance != null && _playerTransform != null)
                {
                    OasisSpawnSystem.Instance.RespawnCombatant(_playerTransform, myTeam);
                }

                ShowBanner("CHASSIS RE-DEPLOYED AT BASE // ENGAGE OPPONENTS", 2.0f);
            }
            return;
        }

        // Shield regeneration after a delay from taking damage
        if (_regenDelayTimer > 0f)
        {
            _regenDelayTimer -= Time.deltaTime;
        }
        else if (currentHealth < maxHealth)
        {
            currentHealth = Mathf.Min(maxHealth, currentHealth + 12f * Time.deltaTime);
            UpdateHealthUI();
        }

        // Void catch safeguard: safely return player to their initial ground spawn
        if (_playerTransform != null && _playerTransform.position.y < -15f)
        {
            var cc = _playerTransform.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _playerTransform.position = _initialSpawnPosition + Vector3.up * 0.5f;
            if (cc != null) cc.enabled = true;
        }

        // Monitor player entering / leaving safe shelter
        if (_playerTransform != null && HasSafeShelter)
        {
            bool inShelter = IsPositionInSafeShelter(_playerTransform.position);
            if (!inShelter && _wasInShelter)
            {
                _wasInShelter = false;
                ShowBanner("LEAVING SAFE SHELTER: ENGAGING HOSTILES", 2f);
            }
            else if (inShelter && !_wasInShelter)
            {
                _wasInShelter = true;
                ShowBanner("SHELTER SECURED: SHIELDS PROTECTED", 2f);
            }
        }

        // Optional F key to toggle fog effect on/off
        bool fPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            fPressed = Keyboard.current.fKey.wasPressedThisFrame;
        }
#else
        fPressed = Input.GetKeyDown(KeyCode.F);
#endif
        if (fPressed)
        {
            ToggleOasisFog();
        }

        // Scoreboard key toggle for live match leaderboard
        bool tabPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Scoreboard);
        if (tabPressed && OasisNetworkManager.Instance != null)
        {
            OasisNetworkManager.Instance.ToggleScoreboard();
        }
    }

    private void LateUpdate()
    {
        if (isSimulationMode)
        {
            HandleSpectatorControls();
        }
    }

    /// <summary>
    /// Removes any boundary restriction bubbles, invisible walls, and expands fog distance
    /// so the entire canyon and oasis world can be explored freely.
    /// </summary>
    private void RemoveRestrictionsAndBarriers()
    {
        // 1. Disable invisible wall colliders (LeftWall, RightWall, etc.)
        // NOTE: Keep FloorCollider ACTIVE so the player always has solid ground platform underfoot!
        string[] barrierNames = new string[] { "LeftWall", "RightWall", "ShadowProxy", "Terrain_01_ShadowCage_Mesh" };
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        foreach (var obj in allObjects)
        {
            if (obj == null) continue;
            foreach (var bName in barrierNames)
            {
                if (obj.name.Equals(bName, System.StringComparison.OrdinalIgnoreCase) ||
                    (obj.name.Contains("Wall") && obj.GetComponent<BoxCollider>() != null && obj.GetComponent<MeshFilter>() == null))
                {
                    Collider col = obj.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                    obj.SetActive(false);
                    Debug.Log($"[OasisShooter] Disabled restriction barrier: {obj.name}");
                }
            }
        }

        // 1b. Sanitize untextured insect particles that cause floating pink/magenta dots in standalone builds
        SanitizeInsectsAndVFX();

        // 2. Make Outdoor Volume global so atmospheric lighting doesn't drop off
        UnityEngine.Rendering.Volume[] volumes = FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include);
        foreach (var vol in volumes)
        {
            if (vol != null && vol.name.Contains("Outdoor", System.StringComparison.OrdinalIgnoreCase))
            {
                vol.isGlobal = true;
                // Expand or remove box collider trigger
                BoxCollider boxCol = vol.GetComponent<BoxCollider>();
                if (boxCol != null) boxCol.enabled = false;
            }
        }

        // 3. Clear/Expand Fog bubble distance
        OasisFog fogEffect = FindAnyObjectByType<OasisFog>();
        if (fogEffect != null)
        {
            // Adjust volume component settings if present
            if (UnityEngine.Rendering.VolumeManager.instance != null && UnityEngine.Rendering.VolumeManager.instance.stack != null)
            {
                var fogVol = UnityEngine.Rendering.VolumeManager.instance.stack.GetComponent<OasisFogVolumeComponent>();
                if (fogVol != null)
                {
                    fogVol.StartDistance.value = 150f;
                    fogVol.Density.value = 0.0008f;
                }
            }
        }
    }

    /// <summary>
    /// In standalone builds, VFX Graph assets that reference missing/stripped built-in particle textures
    /// (such as VFXG_Insects) render untextured bright magenta/pink rectangular dots floating in the sky.
    /// Disabling them removes the pink dots completely.
    /// </summary>
    private void SanitizeInsectsAndVFX()
    {
        var allVFX = FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Include);
        int disabledCount = 0;
        foreach (var vfx in allVFX)
        {
            if (vfx == null) continue;
            string n = vfx.gameObject.name;
            string assetName = (vfx.visualEffectAsset != null) ? vfx.visualEffectAsset.name : "";
            if (n.IndexOf("Insect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                assetName.IndexOf("Insect", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                vfx.enabled = false;
                vfx.gameObject.SetActive(false);
                disabledCount++;
            }
        }
        if (disabledCount > 0)
        {
            Debug.Log($"[OasisShooter] Disabled {disabledCount} untextured insect VFX graphs to prevent pink dots in the sky.");
        }
    }

    private void ToggleOasisFog()
    {
        OasisFog fog = FindAnyObjectByType<OasisFog>();
        if (fog != null)
        {
            fog.enabled = !fog.enabled;
            ShowBanner(fog.enabled ? "ATMOSPHERIC FOG: ON" : "ATMOSPHERIC FOG: OFF", 1.5f);
        }
    }

    private void SetupPlayerAndWeapon()
    {
        // 1. Disable Idle Flythrough on PlayerManager so camera never cuts away
        PlayerManager pm = FindAnyObjectByType<PlayerManager>();
        if (pm != null)
        {
            var field = typeof(PlayerManager).GetField("m_FlythroughWhenIdle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) field.SetValue(pm, false);
            pm.EnableFirstPersonController();
        }

        // 2. Tune FirstPersonController for fast, fluid movement and jump
        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.MoveSpeed = 7.0f;
            fpc.SprintSpeed = 10.5f;
            fpc.JumpHeight = 1.65f;
            fpc.Gravity = -18.0f;
            fpc.GroundedOffset = 0.14f;
            fpc.GroundedRadius = 0.45f;
            int ignoreMask = (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << fpc.gameObject.layer) | (1 << 8);
            fpc.GroundLayers = Physics.AllLayers & ~ignoreMask;

            if (fpc.GetComponent<OasisCombatMovement>() == null)
            {
                fpc.gameObject.AddComponent<OasisCombatMovement>();
            }
        }

        // 3. Attach BlasterWeapon and tune Camera FOV
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.fieldOfView = 75f;
            _weapon = cam.GetComponent<BlasterWeapon>();
            if (_weapon == null)
            {
                _weapon = cam.gameObject.AddComponent<BlasterWeapon>();
            }
            spawnCenter = cam.transform.position + cam.transform.forward * 12f;
        }

        var playerTrans = OasisPlayerHelper.FindPlayerTransform();
        if (playerTrans != null)
        {
            _playerTransform = playerTrans;
            _initialSpawnPosition = _playerTransform.position;
            SafeShelterCenter = _initialSpawnPosition;
            HasSafeShelter = true;
        }

        // Ensure cursor is locked for FPS gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void StartWave(int waveNumber)
    {
        // In multiplayer client mode, wave spawning is authoritative on the Host.
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
        {
            return;
        }

        isWaitingForOperatives = false;
        currentWave = waveNumber;
        _isWaveTransition = false;

        // Scale drone count and difficulty
        totalDronesInWave = 4 + (waveNumber * 3); // Wave 1: 7, Wave 2: 10, Wave 3: 13
        dronesRemaining = totalDronesInWave;

        UpdateWaveHUD();
        if (waveNumber == 1)
        {
            ShowBanner("SAFE SHELTER ACTIVE: EXIT TENT TO ENGAGE", 4.5f);
        }
        else
        {
            ShowBanner("WAVE " + waveNumber, 2f);
        }

        StartCoroutine(SpawnWaveDrones());
    }

    private IEnumerator SpawnWaveDrones()
    {
        float prepTime = (currentWave == 1) ? 5.0f : 2.0f;
        yield return new WaitForSeconds(prepTime);

        for (int i = 0; i < totalDronesInWave; i++)
        {
            SpawnSingleDrone(i);
            yield return new WaitForSeconds(1.2f); // Clear, staggered materialization
        }
    }

    private void SpawnSingleDrone(int index)
    {
        // Find player position or default to spawn center
        Vector3 playerPos = (_playerTransform != null) ? _playerTransform.position : spawnCenter;

        // Distinct combat spawn sectors distributed across canyon and dunes (35m to 62m away)
        Vector3[] spawnSectorBases = new Vector3[]
        {
            new Vector3(32f, 0f, 40f),    // Sector Alpha: North-East Dunes Ridge
            new Vector3(-35f, 0f, 38f),   // Sector Beta: North-West Canyon Pass
            new Vector3(42f, 0f, -15f),   // Sector Gamma: East Rock Formations
            new Vector3(-38f, 0f, -22f),  // Sector Delta: South-West Palm Oasis Edge
            new Vector3(0f, 0f, 52f)      // Sector Epsilon: Far Northern Valley Plateau
        };

        Vector3 chosenBase = spawnSectorBases[index % spawnSectorBases.Length];
        Vector3 jitter = new Vector3(Random.Range(-9f, 9f), 0f, Random.Range(-9f, 9f));
        Vector3 targetXZ = chosenBase + jitter;

        // Enforce strict distance from Bunker / Safe Shelter so enemies NEVER spawn near or above the bunker
        Vector3 shelterCenter = HasSafeShelter ? SafeShelterCenter : playerPos;
        float distToShelter = Vector2.Distance(new Vector2(targetXZ.x, targetXZ.z), new Vector2(shelterCenter.x, shelterCenter.z));
        float minRequiredDist = SafeShelterRadius + 18f; // At least 34m away from shelter
        if (distToShelter < minRequiredDist)
        {
            Vector2 away = (new Vector2(targetXZ.x, targetXZ.z) - new Vector2(shelterCenter.x, shelterCenter.z)).normalized;
            if (away.sqrMagnitude < 0.01f) away = Vector2.up;
            Vector2 pushed = new Vector2(shelterCenter.x, shelterCenter.z) + away * (minRequiredDist + Random.Range(6f, 16f));
            targetXZ.x = pushed.x;
            targetXZ.z = pushed.y;
        }

        // Determine enemy archetype based on wave and sequence
        EnemyType archetype = EnemyType.ScoutDrone;
        float roll = Random.value;
        if (currentWave == 1)
        {
            // Wave 1: 55% Scout Drone, 45% Ground Walker Mech
            archetype = (roll < 0.55f) ? EnemyType.ScoutDrone : EnemyType.GroundWalkerMech;
        }
        else
        {
            // Wave 2+: 40% Scout Drone, 35% Ground Walker Mech, 25% Heavy Siege Drone
            if (roll < 0.40f) archetype = EnemyType.ScoutDrone;
            else if (roll < 0.75f) archetype = EnemyType.GroundWalkerMech;
            else archetype = EnemyType.HeavySiegeDrone;
        }

        // Sample terrain height
        float spawnY = playerPos.y + 4.0f;
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(targetXZ.x, 80f, targetXZ.z), Vector3.down, out hit, 120f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (archetype == EnemyType.GroundWalkerMech)
            {
                spawnY = hit.point.y + 0.9f;
            }
            else
            {
                spawnY = hit.point.y + Random.Range(3.5f, 6.0f);
            }
        }

        Vector3 spawnPos = new Vector3(targetXZ.x, spawnY, targetXZ.z);

        string archetypePrefix = (archetype == EnemyType.HeavySiegeDrone) ? "HeavySiege_" :
                                 (archetype == EnemyType.GroundWalkerMech) ? "GroundMech_" : "ScoutDrone_";
        GameObject droneObj = new GameObject(archetypePrefix + "W" + currentWave + "_" + (index + 1));
        droneObj.transform.position = spawnPos;

        TargetDrone drone = droneObj.AddComponent<TargetDrone>();
        drone.enemyType = archetype;
        drone.isHostile = true;

        // Archetype stats tuning
        switch (archetype)
        {
            case EnemyType.HeavySiegeDrone:
                drone.maxHealth = 130f + (currentWave * 20f);
                drone.currentHealth = drone.maxHealth;
                drone.moveSpeed = 1.6f + (currentWave * 0.15f);
                drone.attackInterval = 2.8f;
                drone.scoreValue = 350;
                drone.hoverAmplitude = 0.25f;
                drone.hoverFrequency = 1.2f;
                break;

            case EnemyType.GroundWalkerMech:
                drone.maxHealth = 90f + (currentWave * 15f);
                drone.currentHealth = drone.maxHealth;
                drone.moveSpeed = 2.0f + (currentWave * 0.2f);
                drone.attackInterval = 3.2f;
                drone.scoreValue = 250;
                drone.hoverAmplitude = 0f;
                drone.minTerrainHeight = 0.8f;
                break;

            case EnemyType.ScoutDrone:
            default:
                drone.maxHealth = 50f + (currentWave * 10f);
                drone.currentHealth = drone.maxHealth;
                drone.moveSpeed = 2.6f + (currentWave * 0.25f);
                drone.attackInterval = 3.5f;
                drone.scoreValue = 150;
                drone.hoverAmplitude = Random.Range(0.35f, 0.55f);
                drone.hoverFrequency = Random.Range(1.5f, 2.2f);
                break;
        }

        // Assign incremental network drone ID for multiplayer synchronization
        drone.networkDroneId = _nextNetworkDroneId++;
        drone.isNetworkPuppet = false;

        _activeDrones.Add(drone);

        // If online Host, announce drone spawn to connected clients
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.RegisterHostDrone(drone);
        }
    }

    /// <summary>
    /// Registers a puppet drone received over the network on a client.
    /// Ensures active drone tracking, radar display, and HUD hostiles counter stay 100% in sync with the Host.
    /// </summary>
    public void RegisterClientPuppetDrone(TargetDrone puppet)
    {
        if (puppet == null) return;
        if (!_activeDrones.Contains(puppet))
        {
            _activeDrones.Add(puppet);
        }
        UpdateWaveHUD();
    }

    /// <summary>
    /// Synchronizes the wave number and total/remaining drone counts directly from the authoritative Host.
    /// </summary>
    public void SyncClientWave(int waveNumber, int totalDrones, int remaining)
    {
        currentWave = waveNumber;
        totalDronesInWave = totalDrones;
        dronesRemaining = remaining;
        UpdateWaveHUD();
    }

    public void OnTargetHit(float damage, bool isKill)
    {
        // Flash crosshair hitmarker
        if (_hitmarkerImg != null)
        {
            StartCoroutine(FlashHitmarker(isKill ? Color.red : Color.white));
        }
    }

    private IEnumerator FlashHitmarker(Color color)
    {
        _hitmarkerImg.color = color;
        _hitmarkerImg.enabled = true;
        yield return new WaitForSeconds(0.08f);
        if (_hitmarkerImg != null) _hitmarkerImg.enabled = false;
    }

    /// <summary>
    /// Destroys any locally spawned drones on this client so it only renders Host-synchronized drones.
    /// </summary>
    public void CleanupLocalDrones()
    {
        StopAllCoroutines();
        for (int i = _activeDrones.Count - 1; i >= 0; i--)
        {
            if (_activeDrones[i] != null && _activeDrones[i].gameObject != null)
            {
                Destroy(_activeDrones[i].gameObject);
            }
        }
        _activeDrones.Clear();
        if (OasisNetworkManager.Instance == null || OasisNetworkManager.Instance.currentRole != OasisNetworkManager.NetworkRole.Client)
        {
            dronesRemaining = 0;
        }
        UpdateWaveHUD();
    }

    public void OnTargetDestroyed(int pointsAwarded, TargetDrone destroyedDrone = null, int killerId = 0)
    {
        int myPlayerId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        bool isMyKill = (killerId == myPlayerId);

        // Only credit points and kills to the player who actually destroyed the drone
        if (isMyKill)
        {
            score += pointsAwarded;
            kills++;
            // Sync updated personal score with multiplayer scoreboard
            OasisNetworkManager.Instance?.BroadcastScore(score, kills);
        }

        if (destroyedDrone != null)
        {
            _activeDrones.Remove(destroyedDrone);
        }
        else
        {
            // Remove any null / destroyed references in _activeDrones
            for (int i = _activeDrones.Count - 1; i >= 0; i--)
            {
                if (_activeDrones[i] == null || _activeDrones[i].gameObject == null)
                {
                    _activeDrones.RemoveAt(i);
                }
            }
        }
        dronesRemaining = Mathf.Max(0, dronesRemaining - 1);
        UpdateWaveHUD();

        // Host broadcasts updated wave state to clients
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastWaveState(currentWave, totalDronesInWave, dronesRemaining);
        }

        if (dronesRemaining <= 0 && !_isWaveTransition)
        {
            _isWaveTransition = true;
            StartCoroutine(OnWaveComplete());
        }
    }

    private IEnumerator OnWaveComplete()
    {
        ShowBanner("WAVE CLEARED!", 2.5f);
        yield return new WaitForSeconds(3.5f);
        StartWave(currentWave + 1);
    }

    public static bool IsPositionInSafeShelter(Vector3 position)
    {
        if (Instance == null || !Instance.HasSafeShelter) return false;
        Vector2 posXZ = new Vector2(position.x, position.z);
        Vector2 shelterXZ = new Vector2(Instance.SafeShelterCenter.x, Instance.SafeShelterCenter.z);
        return Vector2.Distance(posXZ, shelterXZ) < Instance.SafeShelterRadius;
    }

    public void TakeDamage(float amount, int attackerTeamId = -1, OasisCombatAI attackerBot = null) => DamagePlayer(amount, attackerTeamId, attackerBot);

    public void DamagePlayer(float amount, int attackerTeamId = -1, OasisCombatAI attackerBot = null)
    {
        if (isTeamBattleActive && isPlayerDowned) return;

        // Safe spawn shelter invulnerability only applies to survival wave drone defense mode, NOT competitive team deathmatch!
        if (!isTeamBattleActive && amount > 0f && _playerTransform != null && IsPositionInSafeShelter(_playerTransform.position))
        {
            // Fully deflected by Safe Spawn Shelter defenses
            BlasterVFX.SpawnImpact(_playerTransform.position + Vector3.up * 1.0f, Vector3.up, false);
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        _regenDelayTimer = 4.0f; // 4 seconds delay after taking damage before shields begin recharging

        string attackerBotName = attackerBot != null ? attackerBot.botName : "ENEMY";
        OasisAITelemetryLogger.Instance?.RecordPlayerDamageTaken(amount, attackerBotName);

        UpdateHealthUI();

        // Sound effect
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        if (_audioSource != null)
        {
            if (currentHealth <= 0f)
            {
                _audioSource.PlayOneShot(ProceduralAudio.GetShieldBreakClip(), 1f);
            }
            else
            {
                _audioSource.PlayOneShot(ProceduralAudio.GetPlayerHurtClip(), 0.9f);
            }
        }

        // Damage flash
        if (_damageFlashImg != null)
        {
            StartCoroutine(FlashDamageScreen());
        }

        // Subtle camera hit shake
        if (Camera.main != null)
        {
            StartCoroutine(CameraHitShake(0.18f, 0.15f));
        }

        if (currentHealth <= 0)
        {
            currentHealth = 0f;
            if (isTeamBattleActive)
            {
                if (!isPlayerDowned)
                {
                    isPlayerDowned = true;
                    playerDownedTimer = 8.0f;
                    playerReviveProgress = 0f;
                    OasisAITelemetryLogger.Instance?.RecordPlayerKnockedDown(attackerBotName);

                    // Credit attacking CPU bot with kill and knockdown score
                    if (attackerBot != null)
                    {
                        attackerBot.kills++;
                        attackerBot.score += 100;
                    }

                    // Opposing team scores on knockdown
                    int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                    int scoringTeam = (attackerTeamId >= 0 && attackerTeamId != myTeam) 
                        ? attackerTeamId 
                        : ((myTeam + 1) % Mathf.Max(2, activeNumTeams));
                    AddTeamKill(scoringTeam);

                    // Broadcast downed state to network peers immediately
                    OasisNetworkManager.Instance?.BroadcastPlayerDowned(attackerBot != null ? attackerBot.teamId : -1, scoringTeam);

                    UpdateTeamBattleHUD();
                    CheckWinCondition();

                    ShowBanner("<color=#FF4444>CHASSIS DOWNED // AWAITING SQUADMATE REVIVE (8s)</color>", 3.0f);
                }
            }
            else
            {
                GameOver();
            }
        }
    }

    private void UpdateHealthUI()
    {
        if (_healthBar != null)
        {
            _healthBar.value = currentHealth;
            // Dynamically change color based on health remaining
            if (_healthFillImage != null)
            {
                if (currentHealth > 60f)
                    _healthFillImage.color = new Color(0.2f, 0.85f, 0.45f);
                else if (currentHealth > 30f)
                    _healthFillImage.color = new Color(0.95f, 0.75f, 0.2f);
                else
                    _healthFillImage.color = new Color(0.95f, 0.2f, 0.2f);
            }
        }

        if (_shieldPercentText != null)
        {
            _shieldPercentText.text = $"SHIELD: {Mathf.CeilToInt((currentHealth / maxHealth) * 100f)}%";
        }
    }

    public void TriggerCameraShake(float duration = 0.2f, float magnitude = 0.2f)
    {
        StartCoroutine(CameraHitShake(duration, magnitude));
    }

    private IEnumerator CameraHitShake(float duration, float magnitude)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 originalPos = cam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            cam.transform.localPosition = originalPos + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.transform.localPosition = originalPos;
    }

    private IEnumerator FlashDamageScreen()
    {
        _damageFlashImg.color = new Color(0.9f, 0.15f, 0.15f, 0.45f);
        yield return new WaitForSeconds(0.14f);
        _damageFlashImg.color = Color.clear;
    }

    private void GameOver()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (_gameOverPanel != null) _gameOverPanel.SetActive(true);
    }

    public void RestartShooter()
    {
        Time.timeScale = 1f;
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastRestart();
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenu()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(0);
    }

    public void StartTeamBattle()
    {
        isTeamBattleActive = true;
        isWaitingForOperatives = false;
        isMatchFinished = false;
        matchWinningTeam = -1;
        matchEndTitle = "";
        matchEndDetails = "";
        if (_gameOverPanel != null) _gameOverPanel.SetActive(false);

        alphaKills = 0;
        bravoKills = 0;
        charlieKills = 0;
        deltaKills = 0;
        teamKills = new int[4];

        // Clean up any remaining target drones
        foreach (var drone in _activeDrones)
        {
            if (drone != null && drone.gameObject != null) Destroy(drone.gameObject);
        }
        _activeDrones.Clear();

        // Clean up any previously spawned AI bots
        foreach (var bot in _spawnedAIBots)
        {
            if (bot != null && bot.gameObject != null) Destroy(bot.gameObject);
        }
        _spawnedAIBots.Clear();

        // Read number of teams (2..4), players per team (1..4), and player team
        activeNumTeams = Mathf.Clamp(PlayerPrefs.GetInt("OasisNumTeams", 2), 2, 4);
        activePlayersPerTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayersPerTeam", 2), 1, 4);
        int localPlayerTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayerTeam", 0), 0, activeNumTeams - 1);

        int teamCapacity = activePlayersPerTeam;
        targetKills = Mathf.Max(8, teamCapacity * 5);

        // Calculate human count on each team based on actual selected team of local player & connected remote operatives
        int[] humansPerTeam = new int[4];
        List<OasisNetworkManager.ConnectedOperativeInfo> remoteOps = (OasisNetworkManager.Instance != null)
            ? OasisNetworkManager.Instance.GetRemoteOperatives()
            : new List<OasisNetworkManager.ConnectedOperativeInfo>();

        if (!isSimulationMode)
        {
            humansPerTeam[localPlayerTeam] = 1;
            if (OasisNetworkManager.Instance != null)
            {
                OasisNetworkManager.Instance.PrepareRemotePlayersForBattle(activeNumTeams);
                for (int r = 0; r < remoteOps.Count; r++)
                {
                    int rTeam = Mathf.Clamp(remoteOps[r].teamId, 0, activeNumTeams - 1);
                    if (teamCapacity == 1 && remoteOps[r].isLocalPlaceholder && rTeam == localPlayerTeam)
                    {
                        rTeam = (localPlayerTeam + 1) % activeNumTeams;
                    }
                    humansPerTeam[rTeam]++;
                }
            }
        }
        else
        {
            targetKills = 9999; // Simulation mode runs for full 3-minute timed duration
        }

        // Always initialize AI Telemetry Logger so post-match analytics & Copy Telemetry work in both Character and Simulation modes
        if (OasisAITelemetryLogger.Instance == null)
        {
            gameObject.AddComponent<OasisAITelemetryLogger>();
        }
        OasisAITelemetryLogger.Instance.StartNewSession();
        OasisAITelemetryLogger.Instance.showLiveTelemetryHUD = isSimulationMode;

        // Randomly load teams at distinct tactical sectors (North, East, South, West, or Core)
        if (OasisSpawnSystem.Instance != null)
        {
            OasisSpawnSystem.Instance.PickRandomMatchup();
        }

        // Operative roster definitions for bot random variety
        string[] classIds = new string[] {
            "heavy_warrior", "desert_commando", "cyborg_specops", "unit07_android",
            "wasteland_nomad", "combat_medic", "cyber_outlaw"
        };
        string[] texPaths = new string[] {
            "RobotHero_URP", "Characters/Textures/skin_soldier", "Characters/Textures/cyborgFemaleA",
            "Characters/Textures/skin_robot", "Characters/Textures/skin_adventurer",
            "Characters/Textures/survivorFemaleA", "Characters/Textures/criminalMaleA"
        };
        string[][] teamCallsigns = new string[][]
        {
            new string[] { "AXEL", "JINX", "TORQUE", "CIPHER", "VALKYRIE" },
            new string[] { "RAZOR", "VIPER", "HAVOC", "STRIKER", "BLITZ" },
            new string[] { "SPECTRE", "TITAN", "PHANTOM", "VORTEX", "KINETIC" },
            new string[] { "REAPER", "BLAZE", "DRAKE", "WARP", "APEX" }
        };

        // If any remote operative is a local-test squad placeholder (e.g. testing CONNECT & JOIN SQUAD on one PC),
        // spawn an active autonomous operative for them with their exact callsign, team, and chosen skin!
        if (!isSimulationMode)
        {
            for (int r = 0; r < remoteOps.Count; r++)
            {
                var rop = remoteOps[r];
                if (rop.isLocalPlaceholder)
                {
                    int rTeam = Mathf.Clamp(rop.teamId, 0, activeNumTeams - 1);
                    if (teamCapacity == 1 && rTeam == localPlayerTeam)
                    {
                        rTeam = (localPlayerTeam + 1) % activeNumTeams;
                    }
                    string tShort = GetTeamShortName(rTeam);
                    int opIdx = Mathf.Clamp(rop.operativeIndex, 0, classIds.Length - 1);
                    SpawnCombatAIBot(rTeam, rop.playerName, classIds[opIdx], texPaths[opIdx]);
                }
            }
        }

        // Spawn AI Bots for all active teams (0 .. activeNumTeams - 1), swapping out AI bots for every real player on that team
        for (int t = 0; t < activeNumTeams; t++)
        {
            int botsNeeded = isSimulationMode ? teamCapacity : Mathf.Max(0, teamCapacity - humansPerTeam[t]);
            string tShort = GetTeamShortName(t);
            string[] callsigns = teamCallsigns[t % teamCallsigns.Length];

            int slotOffset = (!isSimulationMode && t == localPlayerTeam) ? 1 : 0;
            for (int i = 0; i < botsNeeded; i++)
            {
                int rIndex = Random.Range(0, classIds.Length);
                string callsign = callsigns[i % callsigns.Length];
                int slot = (i + slotOffset) % 4;
                SpawnCombatAIBot(t, callsign, classIds[rIndex], texPaths[rIndex], slot);
            }
        }

        // Teleport local player to team base or spectator vantage
        if (_playerTransform != null)
        {
            if (isSimulationMode)
            {
                _playerTransform.gameObject.SetActive(false);
            }
            else
            {
                _playerTransform.gameObject.SetActive(true);
                var fpc = _playerTransform.GetComponent<StarterAssets.FirstPersonController>();
                if (fpc != null) fpc.enabled = true;
                var inputs = _playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>();
                if (inputs != null)
                {
                    inputs.enabled = true;
                    inputs.cursorLocked = true;
                    inputs.cursorInputForLook = true;
                }
                if (_weapon != null) _weapon.enabled = true;
                OasisSpawnSystem.Instance.RespawnCombatant(_playerTransform, localPlayerTeam);
            }
        }

        // Initialize Match Countdown Timer (3 Minutes / 180s)
        matchDuration = 180f;
        matchTimeRemaining = matchDuration;
        isTimerRunning = true;

        UpdateTeamBattleHUD();
        string modeDesc = $"{activeNumTeams} TEAMS × {activePlayersPerTeam} PLAYERS/TEAM";
        if (isSimulationMode)
        {
            ShowBanner($"<color=#26D9FF>AI SIMULATION ACTIVE ({modeDesc})</color>\n<size=13>[V / TAB: Next Bot]  [F: Drone]  [F8: Telemetry]  [M: Map]</size>", 6.0f);
        }
        else
        {
            string tHex = GetTeamColorHex(localPlayerTeam);
            string tFull = GetTeamFullName(localPlayerTeam);
            ShowBanner($"TACTICAL ENGAGEMENT: {modeDesc}\nYOUR SQUAD: <color={tHex}>{tFull}</color>", 4.5f);
        }
    }

    private Transform _originalCamParent = null;
    private readonly List<CinemachineBrain> _disabledBrains = new List<CinemachineBrain>();
    private readonly List<CinemachineCamera> _disabledCameras = new List<CinemachineCamera>();

    public void ToggleSimulationMode()
    {
        if (isSimulationMode) StopSimulationMode();
        else StartSimulationMode();
    }

    public void StartSimulationMode()
    {
        isSimulationMode = true;
        isFreeFlySpectator = false;
        spectatedBotIndex = 0;

        var customizer = FindAnyObjectByType<OasisCharacterCustomizer>();
        if (customizer != null && customizer.isSelectionActive)
        {
            customizer.CloseCustomizerForSimulation();
        }

        // 1. Unparent Camera.main before disabling player GameObject
        Camera cam = Camera.main;
        if (cam != null && cam.transform.parent != null)
        {
            _originalCamParent = cam.transform.parent;
            cam.transform.SetParent(null);
        }

        // 2. Disable CinemachineBrain so it doesn't override Camera.main in LateUpdate
        _disabledBrains.Clear();
        foreach (var brain in UnityEngine.Object.FindObjectsByType<CinemachineBrain>(FindObjectsInactive.Include))
        {
            if (brain != null && brain.enabled)
            {
                brain.enabled = false;
                _disabledBrains.Add(brain);
            }
        }

        // Also disable any CinemachineCamera components in the scene
        _disabledCameras.Clear();
        foreach (var cmCam in UnityEngine.Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include))
        {
            if (cmCam != null && cmCam.enabled)
            {
                cmCam.enabled = false;
                _disabledCameras.Add(cmCam);
            }
        }

        // 3. Disable weapon on camera
        if (_weapon != null)
        {
            _weapon.enabled = false;
            _weapon.SetWeaponVisualActive(false);
        }

        // 4. Fully deactivate local player GameObject and its input components
        if (_playerTransform != null)
        {
            var move = _playerTransform.GetComponent<OasisCombatMovement>();
            if (move != null)
            {
                move.enabled = false;
                if (move.robotHeroVisual != null) move.robotHeroVisual.SetActive(false);
            }

            var inputs = _playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorLocked = false;
                inputs.cursorInputForLook = false;
                inputs.enabled = false;
            }

            var fpc = _playerTransform.GetComponent<StarterAssets.FirstPersonController>();
            if (fpc != null) fpc.enabled = false;

            var cc = _playerTransform.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            _playerTransform.gameObject.SetActive(false);
        }

        // 5. Force cursor to be completely free and visible for clicking buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        StartTeamBattle();

        // 6. Open Bunker Doors immediately so AI bots can exit without delay
        var doorCtrl = FindAnyObjectByType<BunkerDoorController>();
        if (doorCtrl != null)
        {
            doorCtrl.ForceOpen();
        }

        // 7. Instantly position camera behind bot 0
        SnapCameraToSpectatedBot();
    }

    public void StopSimulationMode()
    {
        isSimulationMode = false;

        // 1. Reactivate player GameObject
        if (_playerTransform != null)
        {
            _playerTransform.gameObject.SetActive(true);

            var fpc = _playerTransform.GetComponent<StarterAssets.FirstPersonController>();
            if (fpc != null) fpc.enabled = true;

            var cc = _playerTransform.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;

            var move = _playerTransform.GetComponent<OasisCombatMovement>();
            if (move != null)
            {
                move.enabled = true;
                if (move.robotHeroVisual != null) move.robotHeroVisual.SetActive(true);
            }

            var inputs = _playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (_weapon != null)
            {
                _weapon.enabled = true;
                _weapon.SetWeaponVisualActive(true);
            }
        }

        // 2. Re-parent Camera.main to player camera target
        Camera cam = Camera.main;
        if (cam != null && _originalCamParent != null)
        {
            cam.transform.SetParent(_originalCamParent);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
        }

        // 3. Re-enable CinemachineBrain and CinemachineCameras
        foreach (var brain in _disabledBrains)
        {
            if (brain != null) brain.enabled = true;
        }
        _disabledBrains.Clear();

        foreach (var cmCam in _disabledCameras)
        {
            if (cmCam != null) cmCam.enabled = true;
        }
        _disabledCameras.Clear();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        StartTeamBattle();
    }

    public void SnapCameraToSpectatedBot()
    {
        Camera cam = Camera.main;
        if (cam == null || _spawnedAIBots == null || _spawnedAIBots.Count == 0) return;
        if (spectatedBotIndex < 0 || spectatedBotIndex >= _spawnedAIBots.Count) spectatedBotIndex = 0;

        var bot = _spawnedAIBots[spectatedBotIndex];
        if (bot != null)
        {
            Vector3 idealPos = bot.transform.position - bot.transform.forward * 3.8f + Vector3.up * 2.2f;
            Vector3 lookTarget = bot.transform.position + Vector3.up * 1.3f;
            cam.transform.position = idealPos;
            cam.transform.LookAt(lookTarget);
        }
    }

    private void HandleSpectatorControls()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Ensure cursor is free for clicking buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        bool nextPressed = false;
        bool freeFlyPressed = false;
        bool escPressed = false;
        int directSelect = -1;

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            nextPressed = kb.vKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            freeFlyPressed = kb.fKey.wasPressedThisFrame;
            escPressed = kb.escapeKey.wasPressedThisFrame;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) directSelect = 0;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) directSelect = 1;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) directSelect = 2;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) directSelect = 3;
        }
#else
        if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Space)) nextPressed = true;
        if (Input.GetKeyDown(KeyCode.F)) freeFlyPressed = true;
        if (Input.GetKeyDown(KeyCode.Escape)) escPressed = true;
        if (Input.GetKeyDown(KeyCode.Alpha1)) directSelect = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) directSelect = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) directSelect = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) directSelect = 3;
#endif

        if (escPressed)
        {
            ReturnToMenu();
            return;
        }

        if (directSelect >= 0 && directSelect < _spawnedAIBots.Count)
        {
            spectatedBotIndex = directSelect;
            isFreeFlySpectator = false;
            SnapCameraToSpectatedBot();
            var b = _spawnedAIBots[spectatedBotIndex];
            if (b != null) ShowBanner($"SPECTATING: {b.botName} [1-4 to Switch]", 1.5f);
        }
        else if (nextPressed)
        {
            isFreeFlySpectator = false;
            if (_spawnedAIBots.Count > 0)
            {
                spectatedBotIndex = (spectatedBotIndex + 1) % _spawnedAIBots.Count;
                SnapCameraToSpectatedBot();
                var targetBot = _spawnedAIBots[spectatedBotIndex];
                if (targetBot != null)
                {
                    ShowBanner($"SPECTATING: {targetBot.botName} [HP: {targetBot.currentHealth:F0} | SHIELD: {targetBot.currentShield:F0}]", 1.5f);
                }
            }
        }

        if (freeFlyPressed)
        {
            isFreeFlySpectator = !isFreeFlySpectator;
            if (!isFreeFlySpectator)
            {
                SnapCameraToSpectatedBot();
            }
            ShowBanner(isFreeFlySpectator ? "FREE-FLY DRONE (WASD + RIGHT-CLICK MOUSE)" : "FOLLOWING BOT [1-4 / V]", 2.0f);
        }

        if (isFreeFlySpectator)
        {
            float speed = 14f;
            Vector3 move = Vector3.zero;
            bool w = false, s = false, d = false, a = false, up = false, dn = false;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.leftShiftKey.isPressed) speed = 28f;
                w = kb.wKey.isPressed;
                s = kb.sKey.isPressed;
                d = kb.dKey.isPressed;
                a = kb.aKey.isPressed;
                up = kb.eKey.isPressed || kb.spaceKey.isPressed;
                dn = kb.qKey.isPressed || kb.leftCtrlKey.isPressed;
            }
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m.rightButton.isPressed)
                {
                    Vector2 delta = m.delta.ReadValue() * 0.15f;
                    float rotY = cam.transform.eulerAngles.y + delta.x;
                    float rotX = cam.transform.eulerAngles.x - delta.y;
                    if (rotX > 180f) rotX -= 360f;
                    rotX = Mathf.Clamp(rotX, -85f, 85f);
                    cam.transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
                }
            }
#else
            if (Input.GetKey(KeyCode.LeftShift)) speed = 28f;
            w = Input.GetKey(KeyCode.W);
            s = Input.GetKey(KeyCode.S);
            d = Input.GetKey(KeyCode.D);
            a = Input.GetKey(KeyCode.A);
            up = Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space);
            dn = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl);

            if (Input.GetMouseButton(1))
            {
                float deltaX = Input.GetAxis("Mouse X") * 3.5f;
                float deltaY = Input.GetAxis("Mouse Y") * 3.5f;
                float rotY = cam.transform.eulerAngles.y + deltaX;
                float rotX = cam.transform.eulerAngles.x - deltaY;
                if (rotX > 180f) rotX -= 360f;
                rotX = Mathf.Clamp(rotX, -85f, 85f);
                cam.transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
            }
#endif

            if (w) move += cam.transform.forward;
            if (s) move -= cam.transform.forward;
            if (d) move += cam.transform.right;
            if (a) move -= cam.transform.right;
            if (up) move += Vector3.up;
            if (dn) move -= Vector3.up;

            cam.transform.position += move * speed * Time.deltaTime;
        }
        else
        {
            // Follow the spectated bot
            if (_spawnedAIBots.Count > 0 && spectatedBotIndex >= 0 && spectatedBotIndex < _spawnedAIBots.Count)
            {
                var bot = _spawnedAIBots[spectatedBotIndex];
                if (bot != null)
                {
                    Vector3 idealPos = bot.transform.position - bot.transform.forward * 3.8f + Vector3.up * 2.2f;
                    Vector3 lookTarget = bot.transform.position + Vector3.up * 1.3f;

                    // If distant (switch bot or bot respawned), snap immediately
                    if (Vector3.Distance(cam.transform.position, idealPos) > 14f)
                    {
                        cam.transform.position = idealPos;
                    }
                    else
                    {
                        cam.transform.position = Vector3.Lerp(cam.transform.position, idealPos, Time.deltaTime * 12f);
                    }
                    cam.transform.LookAt(lookTarget);
                }
            }
        }
    }

    public string FormatAllTeamScoresRich(bool compact = false)
    {
        if (teamKills == null || teamKills.Length < 4) teamKills = new int[] { alphaKills, bravoKills, charlieKills, deltaKills };
        int n = Mathf.Clamp(activeNumTeams, 2, 4);
        var parts = new List<string>();
        for (int t = 0; t < n; t++)
        {
            string hex = GetTeamColorHex(t);
            string name = compact ? GetTeamShortName(t).Substring(0, 1) : GetTeamShortName(t);
            parts.Add($"<color={hex}>{name}: {teamKills[t]}</color>");
        }
        return string.Join("  •  ", parts);
    }

    public string FormatAllTeamScoresPlain()
    {
        if (teamKills == null || teamKills.Length < 4) teamKills = new int[] { alphaKills, bravoKills, charlieKills, deltaKills };
        int n = Mathf.Clamp(activeNumTeams, 2, 4);
        var parts = new List<string>();
        for (int t = 0; t < n; t++)
        {
            parts.Add($"{GetTeamShortName(t)}: {teamKills[t]}");
        }
        return string.Join(" | ", parts);
    }

    private void OnGUI()
    {
        // 1. Post-Match Victory / Summary Modal Card (Renders for BOTH Character Play & Simulation Mode)
        if (isMatchFinished)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            float cardW = 720f;
            float cardH = 540f;
            float cardX = (Screen.width - cardW) * 0.5f;
            float cardY = (Screen.height - cardH) * 0.5f;

            Color accentCol = (matchWinningTeam >= 0 && matchWinningTeam < 4) 
                ? GetTeamColor(matchWinningTeam) 
                : new Color(1f, 0.85f, 0.2f);

            // Card background & outline
            GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.96f);
            GUI.DrawTexture(new Rect(cardX, cardY, cardW, cardH), Texture2D.whiteTexture);
            GUI.color = accentCol * 0.85f;
            GUI.Box(new Rect(cardX, cardY, cardW, cardH), "");
            GUI.color = Color.white;

            // Header Style
            GUIStyle hdrStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.85f, 1f) }
            };
            string modeSummary = $"{activeNumTeams} TEAMS × {activePlayersPerTeam} PLAYERS/TEAM";
            string headerText = isSimulationMode 
                ? $"— AI COMBAT SIMULATION CONCLUDED ({modeSummary}) —" 
                : $"— TACTICAL MATCH CONCLUDED ({modeSummary}) —";
            GUI.Label(new Rect(cardX + 20, cardY + 12, cardW - 40, 20), headerText, hdrStyle);

            // Big Winner Banner
            GUIStyle winStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                richText = true,
                normal = { textColor = accentCol }
            };
            GUI.Label(new Rect(cardX + 20, cardY + 34, cardW - 40, 30), matchEndTitle, winStyle);

            // Team Scores Box
            GUIStyle scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = (activeNumTeams <= 2) ? 24 : 18,
                fontStyle = FontStyle.Bold,
                richText = true
            };
            string scoreFormatted = FormatAllTeamScoresRich(false);
            GUI.Label(new Rect(cardX + 20, cardY + 66, cardW - 40, 34), scoreFormatted, scoreStyle);

            // Reason / Details
            GUIStyle descStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color(0.8f, 0.88f, 0.95f) }
            };
            GUI.Label(new Rect(cardX + 30, cardY + 102, cardW - 60, 24), matchEndDetails, descStyle);

            // ------------------ FINAL OPERATIVE SCORES TABLE ------------------
            float tableY = cardY + 130f;
            float tableW = cardW - 50f;
            float tableX = cardX + 25f;

            // Table Header Bar
            GUI.color = new Color(0.12f, 0.18f, 0.28f, 0.95f);
            GUI.DrawTexture(new Rect(tableX, tableY, tableW, 26f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle colHdrStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(tableX, tableY + 3, 60, 20), "RANK", colHdrStyle);
            colHdrStyle.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(tableX + 70, tableY + 3, 330, 20), "OPERATIVE / SQUAD", colHdrStyle);
            colHdrStyle.alignment = TextAnchor.MiddleCenter;
            colHdrStyle.normal.textColor = new Color(1f, 0.4f, 0.3f);
            GUI.Label(new Rect(tableX + 410, tableY + 3, 100, 20), "KILLS", colHdrStyle);
            colHdrStyle.normal.textColor = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(tableX + 520, tableY + 3, 140, 20), "FINAL SCORE", colHdrStyle);

            var entries = (OasisNetworkManager.Instance != null) ? OasisNetworkManager.Instance.GetScoreboardData() : null;
            int maxRows = Mathf.Min(6, entries != null ? entries.Count : 0);

            for (int r = 0; r < maxRows; r++)
            {
                var entry = entries[r];
                float rowY = tableY + 28f + (r * 28f);

                // Alternating row background
                GUI.color = entry.isLocal 
                    ? new Color(0.12f, 0.28f, 0.48f, 0.65f) 
                    : (r % 2 == 0 ? new Color(0.08f, 0.11f, 0.16f, 0.5f) : new Color(0.05f, 0.07f, 0.11f, 0.3f));
                GUI.DrawTexture(new Rect(tableX, rowY, tableW, 26f), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUIStyle cellStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = (r == 0) ? new Color(1f, 0.85f, 0.25f) : Color.white }
                };
                GUI.Label(new Rect(tableX, rowY + 3, 60, 20), $"#{r + 1}", cellStyle);

                cellStyle.alignment = TextAnchor.MiddleLeft;
                cellStyle.normal.textColor = entry.playerColor;
                string opDisplayName = entry.isLocal ? $"{entry.playerName} (YOU)" : entry.playerName;
                GUI.Label(new Rect(tableX + 70, rowY + 3, 330, 20), opDisplayName, cellStyle);

                cellStyle.alignment = TextAnchor.MiddleCenter;
                cellStyle.normal.textColor = new Color(1f, 0.5f, 0.4f);
                GUI.Label(new Rect(tableX + 410, rowY + 3, 100, 20), $"{entry.kills}", cellStyle);

                cellStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(tableX + 520, rowY + 3, 140, 20), $"{entry.score} pts", cellStyle);
            }

            // Action Buttons Row 1: Core match navigation
            float bY = cardY + 440f;
            float bW = 190f;
            float bH = 36f;
            float gap = 16f;
            float bStartX = cardX + (cardW - (3 * bW + 2 * gap)) * 0.5f;

            GUI.color = new Color(0.2f, 0.9f, 0.4f);
            if (GUI.Button(new Rect(bStartX, bY, bW, bH), "PLAY REMATCH"))
            {
                if (isSimulationMode)
                {
                    StartSimulationMode();
                }
                else
                {
                    RestartShooter();
                }
            }

            GUI.color = new Color(0.3f, 0.8f, 1f);
            if (GUI.Button(new Rect(bStartX + bW + gap, bY, bW, bH), "COPY TELEMETRY"))
            {
                if (OasisAITelemetryLogger.Instance != null)
                {
                    OasisAITelemetryLogger.Instance.CopyMarkdownToClipboard();
                }
            }

            GUI.color = new Color(1f, 0.35f, 0.3f);
            if (GUI.Button(new Rect(bStartX + 2 * (bW + gap), bY, bW, bH), "RETURN TO MENU"))
            {
                ReturnToMenu();
            }

            // Action Buttons Row 2: Secondary options
            float bY2 = cardY + 486f;
            float bW2 = 200f;
            float bH2 = 28f;
            float bStartX2 = cardX + (cardW - (2 * bW2 + gap)) * 0.5f;

            GUI.color = isSimulationMode ? new Color(0.7f, 0.85f, 1f) : new Color(0.2f, 0.75f, 1f);
            string modeToggleLabel = isSimulationMode ? "PLAY AS OPERATIVE" : "SPECTATE AI SIM";
            if (GUI.Button(new Rect(bStartX2, bY2, bW2, bH2), modeToggleLabel))
            {
                if (isSimulationMode)
                {
                    StopSimulationMode();
                }
                else
                {
                    StartSimulationMode();
                }
            }

            GUI.color = (OasisAITelemetryLogger.Instance != null && OasisAITelemetryLogger.Instance.showLiveTelemetryHUD) ? new Color(0.3f, 1f, 0.6f) : Color.white;
            if (GUI.Button(new Rect(bStartX2 + bW2 + gap, bY2, bW2, bH2), "ANALYTICS HUD [F8]"))
            {
                if (OasisAITelemetryLogger.Instance != null)
                {
                    OasisAITelemetryLogger.Instance.showLiveTelemetryHUD = !OasisAITelemetryLogger.Instance.showLiveTelemetryHUD;
                }
            }

            GUI.color = Color.white;
            return;
        }

        // 2. Spectator Controls (Only during active simulation)
        if (!isSimulationMode) return;

        // Force cursor state so buttons can be clicked reliably
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Bottom Spectator HUD Pill
        float pillW = 880f;
        float pillH = 74f;
        float pillX = (Screen.width - pillW) * 0.5f;
        float pillY = Screen.height - 90f;

        GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.94f);
        GUI.DrawTexture(new Rect(pillX, pillY, pillW, pillH), Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 0.85f, 1f, 0.7f);
        GUI.Box(new Rect(pillX, pillY, pillW, pillH), "");
        GUI.color = Color.white;

        GUIStyle specStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            richText = true,
            normal = { textColor = Color.white }
        };

        string targetInfo = isFreeFlySpectator ? "<color=#FFCC00>FREE-FLY DRONE CAMERA (WASD + RIGHT-CLICK MOUSE LOOK)</color>" : "FOLLOWING OPERATIVE";
        if (!isFreeFlySpectator && _spawnedAIBots.Count > 0 && spectatedBotIndex < _spawnedAIBots.Count)
        {
            var b = _spawnedAIBots[spectatedBotIndex];
            if (b != null)
            {
                string tCol = GetTeamColorHex(b.teamId);
                targetInfo = $"SPECTATING ({spectatedBotIndex + 1}/{_spawnedAIBots.Count}): <color={tCol}><b>{b.botName}</b></color> [HP: {b.currentHealth:F0}/100 | SHIELD: {b.currentShield:F0}/100]";
            }
        }

        GUI.Label(new Rect(pillX + 10, pillY + 4, pillW - 20, 22), targetInfo, specStyle);

        // Spectator Action Bar: [PREV BOT] [NEXT BOT (V/SPACE)] [DRONE (F)] [TELEMETRY (F8)] [MENU (ESC)]
        float btnStartX = pillX + 20f;
        float btnY = pillY + 34f;
        float btnH = 30f;

        GUI.color = new Color(0.25f, 0.80f, 1.0f);
        if (GUI.Button(new Rect(btnStartX, btnY, 150, btnH), "< PREV OPERATIVE"))
        {
            if (_spawnedAIBots.Count > 0)
            {
                spectatedBotIndex = (spectatedBotIndex - 1 + _spawnedAIBots.Count) % _spawnedAIBots.Count;
                isFreeFlySpectator = false;
                SnapCameraToSpectatedBot();
            }
        }

        if (GUI.Button(new Rect(btnStartX + 160, btnY, 175, btnH), "NEXT OPERATIVE [V] >"))
        {
            if (_spawnedAIBots.Count > 0)
            {
                spectatedBotIndex = (spectatedBotIndex + 1) % _spawnedAIBots.Count;
                isFreeFlySpectator = false;
                SnapCameraToSpectatedBot();
            }
        }

        // Free Drone button
        GUI.color = isFreeFlySpectator ? new Color(1f, 0.85f, 0.2f) : Color.white;
        if (GUI.Button(new Rect(btnStartX + 345, btnY, 150, btnH), "FREE DRONE [F]"))
        {
            isFreeFlySpectator = !isFreeFlySpectator;
            if (!isFreeFlySpectator)
            {
                SnapCameraToSpectatedBot();
            }
        }

        // Telemetry Dashboard button
        GUI.color = (OasisAITelemetryLogger.Instance != null && OasisAITelemetryLogger.Instance.showLiveTelemetryHUD) ? new Color(0.3f, 1f, 0.6f) : Color.white;
        if (GUI.Button(new Rect(btnStartX + 505, btnY, 175, btnH), "TELEMETRY HUD [F8]"))
        {
            if (OasisAITelemetryLogger.Instance != null)
            {
                OasisAITelemetryLogger.Instance.showLiveTelemetryHUD = !OasisAITelemetryLogger.Instance.showLiveTelemetryHUD;
            }
        }

        // Return to Menu button
        GUI.color = new Color(1f, 0.4f, 0.35f);
        if (GUI.Button(new Rect(btnStartX + 690, btnY, 150, btnH), "MENU [ESC]"))
        {
            ReturnToMenu();
        }

        GUI.color = Color.white;
    }

    private void SpawnCombatAIBot(int team, string botName, string classId, string texPath, int squadSlot = 0)
    {
        Vector3 spawnPos = OasisSpawnSystem.Instance.GetTeamSpawnPosition(team, squadSlot);

        string tShort = GetTeamShortName(team);
        GameObject botObj = new GameObject($"Bot_{tShort}_{botName}");
        botObj.transform.position = spawnPos;

        OasisCombatAI ai = botObj.AddComponent<OasisCombatAI>();

        Texture2D skinTex = null;
        if (!string.IsNullOrEmpty(texPath) && texPath != "RobotHero_URP")
        {
            skinTex = Resources.Load<Texture2D>(texPath);
        }

        Color accent = GetTeamColor(team);
        ai.InitializeBot(team, botName, classId, skinTex, accent, squadSlot);

        // Snap and orient bot to canyon ground in its formation slot facing center
        OasisSpawnSystem.Instance.RespawnCombatant(botObj.transform, team, squadSlot);
        OasisAITelemetryLogger.Instance?.RecordBotSpawn(ai);

        _spawnedAIBots.Add(ai);
    }

    public void OnCombatAIDowned(OasisCombatAI downedAI, bool isPlayerAttacker, int attackerTeamId = -1)
    {
        if (downedAI == null || !isTeamBattleActive || isMatchFinished) return;

        int scoringTeam = attackerTeamId;
        if (scoringTeam < 0 || scoringTeam == downedAI.teamId)
        {
            if (isPlayerAttacker && !isSimulationMode)
            {
                scoringTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            }
            else
            {
                scoringTeam = (downedAI.teamId + 1) % Mathf.Max(2, activeNumTeams);
            }
        }

        AddTeamKill(scoringTeam);

        if (isPlayerAttacker)
        {
            score += 100;
            kills++;
            OasisNetworkManager.Instance?.BroadcastScore(score, kills);
            if (_hitmarkerImg != null) StartCoroutine(FlashHitmarker(Color.red));
            if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.9f);
            ShowBanner($"<color=#FFCC00>HOSTILE KNOCKED DOWN: {downedAI.botName}</color>\n<color=#26D9FF>+100 PTS</color>", 2.2f);
        }

        UpdateTeamBattleHUD();
        CheckWinCondition();
    }

    public void OnCombatAIDefeated(OasisCombatAI defeatedAI, bool isPlayerKill)
    {
        if (defeatedAI == null || !isTeamBattleActive || isMatchFinished) return;

        if (isPlayerKill)
        {
            score += 50;
            OasisNetworkManager.Instance?.BroadcastScore(score, kills);
            if (_hitmarkerImg != null) StartCoroutine(FlashHitmarker(Color.red));
            if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.7f);
            ShowBanner($"<color=#26D9FF>ELIMINATION CONFIRMED: {defeatedAI.botName} (+50 PTS)</color>", 2.0f);
        }

        UpdateTeamBattleHUD();
        CheckWinCondition();
    }

    public void CheckWinCondition()
    {
        if (!isTeamBattleActive || isMatchFinished) return;

        // In AI simulation mode, run for the entire 3-minute timed duration without early knockout
        if (isSimulationMode) return;

        // In multiplayer client mode, Host is authoritative for win condition evaluation
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
        {
            return;
        }

        if (teamKills == null) return;
        int n = Mathf.Clamp(activeNumTeams, 2, 4);
        for (int t = 0; t < n; t++)
        {
            if (teamKills[t] >= targetKills)
            {
                ConcludeMatch(t, $"{GetTeamFullName(t)} reached {targetKills} eliminations limit!");
                return;
            }
        }
    }

    private void EndMatchByTime()
    {
        if (isMatchFinished) return;

        // In multiplayer client mode, Host is authoritative for match conclusion
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
        {
            return;
        }

        int n = Mathf.Clamp(activeNumTeams, 2, 4);
        int bestTeam = -1;
        int highestScore = -1;
        bool isTie = false;

        for (int t = 0; t < n; t++)
        {
            int k = (teamKills != null && t < teamKills.Length) ? teamKills[t] : 0;
            if (k > highestScore)
            {
                highestScore = k;
                bestTeam = t;
                isTie = false;
            }
            else if (k == highestScore)
            {
                isTie = true;
            }
        }

        int winner = isTie ? -1 : bestTeam;
        string detail = (winner >= 0) 
            ? $"{GetTeamFullName(winner)} held the score lead ({highestScore} kills) when the 3-minute timer expired!"
            : "Timer expired with tied top scores (Stalemate Draw)!";
        ConcludeMatch(winner, detail);
    }

    public void ConcludeMatch(int winningTeam, string reason)
    {
        if (isMatchFinished) return;

        isTeamBattleActive = false;
        isTimerRunning = false;
        isMatchFinished = true;
        matchWinningTeam = winningTeam;
        matchTimeRemaining = 0f;

        // If Host in multiplayer, broadcast authoritative match conclusion to all clients
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastMatchConcluded(winningTeam, reason, alphaKills, bravoKills, charlieKills, deltaKills);
        }

        int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        string teamWinnerName = (winningTeam >= 0 && winningTeam < 4) ? GetTeamFullName(winningTeam) : "STALEMATE DRAW";

        if (winningTeam == -1)
        {
            matchEndTitle = "STALEMATE DRAW";
        }
        else if (isSimulationMode)
        {
            matchEndTitle = $"{teamWinnerName} VICTORY!";
        }
        else
        {
            bool weWon = (winningTeam == myTeam);
            matchEndTitle = weWon ? "VICTORY" : "DEFEAT";
        }

        matchEndDetails = $"{reason}\nFinal Score: {FormatAllTeamScoresPlain()}";

        string banner;
        if (winningTeam >= 0 && winningTeam < 4)
        {
            string hex = GetTeamColorHex(winningTeam);
            banner = $"<color={hex}>MATCH CONCLUDED: VICTORY FOR {GetTeamFullName(winningTeam)}!</color>";
        }
        else
        {
            banner = "<color=#FFD700>MATCH CONCLUDED: STALEMATE DRAW!</color>";
        }

        ShowBanner(banner, 10.0f);
        ShowPostMatchSummary(matchEndTitle, matchEndDetails);

        // Halt all active combat AI bots
        foreach (var bot in _spawnedAIBots)
        {
            if (bot != null)
            {
                bot.HaltCombat();
            }
        }

        // Disable player weapon firing
        if (_weapon != null)
        {
            _weapon.enabled = false;
        }

        // Release cursor from look controls and freeze camera look so post-match buttons can be clicked cleanly
        var playerInputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        if (playerInputs != null)
        {
            playerInputs.SetCursorState(false);
            playerInputs.look = Vector2.zero;
            playerInputs.move = Vector2.zero;
        }
        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Notify telemetry logger and display live analytics HUD
        if (OasisAITelemetryLogger.Instance != null)
        {
            OasisAITelemetryLogger.Instance.EndSession(matchEndTitle);
            OasisAITelemetryLogger.Instance.showLiveTelemetryHUD = true;
        }

        UpdateTeamBattleHUD();
    }

    public void ShowPostMatchSummary(string title, string details)
    {
        isMatchFinished = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(false);
        }
    }

    public void UpdateTeamBattleHUD()
    {
        int nTeams = Mathf.Clamp(activeNumTeams, 2, 4);
        int pPerTeam = Mathf.Clamp(activePlayersPerTeam, 1, 4);
        int myTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayerTeam", 0), 0, nTeams - 1);
        string myTeamTag = $"<color={GetTeamColorHex(myTeam)}>[{GetTeamShortName(myTeam)}]</color>";

        int minutes = Mathf.FloorToInt(matchTimeRemaining / 60f);
        int seconds = Mathf.FloorToInt(matchTimeRemaining % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";
        if (isMatchFinished)
        {
            timeStr = "<color=#FF4444>00:00 [ENDED]</color>";
        }
        else if (matchTimeRemaining <= 30f)
        {
            timeStr = $"<color=#FF4444>{timeStr}</color>";
        }

        string scoresLine = FormatAllTeamScoresRich(nTeams > 2);

        if (_waveText != null)
        {
            if (isMatchFinished)
            {
                string winTxt = (matchWinningTeam >= 0 && matchWinningTeam < 4) 
                    ? $"<color={GetTeamColorHex(matchWinningTeam)}>{GetTeamShortName(matchWinningTeam)} WIN</color>" 
                    : "<color=#FFD700>DRAW</color>";
                _waveText.text = $"{scoresLine}  [{winTxt}]";
            }
            else
            {
                _waveText.text = $"{scoresLine}  [{timeStr}]";
            }
        }
        if (_dronesText != null)
        {
            if (isMatchFinished)
            {
                _dronesText.text = $"FINAL: {FormatAllTeamScoresPlain()}";
            }
            else if (isSimulationMode)
            {
                _dronesText.text = $"AI SIMULATION ({nTeams} TEAMS × {pPerTeam} PLAYERS) // 3-MIN EVALUATION";
            }
            else
            {
                _dronesText.text = $"SQUAD: {myTeamTag} // FIRST TO {targetKills} OR LEAD AT 00:00";
            }
        }
        if (_scoreText != null)
        {
            _scoreText.text = $"SCORE: {score}  ({nTeams} Teams × {pPerTeam}/Team)";
        }
    }

    public void UpdateAmmo(int current, int max, bool isReloading)
    {
        if (_ammoText != null)
        {
            _ammoText.text = isReloading ? "RELOADING..." : $"{current} / {max}";
            _ammoText.color = current <= 3 ? Color.red : new Color(0.2f, 0.95f, 1f);
        }
    }

    private void UpdateWaveHUD()
    {
        if (isTeamBattleActive)
        {
            UpdateTeamBattleHUD();
            return;
        }
        if (_waveText != null) _waveText.text = $"WAVE {currentWave}";
        if (_dronesText != null) _dronesText.text = $"HOSTILES: {dronesRemaining} / {totalDronesInWave}";
        if (_scoreText != null) _scoreText.text = $"SCORE: {score}";
    }

    public string ActiveNotificationBanner { get; private set; }

    private void ShowBanner(string message, float duration)
    {
        ActiveNotificationBanner = message;
        if (_bannerObj != null && _bannerText != null)
        {
            _bannerText.text = message;
            _bannerObj.SetActive(true);
        }
        StopCoroutine(nameof(HideBannerAfter));
        StartCoroutine(HideBannerAfter(duration));
    }

    private IEnumerator HideBannerAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        ActiveNotificationBanner = null;
        if (_bannerObj != null) _bannerObj.SetActive(false);
    }

    private void BuildShooterHUD()
    {
        _hudCanvas = new GameObject("OasisShooterHUD");
        _hudCanvas.transform.SetParent(transform);

        Canvas canvas = _hudCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = _hudCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _hudCanvas.AddComponent<GraphicRaycaster>();

        // 1. Damage Screen Flash
        GameObject flashObj = new GameObject("DamageFlash");
        flashObj.transform.SetParent(_hudCanvas.transform, false);
        _damageFlashImg = flashObj.AddComponent<Image>();
        _damageFlashImg.color = Color.clear;
        _damageFlashImg.raycastTarget = false;
        RectTransform flashRt = flashObj.GetComponent<RectTransform>();
        flashRt.anchorMin = Vector2.zero;
        flashRt.anchorMax = Vector2.one;
        flashRt.sizeDelta = Vector2.zero;

        // 2. Crosshair Reticle & Hitmarker
        GameObject crosshairObj = new GameObject("TacticalCrosshair");
        crosshairObj.transform.SetParent(_hudCanvas.transform, false);
        RectTransform cRt = crosshairObj.AddComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0.5f, 0.5f);
        cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(32, 32);

        // Center dot
        GameObject centerDot = new GameObject("CenterDot");
        centerDot.transform.SetParent(crosshairObj.transform, false);
        Image dotImg = centerDot.AddComponent<Image>();
        dotImg.color = new Color(0.2f, 0.95f, 1f, 0.9f);
        dotImg.raycastTarget = false;
        RectTransform dotRt = centerDot.GetComponent<RectTransform>();
        dotRt.sizeDelta = new Vector2(4, 4);

        // Reticle ticks (Top, Bottom, Left, Right)
        float tickOffset = 10f;
        float tickLen = 6f;
        float tickThick = 2f;
        Color reticleColor = new Color(0.2f, 0.95f, 1f, 0.75f);

        // Top tick
        GameObject tTick = new GameObject("TickTop");
        tTick.transform.SetParent(crosshairObj.transform, false);
        Image tImg = tTick.AddComponent<Image>();
        tImg.color = reticleColor;
        tImg.raycastTarget = false;
        RectTransform tRt = tTick.GetComponent<RectTransform>();
        tRt.anchoredPosition = new Vector2(0, tickOffset);
        tRt.sizeDelta = new Vector2(tickThick, tickLen);

        // Bottom tick
        GameObject bTick = new GameObject("TickBottom");
        bTick.transform.SetParent(crosshairObj.transform, false);
        Image bImg = bTick.AddComponent<Image>();
        bImg.color = reticleColor;
        bImg.raycastTarget = false;
        RectTransform bRt = bTick.GetComponent<RectTransform>();
        bRt.anchoredPosition = new Vector2(0, -tickOffset);
        bRt.sizeDelta = new Vector2(tickThick, tickLen);

        // Left tick
        GameObject lTick = new GameObject("TickLeft");
        lTick.transform.SetParent(crosshairObj.transform, false);
        Image lImg = lTick.AddComponent<Image>();
        lImg.color = reticleColor;
        lImg.raycastTarget = false;
        RectTransform lRt = lTick.GetComponent<RectTransform>();
        lRt.anchoredPosition = new Vector2(-tickOffset, 0);
        lRt.sizeDelta = new Vector2(tickLen, tickThick);

        // Right tick
        GameObject rTick = new GameObject("TickRight");
        rTick.transform.SetParent(crosshairObj.transform, false);
        Image rImg = rTick.AddComponent<Image>();
        rImg.color = reticleColor;
        rImg.raycastTarget = false;
        RectTransform rRt = rTick.GetComponent<RectTransform>();
        rRt.anchoredPosition = new Vector2(tickOffset, 0);
        rRt.sizeDelta = new Vector2(tickLen, tickThick);

        // Hitmarker overlay
        GameObject hitmarkerObj = new GameObject("Hitmarker");
        hitmarkerObj.transform.SetParent(_hudCanvas.transform, false);
        _hitmarkerImg = hitmarkerObj.AddComponent<Image>();
        _hitmarkerImg.color = Color.white;
        _hitmarkerImg.raycastTarget = false;
        _hitmarkerImg.enabled = false;
        RectTransform hmRt = hitmarkerObj.GetComponent<RectTransform>();
        hmRt.anchorMin = new Vector2(0.5f, 0.5f);
        hmRt.anchorMax = new Vector2(0.5f, 0.5f);
        hmRt.sizeDelta = new Vector2(28, 28);

        // 3. Ammo Display (Bottom Right)
        GameObject ammoPanel = new GameObject("AmmoDisplay");
        ammoPanel.transform.SetParent(_hudCanvas.transform, false);
        RectTransform ammoRt = ammoPanel.AddComponent<RectTransform>();
        ammoRt.anchorMin = new Vector2(1, 0);
        ammoRt.anchorMax = new Vector2(1, 0);
        ammoRt.pivot = new Vector2(1, 0);
        ammoRt.anchoredPosition = new Vector2(-50, 45);
        ammoRt.sizeDelta = new Vector2(240, 80);

        Image ammoBg = ammoPanel.AddComponent<Image>();
        ammoBg.color = new Color(0.08f, 0.1f, 0.15f, 0.85f);
        ammoBg.raycastTarget = false;

        _ammoText = CreateLabel(ammoPanel.transform, "20 / 20", 32, new Vector2(0, 10), new Vector2(220, 45), FontStyle.Bold, new Color(0.2f, 0.95f, 1f));
        CreateLabel(ammoPanel.transform, "[C: CROUCH | SPACE: JUMP | TAB: SCORES]", 10, new Vector2(0, -22), new Vector2(236, 25), FontStyle.Normal, new Color(0.65f, 0.85f, 1f));

        // 4. Wave & Target Info (Top Left)
        GameObject infoPanel = new GameObject("WaveInfoDisplay");
        infoPanel.transform.SetParent(_hudCanvas.transform, false);
        RectTransform infoRt = infoPanel.AddComponent<RectTransform>();
        infoRt.anchorMin = new Vector2(0, 1);
        infoRt.anchorMax = new Vector2(0, 1);
        infoRt.pivot = new Vector2(0, 1);
        infoRt.anchoredPosition = new Vector2(50, -45);
        infoRt.sizeDelta = new Vector2(380, 120);

        Image infoBg = infoPanel.AddComponent<Image>();
        infoBg.color = new Color(0.08f, 0.1f, 0.15f, 0.85f);
        infoBg.raycastTarget = false;

        _waveText = CreateLabel(infoPanel.transform, "WAVE 1", 20, new Vector2(0, 32), new Vector2(360, 35), FontStyle.Bold, new Color(0.95f, 0.65f, 0.2f));
        _dronesText = CreateLabel(infoPanel.transform, "HOSTILES: 5 / 5", 15, new Vector2(0, 0), new Vector2(360, 30), FontStyle.Normal, Color.white);
        _scoreText = CreateLabel(infoPanel.transform, "SCORE: 0", 16, new Vector2(0, -32), new Vector2(360, 25), FontStyle.Bold, new Color(0.2f, 0.95f, 1f));

        // 5. Health Bar (Bottom Left)
        GameObject healthPanel = new GameObject("HealthDisplay");
        healthPanel.transform.SetParent(_hudCanvas.transform, false);
        RectTransform healthRt = healthPanel.AddComponent<RectTransform>();
        healthRt.anchorMin = new Vector2(0, 0);
        healthRt.anchorMax = new Vector2(0, 0);
        healthRt.pivot = new Vector2(0, 0);
        healthRt.anchoredPosition = new Vector2(50, 45);
        healthRt.sizeDelta = new Vector2(260, 40);

        GameObject sliderObj = new GameObject("HealthSlider");
        sliderObj.transform.SetParent(healthPanel.transform, false);
        _healthBar = sliderObj.AddComponent<Slider>();
        _healthBar.minValue = 0f;
        _healthBar.maxValue = maxHealth;
        _healthBar.value = currentHealth;
        RectTransform slRt = sliderObj.GetComponent<RectTransform>();
        slRt.sizeDelta = new Vector2(260, 28);

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillRt = fillArea.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.sizeDelta = Vector2.zero;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        _healthFillImage = fill.AddComponent<Image>();
        _healthFillImage.color = new Color(0.2f, 0.85f, 0.45f);
        _healthFillImage.raycastTarget = false;
        _healthBar.fillRect = fill.GetComponent<RectTransform>();

        _shieldPercentText = CreateLabel(healthPanel.transform, "SHIELD: 100%", 12, new Vector2(0, 24), new Vector2(260, 20), FontStyle.Bold, new Color(0.7f, 0.85f, 1f));

        // 6. Center Wave Cleared Banner
        _bannerObj = new GameObject("WaveBanner");
        _bannerObj.transform.SetParent(_hudCanvas.transform, false);
        RectTransform bannerRt = _bannerObj.AddComponent<RectTransform>();
        bannerRt.anchorMin = new Vector2(0.5f, 0.7f);
        bannerRt.anchorMax = new Vector2(0.5f, 0.7f);
        bannerRt.sizeDelta = new Vector2(600, 100);

        Image bannerBg = _bannerObj.AddComponent<Image>();
        bannerBg.color = new Color(0.06f, 0.08f, 0.12f, 0.9f);
        bannerBg.raycastTarget = false;

        _bannerText = CreateLabel(_bannerObj.transform, "WAVE 1", 42, Vector2.zero, new Vector2(580, 80), FontStyle.Bold, new Color(1f, 0.8f, 0.2f));
        _bannerObj.SetActive(false);

        // 7. Game Over Panel
        BuildGameOverPanel();
    }

    private void BuildGameOverPanel()
    {
        _gameOverPanel = new GameObject("GameOverPanel");
        _gameOverPanel.transform.SetParent(_hudCanvas.transform, false);
        RectTransform goRt = _gameOverPanel.AddComponent<RectTransform>();
        goRt.sizeDelta = new Vector2(520, 360);

        Image goBg = _gameOverPanel.AddComponent<Image>();
        goBg.color = new Color(0.08f, 0.10f, 0.16f, 0.96f);

        _gameOverTitleText = CreateLabel(_gameOverPanel.transform, "MATCH COMPLETED", 36, new Vector2(0, 110), new Vector2(480, 50), FontStyle.Bold, new Color(0.25f, 0.9f, 1f));
        _gameOverDescText = CreateLabel(_gameOverPanel.transform, "Tactical Engagement Concluded", 16, new Vector2(0, 55), new Vector2(480, 45), FontStyle.Normal, new Color(0.85f, 0.88f, 0.94f));

        // Rematch Button
        CreateButton(_gameOverPanel.transform, "PLAY REMATCH", new Vector2(0, -20), new Vector2(300, 50), new Color(0.18f, 0.65f, 0.40f), RestartShooter);

        // Menu Button
        CreateButton(_gameOverPanel.transform, "RETURN TO HOME BASE", new Vector2(0, -90), new Vector2(300, 50), new Color(0.55f, 0.2f, 0.2f), ReturnToMenu);

        _gameOverPanel.SetActive(false);
    }

    private Text CreateLabel(Transform parent, string text, int fontSize, Vector2 position, Vector2 size, FontStyle style, Color color)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Text txt = obj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        return txt;
    }

    private void CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Color bgColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject(label + "_Btn", typeof(RectTransform));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        CreateLabel(btnObj.transform, label, 18, Vector2.zero, size, FontStyle.Bold, Color.white);
    }
}
