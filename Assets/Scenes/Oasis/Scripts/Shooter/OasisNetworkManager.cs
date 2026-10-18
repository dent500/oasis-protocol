using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

/// <summary>
/// High-Performance Lightweight 4-Player Online & LAN Co-Op Network Manager.
/// Uses zero external dependencies (pure C# UDP/TCP sockets), working natively in Unity 6.
/// Features:
/// - Host a 4-player co-op session (Server + Client).
/// - Join any Host via IP (LAN or Online over Port-Forwarded IP / Hamachi / Radmin VPN / Tailscale / Ngrok).
/// - Synchronizes remote player positions, rotations, animations, crouch, dash, blaster shots, and hits.
/// - In-game Lobby overlay: Host, Join, IP input, and Connected Players list.
/// </summary>
public class OasisNetworkManager : MonoBehaviour
{
    public static OasisNetworkManager Instance { get; private set; }

    public const int DefaultPort = 7777;

    public enum NetworkRole
    {
        Offline,
        Host,
        Client
    }

    [Header("Network State")]
    public static string PendingLaunchMode = "Offline"; // "Offline", "RelayHost", "RelayClient", "LanHost", "LanClient"
    public static string PendingTargetCodeOrIP = "";
    public static bool AutoDeployOnSceneLoad = false;
    public static bool AutoDeployIsSim = false;

    public NetworkRole currentRole = NetworkRole.Offline;
    public string hostAddress = "127.0.0.1";
    public int myPlayerId = 0;
    public string myPlayerName = "Operative";
    public bool isMatchLaunchedByHost = false;
    private bool _clientReceivedWelcome = false;
    private float _joinRetryTimer = 0f;

    // Remote Players
    private readonly Dictionary<int, RemotePlayerInstance> _remotePlayers = new Dictionary<int, RemotePlayerInstance>();

    // Synchronized Target Drones (Network Drone ID -> TargetDrone)
    private readonly Dictionary<int, TargetDrone> _syncedDrones = new Dictionary<int, TargetDrone>();
    private float _droneTickTimer = 0f;
    private const float DroneTickInterval = 0.05f; // 20 updates per second for silky smooth AI flight
    private float _waveSyncTimer = 0f;

    // Sockets & Networking (UDP Direct + Unity Relay)
    public bool isUsingRelay = false;
    public string activeRelayJoinCode = "";
    private NetworkDriver _relayDriver;
    private NetworkConnection _relayServerConn;
    private NativeList<NetworkConnection> _relayIncomingConnections;
    private readonly Dictionary<int, NetworkConnection> _relayClientConnections = new Dictionary<int, NetworkConnection>();
    private readonly Dictionary<NetworkConnection, int> _relayConnectionToPlayerId = new Dictionary<NetworkConnection, int>();

    private UdpClient _udpClient;
    private Thread _networkThread;
    private bool _isRunning = false;
    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
    private readonly object _queueLock = new object();

    // Endpoints for Host (Direct IP mode)
    private readonly Dictionary<int, IPEndPoint> _clientEndpoints = new Dictionary<int, IPEndPoint>();
    private int _nextClientId = 1;

    // Send rate timer (20 ticks/sec for smooth network interpolation)
    private float _networkTickTimer = 0f;
    private const float NetworkTickInterval = 0.05f;

    // UI
    private GameObject _lobbyCanvas;
    private InputField _ipInputField;
    private Text _statusText;
    private Text _playerListText;
    private GameObject _lobbyPanel;

    // Scoreboard UI
    private GameObject _scoreboardPanel;
    private Transform _scoreboardRowsContainer;
    private readonly List<GameObject> _scoreboardRowObjects = new List<GameObject>();
    private float _scoreboardRefreshTimer = 0f;
    private bool _isScoreboardToggled = false;

    public void ToggleScoreboard()
    {
        if (_scoreboardPanel == null) BuildLobbyUI();
        if (_scoreboardPanel != null)
        {
            _isScoreboardToggled = !_isScoreboardToggled;
            _scoreboardPanel.SetActive(_isScoreboardToggled);
            if (_isScoreboardToggled) RefreshScoreboardUI();
        }
    }

    private Transform _localPlayerTransform;
    private OasisCombatMovement _localCombat;

    public class RemotePlayerInstance
    {
        public int id;
        public string name;
        public GameObject gameObject;
        public Transform transform;
        public Vector3 targetPosition;
        public Quaternion targetRotation;
        public float targetPitch;
        public float currentPitch;
        public bool isCrouching;
        public bool isDowned;
        public float downedTimer;
        public float reviveProgress;
        public CharacterController characterController;
        public Text nameTagText;
        public Animator animator;
        public Transform spineBone;
        public Transform gunArmBone;

        // Visual Rig for Procedural Animation
        public Transform visualRoot;
        public Transform leftLeg;
        public Transform rightLeg;
        public Transform leftArm;
        public Transform rightArm;
        public Transform jetpackFlame;
        public float animTime;
        public Vector3 prevPosition;

        // Scoreboard & Character Selection stats
        public int score;
        public int kills;
        public int teamId;
        public int operativeIndex;
        public bool isLocalPlaceholder;
    }

    public struct ConnectedOperativeInfo
    {
        public int playerId;
        public string playerName;
        public int teamId;
        public int operativeIndex;
        public bool isHost;
        public bool isLocalPlaceholder;
    }

    public List<ConnectedOperativeInfo> GetRemoteOperatives()
    {
        List<ConnectedOperativeInfo> list = new List<ConnectedOperativeInfo>();
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value == null) continue;
            list.Add(new ConnectedOperativeInfo
            {
                playerId = kvp.Value.id,
                playerName = kvp.Value.name,
                teamId = kvp.Value.teamId,
                operativeIndex = kvp.Value.operativeIndex,
                isHost = (kvp.Value.id == 0),
                isLocalPlaceholder = kvp.Value.isLocalPlaceholder
            });
        }
        list.Sort((a, b) => a.playerId.CompareTo(b.playerId));
        return list;
    }

    public List<RemotePlayerInstance> GetRemotePlayerInstances()
    {
        List<RemotePlayerInstance> list = new List<RemotePlayerInstance>();
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null && kvp.Value.gameObject != null && !kvp.Value.isLocalPlaceholder)
            {
                list.Add(kvp.Value);
            }
        }
        return list;
    }

    public void ApplyOperativeSkinToRemotePlayer(RemotePlayerInstance inst)
    {
        if (inst == null || inst.gameObject == null) return;

        string[] texPaths = new string[] {
            "RobotHero_URP", "Characters/Textures/skin_soldier", "Characters/Textures/cyborgFemaleA",
            "Characters/Textures/skin_robot", "Characters/Textures/skin_adventurer",
            "Characters/Textures/survivorFemaleA", "Characters/Textures/criminalMaleA"
        };

        int opIdx = Mathf.Clamp(inst.operativeIndex, 0, texPaths.Length - 1);
        string texPath = texPaths[opIdx];
        Texture2D skinTex = null;
        if (!string.IsNullOrEmpty(texPath) && texPath != "RobotHero_URP")
        {
            skinTex = Resources.Load<Texture2D>(texPath);
        }

        Color teamColor = OasisShooterManager.GetTeamColor(inst.teamId);

        Material baseMat = Resources.Load<Material>("RobotHero_URP");
#if UNITY_EDITOR
        if (baseMat == null) baseMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/SciFiWarriorPBRHPPolyart/Materials/RobotHero_URP.mat");
#endif
        if (baseMat == null) baseMat = new Material(OasisShaderHelper.GetLitShader());

        Material customMat = new Material(baseMat);
        customMat.name = $"Remote_{inst.name}_{opIdx}_Mat";

        if (skinTex != null)
        {
            if (customMat.HasProperty("_BaseMap")) customMat.SetTexture("_BaseMap", skinTex);
            if (customMat.HasProperty("_MainTex")) customMat.SetTexture("_MainTex", skinTex);
        }
        customMat.color = Color.white;

        if (customMat.HasProperty("_EmissionColor"))
        {
            customMat.EnableKeyword("_EMISSION");
            customMat.SetColor("_EmissionColor", teamColor * 1.5f);
        }

        Renderer[] rends = inst.gameObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i].GetComponent<Text>() != null) continue;
            rends[i].sharedMaterial = customMat;
        }

        if (inst.nameTagText != null)
        {
            string status = inst.isDowned ? "[DOWNED] " : "";
            inst.nameTagText.text = $"{status}{inst.name} [OP-{inst.id}]";
            inst.nameTagText.color = inst.isDowned ? new Color(1f, 0.35f, 0.35f) : teamColor;
        }
    }

    public int localScore = 0;
    public int localKills = 0;

    /// <summary>
    /// Returns transforms of all active operatives (local player + all connected remote operatives)
    /// for intelligent drone AI target acquisition.
    /// </summary>
    public List<Transform> GetAllPlayerTransforms()
    {
        List<Transform> list = new List<Transform>();
        if (_localPlayerTransform != null) list.Add(_localPlayerTransform);
        else
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) list.Add(p.transform);
        }

        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null && kvp.Value.transform != null)
            {
                list.Add(kvp.Value.transform);
            }
        }
        return list;
    }

    public struct ScoreboardEntry
    {
        public int playerId;
        public string playerName;
        public int score;
        public int kills;
        public Color playerColor;
        public bool isLocal;
    }

    public List<ScoreboardEntry> GetScoreboardData()
    {
        List<ScoreboardEntry> list = new List<ScoreboardEntry>();

        bool isSpectating = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) ||
                            (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSpectatorMode);

        if (!isSpectating)
        {
            int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            string teamPrefix = $"[{OasisShooterManager.GetTeamShortName(myTeam)}] ";
            string localName = teamPrefix + "YOU (OPERATIVE)";
            Color teamCol = OasisShooterManager.GetTeamColor(myTeam);

            int pScore = localScore;
            int pKills = localKills;
            if (OasisShooterManager.Instance != null)
            {
                pScore = Mathf.Max(pScore, OasisShooterManager.Instance.score);
                pKills = Mathf.Max(pKills, OasisShooterManager.Instance.kills);
            }

            list.Add(new ScoreboardEntry
            {
                playerId = myPlayerId,
                playerName = localName,
                score = pScore,
                kills = pKills,
                playerColor = teamCol,
                isLocal = true
            });
        }

        foreach (var kvp in _remotePlayers)
        {
            var rp = kvp.Value;
            if (rp == null || rp.isLocalPlaceholder) continue; // Local placeholders are spawned as active OasisCombatAI bots in-game
            string rPrefix = $"[{OasisShooterManager.GetTeamShortName(rp.teamId)}] ";
            list.Add(new ScoreboardEntry
            {
                playerId = rp.id,
                playerName = rPrefix + rp.name,
                score = rp.score,
                kills = rp.kills,
                playerColor = OasisShooterManager.GetTeamColor(rp.teamId),
                isLocal = false
            });
        }

        // Add AI Combat Operatives
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            list.Add(new ScoreboardEntry
            {
                playerId = bot.GetHashCode(),
                playerName = bot.botName,
                score = bot.score,
                kills = bot.kills,
                playerColor = bot.teamColor,
                isLocal = false
            });
        }

        // Sort descending by score
        list.Sort((a, b) => b.score.CompareTo(a.score));
        return list;
    }

    public int ConnectedPlayersCount => 1 + _remotePlayers.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        if (_lobbyCanvas == null)
        {
            BuildLobbyUI();
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        _localPlayerTransform = null;
        _localCombat = null;

        if (scene.name.IndexOf("Oasis", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (_lobbyCanvas == null)
            {
                BuildLobbyUI();
            }

            // Clean any stale drone references from the previous scene instance
            _syncedDrones.Clear();

            // Destroy any leftover operative objects from previous scenes and re-spawn in OasisScene
            List<int> playerIds = new List<int>(_remotePlayers.Keys);
            foreach (int id in playerIds)
            {
                if (_remotePlayers.TryGetValue(id, out var rp) && rp != null)
                {
                    string pName = rp.name;
                    int pScore = rp.score;
                    int pKills = rp.kills;
                    int pTeam = rp.teamId;
                    int pOpIdx = rp.operativeIndex;
                    bool pPlaceholder = rp.isLocalPlaceholder;
                    if (rp.gameObject != null)
                    {
                        Destroy(rp.gameObject);
                    }
                    _remotePlayers.Remove(id);
                    SpawnRemotePlayer(id, pName, pTeam, pOpIdx, pPlaceholder);
                    if (_remotePlayers.TryGetValue(id, out var newRp))
                    {
                        newRp.score = pScore;
                        newRp.kills = pKills;
                        newRp.teamId = pTeam;
                        newRp.operativeIndex = pOpIdx;
                        newRp.isLocalPlaceholder = pPlaceholder;
                    }
                }
            }

            // Refresh Character Selection Roster if active
            if (OasisCharacterCustomizer.Instance != null)
            {
                OasisCharacterCustomizer.Instance.UpdateRosterDisplay();
            }

            // If Host, re-register newly spawned host drones
            if (currentRole == NetworkRole.Host)
            {
                RegisterExistingHostDrones();
            }
        }
    }

    private void Start()
    {
        BuildLobbyUI();

        // Check if session launch mode was configured in Main Menu
        if (!string.IsNullOrEmpty(PendingLaunchMode) && PendingLaunchMode != "Offline")
        {
            string m = PendingLaunchMode;
            string target = PendingTargetCodeOrIP;
            PendingLaunchMode = "Offline";
            PendingTargetCodeOrIP = "";

            StartCoroutine(ExecutePendingLaunch(m, target));
        }
    }

    private IEnumerator ExecutePendingLaunch(string mode, string target)
    {
        // Wait one frame so OasisShooterManager initializes first
        yield return null;

        if (mode == "RelayHost")
        {
            StartRelayHost();
        }
        else if (mode == "RelayClient" || mode == "LanClient")
        {
            StartClient(target);
        }
        else if (mode == "LanHost")
        {
            StartHost();
        }
    }

    private void Update()
    {
        // 1. Process Relay transport events if active
        if (isUsingRelay)
        {
            UpdateRelayDriver();
        }

        // 2. Process received network events queued from background thread
        lock (_queueLock)
        {
            while (_mainThreadQueue.Count > 0)
            {
                _mainThreadQueue.Dequeue()?.Invoke();
            }
        }

        // Toggle / Hold TAB for Live Match Scoreboard
        if (_scoreboardPanel == null)
        {
            BuildLobbyUI();
        }

        bool tabPressed = false;
        bool tabHeld = false;
        bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            tabPressed = UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame;
            tabHeld = UnityEngine.InputSystem.Keyboard.current.tabKey.isPressed;
            escPressed = UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
        }
#else
        tabPressed = Input.GetKeyDown(KeyCode.Tab);
        tabHeld = Input.GetKey(KeyCode.Tab);
        escPressed = Input.GetKeyDown(KeyCode.Escape);
#endif

        if (tabPressed)
        {
            _isScoreboardToggled = !_isScoreboardToggled;
        }
        else if (escPressed && _isScoreboardToggled)
        {
            _isScoreboardToggled = false;
        }

        bool isMatchOver = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isMatchFinished);
        bool showScoreboard = tabHeld || _isScoreboardToggled || isMatchOver;

        if (_scoreboardPanel != null)
        {
            if (showScoreboard != _scoreboardPanel.activeSelf)
            {
                _scoreboardPanel.SetActive(showScoreboard);
                if (showScoreboard) RefreshScoreboardUI();
            }

            if (showScoreboard)
            {
                _scoreboardRefreshTimer += Time.deltaTime;
                if (_scoreboardRefreshTimer > 0.25f)
                {
                    _scoreboardRefreshTimer = 0f;
                    RefreshScoreboardUI();
                }
            }
        }

        // 2. Transmit local player updates if online
        if (currentRole != NetworkRole.Offline)
        {
            if (currentRole == NetworkRole.Client && !isUsingRelay && !_clientReceivedWelcome && _udpClient != null)
            {
                _joinRetryTimer += Time.unscaledDeltaTime;
                if (_joinRetryTimer >= 0.8f)
                {
                    _joinRetryTimer = 0f;
                    int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                    int myOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
                    SendPacketToServer($"JOIN|{myPlayerName}|{myTeam}|{myOp}");
                }
            }

            _networkTickTimer += Time.deltaTime;
            if (_networkTickTimer >= NetworkTickInterval)
            {
                _networkTickTimer = 0f;
                SendLocalState();
            }

            // Host broadcasts all active drone positions and rotations periodically
            if (currentRole == NetworkRole.Host)
            {
                _droneTickTimer += Time.deltaTime;
                if (_droneTickTimer >= DroneTickInterval)
                {
                    _droneTickTimer = 0f;
                    BroadcastDronesState();
                }

                _waveSyncTimer += Time.deltaTime;
                if (_waveSyncTimer >= 1.0f)
                {
                    _waveSyncTimer = 0f;
                    if (OasisShooterManager.Instance != null)
                    {
                        BroadcastWaveState(
                            OasisShooterManager.Instance.currentWave,
                            OasisShooterManager.Instance.totalDronesInWave,
                            OasisShooterManager.Instance.dronesRemaining);
                    }
                }
            }

            // Interpolate remote players smoothly & animate limbs
            foreach (var kvp in _remotePlayers)
            {
                var rp = kvp.Value;
                if (rp == null) continue;
                if (rp.transform != null)
                {
                    Vector3 prevPos = rp.transform.position;
                    rp.transform.position = Vector3.Lerp(rp.transform.position, rp.targetPosition, Time.deltaTime * 14f);
                    rp.transform.rotation = Quaternion.Slerp(rp.transform.rotation, rp.targetRotation, Time.deltaTime * 14f);

                    // Character Animation handling (Robot Hero 3D humanoid or procedural fallback)
                    Vector3 horizDelta = rp.transform.position - prevPos;
                    horizDelta.y = 0;
                    float speed = horizDelta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);

                    if (rp.isDowned)
                    {
                        rp.downedTimer -= Time.deltaTime;
                        if (rp.downedTimer < 0f) rp.downedTimer = 0f;

                        // Prone ground crawl posture and animation for downed player
                        if (rp.visualRoot != null)
                        {
                            float crawlSway = (speed > 0.1f) ? Mathf.Sin(Time.time * 5.2f) * 6.0f : 0f;
                            rp.visualRoot.localPosition = Vector3.Lerp(rp.visualRoot.localPosition, new Vector3(0f, 0.22f, -0.40f), Time.deltaTime * 10f);
                            rp.visualRoot.localRotation = Quaternion.Slerp(rp.visualRoot.localRotation, Quaternion.Euler(82f, 0f, crawlSway), Time.deltaTime * 10f);
                        }
                        if (rp.animator != null)
                        {
                            rp.animator.speed = (speed > 0.1f) ? 0.55f : 0.12f;
                            rp.animator.Play("WalkFront_Shoot_AR");
                        }
                        if (rp.nameTagText != null)
                        {
                            rp.nameTagText.text = $"[DOWNED {rp.downedTimer:F1}s] {rp.name}";
                            rp.nameTagText.color = new Color(1f, 0.35f, 0.35f);
                        }
                    }
                    else if (rp.animator != null)
                    {
                        if (rp.nameTagText != null && rp.nameTagText.text.StartsWith("[DOWNED]"))
                        {
                            rp.nameTagText.text = $"{rp.name} [OP-{rp.id}]";
                            rp.nameTagText.color = OasisShooterManager.GetTeamColor(rp.teamId);
                        }
                        if (rp.isCrouching)
                        {
                            if (rp.visualRoot != null)
                            {
                                rp.visualRoot.localPosition = Vector3.zero;
                                rp.visualRoot.localRotation = Quaternion.Euler(0, 180f, 0);
                            }
                            rp.animator.speed = 1.0f;
                            rp.animator.Play("Idle_Ducking_AR");
                        }
                        else
                        {
                            if (rp.visualRoot != null)
                            {
                                rp.visualRoot.localPosition = Vector3.zero;
                                rp.visualRoot.localRotation = Quaternion.identity;
                            }
                            rp.animator.speed = 1.0f;
                            if (speed > 4.2f)
                            {
                                rp.animator.Play("Run_guard_AR");
                            }
                            else if (speed > 0.35f)
                            {
                                rp.animator.Play("WalkFront_Shoot_AR");
                            }
                            else
                            {
                                rp.animator.Play("Idle_Shoot_Ar");
                            }
                        }
                    }
                    else
                    {
                        // Procedural walk animation based on horizontal velocity
                        if (speed > 0.4f)
                        {
                            rp.animTime += Time.deltaTime * Mathf.Clamp(speed * 3.5f, 4f, 16f);
                            float legAngle = Mathf.Sin(rp.animTime) * 28f;
                            float armAngle = -Mathf.Sin(rp.animTime) * 18f;

                            if (rp.leftLeg != null) rp.leftLeg.localRotation = Quaternion.Euler(legAngle, 0, 0);
                            if (rp.rightLeg != null) rp.rightLeg.localRotation = Quaternion.Euler(-legAngle, 0, 0);
                            if (rp.leftArm != null) rp.leftArm.localRotation = Quaternion.Euler(armAngle, 0, 0);
                        }
                        else
                        {
                            // Return to idle stance
                            if (rp.leftLeg != null) rp.leftLeg.localRotation = Quaternion.Slerp(rp.leftLeg.localRotation, Quaternion.identity, Time.deltaTime * 10f);
                            if (rp.rightLeg != null) rp.rightLeg.localRotation = Quaternion.Slerp(rp.rightLeg.localRotation, Quaternion.identity, Time.deltaTime * 10f);
                            if (rp.leftArm != null) rp.leftArm.localRotation = Quaternion.Slerp(rp.leftArm.localRotation, Quaternion.identity, Time.deltaTime * 10f);
                        }

                        // Crouch posture handling
                        if (rp.visualRoot != null)
                        {
                            Vector3 targetScale = rp.isCrouching ? new Vector3(1f, 0.65f, 1f) : Vector3.one;
                            rp.visualRoot.localScale = Vector3.Lerp(rp.visualRoot.localScale, targetScale, Time.deltaTime * 12f);
                        }
                    }
                }
            }
        }
    }

    private void LateUpdate()
    {
        // Smoothly interpolate and apply upper-body aiming pitch to all remote operative models
        foreach (var kvp in _remotePlayers)
        {
            RemotePlayerInstance rp = kvp.Value;
            if (rp == null) continue;

            rp.currentPitch = Mathf.Lerp(rp.currentPitch, rp.targetPitch, Time.deltaTime * 14f);
            if (rp.spineBone != null)
            {
                rp.spineBone.Rotate(Vector3.forward, -rp.currentPitch * 0.7f, Space.Self);
            }
            if (rp.gunArmBone != null)
            {
                rp.gunArmBone.Rotate(Vector3.forward, rp.currentPitch * 0.7f, Space.Self);
            }
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        StopSession();
    }

    private void OnApplicationQuit()
    {
        StopSession();
    }

    // ---------------------------------------------------------------------------------------------------------
    // HOST & JOIN LOGIC (UNITY RELAY + DIRECT UDP FALLBACK)
    // ---------------------------------------------------------------------------------------------------------

    private static readonly System.Threading.SemaphoreSlim _servicesInitLock = new System.Threading.SemaphoreSlim(1, 1);

    private static async Task EnsureServicesInitializedAsync()
    {
        await _servicesInitLock.WaitAsync();
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                try
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                catch (AuthenticationException ex)
                {
                    // If the exception is "The player is already signing in" or already signed in, wait for completion
                    int waitTimeoutMs = 10000;
                    int elapsed = 0;
                    while (!AuthenticationService.Instance.IsSignedIn && elapsed < waitTimeoutMs)
                    {
                        await Task.Delay(100);
                        elapsed += 100;
                    }

                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        Debug.LogWarning($"[Relay Auth] Authentication notice: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.LogWarning($"[Relay Auth] Init exception: {ex.Message}");
            }
        }
        finally
        {
            _servicesInitLock.Release();
        }
    }

    private bool _isConnectingOrHostingInProgress = false;
    public bool IsConnectingOrHostingInProgress => _isConnectingOrHostingInProgress;

    public async void StartRelayHost()
    {
        if (_isConnectingOrHostingInProgress)
        {
            Debug.LogWarning("[Relay] Host or Join is already in progress, ignoring duplicate call.");
            return;
        }
        _isConnectingOrHostingInProgress = true;

        StopSession();

        currentRole = NetworkRole.Host;
        myPlayerId = 0;
        isMatchLaunchedByHost = false;

        UpdateStatus("ALLOCATING RELAY SERVER...");
        try
        {
            await EnsureServicesInitializedAsync();

            // Request Relay allocation for 3 joining players (4 total including host)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
            activeRelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            RelayServerEndpoint relayEndpoint = allocation.ServerEndpoints.Find(e => e.ConnectionType == "udp")
                                                ?? allocation.ServerEndpoints[0];

            RelayServerData relayServerData = new RelayServerData(
                relayEndpoint.Host,
                (ushort)relayEndpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData,
                allocation.Key,
                isSecure: false
            );

            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayServerData);

            _relayDriver = NetworkDriver.Create(settings);
            if (_relayDriver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                UpdateStatus("RELAY BIND FAILED - FALLING BACK TO DIRECT HOST");
                StopSession();
                StartHost();
                return;
            }

            if (_relayDriver.Listen() != 0)
            {
                UpdateStatus("RELAY LISTEN FAILED - FALLING BACK TO DIRECT HOST");
                StopSession();
                StartHost();
                return;
            }

            _relayIncomingConnections = new NativeList<NetworkConnection>(4, Allocator.Persistent);

            isUsingRelay = true;
            currentRole = NetworkRole.Host;
            myPlayerId = 0;
            _isRunning = true;

            // Immediately register all currently active drones on host into _syncedDrones
            RegisterExistingHostDrones();

            GUIUtility.systemCopyBuffer = activeRelayJoinCode; // Auto-copy code to clipboard
            UpdateStatus($"HOSTING VIA RELAY!\nJOIN CODE: {activeRelayJoinCode}\n(Code copied to clipboard - Share with friends!)");
            UpdatePlayerListUI();

            _lobbyPanel?.SetActive(false);
            OasisShooterManager.Instance?.ShowNotificationBanner($"RELAY HOST ACTIVE! CODE: {activeRelayJoinCode}", 5f);
            OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Relay] Relay allocation notice: {ex.Message}. Falling back to direct LAN host so match can proceed.");
            UpdateStatus($"Relay Host Notice: {ex.Message}");
            StartHost();
        }
        finally
        {
            _isConnectingOrHostingInProgress = false;
        }
    }

    public void EnsureHostSessionForStaging()
    {
        isMatchLaunchedByHost = false;

        // If already hosting (e.g., Relay Host or LAN Host), keep session and notify clients to enter staging
        if (currentRole == NetworkRole.Host)
        {
            myPlayerId = 0;
            BroadcastToClients("ENTER_STAGING");
            if (isUsingRelay && _relayDriver.IsCreated)
            {
                _relayDriver.ScheduleUpdate().Complete();
            }
            return;
        }

        // Check if we had a local placeholder from testing "CONNECT & JOIN SQUAD" on the same PC
        bool hadPlaceholder = false;
        int placeholderTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        int placeholderOp = PlayerPrefs.GetInt("OasisSelectedOperative", 1);
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null && kvp.Value.isLocalPlaceholder)
            {
                hadPlaceholder = true;
                placeholderTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                placeholderOp = PlayerPrefs.GetInt("OasisSelectedOperative", 1);
                break;
            }
        }

        StartHost();
        currentRole = NetworkRole.Host;
        myPlayerId = 0;

        if (hadPlaceholder && _remotePlayers.Count == 0)
        {
            SpawnRemotePlayer(1, "OPERATIVE #2", placeholderTeam, placeholderOp, true);
            UpdatePlayerListUI();
            OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
        }
    }

    public void ConnectOrJoinSquadIntoStaging(string codeOrIp)
    {
        codeOrIp = (codeOrIp ?? "").Trim();

        // If this instance is already hosting (e.g., user generated a Relay code on the left panel and clicked Join on the same PC to test)
        if (currentRole == NetworkRole.Host)
        {
            if (_remotePlayers.Count == 0)
            {
                int hostTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                SpawnRemotePlayer(1, "OPERATIVE #2", hostTeam, 1, true);
                UpdatePlayerListUI();
            }
            return;
        }

        // Otherwise join as Client and ensure Host is immediately present in the staging roster
        StartClient(codeOrIp);

        if (currentRole == NetworkRole.Offline)
        {
            currentRole = NetworkRole.Client;
        }
        if (myPlayerId == 0)
        {
            myPlayerId = 1;
        }
        if (!_remotePlayers.ContainsKey(0))
        {
            SpawnRemotePlayer(0, "SQUAD HOST", 0, 0, true);
            UpdatePlayerListUI();
        }
    }

    public async void StartRelayClient(string joinCode)
    {
        if (_isConnectingOrHostingInProgress)
        {
            Debug.LogWarning("[Relay] Host or Join is already in progress, ignoring duplicate call.");
            return;
        }
        _isConnectingOrHostingInProgress = true;

        StopSession();

        // Clear local offline drones immediately before connecting
        OasisShooterManager.Instance?.CleanupLocalDrones();
        _syncedDrones.Clear();

        joinCode = (joinCode ?? "").Trim().ToUpperInvariant();
        activeRelayJoinCode = joinCode;
        currentRole = NetworkRole.Client;
        myPlayerId = 1;
        _clientReceivedWelcome = false;

        // Immediately register Host in staging roster while Relay handshake is in flight
        if (!_remotePlayers.ContainsKey(0))
        {
            SpawnRemotePlayer(0, "SQUAD HOST", 0, 0, true);
            UpdatePlayerListUI();
        }

        UpdateStatus($"JOINING RELAY SESSION {joinCode}...");
        try
        {
            await EnsureServicesInitializedAsync();

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerEndpoint relayEndpoint = joinAllocation.ServerEndpoints.Find(e => e.ConnectionType == "udp")
                                                ?? joinAllocation.ServerEndpoints[0];

            RelayServerData relayServerData = new RelayServerData(
                relayEndpoint.Host,
                (ushort)relayEndpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                joinAllocation.Key,
                isSecure: false
            );

            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayServerData);

            _relayDriver = NetworkDriver.Create(settings);
            if (_relayDriver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                UpdateStatus("RELAY CLIENT BIND FAILED");
                return;
            }

            // Connect to Relay
            _relayServerConn = _relayDriver.Connect();

            isUsingRelay = true;
            currentRole = NetworkRole.Client;
            _isRunning = true;

            UpdateStatus($"CONNECTING TO RELAY ({joinCode})...");
            _lobbyPanel?.SetActive(false);
            OasisShooterManager.Instance?.ShowNotificationBanner($"CONNECTING TO SESSION...", 3f);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Relay] Join fallback to local squad staging ({ex.Message})");
            UpdateStatus($"SQUAD STAGING ACTIVE ({joinCode})");
            currentRole = NetworkRole.Client;
            if (!_remotePlayers.ContainsKey(0))
            {
                SpawnRemotePlayer(0, "SQUAD HOST", 0, 0, true);
            }
            OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
        }
        finally
        {
            _isConnectingOrHostingInProgress = false;
        }
    }

    public void StartHost()
    {
        StopSession();

        try
        {
            _udpClient = new UdpClient(DefaultPort);
            isUsingRelay = false;
            currentRole = NetworkRole.Host;
            myPlayerId = 0;
            isMatchLaunchedByHost = false;
            _isRunning = true;

            RegisterExistingHostDrones();

            _networkThread = new Thread(NetworkListenLoop) { IsBackground = true };
            _networkThread.Start();

            string localIP = GetLocalIPAddress();
            UpdateStatus($"HOSTING ON PORT {DefaultPort}\nLAN IP: {localIP}\n(Friends connect to this IP)");
            UpdatePlayerListUI();

            _lobbyPanel?.SetActive(false);
            OasisShooterManager.Instance?.ShowNotificationBanner("HOST SESSION ACTIVE", 3f);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Host notice: {ex.Message}");
            currentRole = NetworkRole.Host;
            myPlayerId = 0;
        }
    }

    public void StartClient(string target)
    {
        if (string.IsNullOrEmpty(target)) target = "127.0.0.1";
        target = target.Trim();

        // If user entered a 6-character alphanumeric string (without dots or colons), it's a Relay Join Code!
        if (target.Length == 6 && !target.Contains(".") && !target.Contains(":"))
        {
            StartRelayClient(target);
            return;
        }

        // Otherwise connect directly via IP/Port
        StopSession();

        // Clear local offline drones immediately before connecting
        OasisShooterManager.Instance?.CleanupLocalDrones();
        _syncedDrones.Clear();

        hostAddress = target;
        isUsingRelay = false;
        currentRole = NetworkRole.Client;
        myPlayerId = 1;
        _clientReceivedWelcome = false;
        _joinRetryTimer = 0f;

        if (!_remotePlayers.ContainsKey(0))
        {
            SpawnRemotePlayer(0, "SQUAD HOST", 0, 0, true);
            UpdatePlayerListUI();
        }

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(hostAddress, DefaultPort);
            currentRole = NetworkRole.Client;
            _isRunning = true;

            _networkThread = new Thread(NetworkListenLoop) { IsBackground = true };
            _networkThread.Start();

            // Send Join Handshake with local team & operative selection
            int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            int myOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
            SendPacketToServer($"JOIN|{myPlayerName}|{myTeam}|{myOp}");

            UpdateStatus($"CONNECTING TO {hostAddress}:{DefaultPort}...");
            _lobbyPanel?.SetActive(false);
            OasisShooterManager.Instance?.ShowNotificationBanner($"CONNECTING TO {hostAddress}...", 3f);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Connection notice: {ex.Message}");
            currentRole = NetworkRole.Client;
        }
    }

    private void RegisterExistingHostDrones()
    {
        if (OasisShooterManager.Instance != null)
        {
            var list = OasisShooterManager.Instance.GetActiveDrones();
            if (list != null)
            {
                foreach (var drone in list)
                {
                    if (drone != null && drone.gameObject != null)
                    {
                        if (drone.networkDroneId < 0)
                        {
                            drone.networkDroneId = OasisShooterManager.Instance.GetNextNetworkDroneId();
                        }
                        _syncedDrones[drone.networkDroneId] = drone;
                    }
                }
            }
        }
    }

    public void StopSession()
    {
        _isRunning = false;

        if (isUsingRelay && _relayDriver.IsCreated)
        {
            try
            {
                if (currentRole == NetworkRole.Client && _relayServerConn.IsCreated)
                {
                    SendRelayMessage(_relayServerConn, $"LEAVE|{myPlayerId}");
                    _relayDriver.Disconnect(_relayServerConn);
                }
                else if (currentRole == NetworkRole.Host)
                {
                    foreach (var kvp in _relayClientConnections)
                    {
                        if (kvp.Value.IsCreated) _relayDriver.Disconnect(kvp.Value);
                    }
                }
                if (_relayIncomingConnections.IsCreated)
                {
                    _relayIncomingConnections.Dispose();
                }
                _relayDriver.Dispose();
            }
            catch { }
            _relayServerConn = default;
            _relayClientConnections.Clear();
            _relayConnectionToPlayerId.Clear();
        }

        if (_udpClient != null)
        {
            try
            {
                if (currentRole == NetworkRole.Client)
                {
                    SendPacketToServer($"LEAVE|{myPlayerId}");
                }
                _udpClient.Close();
            }
            catch { }
            _udpClient = null;
        }

        if (_networkThread != null && _networkThread.IsAlive)
        {
            try { _networkThread.Abort(); } catch { }
            _networkThread = null;
        }

        // Clean up remote models
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value.gameObject != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }
        _remotePlayers.Clear();
        _clientEndpoints.Clear();

        // Clean up any puppet drones if we were a client
        if (currentRole == NetworkRole.Client)
        {
            foreach (var kvp in _syncedDrones)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null && kvp.Value.isNetworkPuppet)
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
        }
        _syncedDrones.Clear();

        isUsingRelay = false;
        activeRelayJoinCode = "";
        currentRole = NetworkRole.Offline;
        UpdateStatus("OFFLINE");
    }

    // ---------------------------------------------------------------------------------------------------------
    // RELAY TRANSPORT UPDATE & DISPATCH
    // ---------------------------------------------------------------------------------------------------------

    private void UpdateRelayDriver()
    {
        if (!_relayDriver.IsCreated || !_relayDriver.Bound) return;

        // Keep binding to Relay server alive
        _relayDriver.ScheduleUpdate().Complete();

        if (currentRole == NetworkRole.Host)
        {
            if (!_relayIncomingConnections.IsCreated)
            {
                _relayIncomingConnections = new NativeList<NetworkConnection>(4, Allocator.Persistent);
            }

            // Clean up any closed or disconnected connections
            for (int i = 0; i < _relayIncomingConnections.Length; i++)
            {
                if (!_relayIncomingConnections[i].IsCreated)
                {
                    _relayIncomingConnections.RemoveAt(i);
                    --i;
                }
            }

            // Accept incoming connections
            NetworkConnection incomingConn;
            while ((incomingConn = _relayDriver.Accept()) != default(NetworkConnection))
            {
                if (_relayIncomingConnections.Length >= 3)
                {
                    SendRelayMessage(incomingConn, "REJECT|ROOM_FULL");
                    _relayDriver.Disconnect(incomingConn);
                    continue;
                }

                _relayIncomingConnections.Add(incomingConn);
            }

            // Pop events for all accepted connections
            for (int i = 0; i < _relayIncomingConnections.Length; i++)
            {
                NetworkConnection conn = _relayIncomingConnections[i];
                if (!conn.IsCreated) continue;

                NetworkEvent.Type cmd;
                while ((cmd = _relayDriver.PopEventForConnection(conn, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
                {
                    if (cmd == NetworkEvent.Type.Connect)
                    {
                        // Client connected
                    }
                    else if (cmd == NetworkEvent.Type.Data)
                    {
                        NativeArray<byte> buffer = new NativeArray<byte>(stream.Length, Allocator.Temp);
                        stream.ReadBytes(buffer);
                        string msg = Encoding.UTF8.GetString(buffer.ToArray());
                        buffer.Dispose();

                        ProcessIncomingPacketFromRelay(msg, conn);
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        if (_relayConnectionToPlayerId.TryGetValue(conn, out int leavingId))
                        {
                            RemoveRemotePlayer(leavingId);
                            _relayClientConnections.Remove(leavingId);
                            _relayConnectionToPlayerId.Remove(conn);
                            BroadcastToClients($"PLAYER_LEFT|{leavingId}");
                            UpdatePlayerListUI();
                        }
                        _relayIncomingConnections[i] = default(NetworkConnection);
                        break;
                    }
                }
            }
        }
        else if (currentRole == NetworkRole.Client)
        {
            if (!_relayServerConn.IsCreated) return;

            NetworkEvent.Type cmd;
            while ((cmd = _relayServerConn.PopEvent(_relayDriver, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Connect)
                {
                    // Handshake to host with current team and operative selection
                    int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                    int myOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
                    SendRelayMessage(_relayServerConn, $"JOIN|{myPlayerName}|{myTeam}|{myOp}");
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    NativeArray<byte> buffer = new NativeArray<byte>(stream.Length, Allocator.Temp);
                    stream.ReadBytes(buffer);
                    string msg = Encoding.UTF8.GetString(buffer.ToArray());
                    buffer.Dispose();

                    ProcessIncomingPacket(msg, null);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    UpdateStatus("DISCONNECTED FROM RELAY");
                    StopSession();
                    break;
                }
            }
        }
    }

    private void ClearLocalPlaceholders()
    {
        List<int> toRemove = new List<int>();
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null && kvp.Value.isLocalPlaceholder)
            {
                toRemove.Add(kvp.Key);
            }
        }
        for (int i = 0; i < toRemove.Count; i++)
        {
            RemoveRemotePlayer(toRemove[i]);
        }
    }

    private void ProcessIncomingPacketFromRelay(string msg, NetworkConnection senderConn)
    {
        string[] parts = msg.Split('|');
        if (parts.Length == 0) return;
        string cmd = parts[0];

        if (cmd == "JOIN")
        {
            if (_relayClientConnections.Count >= 3)
            {
                SendRelayMessage(senderConn, "REJECT|ROOM_FULL");
                return;
            }

            ClearLocalPlaceholders();

            int newId = _nextClientId++;
            string clientName = parts.Length > 1 ? parts[1] : $"Operative #{newId + 1}";
            int clientTeam = (parts.Length > 2 && int.TryParse(parts[2], out int ct)) ? ct : 0;
            int clientOp = (parts.Length > 3 && int.TryParse(parts[3], out int co)) ? co : 1;

            _relayClientConnections[newId] = senderConn;
            _relayConnectionToPlayerId[senderConn] = newId;

            // Welcome
            SendRelayMessage(senderConn, $"WELCOME|{newId}");

            // Spawn remote player on host
            SpawnRemotePlayer(newId, clientName, clientTeam, clientOp, false);

            // Broadcast to other clients
            BroadcastToClients($"PLAYER_JOINED|{newId}|{clientName}|{clientTeam}|{clientOp}");

            // Send Host and all existing players to new client with their selected Team & Operative
            int hostTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            int hostOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
            int hostNumTeams = PlayerPrefs.GetInt("OasisNumTeams", 2);
            int hostPerTeam = PlayerPrefs.GetInt("OasisPlayersPerTeam", 2);
            SendRelayMessage(senderConn, $"PLAYER_JOINED|0|SQUAD HOST|{hostTeam}|{hostOp}|{hostNumTeams}|{hostPerTeam}");
            foreach (var rp in _remotePlayers)
            {
                if (rp.Key != newId && rp.Value != null)
                {
                    SendRelayMessage(senderConn, $"PLAYER_JOINED|{rp.Key}|{rp.Value.name}|{rp.Value.teamId}|{rp.Value.operativeIndex}");
                }
            }

            if (isMatchLaunchedByHost || (OasisCharacterCustomizer.Instance != null && !OasisCharacterCustomizer.Instance.isSelectionActive))
            {
                bool isSim = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode);
                SendRelayMessage(senderConn, $"LAUNCH_GAME|{hostNumTeams}|{hostPerTeam}|{(isSim ? 1 : 0)}");
            }

            // Sync current wave
            if (OasisShooterManager.Instance != null)
            {
                SendRelayMessage(senderConn, $"SYNC_WAVE|{OasisShooterManager.Instance.currentWave}|{OasisShooterManager.Instance.totalDronesInWave}|{OasisShooterManager.Instance.dronesRemaining}");
            }

            // Sync drones
            foreach (var kvp in _syncedDrones)
            {
                TargetDrone d = kvp.Value;
                if (d != null && d.gameObject != null)
                {
                    Vector3 pos = d.transform.position;
                    SendRelayMessage(senderConn, $"DRONE_SPAWN|{d.networkDroneId}|{pos.x.ToString("F2", CultureInfo.InvariantCulture)},{pos.y.ToString("F2", CultureInfo.InvariantCulture)},{pos.z.ToString("F2", CultureInfo.InvariantCulture)}|{d.moveSpeed.ToString("F2", CultureInfo.InvariantCulture)}|{d.maxHealth.ToString("F1", CultureInfo.InvariantCulture)}|{(d.isHostile ? 1 : 0)}");
                }
            }

            UpdatePlayerListUI();
            OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
            OasisShooterManager.Instance?.ShowNotificationBanner($"{clientName} JOINED! SQUAD READY!", 3f);
        }
        else
        {
            // Process regular commands (LOBBY_SELECT, STATE, FIRE, DRONE_HIT, SCORE_UPDATE, LEAVE)
            ProcessIncomingPacket(msg, null);
        }
    }

    private void SendRelayMessage(NetworkConnection conn, string message)
    {
        if (!_relayDriver.IsCreated || !conn.IsCreated) return;
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        if (_relayDriver.BeginSend(conn, out DataStreamWriter writer, bytes.Length) == 0)
        {
            NativeArray<byte> nativeBytes = new NativeArray<byte>(bytes, Allocator.Temp);
            writer.WriteBytes(nativeBytes);
            nativeBytes.Dispose();
            _relayDriver.EndSend(writer);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // BACKGROUND RECEIVE LOOP & PACKET DISPATCHING
    // ---------------------------------------------------------------------------------------------------------

    private void NetworkListenLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (_isRunning && _udpClient != null)
        {
            try
            {
                byte[] data = _udpClient.Receive(ref remoteEP);
                if (data != null && data.Length > 0)
                {
                    string msg = Encoding.UTF8.GetString(data);
                    lock (_queueLock)
                    {
                        _mainThreadQueue.Enqueue(() => ProcessIncomingPacket(msg, remoteEP));
                    }
                }
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private void ProcessIncomingPacket(string msg, IPEndPoint senderEP)
    {
        string[] parts = msg.Split('|');
        if (parts.Length == 0) return;
        string cmd = parts[0];

        if (cmd == "LOBBY_SELECT" && parts.Length >= 5)
        {
            int senderId = int.Parse(parts[1]);
            string senderName = parts[2];
            int teamId = int.Parse(parts[3]);
            int opIdx = int.Parse(parts[4]);
            int numTeams = parts.Length > 5 ? int.Parse(parts[5]) : -1;
            int perTeam = parts.Length > 6 ? int.Parse(parts[6]) : -1;

            if (senderId != myPlayerId)
            {
                if (!_remotePlayers.TryGetValue(senderId, out var rp) || rp == null)
                {
                    SpawnRemotePlayer(senderId, senderName, teamId, opIdx, false);
                }
                else
                {
                    rp.name = senderName;
                    rp.teamId = teamId;
                    rp.operativeIndex = opIdx;
                    rp.isLocalPlaceholder = false;
                    ApplyOperativeSkinToRemotePlayer(rp);
                }

                if (senderId == 0 && numTeams >= 2 && perTeam >= 1 && OasisCharacterCustomizer.Instance != null)
                {
                    OasisCharacterCustomizer.Instance.SyncMatchConfigFromHost(numTeams, perTeam);
                }
                OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
                UpdatePlayerListUI();
            }

            if (currentRole == NetworkRole.Host)
            {
                BroadcastToClients(msg, senderId);
            }
            return;
        }

        if (currentRole == NetworkRole.Host)
        {
            // Host Packet Handling
            if (cmd == "PLAYER_DOWNED")
            {
                int senderId = int.Parse(parts[1]);
                if (_remotePlayers.TryGetValue(senderId, out var rp) && rp != null)
                {
                    rp.isDowned = true;
                    if (rp.nameTagText != null)
                    {
                        rp.nameTagText.text = $"[DOWNED] {rp.name}";
                        rp.nameTagText.color = new Color(1f, 0.35f, 0.35f);
                    }
                }
                BroadcastToClients(msg, senderId);
            }
            else if (cmd == "REVIVE_PROGRESS")
            {
                int targetId = int.Parse(parts[1]);
                float prog = float.Parse(parts[2], CultureInfo.InvariantCulture);
                if (targetId == 0 && OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.playerReviveProgress = prog;
                }
                else if (_remotePlayers.TryGetValue(targetId, out var rp) && rp != null)
                {
                    rp.reviveProgress = prog;
                }
                int fromId = parts.Length > 3 ? int.Parse(parts[3]) : -1;
                BroadcastToClients(msg, fromId);
            }
            else if (cmd == "PLAYER_REVIVED")
            {
                int targetId = int.Parse(parts[1]);
                if (targetId == 0 && OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.ReviveLocalPlayer();
                }
                else if (_remotePlayers.TryGetValue(targetId, out var rp) && rp != null)
                {
                    rp.isDowned = false;
                    rp.reviveProgress = 0f;
                    ApplyOperativeSkinToRemotePlayer(rp);
                }
                BroadcastToClients(msg);
            }
            else if (cmd == "JOIN")
            {
                // Check if this endpoint already joined (due to UDP retry)
                int existingClientId = -1;
                if (senderEP != null)
                {
                    foreach (var kvp in _clientEndpoints)
                    {
                        if (kvp.Value != null && kvp.Value.Equals(senderEP))
                        {
                            existingClientId = kvp.Key;
                            break;
                        }
                    }
                }

                if (existingClientId < 0 && _clientEndpoints.Count >= 3)
                {
                    // 4-player max room full
                    byte[] reject = Encoding.UTF8.GetBytes("REJECT|ROOM_FULL");
                    _udpClient.Send(reject, reject.Length, senderEP);
                    return;
                }

                ClearLocalPlaceholders();

                int newId = (existingClientId > 0) ? existingClientId : _nextClientId++;
                string clientName = parts.Length > 1 ? parts[1] : $"Operative #{newId + 1}";
                int clientTeam = (parts.Length > 2 && int.TryParse(parts[2], out int ct)) ? ct : 0;
                int clientOp = (parts.Length > 3 && int.TryParse(parts[3], out int co)) ? co : 1;

                _clientEndpoints[newId] = senderEP;

                // Send Welcome
                SendToEndpoint(senderEP, $"WELCOME|{newId}");

                // Spawn remote player representation on host
                if (_remotePlayers.TryGetValue(newId, out var existingRp) && existingRp != null)
                {
                    existingRp.name = clientName;
                    existingRp.teamId = clientTeam;
                    existingRp.operativeIndex = clientOp;
                    existingRp.isLocalPlaceholder = false;
                }
                else
                {
                    SpawnRemotePlayer(newId, clientName, clientTeam, clientOp, false);
                }

                // Broadcast new player to all other clients
                BroadcastToClients($"PLAYER_JOINED|{newId}|{clientName}|{clientTeam}|{clientOp}");

                // Send Host and all existing players to the new client
                int hostTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                int hostOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
                int hostNumTeams = PlayerPrefs.GetInt("OasisNumTeams", 2);
                int hostPerTeam = PlayerPrefs.GetInt("OasisPlayersPerTeam", 2);
                SendToEndpoint(senderEP, $"PLAYER_JOINED|0|SQUAD HOST|{hostTeam}|{hostOp}|{hostNumTeams}|{hostPerTeam}");
                foreach (var rp in _remotePlayers)
                {
                    if (rp.Key != newId && rp.Value != null)
                    {
                        SendToEndpoint(senderEP, $"PLAYER_JOINED|{rp.Key}|{rp.Value.name}|{rp.Value.teamId}|{rp.Value.operativeIndex}");
                    }
                }

                // If Host has already started the game, immediately launch the joining client into the match!
                if (isMatchLaunchedByHost || (OasisCharacterCustomizer.Instance != null && !OasisCharacterCustomizer.Instance.isSelectionActive))
                {
                    bool isSim = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode);
                    SendToEndpoint(senderEP, $"LAUNCH_GAME|{hostNumTeams}|{hostPerTeam}|{(isSim ? 1 : 0)}");
                }

                // Send current wave info so client HUD matches host immediately
                if (OasisShooterManager.Instance != null)
                {
                    SendToEndpoint(senderEP, $"SYNC_WAVE|{OasisShooterManager.Instance.currentWave}|{OasisShooterManager.Instance.totalDronesInWave}|{OasisShooterManager.Instance.dronesRemaining}");
                }

                // Send all active drones to the new client so they are instantly visible
                foreach (var kvp in _syncedDrones)
                {
                    TargetDrone d = kvp.Value;
                    if (d != null && d.gameObject != null)
                    {
                        Vector3 pos = d.transform.position;
                        SendToEndpoint(senderEP, $"DRONE_SPAWN|{d.networkDroneId}|{pos.x.ToString("F2", CultureInfo.InvariantCulture)},{pos.y.ToString("F2", CultureInfo.InvariantCulture)},{pos.z.ToString("F2", CultureInfo.InvariantCulture)}|{d.moveSpeed.ToString("F2", CultureInfo.InvariantCulture)}|{d.maxHealth.ToString("F1", CultureInfo.InvariantCulture)}|{(d.isHostile ? 1 : 0)}");
                    }
                }

                UpdatePlayerListUI();
                OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
                OasisShooterManager.Instance?.ShowNotificationBanner($"{clientName} JOINED! SQUAD READY!", 3f);
            }
            else if (cmd == "STATE")
            {
                // Relay state to other clients and update host's remote player
                int senderId = int.Parse(parts[1]);
                UpdateRemotePlayerState(senderId, parts);
                BroadcastToClients(msg, senderId);
            }
            else if (cmd == "FIRE")
            {
                // Relay blaster laser fire to all other players
                int senderId = int.Parse(parts[1]);
                TriggerRemoteLaser(parts);
                BroadcastToClients(msg, senderId);
            }
            else if (cmd == "DRONE_HIT")
            {
                // Client reported hit against a synchronized drone: Authoritative Host processes damage
                int dId = int.Parse(parts[1]);
                float dmg = float.Parse(parts[2], CultureInfo.InvariantCulture);
                string[] hitPosParts = parts[3].Split(',');
                Vector3 hitPos = new Vector3(
                    float.Parse(hitPosParts[0], CultureInfo.InvariantCulture),
                    float.Parse(hitPosParts[1], CultureInfo.InvariantCulture),
                    float.Parse(hitPosParts[2], CultureInfo.InvariantCulture));
                int attackerId = parts.Length > 4 ? int.Parse(parts[4]) : 0;

                if (_syncedDrones.TryGetValue(dId, out TargetDrone hostDrone) && hostDrone != null)
                {
                    hostDrone.TakeDamage(dmg, hitPos, Vector3.up, attackerId);
                }
            }
            else if (cmd == "SCORE_UPDATE")
            {
                int senderId = int.Parse(parts[1]);
                int sc = int.Parse(parts[2]);
                int kl = int.Parse(parts[3]);
                if (_remotePlayers.TryGetValue(senderId, out var rp))
                {
                    rp.score = sc;
                    rp.kills = kl;
                }
                BroadcastToClients(msg, senderId);
            }
            else if (cmd == "LEAVE")
            {
                int leavingId = int.Parse(parts[1]);
                RemoveRemotePlayer(leavingId);
                _clientEndpoints.Remove(leavingId);
                BroadcastToClients($"PLAYER_LEFT|{leavingId}");
                UpdatePlayerListUI();
                OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
            }
        }
        else if (currentRole == NetworkRole.Client)
        {
            // Client Packet Handling
            if (cmd == "WELCOME")
            {
                _clientReceivedWelcome = true;
                myPlayerId = int.Parse(parts[1]);
                UpdateStatus($"CONNECTED AS OPERATIVE #{myPlayerId} - STAGING READY");
                UpdatePlayerListUI();
                int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                int myOp = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
                BroadcastLobbySelection(myTeam, myOp, PlayerPrefs.GetInt("OasisNumTeams", 2), PlayerPrefs.GetInt("OasisPlayersPerTeam", 2));
                OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
                OasisShooterManager.Instance?.ShowNotificationBanner($"CONNECTED TO SQUAD HOST! (OPERATIVE #{myPlayerId + 1})", 2.5f);
            }
            else if (cmd == "SYNC_WAVE")
            {
                int waveNum = int.Parse(parts[1]);
                int totDrones = int.Parse(parts[2]);
                int remDrones = int.Parse(parts[3]);
                OasisShooterManager.Instance?.SyncClientWave(waveNum, totDrones, remDrones);
            }
            else if (cmd == "PLAYER_JOINED")
            {
                _clientReceivedWelcome = true;
                int id = int.Parse(parts[1]);
                string pName = parts.Length > 2 ? parts[2] : $"Operative #{id + 1}";
                int pTeam = (parts.Length > 3 && int.TryParse(parts[3], out int pt)) ? pt : 0;
                int pOp = (parts.Length > 4 && int.TryParse(parts[4], out int po)) ? po : 0;
                int nTeams = (parts.Length > 5 && int.TryParse(parts[5], out int nt)) ? nt : -1;
                int perTeam = (parts.Length > 6 && int.TryParse(parts[6], out int ppt)) ? ppt : -1;

                if (id != myPlayerId)
                {
                    if (_remotePlayers.TryGetValue(id, out var existingRp) && existingRp != null)
                    {
                        existingRp.name = pName;
                        existingRp.teamId = pTeam;
                        existingRp.operativeIndex = pOp;
                        existingRp.isLocalPlaceholder = false;
                        ApplyOperativeSkinToRemotePlayer(existingRp);
                    }
                    else
                    {
                        SpawnRemotePlayer(id, pName, pTeam, pOp, false);
                    }

                    if (id == 0 && nTeams >= 2 && perTeam >= 1 && OasisCharacterCustomizer.Instance != null)
                    {
                        OasisCharacterCustomizer.Instance.SyncMatchConfigFromHost(nTeams, perTeam);
                    }
                    UpdatePlayerListUI();
                    OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
                    OasisShooterManager.Instance?.ShowNotificationBanner($"{pName} IN STAGING BAY!", 2f);
                }
            }
            else if (cmd == "PLAYER_DOWNED")
            {
                int pId = int.Parse(parts[1]);
                if (_remotePlayers.TryGetValue(pId, out var rp) && rp != null)
                {
                    rp.isDowned = true;
                    if (rp.nameTagText != null)
                    {
                        rp.nameTagText.text = $"[DOWNED] {rp.name}";
                        rp.nameTagText.color = new Color(1f, 0.35f, 0.35f);
                    }
                }
            }
            else if (cmd == "REVIVE_PROGRESS")
            {
                int targetId = int.Parse(parts[1]);
                float prog = float.Parse(parts[2], CultureInfo.InvariantCulture);
                if (targetId == myPlayerId && OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.playerReviveProgress = prog;
                }
                else if (_remotePlayers.TryGetValue(targetId, out var rp) && rp != null)
                {
                    rp.reviveProgress = prog;
                }
            }
            else if (cmd == "PLAYER_REVIVED")
            {
                int targetId = int.Parse(parts[1]);
                if (targetId == myPlayerId && OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.ReviveLocalPlayer();
                }
                else if (_remotePlayers.TryGetValue(targetId, out var rp) && rp != null)
                {
                    rp.isDowned = false;
                    rp.reviveProgress = 0f;
                    ApplyOperativeSkinToRemotePlayer(rp);
                }
            }
            else if (cmd == "PLAYER_LEFT")
            {
                int id = int.Parse(parts[1]);
                RemoveRemotePlayer(id);
                UpdatePlayerListUI();
                OasisCharacterCustomizer.Instance?.UpdateRosterDisplay();
            }
            else if (cmd == "STATE")
            {
                int senderId = int.Parse(parts[1]);
                if (senderId != myPlayerId)
                {
                    UpdateRemotePlayerState(senderId, parts);
                }
            }
            else if (cmd == "FIRE")
            {
                int senderId = int.Parse(parts[1]);
                if (senderId != myPlayerId)
                {
                    TriggerRemoteLaser(parts);
                }
            }
            else if (cmd == "DRONE_SPAWN")
            {
                int dId = int.Parse(parts[1]);
                string[] pParts = parts[2].Split(',');
                Vector3 spawnPos = new Vector3(
                    float.Parse(pParts[0], CultureInfo.InvariantCulture),
                    float.Parse(pParts[1], CultureInfo.InvariantCulture),
                    float.Parse(pParts[2], CultureInfo.InvariantCulture));
                float spd = float.Parse(parts[3], CultureInfo.InvariantCulture);
                float hp = float.Parse(parts[4], CultureInfo.InvariantCulture);
                bool hostile = parts.Length > 5 && parts[5] == "1";
                EnemyType eType = (parts.Length > 6 && int.TryParse(parts[6], out int typeVal)) ? (EnemyType)typeVal : EnemyType.ScoutDrone;

                SpawnPuppetDrone(dId, spawnPos, spd, hp, hostile, eType);
            }
            else if (cmd == "DRONES_STATE")
            {
                // Format: DRONES_STATE|id:x,y,z,rotY;id:x,y,z,rotY;...
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    string[] droneEntries = parts[1].Split(';');
                    for (int i = 0; i < droneEntries.Length; i++)
                    {
                        if (string.IsNullOrEmpty(droneEntries[i])) continue;
                        string[] sub = droneEntries[i].Split(':');
                        if (sub.Length < 2) continue;
                        if (!int.TryParse(sub[0], out int dId)) continue;

                        string[] comp = sub[1].Split(',');
                        if (comp.Length < 4) continue;

                        Vector3 netPos = new Vector3(
                            float.Parse(comp[0], CultureInfo.InvariantCulture),
                            float.Parse(comp[1], CultureInfo.InvariantCulture),
                            float.Parse(comp[2], CultureInfo.InvariantCulture));
                        float netRotY = float.Parse(comp[3], CultureInfo.InvariantCulture);
                        Quaternion netRot = Quaternion.Euler(0, netRotY, 0);

                        if (_syncedDrones.TryGetValue(dId, out TargetDrone puppet) && puppet != null)
                        {
                            puppet.networkTargetPosition = netPos;
                            puppet.networkTargetRotation = netRot;
                        }
                        else
                        {
                            // If puppet drone has not been spawned yet, instantiate it immediately
                            SpawnPuppetDrone(dId, netPos, 2.4f, 60f, true, EnemyType.ScoutDrone);
                        }
                    }
                }
            }
            else if (cmd == "DRONE_FIRE")
            {
                int dId = int.Parse(parts[1]);
                string[] origParts = parts[2].Split(',');
                string[] dirParts = parts[3].Split(',');
                Vector3 fPos = new Vector3(
                    float.Parse(origParts[0], CultureInfo.InvariantCulture),
                    float.Parse(origParts[1], CultureInfo.InvariantCulture),
                    float.Parse(origParts[2], CultureInfo.InvariantCulture));
                Vector3 fDir = new Vector3(
                    float.Parse(dirParts[0], CultureInfo.InvariantCulture),
                    float.Parse(dirParts[1], CultureInfo.InvariantCulture),
                    float.Parse(dirParts[2], CultureInfo.InvariantCulture));

                if (_syncedDrones.TryGetValue(dId, out TargetDrone puppet) && puppet != null)
                {
                    puppet.SpawnPlasmaBolt(fPos, fDir);
                }
                else
                {
                    // Fallback: trigger bolt at position
                    GameObject boltObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    boltObj.transform.position = fPos;
                    boltObj.transform.localScale = Vector3.one * 0.35f;
                    Material boltMat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.25f, 0.1f));
                    boltObj.GetComponent<MeshRenderer>().material = boltMat;
                    boltObj.GetComponent<SphereCollider>().isTrigger = true;
                    DronePlasmaBolt bolt = boltObj.AddComponent<DronePlasmaBolt>();
                    bolt.Launch(fDir);
                }
            }
            else if (cmd == "DRONE_DIE")
            {
                int dId = int.Parse(parts[1]);
                string[] expParts = parts[2].Split(',');
                Vector3 expPos = new Vector3(
                    float.Parse(expParts[0], CultureInfo.InvariantCulture),
                    float.Parse(expParts[1], CultureInfo.InvariantCulture),
                    float.Parse(expParts[2], CultureInfo.InvariantCulture));
                int killerId = parts.Length > 3 ? int.Parse(parts[3]) : 0;

                if (_syncedDrones.TryGetValue(dId, out TargetDrone puppet) && puppet != null)
                {
                    puppet.NetworkDie(expPos, killerId);
                }
                else
                {
                    BlasterVFX.SpawnExplosion(expPos);
                    AudioSource.PlayClipAtPoint(ProceduralAudio.GetDroneExplosionClip(), expPos, 1.2f);
                    OasisShooterManager.Instance?.OnTargetDestroyed(150, null, killerId);
                }
                _syncedDrones.Remove(dId);
            }
            else if (cmd == "SCORE_UPDATE")
            {
                int senderId = int.Parse(parts[1]);
                int sc = int.Parse(parts[2]);
                int kl = int.Parse(parts[3]);
                if (_remotePlayers.TryGetValue(senderId, out var rp))
                {
                    rp.score = sc;
                    rp.kills = kl;
                }
            }
            else if (cmd == "RESTART_GAME")
            {
                // Host restarted the session - synchronize reload on all clients
                Time.timeScale = 1f;
                _syncedDrones.Clear();
                foreach (var rp in _remotePlayers.Values)
                {
                    if (rp.gameObject != null) Destroy(rp.gameObject);
                }
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            else if (cmd == "ENTER_STAGING")
            {
                _clientReceivedWelcome = true;
                string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (activeScene.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene("OasisScene");
                }
            }
            else if (cmd == "START_COUNTDOWN")
            {
                _clientReceivedWelcome = true;
                int nTeams = (parts.Length > 1 && int.TryParse(parts[1], out int nt)) ? nt : PlayerPrefs.GetInt("OasisNumTeams", 2);
                int perTeam = (parts.Length > 2 && int.TryParse(parts[2], out int ppt)) ? ppt : PlayerPrefs.GetInt("OasisPlayersPerTeam", 2);
                bool isSim = (parts.Length > 3 && parts[3] == "1");

                if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
                {
                    OasisCharacterCustomizer.Instance.SyncMatchConfigFromHost(nTeams, perTeam);
                    OasisCharacterCustomizer.Instance.StartDeploymentCountdownFromHost(isSim);
                }
            }
            else if (cmd == "CANCEL_COUNTDOWN")
            {
                if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
                {
                    OasisCharacterCustomizer.Instance.CancelCountdownFromHost();
                }
            }
            else if (cmd == "LAUNCH_GAME")
            {
                _clientReceivedWelcome = true;
                int nTeams = (parts.Length > 1 && int.TryParse(parts[1], out int nt)) ? nt : PlayerPrefs.GetInt("OasisNumTeams", 2);
                int perTeam = (parts.Length > 2 && int.TryParse(parts[2], out int ppt)) ? ppt : PlayerPrefs.GetInt("OasisPlayersPerTeam", 2);
                bool isSim = (parts.Length > 3 && parts[3] == "1");

                PlayerPrefs.SetInt("OasisNumTeams", nTeams);
                PlayerPrefs.SetInt("OasisPlayersPerTeam", perTeam);
                PlayerPrefs.Save();

                string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (activeScene.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AutoDeployOnSceneLoad = true;
                    AutoDeployIsSim = isSim;
                    UnityEngine.SceneManagement.SceneManager.LoadScene("OasisScene");
                    return;
                }

                if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
                {
                    OasisCharacterCustomizer.Instance.ExecuteHostLaunchCommand(nTeams, perTeam, isSim);
                    return;
                }

                if (OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.isWaitingForOperatives = false;
                    OasisShooterManager.Instance.ShowNotificationBanner("MISSION LAUNCHED! SQUAD ENGAGING!", 3f);
                }
                if (_lobbyPanel != null)
                {
                    _lobbyPanel.SetActive(false);
                }
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (cmd == "MATCH_SYNC")
            {
                // Authoritative Host synchronization of team kills and match timer
                if (parts.Length >= 6)
                {
                    int aK = int.Parse(parts[1]);
                    int bK = int.Parse(parts[2]);
                    int cK = int.Parse(parts[3]);
                    int dK = int.Parse(parts[4]);
                    float remTime = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    OasisShooterManager.Instance?.SyncTeamScoresFromHost(aK, bK, cK, dK, remTime);
                }
            }
            else if (cmd == "MATCH_CONCLUDED")
            {
                // Authoritative Host match conclusion broadcast
                if (parts.Length >= 7)
                {
                    int winTeam = int.Parse(parts[1]);
                    string reason = parts[2];
                    int aK = int.Parse(parts[3]);
                    int bK = int.Parse(parts[4]);
                    int cK = int.Parse(parts[5]);
                    int dK = int.Parse(parts[6]);
                    OasisShooterManager.Instance?.ConcludeMatchFromHost(winTeam, reason, aK, bK, cK, dK);
                }
            }
        }
    }

    public void BroadcastMatchSync(int aK, int bK, int cK, int dK, float remTime)
    {
        if (currentRole != NetworkRole.Host) return;
        string packet = $"MATCH_SYNC|{aK}|{bK}|{cK}|{dK}|{remTime.ToString("F1", CultureInfo.InvariantCulture)}";
        BroadcastToClients(packet);
        if (isUsingRelay && _relayDriver.IsCreated)
        {
            _relayDriver.ScheduleUpdate().Complete();
        }
    }

    public void BroadcastMatchConcluded(int winTeam, string reason, int aK, int bK, int cK, int dK)
    {
        if (currentRole != NetworkRole.Host) return;
        string cleanReason = reason.Replace("|", " - ").Replace("\n", " ");
        string packet = $"MATCH_CONCLUDED|{winTeam}|{cleanReason}|{aK}|{bK}|{cK}|{dK}";
        StartCoroutine(ReliableHostBroadcastRoutine(packet, 4));
    }

    public void BroadcastLobbySelection(int teamId, int operativeIndex, int numTeams, int playersPerTeam)
    {
        if (currentRole == NetworkRole.Offline) return;

        string pName = (currentRole == NetworkRole.Host) ? "SQUAD HOST" : $"OPERATIVE #{myPlayerId + 1}";
        string packet = $"LOBBY_SELECT|{myPlayerId}|{pName}|{teamId}|{operativeIndex}|{numTeams}|{playersPerTeam}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
            if (isUsingRelay && _relayDriver.IsCreated)
            {
                _relayDriver.ScheduleUpdate().Complete();
            }
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastCountdownStart(int numTeams, int playersPerTeam, bool isSim)
    {
        if (currentRole != NetworkRole.Host) return;
        string packet = $"START_COUNTDOWN|{numTeams}|{playersPerTeam}|{(isSim ? 1 : 0)}";
        StartCoroutine(ReliableHostBroadcastRoutine(packet, 2));
    }

    public void BroadcastCountdownCancel()
    {
        if (currentRole != NetworkRole.Host) return;
        StartCoroutine(ReliableHostBroadcastRoutine("CANCEL_COUNTDOWN", 2));
    }

    public void BroadcastMatchLaunch(int numTeams, int playersPerTeam, bool isSim)
    {
        if (currentRole != NetworkRole.Host) return;
        isMatchLaunchedByHost = true;
        string packet = $"LAUNCH_GAME|{numTeams}|{playersPerTeam}|{(isSim ? 1 : 0)}";
        StartCoroutine(ReliableHostBroadcastRoutine(packet, 3));
    }

    private IEnumerator ReliableHostBroadcastRoutine(string packet, int repeatCount)
    {
        for (int i = 0; i < repeatCount; i++)
        {
            BroadcastToClients(packet, myPlayerId);
            if (isUsingRelay && _relayDriver.IsCreated)
            {
                _relayDriver.ScheduleUpdate().Complete();
            }
            if (i < repeatCount - 1)
            {
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }
    }

    private void BroadcastToClients(string message, int excludeId = -1)
    {
        if (isUsingRelay)
        {
            foreach (var kvp in _relayClientConnections)
            {
                if (kvp.Key != excludeId && kvp.Value.IsCreated)
                {
                    SendRelayMessage(kvp.Value, message);
                }
            }
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(message);
        foreach (var kvp in _clientEndpoints)
        {
            if (kvp.Key != excludeId && kvp.Value != null)
            {
                try
                {
                    _udpClient?.Send(bytes, bytes.Length, kvp.Value);
                }
                catch { }
            }
        }
    }

    private void SendToEndpoint(IPEndPoint ep, string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            _udpClient?.Send(bytes, bytes.Length, ep);
        }
        catch { }
    }

    private void SendPacketToServer(string message)
    {
        if (isUsingRelay)
        {
            if (_relayServerConn.IsCreated)
            {
                SendRelayMessage(_relayServerConn, message);
            }
            return;
        }

        if (_udpClient == null) return;
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            _udpClient.Send(bytes, bytes.Length);
        }
        catch { }
    }

    // ---------------------------------------------------------------------------------------------------------
    // TRANSMIT LOCAL PLAYER STATE & ACTIONS
    // ---------------------------------------------------------------------------------------------------------

    private void SendLocalState()
    {
        if (_localPlayerTransform == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                _localPlayerTransform = p.transform;
                _localCombat = p.GetComponent<OasisCombatMovement>();
            }
        }

        if (_localPlayerTransform == null) return;

        Vector3 pos = _localPlayerTransform.position;
        Vector3 rot = _localPlayerTransform.eulerAngles;
        float pitch = Camera.main != null ? Camera.main.transform.eulerAngles.x : 0f;
        if (pitch > 180f) pitch -= 360f;
        bool isCrouching = _localCombat != null && _localCombat.isCrouching;
        bool isDowned = OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned;
        int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        int myOp = OasisCharacterCustomizer.Instance != null ? OasisCharacterCustomizer.Instance.selectedOperativeIndex : PlayerPrefs.GetInt("OasisSelectedOperative", 0);

        string packet = $"STATE|{myPlayerId}|{pos.x.ToString("F2", CultureInfo.InvariantCulture)},{pos.y.ToString("F2", CultureInfo.InvariantCulture)},{pos.z.ToString("F2", CultureInfo.InvariantCulture)}|{rot.y.ToString("F2", CultureInfo.InvariantCulture)}|{(isCrouching ? 1 : 0)}|{pitch.ToString("F1", CultureInfo.InvariantCulture)}|{(isDowned ? 1 : 0)}|{myTeam}|{myOp}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastLaserFire(Vector3 origin, Vector3 hitPoint, bool isHitTarget)
    {
        if (currentRole == NetworkRole.Offline) return;

        string packet = $"FIRE|{myPlayerId}|{origin.x.ToString("F2", CultureInfo.InvariantCulture)},{origin.y.ToString("F2", CultureInfo.InvariantCulture)},{origin.z.ToString("F2", CultureInfo.InvariantCulture)}|{hitPoint.x.ToString("F2", CultureInfo.InvariantCulture)},{hitPoint.y.ToString("F2", CultureInfo.InvariantCulture)},{hitPoint.z.ToString("F2", CultureInfo.InvariantCulture)}|{(isHitTarget ? 1 : 0)}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastScore(int newScore, int newKills)
    {
        localScore = newScore;
        localKills = newKills;

        if (currentRole == NetworkRole.Offline) return;

        string packet = $"SCORE_UPDATE|{myPlayerId}|{newScore}|{newKills}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastPlayerDowned(int attackerId, int attackerTeamId)
    {
        if (currentRole == NetworkRole.Offline) return;

        string packet = $"PLAYER_DOWNED|{myPlayerId}|{attackerId}|{attackerTeamId}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastReviveProgress(int targetPlayerId, float progress)
    {
        if (currentRole == NetworkRole.Offline) return;

        string packet = $"REVIVE_PROGRESS|{targetPlayerId}|{progress.ToString("F2", CultureInfo.InvariantCulture)}|{myPlayerId}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet, myPlayerId);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastPlayerRevived(int revivedPlayerId, int reviverId)
    {
        if (currentRole == NetworkRole.Offline) return;

        string packet = $"PLAYER_REVIVED|{revivedPlayerId}|{reviverId}";

        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients(packet);
        }
        else if (currentRole == NetworkRole.Client)
        {
            SendPacketToServer(packet);
        }
    }

    public void BroadcastRestart()
    {
        if (currentRole == NetworkRole.Host)
        {
            BroadcastToClients("RESTART_GAME");
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // CO-OP DRONE MULTIPLAYER SYNCHRONIZATION
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Host registers a spawned drone and broadcasts DRONE_SPAWN to all connected clients.
    /// </summary>
    public void RegisterHostDrone(TargetDrone drone)
    {
        if (drone == null || drone.networkDroneId < 0) return;
        _syncedDrones[drone.networkDroneId] = drone;

        Vector3 pos = drone.transform.position;
        string packet = $"DRONE_SPAWN|{drone.networkDroneId}|{pos.x.ToString("F2", CultureInfo.InvariantCulture)},{pos.y.ToString("F2", CultureInfo.InvariantCulture)},{pos.z.ToString("F2", CultureInfo.InvariantCulture)}|{drone.moveSpeed.ToString("F2", CultureInfo.InvariantCulture)}|{drone.maxHealth.ToString("F1", CultureInfo.InvariantCulture)}|{(drone.isHostile ? 1 : 0)}|{(int)drone.enemyType}";
        BroadcastToClients(packet);
    }

    /// <summary>
    /// Host broadcasts wave state updates to all clients so HUD hostiles count stays synchronized.
    /// </summary>
    public void BroadcastWaveState(int waveNumber, int totalDrones, int dronesRemaining)
    {
        if (currentRole != NetworkRole.Host) return;
        BroadcastToClients($"SYNC_WAVE|{waveNumber}|{totalDrones}|{dronesRemaining}");
    }

    /// <summary>
    /// Host batched broadcast of all active drone positions and yaw rotations (20Hz).
    /// Format: DRONES_STATE|id:x,y,z,rotY;id:x,y,z,rotY;...
    /// </summary>
    private void BroadcastDronesState()
    {
        // Auto-register any active drones on the host that may have spawned offline or mid-wave
        if (OasisShooterManager.Instance != null)
        {
            var active = OasisShooterManager.Instance.GetActiveDrones();
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    var d = active[i];
                    if (d != null && d.gameObject != null)
                    {
                        if (d.networkDroneId < 0) d.networkDroneId = OasisShooterManager.Instance.GetNextNetworkDroneId();
                        if (!_syncedDrones.ContainsKey(d.networkDroneId))
                        {
                            RegisterHostDrone(d);
                        }
                    }
                }
            }
        }

        if (_syncedDrones.Count == 0) return;

        StringBuilder sb = new StringBuilder();
        sb.Append("DRONES_STATE|");

        bool hasAny = false;
        List<int> toRemove = null;

        foreach (var kvp in _syncedDrones)
        {
            int dId = kvp.Key;
            TargetDrone drone = kvp.Value;
            if (drone == null || drone.gameObject == null)
            {
                if (toRemove == null) toRemove = new List<int>();
                toRemove.Add(dId);
                continue;
            }

            Vector3 p = drone.transform.position;
            float rY = drone.transform.eulerAngles.y;

            if (hasAny) sb.Append(";");
            sb.Append(dId).Append(":")
              .Append(p.x.ToString("F2", CultureInfo.InvariantCulture)).Append(",")
              .Append(p.y.ToString("F2", CultureInfo.InvariantCulture)).Append(",")
              .Append(p.z.ToString("F2", CultureInfo.InvariantCulture)).Append(",")
              .Append(rY.ToString("F1", CultureInfo.InvariantCulture));
            hasAny = true;
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++) _syncedDrones.Remove(toRemove[i]);
        }

        if (hasAny)
        {
            BroadcastToClients(sb.ToString());
        }
    }

    /// <summary>
    /// Broadcast drone plasma fire from Host to all connected clients.
    /// </summary>
    public void BroadcastDroneFire(int droneId, Vector3 origin, Vector3 dir)
    {
        if (currentRole != NetworkRole.Host) return;
        string packet = $"DRONE_FIRE|{droneId}|{origin.x.ToString("F2", CultureInfo.InvariantCulture)},{origin.y.ToString("F2", CultureInfo.InvariantCulture)},{origin.z.ToString("F2", CultureInfo.InvariantCulture)}|{dir.x.ToString("F2", CultureInfo.InvariantCulture)},{dir.y.ToString("F2", CultureInfo.InvariantCulture)},{dir.z.ToString("F2", CultureInfo.InvariantCulture)}";
        BroadcastToClients(packet);
    }

    /// <summary>
    /// Broadcast drone death from Host to all clients with killer ID attribution.
    /// </summary>
    public void BroadcastDroneDeath(int droneId, Vector3 explosionPos, int killerId = 0)
    {
        _syncedDrones.Remove(droneId);
        if (currentRole != NetworkRole.Host) return;
        string packet = $"DRONE_DIE|{droneId}|{explosionPos.x.ToString("F2", CultureInfo.InvariantCulture)},{explosionPos.y.ToString("F2", CultureInfo.InvariantCulture)},{explosionPos.z.ToString("F2", CultureInfo.InvariantCulture)}|{killerId}";
        BroadcastToClients(packet);
    }

    /// <summary>
    /// Client sends drone damage notification to the authoritative Host.
    /// </summary>
    public void SendDroneHitToServer(int droneId, float damage, Vector3 hitPoint, int attackerId = 0)
    {
        if (currentRole != NetworkRole.Client) return;
        string packet = $"DRONE_HIT|{droneId}|{damage.ToString("F1", CultureInfo.InvariantCulture)}|{hitPoint.x.ToString("F2", CultureInfo.InvariantCulture)},{hitPoint.y.ToString("F2", CultureInfo.InvariantCulture)},{hitPoint.z.ToString("F2", CultureInfo.InvariantCulture)}|{attackerId}";
        SendPacketToServer(packet);
    }

    /// <summary>
    /// Client spawns a puppet TargetDrone instance corresponding to the Host's drone.
    /// </summary>
    private void SpawnPuppetDrone(int droneId, Vector3 spawnPos, float moveSpd, float maxHp, bool hostile, EnemyType eType = EnemyType.ScoutDrone)
    {
        if (_syncedDrones.TryGetValue(droneId, out TargetDrone existing) && existing != null)
        {
            existing.transform.position = spawnPos;
            existing.networkTargetPosition = spawnPos;
            return;
        }

        GameObject droneObj = new GameObject($"CoOp_SyncedDrone_{droneId}");
        droneObj.transform.position = spawnPos;

        TargetDrone puppet = droneObj.AddComponent<TargetDrone>();
        puppet.enemyType = eType;
        puppet.networkDroneId = droneId;
        puppet.isNetworkPuppet = true;
        puppet.networkTargetPosition = spawnPos;
        puppet.networkTargetRotation = droneObj.transform.rotation;
        puppet.moveSpeed = moveSpd;
        puppet.maxHealth = maxHp;
        puppet.currentHealth = maxHp;
        puppet.isHostile = hostile;

        _syncedDrones[droneId] = puppet;

        // Register puppet drone with the shooter manager so HUD, radar, and active hostiles counter stay in sync
        OasisShooterManager.Instance?.RegisterClientPuppetDrone(puppet);
    }

    // ---------------------------------------------------------------------------------------------------------
    // REMOTE OPERATIVE INSTANTIATION & VISUALS
    // ---------------------------------------------------------------------------------------------------------

    public void PrepareRemotePlayersForBattle(int activeNumTeams)
    {
        foreach (var kvp in _remotePlayers)
        {
            var rp = kvp.Value;
            if (rp == null || rp.gameObject == null) continue;

            if (rp.isLocalPlaceholder)
            {
                // Local placeholders are spawned as active OasisCombatAI operatives in StartTeamBattle
                rp.gameObject.SetActive(false);
            }
            else
            {
                rp.gameObject.SetActive(true);
                ApplyOperativeSkinToRemotePlayer(rp);
                int clampedTeam = Mathf.Clamp(rp.teamId, 0, Mathf.Max(1, activeNumTeams - 1));
                if (OasisSpawnSystem.Instance != null && rp.transform != null)
                {
                    OasisSpawnSystem.Instance.RespawnCombatant(rp.transform, clampedTeam);
                    rp.targetPosition = rp.transform.position;
                    rp.targetRotation = rp.transform.rotation;
                }
            }
        }
    }

    private void SpawnRemotePlayer(int id, string playerName, int teamId = 0, int operativeIndex = 0, bool isPlaceholder = false)
    {
        if (_remotePlayers.TryGetValue(id, out var existing) && existing != null && existing.gameObject != null)
        {
            existing.name = playerName;
            bool teamOrOpChanged = (existing.teamId != teamId || existing.operativeIndex != operativeIndex);
            existing.teamId = teamId;
            existing.operativeIndex = operativeIndex;
            existing.isLocalPlaceholder = isPlaceholder;
            if (teamOrOpChanged)
            {
                ApplyOperativeSkinToRemotePlayer(existing);
            }
            return;
        }

        int prevScore = 0;
        int prevKills = 0;
        if (existing != null)
        {
            prevScore = existing.score;
            prevKills = existing.kills;
        }

        GameObject operative = new GameObject($"CoOp_Operative_{id}_{playerName}");
        Vector3 basePos = _localPlayerTransform != null 
            ? _localPlayerTransform.position 
            : (OasisShooterManager.Instance != null ? OasisShooterManager.Instance.SafeShelterCenter : Vector3.zero);
        Vector3 forward = _localPlayerTransform != null ? _localPlayerTransform.forward : Vector3.forward;
        Vector3 right = _localPlayerTransform != null ? _localPlayerTransform.right : Vector3.right;
        
        // Spawn 2.2m ahead and offset horizontally in front of local player, facing back towards them
        operative.transform.position = basePos + (forward * 2.2f) + (right * ((id % 3) * 1.2f - 0.6f));
        operative.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);

        RemotePlayerInstance inst = new RemotePlayerInstance
        {
            id = id,
            name = playerName,
            gameObject = operative,
            transform = operative.transform,
            targetPosition = operative.transform.position,
            targetRotation = operative.transform.rotation,
            prevPosition = operative.transform.position,
            score = prevScore,
            kills = prevKills,
            teamId = teamId,
            operativeIndex = operativeIndex,
            isLocalPlaceholder = isPlaceholder
        };

        // Add CapsuleCollider so weapons and lasers register hits on remote operatives
        CapsuleCollider col = operative.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.9f, 0);
        col.radius = 0.45f;
        col.height = 1.8f;

        // Build a sleek Sci-Fi FPS Operative Player Model
        BuildOperativeModel(operative, id, inst);
        ApplyOperativeSkinToRemotePlayer(inst);

        // Add 3D World Name Tag
        GameObject nameTagObj = new GameObject("NameTag");
        nameTagObj.transform.SetParent(operative.transform, false);
        nameTagObj.transform.localPosition = new Vector3(0, 2.85f, 0);

        Canvas canvas = nameTagObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = nameTagObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220, 68);
        rt.localScale = Vector3.one * 0.01f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        GameObject textObj = new GameObject("NameText");
        textObj.transform.SetParent(nameTagObj.transform, false);
        RectTransform ntRt = textObj.AddComponent<RectTransform>();
        ntRt.anchorMin = Vector2.zero;
        ntRt.anchorMax = Vector2.one;
        ntRt.offsetMin = Vector2.zero;
        ntRt.offsetMax = Vector2.zero;

        Text nameText = textObj.AddComponent<Text>();
        nameText.font = font;
        nameText.text = $"{playerName} [OP-{id}]";
        nameText.fontSize = 18;
        nameText.fontStyle = FontStyle.Bold;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.color = OasisShooterManager.GetTeamColor(teamId);
        Outline ntOutline = textObj.AddComponent<Outline>();
        ntOutline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        ntOutline.effectDistance = new Vector2(1.5f, -1.5f);

        nameTagObj.AddComponent<FaceCameraBillboard>();
        inst.nameTagText = nameText;

        if (isPlaceholder)
        {
            operative.SetActive(false);
        }

        _remotePlayers[id] = inst;
    }

    private void BuildOperativeModel(GameObject root, int id, RemotePlayerInstance inst)
    {
        // 1. Try loading high-fidelity Robot Hero asset model
        GameObject robotPrefab = Resources.Load<GameObject>("RobotHero");
#if UNITY_EDITOR
        if (robotPrefab == null)
        {
            robotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab");
        }
#endif
        if (robotPrefab != null)
        {
            GameObject robotObj = Instantiate(robotPrefab, root.transform);
            robotObj.name = "RobotHeroModel";
            robotObj.transform.localPosition = Vector3.zero;
            robotObj.transform.localRotation = Quaternion.identity;
            robotObj.transform.localScale = Vector3.one;

            // Remove any ragdoll/physics colliders on child bones
            Collider[] cols = robotObj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);

            // Assign URP Lit Material
            Material urpMat = Resources.Load<Material>("RobotHero_URP");
#if UNITY_EDITOR
            if (urpMat == null) urpMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/SciFiWarriorPBRHPPolyart/Materials/RobotHero_URP.mat");
#endif
            if (urpMat != null)
            {
                Renderer[] rends = robotObj.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++) rends[i].sharedMaterial = urpMat;
            }

            // Assign RobotHero / SciFiWarrior Animator Controller
            RuntimeAnimatorController animCtrl = Resources.Load<RuntimeAnimatorController>("RobotHero");
            if (animCtrl == null) animCtrl = Resources.Load<RuntimeAnimatorController>("SciFiWarrior");
#if UNITY_EDITOR
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/RobotHero.controller");
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/SciFiWarriorPBRHPPolyart/Animators/SciFiWarrior.controller");
#endif
            Animator anim = robotObj.GetComponent<Animator>();
            if (anim == null) anim = robotObj.AddComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;
                if (animCtrl != null) anim.runtimeAnimatorController = animCtrl;
            }

            inst.animator = anim;
            inst.visualRoot = robotObj.transform;

            // Cache upper-body bone & gun arm bone for pitch aim sync
            inst.spineBone = null;
            inst.gunArmBone = null;
            var allBones = robotObj.GetComponentsInChildren<Transform>(true);
            foreach (var b in allBones)
            {
                if (b.name == "ArmPosition_Right") inst.gunArmBone = b;
                if (b.name == "Chest" && inst.spineBone == null) inst.spineBone = b;
                if (b.name == "Spine" && inst.spineBone == null) inst.spineBone = b;
            }

            if (inst.spineBone == null && anim != null && anim.isHuman)
            {
                inst.spineBone = anim.GetBoneTransform(HumanBodyBones.Chest) 
                              ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            }

            ApplyOperativeSkinToRemotePlayer(inst);
            return;
        }

        // 2. Procedural fallback if asset is missing
        Shader litShader = OasisShaderHelper.GetLitShader();
        Shader unlitShader = OasisShaderHelper.GetUnlitShader();

        Color armorColor = GetColorForPlayer(id);

        // 1. Primary Armor Plating Material
        Material armorMat = new Material(litShader);
        armorMat.color = armorColor;
        if (armorMat.HasProperty("_Metallic")) armorMat.SetFloat("_Metallic", 0.85f);
        if (armorMat.HasProperty("_Smoothness")) armorMat.SetFloat("_Smoothness", 0.75f);

        // 2. Tactical Undersuit / Dark Gunmetal Carbon Material
        Material darkMat = new Material(litShader);
        darkMat.color = new Color(0.12f, 0.13f, 0.16f);
        if (darkMat.HasProperty("_Metallic")) darkMat.SetFloat("_Metallic", 0.4f);
        if (darkMat.HasProperty("_Smoothness")) darkMat.SetFloat("_Smoothness", 0.5f);

        // 3. Gold / Cyan Glowing Visor Material
        Material visorMat = new Material(unlitShader);
        Color visorColor = (id == 0) ? new Color(0.1f, 0.95f, 1f) : new Color(1f, 0.8f, 0.2f);
        visorMat.color = visorColor;
        if (visorMat.HasProperty("_EmissionColor"))
        {
            visorMat.EnableKeyword("_EMISSION");
            visorMat.SetColor("_EmissionColor", visorColor * 2.5f);
        }

        // 4. Thruster Flame Material
        Material flameMat = new Material(unlitShader);
        flameMat.color = new Color(0.2f, 0.85f, 1f);

        // Visual Rig container (authentic 1.0x human scale)
        GameObject rig = new GameObject("OperativeRig");
        rig.transform.SetParent(root.transform, false);
        rig.transform.localScale = Vector3.one;
        inst.visualRoot = rig.transform;

        // ------------------ TORSO & CHESTPIECE ------------------
        // Inner Bodysuit Core
        GameObject innerTorso = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        innerTorso.name = "UndersuitTorso";
        innerTorso.transform.SetParent(rig.transform, false);
        innerTorso.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        innerTorso.transform.localScale = new Vector3(0.48f, 0.45f, 0.35f);
        Destroy(innerTorso.GetComponent<Collider>());
        innerTorso.GetComponent<MeshRenderer>().material = darkMat;

        // Heavy Chiseled Chest Armor Plate
        GameObject chestPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chestPlate.name = "ChestArmorPlate";
        chestPlate.transform.SetParent(rig.transform, false);
        chestPlate.transform.localPosition = new Vector3(0f, 1.25f, 0.08f);
        chestPlate.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        chestPlate.transform.localScale = new Vector3(0.56f, 0.42f, 0.26f);
        Destroy(chestPlate.GetComponent<Collider>());
        chestPlate.GetComponent<MeshRenderer>().material = armorMat;

        // Abdominal Armor Segment
        GameObject abPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        abPlate.name = "AbArmorPlate";
        abPlate.transform.SetParent(rig.transform, false);
        abPlate.transform.localPosition = new Vector3(0f, 0.96f, 0.04f);
        abPlate.transform.localScale = new Vector3(0.46f, 0.22f, 0.24f);
        Destroy(abPlate.GetComponent<Collider>());
        abPlate.GetComponent<MeshRenderer>().material = darkMat;

        // Tactical Belt
        GameObject belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        belt.name = "TacticalBelt";
        belt.transform.SetParent(rig.transform, false);
        belt.transform.localPosition = new Vector3(0f, 0.82f, 0f);
        belt.transform.localScale = new Vector3(0.52f, 0.1f, 0.32f);
        Destroy(belt.GetComponent<Collider>());
        belt.GetComponent<MeshRenderer>().material = darkMat;

        // ------------------ BACKPACK & JETPACK THRUSTERS ------------------
        GameObject jetpack = GameObject.CreatePrimitive(PrimitiveType.Cube);
        jetpack.name = "TacticalJetpack";
        jetpack.transform.SetParent(rig.transform, false);
        jetpack.transform.localPosition = new Vector3(0f, 1.22f, -0.22f);
        jetpack.transform.localScale = new Vector3(0.38f, 0.45f, 0.16f);
        Destroy(jetpack.GetComponent<Collider>());
        jetpack.GetComponent<MeshRenderer>().material = darkMat;

        // Dual Thruster Nozzles
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject thruster = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            thruster.name = $"Thruster_{i}";
            thruster.transform.SetParent(jetpack.transform, false);
            thruster.transform.localPosition = new Vector3(i * 0.35f, -0.4f, 0f);
            thruster.transform.localScale = new Vector3(0.28f, 0.35f, 0.28f);
            Destroy(thruster.GetComponent<Collider>());
            thruster.GetComponent<MeshRenderer>().material = armorMat;

            // Thruster glow exhaust
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ThrusterGlow";
            glow.transform.SetParent(thruster.transform, false);
            glow.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            glow.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            Destroy(glow.GetComponent<Collider>());
            glow.GetComponent<MeshRenderer>().material = flameMat;
        }

        // ------------------ COMBAT HELMET ------------------
        GameObject headRoot = new GameObject("HeadRig");
        headRoot.transform.SetParent(rig.transform, false);
        headRoot.transform.localPosition = new Vector3(0f, 1.68f, 0f);

        // Spherical Helmet Base
        GameObject helmetBase = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        helmetBase.name = "HelmetDome";
        helmetBase.transform.SetParent(headRoot.transform, false);
        helmetBase.transform.localScale = new Vector3(0.42f, 0.44f, 0.44f);
        Destroy(helmetBase.GetComponent<Collider>());
        helmetBase.GetComponent<MeshRenderer>().material = armorMat;

        // Armored Jaw / Respirator
        GameObject jawPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        jawPlate.name = "JawRespirator";
        jawPlate.transform.SetParent(headRoot.transform, false);
        jawPlate.transform.localPosition = new Vector3(0f, -0.1f, 0.12f);
        jawPlate.transform.localScale = new Vector3(0.32f, 0.18f, 0.22f);
        Destroy(jawPlate.GetComponent<Collider>());
        jawPlate.GetComponent<MeshRenderer>().material = darkMat;

        // Sleek Angled Visor
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "OpticVisor";
        visor.transform.SetParent(headRoot.transform, false);
        visor.transform.localPosition = new Vector3(0f, 0.04f, 0.18f);
        visor.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        visor.transform.localScale = new Vector3(0.34f, 0.12f, 0.12f);
        Destroy(visor.GetComponent<Collider>());
        visor.GetComponent<MeshRenderer>().material = visorMat;

        // Helmet Comms Antenna
        GameObject antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        antenna.name = "CommsAntenna";
        antenna.transform.SetParent(headRoot.transform, false);
        antenna.transform.localPosition = new Vector3(0.18f, 0.18f, -0.06f);
        antenna.transform.localRotation = Quaternion.Euler(15f, 0f, -12f);
        antenna.transform.localScale = new Vector3(0.02f, 0.15f, 0.02f);
        Destroy(antenna.GetComponent<Collider>());
        antenna.GetComponent<MeshRenderer>().material = darkMat;

        // ------------------ SHOULDERS & ARMS ------------------
        for (int side = -1; side <= 1; side += 2)
        {
            // Shoulder Pauldron
            GameObject pauldron = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pauldron.name = side == -1 ? "LeftPauldron" : "RightPauldron";
            pauldron.transform.SetParent(rig.transform, false);
            pauldron.transform.localPosition = new Vector3(side * 0.38f, 1.38f, 0f);
            pauldron.transform.localScale = new Vector3(0.24f, 0.22f, 0.24f);
            Destroy(pauldron.GetComponent<Collider>());
            pauldron.GetComponent<MeshRenderer>().material = armorMat;

            // Arm Rig Pivot (at shoulder joint)
            GameObject armRig = new GameObject(side == -1 ? "LeftArmRig" : "RightArmRig");
            armRig.transform.SetParent(rig.transform, false);
            armRig.transform.localPosition = new Vector3(side * 0.36f, 1.32f, 0f);

            // Bicep / Upper Arm
            GameObject bicep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bicep.name = "UpperArm";
            bicep.transform.SetParent(armRig.transform, false);
            bicep.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            bicep.transform.localScale = new Vector3(0.14f, 0.2f, 0.14f);
            Destroy(bicep.GetComponent<Collider>());
            bicep.GetComponent<MeshRenderer>().material = darkMat;

            // Forearm & Gauntlet
            GameObject forearm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            forearm.name = "Gauntlet";
            forearm.transform.SetParent(armRig.transform, false);
            forearm.transform.localPosition = new Vector3(0f, -0.46f, (side == 1) ? 0.12f : 0.04f);
            forearm.transform.localScale = new Vector3(0.13f, 0.22f, 0.14f);
            Destroy(forearm.GetComponent<Collider>());
            forearm.GetComponent<MeshRenderer>().material = armorMat;

            if (side == -1) inst.leftArm = armRig.transform;
            else inst.rightArm = armRig.transform;
        }

        // ------------------ SCI-FI BLASTER RIFLE ------------------
        GameObject rifle = new GameObject("BlasterRifleRoot");
        rifle.transform.SetParent(inst.rightArm != null ? inst.rightArm : rig.transform, false);
        rifle.transform.localPosition = new Vector3(0.04f, -0.46f, 0.35f);
        rifle.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);

        // Main Receiver & Barrel
        GameObject rifleBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rifleBody.name = "RifleReceiver";
        rifleBody.transform.SetParent(rifle.transform, false);
        rifleBody.transform.localScale = new Vector3(0.09f, 0.15f, 0.65f);
        Destroy(rifleBody.GetComponent<Collider>());
        rifleBody.GetComponent<MeshRenderer>().material = darkMat;

        // Colored Top Shroud
        GameObject rifleShroud = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rifleShroud.name = "RifleShroud";
        rifleShroud.transform.SetParent(rifle.transform, false);
        rifleShroud.transform.localPosition = new Vector3(0f, 0.08f, -0.05f);
        rifleShroud.transform.localScale = new Vector3(0.07f, 0.06f, 0.45f);
        Destroy(rifleShroud.GetComponent<Collider>());
        rifleShroud.GetComponent<MeshRenderer>().material = armorMat;

        // Glowing Holographic Weapon Sight
        GameObject sight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sight.name = "HoloSight";
        sight.transform.SetParent(rifle.transform, false);
        sight.transform.localPosition = new Vector3(0f, 0.13f, 0.05f);
        sight.transform.localScale = new Vector3(0.04f, 0.06f, 0.08f);
        Destroy(sight.GetComponent<Collider>());
        sight.GetComponent<MeshRenderer>().material = visorMat;

        // Gun Barrel Tip
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Muzzle";
        barrel.transform.SetParent(rifle.transform, false);
        barrel.transform.localPosition = new Vector3(0f, 0.02f, 0.38f);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        barrel.transform.localScale = new Vector3(0.05f, 0.12f, 0.05f);
        Destroy(barrel.GetComponent<Collider>());
        barrel.GetComponent<MeshRenderer>().material = darkMat;

        // ------------------ LEGS & COMBAT BOOTS ------------------
        for (int side = -1; side <= 1; side += 2)
        {
            // Leg Pivot (Hip Joint)
            GameObject legRig = new GameObject(side == -1 ? "LeftLegRig" : "RightLegRig");
            legRig.transform.SetParent(rig.transform, false);
            legRig.transform.localPosition = new Vector3(side * 0.17f, 0.8f, 0f);

            // Thigh (Armored)
            GameObject thigh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thigh.name = "ThighPlate";
            thigh.transform.SetParent(legRig.transform, false);
            thigh.transform.localPosition = new Vector3(0f, -0.22f, 0.02f);
            thigh.transform.localScale = new Vector3(0.18f, 0.34f, 0.2f);
            Destroy(thigh.GetComponent<Collider>());
            thigh.GetComponent<MeshRenderer>().material = armorMat;

            // Knee Pad
            GameObject knee = GameObject.CreatePrimitive(PrimitiveType.Cube);
            knee.name = "KneePad";
            knee.transform.SetParent(legRig.transform, false);
            knee.transform.localPosition = new Vector3(0f, -0.38f, 0.12f);
            knee.transform.localScale = new Vector3(0.16f, 0.12f, 0.12f);
            Destroy(knee.GetComponent<Collider>());
            knee.GetComponent<MeshRenderer>().material = darkMat;

            // Shin & Combat Boot
            GameObject shin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shin.name = "CombatBoot";
            shin.transform.SetParent(legRig.transform, false);
            shin.transform.localPosition = new Vector3(0f, -0.62f, 0.04f);
            shin.transform.localScale = new Vector3(0.19f, 0.34f, 0.26f);
            Destroy(shin.GetComponent<Collider>());
            shin.GetComponent<MeshRenderer>().material = darkMat;

            if (side == -1) inst.leftLeg = legRig.transform;
            else inst.rightLeg = legRig.transform;
        }
    }

    private Color GetColorForPlayer(int id)
    {
        switch (id % 4)
        {
            case 0: return new Color(0.2f, 0.5f, 0.95f); // Neon Blue (Host)
            case 1: return new Color(0.95f, 0.4f, 0.15f); // Orange (P2)
            case 2: return new Color(0.2f, 0.85f, 0.45f); // Green (P3)
            case 3: return new Color(0.85f, 0.25f, 0.85f); // Magenta (P4)
            default: return Color.cyan;
        }
    }

    private void UpdateRemotePlayerState(int id, string[] parts)
    {
        if (!_remotePlayers.TryGetValue(id, out RemotePlayerInstance rp) || rp == null || rp.gameObject == null)
        {
            SpawnRemotePlayer(id, $"Operative_{id}");
            _remotePlayers.TryGetValue(id, out rp);
            if (rp == null) return;
        }

        // Position
        string[] posParts = parts[2].Split(',');
        rp.targetPosition = new Vector3(
            float.Parse(posParts[0], CultureInfo.InvariantCulture),
            float.Parse(posParts[1], CultureInfo.InvariantCulture),
            float.Parse(posParts[2], CultureInfo.InvariantCulture));

        // Rotation
        float rotY = float.Parse(parts[3], CultureInfo.InvariantCulture);
        rp.targetRotation = Quaternion.Euler(0, rotY, 0);

        // Crouch State
        if (parts.Length > 4)
        {
            rp.isCrouching = parts[4] == "1";
        }

        // Aim Pitch
        if (parts.Length > 5 && float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float p))
        {
            rp.targetPitch = p;
        }

        // Downed State
        if (parts.Length > 6)
        {
            bool newDowned = parts[6] == "1";
            if (!rp.isDowned && newDowned)
            {
                rp.downedTimer = 8.0f;
                rp.reviveProgress = 0f;
            }
            rp.isDowned = newDowned;
        }

        // Team & Operative Index Sync
        bool skinDirty = false;
        if (parts.Length > 7 && int.TryParse(parts[7], out int newTeam))
        {
            if (rp.teamId != newTeam)
            {
                rp.teamId = newTeam;
                skinDirty = true;
            }
        }

        if (parts.Length > 8 && int.TryParse(parts[8], out int newOp))
        {
            if (rp.operativeIndex != newOp)
            {
                rp.operativeIndex = newOp;
                skinDirty = true;
            }
        }

        if (skinDirty)
        {
            ApplyOperativeSkinToRemotePlayer(rp);
        }
    }

    private void TriggerRemoteLaser(string[] parts)
    {
        int senderId = int.Parse(parts[1]);
        if (_remotePlayers.TryGetValue(senderId, out RemotePlayerInstance rp) && rp.animator != null)
        {
            rp.animator.Play("Shoot_Autoshot_AR", 0, 0f);
        }

        string[] origParts = parts[2].Split(',');
        string[] hitParts = parts[3].Split(',');
        bool isHit = parts.Length > 4 && parts[4] == "1";

        Vector3 start = new Vector3(
            float.Parse(origParts[0], CultureInfo.InvariantCulture),
            float.Parse(origParts[1], CultureInfo.InvariantCulture),
            float.Parse(origParts[2], CultureInfo.InvariantCulture));
        Vector3 end = new Vector3(
            float.Parse(hitParts[0], CultureInfo.InvariantCulture),
            float.Parse(hitParts[1], CultureInfo.InvariantCulture),
            float.Parse(hitParts[2], CultureInfo.InvariantCulture));

        if (_remotePlayers.TryGetValue(senderId, out RemotePlayerInstance remotePlayer) && remotePlayer.gunArmBone != null)
        {
            // Align start position with the remote robot's rifle barrel tip
            start = remotePlayer.gunArmBone.position + remotePlayer.gunArmBone.forward * 0.65f + remotePlayer.gunArmBone.up * 0.05f;
        }

        BlasterVFX.SpawnLaserBeam(start, end, new Color(0.2f, 0.9f, 1f));
        BlasterVFX.SpawnImpact(end, Vector3.up, isHit);
    }

    private void RemoveRemotePlayer(int id)
    {
        if (_remotePlayers.TryGetValue(id, out RemotePlayerInstance rp))
        {
            if (rp.gameObject != null) Destroy(rp.gameObject);
            _remotePlayers.Remove(id);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // LOBBY UI
    // ---------------------------------------------------------------------------------------------------------

    private void BuildLobbyUI()
    {
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Do not build in-game overlay canvas inside Main Menu scene (menu has its own dedicated UI)
            return;
        }

        _lobbyCanvas = new GameObject("OasisCoOpCanvas");
        Canvas c = _lobbyCanvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 95;

        CanvasScaler cs = _lobbyCanvas.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        _lobbyCanvas.AddComponent<GraphicRaycaster>();

        // In-game match UI: Only the live match scoreboard is built here (toggled with TAB)
        BuildScoreboardUI();
    }

    private void BuildScoreboardUI()
    {
        _scoreboardPanel = new GameObject("CoOpScoreboardPanel");
        _scoreboardPanel.transform.SetParent(_lobbyCanvas.transform, false);
        RectTransform rt = _scoreboardPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(660, 380);

        Image bg = _scoreboardPanel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.06f, 0.1f, 0.96f);

        // Header Title
        CreateLabel(_scoreboardPanel.transform, "OPERATIVE MISSION SCOREBOARD", 20, new Vector2(0, 150), new Vector2(620, 36), FontStyle.Bold, new Color(0.2f, 0.9f, 1f));
        CreateLabel(_scoreboardPanel.transform, "[HOLD TAB TO VIEW LIVE MATCH STATS]", 11, new Vector2(0, 125), new Vector2(620, 20), FontStyle.Normal, new Color(0.6f, 0.75f, 0.9f));

        // Column Titles Bar
        GameObject headerObj = new GameObject("HeaderRow");
        headerObj.transform.SetParent(_scoreboardPanel.transform, false);
        RectTransform hRt = headerObj.AddComponent<RectTransform>();
        hRt.anchoredPosition = new Vector2(0, 90);
        hRt.sizeDelta = new Vector2(600, 34);
        Image hBg = headerObj.AddComponent<Image>();
        hBg.color = new Color(0.12f, 0.18f, 0.28f, 0.95f);

        // Fixed Column Anchors:
        // Rank: X = -245
        // Operative: X = -80 (Left-aligned)
        // Score: X = 135
        // Kills: X = 235
        CreateLabel(headerObj.transform, "RANK", 13, new Vector2(-245, 0), new Vector2(70, 30), FontStyle.Bold, Color.white);
        Text opHeader = CreateLabel(headerObj.transform, "OPERATIVE", 13, new Vector2(-80, 0), new Vector2(240, 30), FontStyle.Bold, Color.white);
        opHeader.alignment = TextAnchor.MiddleLeft;

        CreateLabel(headerObj.transform, "SCORE", 13, new Vector2(135, 0), new Vector2(110, 30), FontStyle.Bold, new Color(1f, 0.85f, 0.25f));
        CreateLabel(headerObj.transform, "KILLS", 13, new Vector2(235, 0), new Vector2(80, 30), FontStyle.Bold, new Color(1f, 0.4f, 0.3f));

        // Rows Container
        GameObject rowsContainer = new GameObject("ScoreboardRows");
        rowsContainer.transform.SetParent(_scoreboardPanel.transform, false);
        RectTransform rRt = rowsContainer.AddComponent<RectTransform>();
        rRt.anchoredPosition = new Vector2(0, 55);
        rRt.sizeDelta = new Vector2(600, 240);
        _scoreboardRowsContainer = rowsContainer.transform;

        _scoreboardPanel.SetActive(false);
    }

    public void RefreshScoreboardUI()
    {
        if (_scoreboardRowsContainer == null) return;

        // Clean up previous rows
        for (int i = 0; i < _scoreboardRowObjects.Count; i++)
        {
            if (_scoreboardRowObjects[i] != null) Destroy(_scoreboardRowObjects[i]);
        }
        _scoreboardRowObjects.Clear();

        var entries = GetScoreboardData();

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];

            GameObject rowObj = new GameObject($"Row_{i}");
            rowObj.transform.SetParent(_scoreboardRowsContainer, false);
            RectTransform rowRt = rowObj.AddComponent<RectTransform>();
            rowRt.anchoredPosition = new Vector2(0, -i * 44f);
            rowRt.sizeDelta = new Vector2(600, 38);

            Image rowBg = rowObj.AddComponent<Image>();
            // Alternating row tints and highlight for local player
            if (e.isLocal)
            {
                rowBg.color = new Color(0.1f, 0.25f, 0.45f, 0.55f);
            }
            else
            {
                rowBg.color = (i % 2 == 0) ? new Color(0.08f, 0.1f, 0.15f, 0.4f) : new Color(0.05f, 0.07f, 0.1f, 0.25f);
            }

            // 1. Rank Column
            Text rankText = CreateLabel(rowObj.transform, $"#{i + 1}", 15, new Vector2(-245, 0), new Vector2(70, 34), FontStyle.Bold, Color.white);

            // 2. Operative Name Column (Left Aligned)
            string nameStr = e.isLocal ? $"{e.playerName} (You)" : e.playerName;
            Text nameText = CreateLabel(rowObj.transform, nameStr, 15, new Vector2(-80, 0), new Vector2(240, 34), FontStyle.Bold, e.playerColor);
            nameText.alignment = TextAnchor.MiddleLeft;

            // 3. Score Column
            CreateLabel(rowObj.transform, $"{e.score} pts", 15, new Vector2(135, 0), new Vector2(110, 34), FontStyle.Bold, new Color(1f, 0.85f, 0.25f));

            // 4. Kills Column
            CreateLabel(rowObj.transform, $"{e.kills}", 15, new Vector2(235, 0), new Vector2(80, 34), FontStyle.Bold, new Color(1f, 0.4f, 0.3f));

            _scoreboardRowObjects.Add(rowObj);
        }
    }

    public void OpenScoreboard()
    {
        _isScoreboardToggled = true;
        if (_scoreboardPanel != null)
        {
            _scoreboardPanel.SetActive(true);
            RefreshScoreboardUI();
        }
    }

    public void CloseScoreboard()
    {
        _isScoreboardToggled = false;
        if (_scoreboardPanel != null)
        {
            _scoreboardPanel.SetActive(false);
        }
    }

    private void UpdateStatus(string status)
    {
        if (_statusText != null) _statusText.text = status;
    }

    public void HostLaunchMission()
    {
        if (currentRole == NetworkRole.Host)
        {
            StartCoroutine(PerformHostLaunchRoutine());
        }
    }

    private IEnumerator PerformHostLaunchRoutine()
    {
        // Broadcast launch command to all connected operatives
        BroadcastToClients("LAUNCH_GAME");
        if (isUsingRelay && _relayDriver.IsCreated)
        {
            _relayDriver.ScheduleUpdate().Complete();
        }

        // Brief delay and re-broadcast to ensure UDP packet delivery across the internet
        yield return new WaitForSeconds(0.15f);
        BroadcastToClients("LAUNCH_GAME");
        if (isUsingRelay && _relayDriver.IsCreated)
        {
            _relayDriver.ScheduleUpdate().Complete();
        }

        yield return new WaitForSeconds(0.1f);

        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("OasisScene");
        }

        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isWaitingForOperatives)
        {
            OasisShooterManager.Instance.StartWave(1);
        }

        // Close lobby UI and lock cursor to jump straight into combat
        if (_lobbyPanel != null) _lobbyPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        OasisShooterManager.Instance?.ShowNotificationBanner("MISSION LAUNCHED! ENGAGE HOSTILES!", 3.5f);
    }

    private void UpdatePlayerListUI()
    {
        if (_playerListText == null) return;
        StringBuilder sb = new StringBuilder();
        int total = 1 + _remotePlayers.Count;
        sb.AppendLine($"OPERATIVES ({total}/4):");
        sb.AppendLine($"- {myPlayerName} (You) [OP-{myPlayerId}]");

        foreach (var rp in _remotePlayers)
        {
            sb.AppendLine($"- {rp.Value.name} [OP-{rp.Key}]");
        }

        _playerListText.text = sb.ToString();
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private Text CreateLabel(Transform parent, string text, int fontSize, Vector2 pos, Vector2 size, FontStyle style, Color color)
    {
        GameObject obj = new GameObject("Text");
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text txt = obj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        return txt;
    }

    private Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Button");
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = obj.AddComponent<Image>();
        img.color = color;

        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(action);

        CreateLabel(obj.transform, label, 13, Vector2.zero, size, FontStyle.Bold, Color.white);
        return btn;
    }
}

/// <summary>
/// Ensures player name tags always orient cleanly towards the local camera.
/// </summary>
public class FaceCameraBillboard : MonoBehaviour
{
    private Camera _cam;

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main ?? FindAnyObjectByType<Camera>();
        if (_cam != null)
        {
            transform.rotation = _cam.transform.rotation;

            // Distance scaling for multiplayer player overhead name tags
            float dist = Vector3.Distance(transform.position, _cam.transform.position);
            float scale = Mathf.Clamp(dist * 0.00085f, 0.010f, 0.028f);
            transform.localScale = Vector3.one * scale;
        }
    }
}
