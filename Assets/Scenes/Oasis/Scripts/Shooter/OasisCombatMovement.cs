using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Combat Movement System for the Oasis FPS Shooter:
/// 1. Crouch / Slide (C or Left Ctrl):
///    - Dynamically reduces CharacterController height and eye level.
///    - Decreases profile to take cover behind dunes and rocks.
///    - If crouching while sprinting, performs a high-speed momentum power slide!
/// 2. Combat Jump (Space):
///    - Snappy vertical thrust with procedural launch sound.
/// 3. Combat Dash / Dodge Thruster (Left Shift or Double-Tap Direction):
///    - Rapid horizontal burst to evade incoming plasma projectiles with cooldown recovery.
/// </summary>
public class OasisCombatMovement : MonoBehaviour
{
    public static OasisCombatMovement Instance { get; private set; }

    [Header("Crouch & Slide Settings")]
    public float standingHeight = 1.80f;
    public float crouchHeight = 1.00f;
    public float standingCameraY = 1.60f;
    public float crouchCameraY = 0.85f;
    public float crouchSpeedMultiplier = 0.55f;
    public float slideSpeedMultiplier = 1.7f;
    public float slideDuration = 0.85f;

    [Header("Dash / Evade Settings")]
    public float dashForce = 16f;
    public float dashDuration = 0.22f;
    public float dashCooldown = 1.2f;

    [Header("Obstacle Auto-Vault & Stuck Jump Settings")]
    [Tooltip("If enabled, automatically assists jumping/vaulting when caught on or running into low obstacles (rocks, dunes, steps).")]
    public bool enableObstacleJump = true;
    public float obstacleVaultForwardBoost = 4.0f;
    private float _playerStuckTimer = 0f;
    private Vector3 _playerLastSampledPos;

    [Header("Robot Hero Character Model")]
    public GameObject robotHeroVisual;
    public Animator robotHeroAnimator;
    public bool isThirdPerson = false;
    public bool isAimingThirdPerson = false;
    public bool isHoldingLeftClickAim = false;

    // State
    public bool isCrouching { get; private set; }
    public bool isSliding { get; private set; }
    public bool isDashing { get; private set; }
    public float dashCooldownRemaining { get; private set; }

    [Header("Special Ability Settings")]
    public int currentOperativeIndex = 0;
    public string abilityName = "TITANIUM SLAM";
    public string abilityDescription = "Kinetic shockwave (45 dmg) + 60 Temporary Overshield";
    public float abilityCooldown = 14f;
    public float abilityCooldownRemaining { get; private set; } = 0f;
    public float abilityActiveDuration = 0f;
    public float abilityActiveRemaining { get; private set; } = 0f;
    public bool isAbilityActive => abilityActiveRemaining > 0f;
    public float cloakTimeRemaining = 0f;
    public float scanReconTimeRemaining = 0f;
    public float medicFieldTimeRemaining = 0f;
    public float aegisTimeRemaining = 0f;
    public float empOverdriveTimeRemaining = 0f;
    public float deadshotTimeRemaining = 0f;
    public float classMobilityMultiplier = 1.0f;
    public Vector3 medicFieldPosition;

    private CharacterController _characterController;
    private StarterAssets.FirstPersonController _fpc;
    private StarterAssets.StarterAssetsInputs _inputs;
    private Transform _cameraTarget;
    private AudioSource _audioSource;
    private Transform _spineBone;
    private Transform _gunArmBone;
    private Transform _gunHandBone;
    public Transform gunArmBone => _gunArmBone;

    [Header("Third Person Dynamic Camera & Zoom")]
    [System.NonSerialized] public float minCameraDistance = 1.15f;
    [System.NonSerialized] public float maxCameraDistance = 4.5f;
    [System.NonSerialized] public float defaultCameraDistance = 2.35f;
    [System.NonSerialized] public float currentCameraDistance = 2.35f;
    [System.NonSerialized] public float cameraZoomSpeed = 1.35f;
    private float _targetCameraDistance = 2.35f;

    public Vector3 GetThirdPersonMuzzlePosition(Vector3 targetPoint)
    {
        Vector3 gripPos;
        if (_gunArmBone != null)
        {
            gripPos = _gunArmBone.position;
        }
        else if (_gunHandBone != null)
        {
            gripPos = _gunHandBone.position;
        }
        else
        {
            gripPos = transform.position + transform.right * 0.38f + transform.up * 1.35f;
        }

        Vector3 aimDir = (targetPoint - gripPos).normalized;
        if (aimDir.sqrMagnitude < 0.001f) aimDir = transform.forward;

        // The rifle barrel extends ~0.72m forward along the firing axis towards the aim target
        return gripPos + aimDir * 0.72f;
    }

    public Vector3 GetThirdPersonMuzzlePosition()
    {
        Camera cam = Camera.main;
        Vector3 targetPoint = cam != null ? cam.transform.position + cam.transform.forward * 50f : transform.position + transform.forward * 50f;
        return GetThirdPersonMuzzlePosition(targetPoint);
    }

    [Header("Speed Settings")]
    public float baseRunSpeed = 7.0f;
    public float baseSprintSpeed = 10.0f;

    private float _currentHeight;
    private float _currentCameraY;
    private Vector3 _initialCenter;
    private Vector3 _dashDirection;
    private float _dashTimeRemaining;
    private Vector3 _slideDirection;
    private float _slideTimeRemaining;
    private float _origMoveSpeed = 7.0f;
    private float _origSprintSpeed = 10.0f;
    private bool _wasDowned = false;

    // Weapon Animation, Recoil IK & Close-Quarters Melee State
    private float _shootAnimTimer = 0f;
    private string _shootAnimClip = "Shoot_Autoshot_AR";
    private float _reloadAnimTimer = 0f;
    private float _reloadTotalDuration = 1.1f;
    private float _tpRecoilPitch = 0f;
    private float _tpRecoilYaw = 0f;
    private float _meleeCooldownTimer = 0f;
    private float _meleeAnimTimer = 0f;
    private bool _isCritHitmarker = false;
    private float _reticleBloom = 0f;

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
        _characterController = GetComponent<CharacterController>();
        _fpc = GetComponent<StarterAssets.FirstPersonController>();
        _inputs = GetComponent<StarterAssets.StarterAssetsInputs>();

        // Ensure player is positioned inside the tactical canyon map rather than outside in the desert
        if (transform.position.z > -50f)
        {
            if (_characterController != null) _characterController.enabled = false;
            transform.position = new Vector3(0f, -1.8f, -124f);
            transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            if (_fpc != null) _fpc.ResetCameraPitch(0f);
            if (_characterController != null) _characterController.enabled = true;
            Physics.SyncTransforms();
        }

        if (_fpc != null)
        {
            _fpc.MoveSpeed = baseRunSpeed;
            _fpc.SprintSpeed = baseSprintSpeed;
            _origMoveSpeed = baseRunSpeed;
            _origSprintSpeed = baseSprintSpeed;
        }

        isCrouching = false;
        ApplyCrouchSpeed();

        if (_fpc != null && _fpc.CinemachineCameraTarget != null)
        {
            _cameraTarget = _fpc.CinemachineCameraTarget.transform;
            standingCameraY = 1.60f; // Standard adult human eye level
            crouchCameraY = 0.85f;
            Vector3 camPos = _cameraTarget.localPosition;
            camPos.y = standingCameraY;
            _cameraTarget.localPosition = camPos;
        }

        if (_characterController != null)
        {
            standingHeight = 1.80f; // Standard adult human height
            _characterController.height = standingHeight;
            _initialCenter = new Vector3(0f, 0.90f, 0f);
            _characterController.center = _initialCenter;
            crouchHeight = 1.00f;
        }

        _currentHeight = standingHeight;
        _currentCameraY = standingCameraY;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 0f;
        _audioSource.volume = 0.7f;

        SetupLocalRobotHero();
        ConfigureOperativeAbility(PlayerPrefs.GetInt("OasisSelectedOperative", 0));

        if (Camera.main != null)
        {
            Camera.main.nearClipPlane = 0.05f;
            var brain = Camera.main.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain != null) brain.enabled = false;
        }

        SetMeshVisibility(!isThirdPerson);

        Transform capsuleChild = transform.Find("Capsule");
        if (capsuleChild != null)
        {
            var r = capsuleChild.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }

        var cmCam = UnityEngine.Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (cmCam != null)
        {
            cmCam.Lens.NearClipPlane = 0.05f;
        }

        if (!Application.isEditor)
        {
            Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
            Screen.fullScreen = true;
        }
    }

    private OasisCombatAI _downedTeammateNearby = null;
    private OasisNetworkManager.RemotePlayerInstance _downedRemotePlayerNearby = null;
    private bool _wasMenuOrMatchEnded = false;

    private void Update()
    {
        HandleFullscreenToggle();
        HandleUpscalerToggle();

        bool menuOrMatchEnded =
            (InGamePauseManager.Instance != null && InGamePauseManager.Instance.IsPaused) ||
            (OasisMapSystem.Instance != null && OasisMapSystem.Instance.IsFullscreenOpen) ||
            (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isSimulationMode || OasisShooterManager.Instance.isMatchFinished)) ||
            (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive);

        // Pause is authoritatively handled by InGamePauseManager to avoid double-toggle race conditions.
        if (menuOrMatchEnded)
        {
            _wasMenuOrMatchEnded = true;
            isHoldingLeftClickAim = false;
            isAimingThirdPerson = false;
            if (_inputs != null)
            {
                _inputs.SetCursorState(false);
                _inputs.look = Vector2.zero;
                _inputs.move = Vector2.zero;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }
        else if (_wasMenuOrMatchEnded)
        {
            _wasMenuOrMatchEnded = false;
            ResetMovementState();
            SetThirdPersonMode(false); // Enter gameplay in natural First-Person Shooter view by default (V toggles 3rd-person)
            if (_inputs != null)
            {
                _inputs.SetCursorState(true);
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        HandleCameraViewToggle();
        HandleCameraZoomInput();

        // Trap and lock cursor inside game window to prevent leaking onto other monitors
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
        if (Cursor.visible)
        {
            Cursor.visible = false;
        }

        if (_characterController == null) return;

        // Manage Dash Cooldown
        if (dashCooldownRemaining > 0f)
        {
            dashCooldownRemaining -= Time.deltaTime;
        }

        // Check if Player is Downed in Team Battle: enter prone ground crawl posture and animation
        bool isDowned = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned);
        if (isDowned)
        {
            _wasDowned = true;
            if (_fpc != null)
            {
                _fpc.MoveSpeed = 1.15f; // Slow prone crawl speed
                _fpc.SprintSpeed = 1.15f;
            }
            if (_characterController != null)
            {
                _characterController.height = 0.55f;
                _characterController.center = new Vector3(0f, 0.28f, 0f);
            }
            if (_cameraTarget != null)
            {
                Vector3 camPos = _cameraTarget.localPosition;
                camPos.y = Mathf.Lerp(camPos.y, 0.35f, Time.deltaTime * 8f);
                _cameraTarget.localPosition = camPos;
            }
            float crawlSpeed = _characterController != null
                ? new Vector3(_characterController.velocity.x, 0f, _characterController.velocity.z).magnitude
                : 0f;
            bool isCrawling = crawlSpeed > 0.1f;

            if (robotHeroVisual != null)
            {
                float crawlSway = isCrawling ? Mathf.Sin(Time.time * 5.2f) * 6.0f : 0f;
                robotHeroVisual.transform.localPosition = Vector3.Lerp(robotHeroVisual.transform.localPosition, new Vector3(0f, 0.18f, -0.40f), Time.deltaTime * 10f);
                robotHeroVisual.transform.localRotation = Quaternion.Slerp(robotHeroVisual.transform.localRotation, Quaternion.Euler(82f, 0f, crawlSway), Time.deltaTime * 10f);
            }
            if (robotHeroAnimator != null)
            {
                robotHeroAnimator.speed = isCrawling ? 0.58f : 0.12f;
                robotHeroAnimator.Play("WalkFront_Shoot_AR");
            }
            return; // Cannot sprint, dash, or revive while downed and crawling
        }
        else if (_wasDowned)
        {
            _wasDowned = false;
            ResetMovementState();
        }

        if (robotHeroVisual != null)
        {
            robotHeroVisual.transform.localPosition = Vector3.Lerp(robotHeroVisual.transform.localPosition, Vector3.zero, Time.deltaTime * 12f);
            robotHeroVisual.transform.localRotation = Quaternion.Slerp(robotHeroVisual.transform.localRotation, Quaternion.identity, Time.deltaTime * 12f);
        }

        HandleRevivalInteractions();
        HandleCrouchInput();
        HandleDashInput();
        HandleMeleeInput();
        HandleAbilityInput();
        HandleAimCameraInput();
        HandleJumpEffect();
        HandleObstacleJumpAssist();
        UpdateCrouchAndSlide();
        UpdateDash();
        UpdateActiveAbility();
        UpdateCharacterAnimation();
    }

    private void HandleRevivalInteractions()
    {
        _downedTeammateNearby = null;
        _downedRemotePlayerNearby = null;
        int localPlayerTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        float closestDist = 2.8f;

        // 1. Check AI squadmates
        foreach (var b in OasisCombatAI.ActiveBots)
        {
            if (b == null || b.teamId != localPlayerTeam || !b.isDowned) continue;
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                _downedTeammateNearby = b;
            }
        }

        // 2. Check remote human squadmates
        if (OasisNetworkManager.Instance != null)
        {
            var remoteList = OasisNetworkManager.Instance.GetRemotePlayerInstances();
            for (int i = 0; i < remoteList.Count; i++)
            {
                var rp = remoteList[i];
                if (rp == null || rp.teamId != localPlayerTeam || !rp.isDowned || rp.transform == null) continue;
                float d = Vector3.Distance(transform.position, rp.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    _downedTeammateNearby = null;
                    _downedRemotePlayerNearby = rp;
                }
            }
        }

        if (_downedTeammateNearby != null)
        {
            bool reviveHeld = OasisKeybindManager.IsPressed(OasisAction.Revive);
            if (reviveHeld)
            {
                _downedTeammateNearby.reviveProgress += Time.deltaTime / 2.5f;
                if (_downedTeammateNearby.reviveProgress >= 1.0f)
                {
                    string mateName = _downedTeammateNearby.botName;
                    _downedTeammateNearby.Revive();
                    OasisAITelemetryLogger.Instance?.RecordPlayerReviveSuccess(mateName);
                    OasisShooterManager.Instance?.AddPlayerScore(75);
                    OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>SQUADMATE RESURRECTED! +75 PTS</color>", 3.0f);
                    _downedTeammateNearby = null;
                }
            }
            else if (_downedTeammateNearby.reviveProgress > 0f)
            {
                _downedTeammateNearby.reviveProgress = Mathf.Max(0f, _downedTeammateNearby.reviveProgress - Time.deltaTime * 0.4f);
            }
        }
        else if (_downedRemotePlayerNearby != null)
        {
            bool reviveHeld = OasisKeybindManager.IsPressed(OasisAction.Revive);
            if (reviveHeld)
            {
                _downedRemotePlayerNearby.reviveProgress += Time.deltaTime / 2.5f;
                OasisNetworkManager.Instance?.BroadcastReviveProgress(_downedRemotePlayerNearby.id, _downedRemotePlayerNearby.reviveProgress);

                if (_downedRemotePlayerNearby.reviveProgress >= 1.0f)
                {
                    string mateName = _downedRemotePlayerNearby.name;
                    OasisNetworkManager.Instance?.BroadcastPlayerRevived(_downedRemotePlayerNearby.id, OasisNetworkManager.Instance.myPlayerId);
                    _downedRemotePlayerNearby.isDowned = false;
                    _downedRemotePlayerNearby.reviveProgress = 0f;
                    OasisNetworkManager.Instance?.ApplyOperativeSkinToRemotePlayer(_downedRemotePlayerNearby);
                    OasisAITelemetryLogger.Instance?.RecordPlayerReviveSuccess(mateName);
                    OasisShooterManager.Instance?.AddPlayerScore(75);
                    OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>SQUADMATE {_downedRemotePlayerNearby.name.ToUpper()} RESURRECTED! +75 PTS</color>", 3.0f);
                    _downedRemotePlayerNearby = null;
                }
            }
            else if (_downedRemotePlayerNearby.reviveProgress > 0f)
            {
                _downedRemotePlayerNearby.reviveProgress = Mathf.Max(0f, _downedRemotePlayerNearby.reviveProgress - Time.deltaTime * 0.4f);
                OasisNetworkManager.Instance?.BroadcastReviveProgress(_downedRemotePlayerNearby.id, _downedRemotePlayerNearby.reviveProgress);
            }
        }
    }

    private void HandleCrouchInput()
    {
        bool crouchPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Crouch) ||
                             OasisKeybindManager.WasKeyCodePressedThisFrame(KeyCode.LeftControl);
        bool ctrlReleased = OasisKeybindManager.WasKeyCodeReleasedThisFrame(KeyCode.LeftControl);

        if (crouchPressed)
        {
            isCrouching = !isCrouching;
            ApplyCrouchSpeed();
        }
        else if (ctrlReleased && isCrouching)
        {
            isCrouching = false;
            ApplyCrouchSpeed();
        }
    }

    public void SyncControllerSpeeds()
    {
        if (_fpc == null) _fpc = GetComponent<StarterAssets.FirstPersonController>();
        if (_fpc == null) return;

        float abilitySpeedMult = 1.0f;
        if (cloakTimeRemaining > 0f) abilitySpeedMult = 1.35f;
        else if (scanReconTimeRemaining > 0f || medicFieldTimeRemaining > 0f) abilitySpeedMult = 1.20f;
        else if (deadshotTimeRemaining > 0f) abilitySpeedMult = 1.15f;

        float effectiveRun = baseRunSpeed * classMobilityMultiplier * abilitySpeedMult;
        float effectiveSprint = Mathf.Max(10.5f, baseSprintSpeed) * classMobilityMultiplier * abilitySpeedMult;

        if (isCrouching)
        {
            _fpc.MoveSpeed = effectiveRun * crouchSpeedMultiplier;
            _fpc.SprintSpeed = effectiveRun * crouchSpeedMultiplier;
        }
        else
        {
            _fpc.MoveSpeed = effectiveRun;
            _fpc.SprintSpeed = effectiveSprint;
        }
    }

    public void ApplyCrouchSpeed()
    {
        SyncControllerSpeeds();
    }

    /// <summary>
    /// Resets all movement states to clean standing combat run posture (e.g. on match start, revive, or respawn).
    /// </summary>
    public void ResetMovementState()
    {
        _wasDowned = false;
        isCrouching = false;
        isSliding = false;
        isDashing = false;
        _currentHeight = standingHeight;
        _currentCameraY = standingCameraY;
        if (_characterController != null)
        {
            _characterController.height = standingHeight;
            _characterController.center = _initialCenter;
        }
        if (_cameraTarget != null)
        {
            Vector3 camPos = _cameraTarget.localPosition;
            camPos.y = standingCameraY;
            _cameraTarget.localPosition = camPos;
        }
        if (robotHeroVisual != null)
        {
            robotHeroVisual.transform.localPosition = Vector3.zero;
            robotHeroVisual.transform.localRotation = Quaternion.identity;
        }
        if (robotHeroAnimator != null)
        {
            robotHeroAnimator.speed = 1.0f;
            robotHeroAnimator.Play("Idle_Shoot_Ar");
        }
        if (_fpc != null)
        {
            _fpc.MoveSpeed = baseRunSpeed;
            _fpc.SprintSpeed = baseSprintSpeed;
            _origMoveSpeed = baseRunSpeed;
            _origSprintSpeed = baseSprintSpeed;
        }
        ApplyCrouchSpeed();
    }

    private void HandleDashInput()
    {
        if (dashCooldownRemaining > 0f || isDashing) return;

        bool dashTriggered = OasisKeybindManager.WasPressedThisFrame(OasisAction.Dash) ||
                             OasisKeybindManager.WasKeyCodePressedThisFrame(KeyCode.LeftAlt);

        if (dashTriggered)
        {
            StartDash();
        }
    }

    private void StartDash()
    {
        isDashing = true;
        dashCooldownRemaining = dashCooldown;
        _dashTimeRemaining = dashDuration;

        Vector3 dir = Vector3.zero;
        if (_inputs != null && _inputs.move.sqrMagnitude > 0.1f)
        {
            dir = (transform.right * _inputs.move.x + transform.forward * _inputs.move.y).normalized;
        }
        else
        {
            dir = transform.forward;
        }

        _dashDirection = dir;

        // Audio & Visual pulse
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetDashClip(), 0.85f);
        }

        BlasterVFX.SpawnImpact(transform.position + Vector3.up * 0.5f, -_dashDirection, false);
    }

    private void UpdateDash()
    {
        if (!isDashing) return;

        _dashTimeRemaining -= Time.deltaTime;
        if (_dashTimeRemaining > 0f)
        {
            float currentDashSpeed = dashForce * Mathf.Clamp01(_dashTimeRemaining / dashDuration);
            _characterController.Move(_dashDirection * (currentDashSpeed * Time.deltaTime));
        }
        else
        {
            isDashing = false;
        }
    }

    public void ConfigureOperativeAbility(int opIndex)
    {
        currentOperativeIndex = opIndex;
        float targetMaxHealth = 165f;

        switch (opIndex)
        {
            case 0: // Titanium Heavy Warrior
                abilityName = "AEGIS SIEGE OVERDRIVE";
                abilityDescription = "+75 Shield/HP, 40% dmg reduction, instant reload & +25% fire rate (8s)";
                abilityCooldown = 14f;
                abilityActiveDuration = 8f;
                classMobilityMultiplier = 0.94f;
                targetMaxHealth = 185f;
                break;
            case 1: // Desert Tactical Commando
                abilityName = "ORBITAL PRECISION STRIKE";
                abilityDescription = "Laser-designates target up to 250m for 3-wave orbital plasma barrage";
                abilityCooldown = 13f;
                abilityActiveDuration = 2.0f;
                classMobilityMultiplier = 1.00f;
                targetMaxHealth = 165f;
                break;
            case 2: // Cyborg Special Operative
                abilityName = "PHANTOM BLINK & CLOAK";
                abilityDescription = "9m forward blink, stealth camo, +35% speed & +25% ambush dmg (6s)";
                abilityCooldown = 14f;
                abilityActiveDuration = 6f;
                classMobilityMultiplier = 1.06f;
                targetMaxHealth = 150f;
                break;
            case 3: // Unit-07 Combat Android
                abilityName = "DIRECTED EMP SHOCKWAVE";
                abilityDescription = "Ranged EMP orb + 20m pulse: stuns (4s), 55 EMP dmg & infinite beam (6s)";
                abilityCooldown = 15f;
                abilityActiveDuration = 6f;
                classMobilityMultiplier = 0.96f;
                targetMaxHealth = 180f;
                break;
            case 4: // Wasteland Nomad
                abilityName = "ORBITAL RECON & LOCK";
                abilityDescription = "Exposes all hostiles (8s), instant reload, +20% speed & +15% dmg";
                abilityCooldown = 14f;
                abilityActiveDuration = 8f;
                classMobilityMultiplier = 1.03f;
                targetMaxHealth = 155f;
                break;
            case 5: // Combat Medic
                abilityName = "NANO-MENDER SANCTUARY";
                abilityDescription = "+40 HP burst, 22 HP/s aura (7s), +20% speed & 15m instant revive";
                abilityCooldown = 15f;
                abilityActiveDuration = 7f;
                classMobilityMultiplier = 1.01f;
                targetMaxHealth = 165f;
                break;
            case 6: // Cyber Outlaw Specialist
            default:
                abilityName = "DEADSHOT OVERCLOCK";
                abilityDescription = "Instant reload, +60% fire rate, +35% crit dmg, smart-snap & +25 HP siphon (7s)";
                abilityCooldown = 14f;
                abilityActiveDuration = 7f;
                classMobilityMultiplier = 1.04f;
                targetMaxHealth = 155f;
                break;
        }

        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.maxHealth = targetMaxHealth;
            if (!OasisShooterManager.Instance.isPlayerDead)
            {
                OasisShooterManager.Instance.currentHealth = targetMaxHealth;
                OasisShooterManager.Instance.UpdateHealthUI();
            }
        }
        SyncControllerSpeeds();
    }

    private void HandleAbilityInput()
    {
        if (abilityCooldownRemaining > 0f) return;
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned) return;

        bool abilityPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Ability);
#if ENABLE_INPUT_SYSTEM
        if (!abilityPressed && Keyboard.current != null)
        {
            abilityPressed = Keyboard.current.fKey.wasPressedThisFrame;
        }
#else
        if (!abilityPressed)
        {
            abilityPressed = Input.GetKeyDown(KeyCode.F);
        }
#endif

        if (abilityPressed)
        {
            ActivateSpecialAbility();
        }
    }

    private void InstantReloadEquippedWeapon()
    {
        var wpn = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
        if (wpn == null) wpn = FindAnyObjectByType<BlasterWeapon>();
        if (wpn != null)
        {
            wpn.currentAmmo = wpn.maxAmmo;
            OasisShooterManager.Instance?.UpdateAmmo(wpn.currentAmmo, wpn.maxAmmo, false);
        }
    }

    private void ActivateSpecialAbility()
    {
        abilityCooldownRemaining = abilityCooldown;
        abilityActiveRemaining = abilityActiveDuration;

        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        int localId = (OasisNetworkManager.Instance != null) ? OasisNetworkManager.Instance.myPlayerId : 0;

        switch (currentOperativeIndex)
        {
            case 0: // Titanium Heavy Warrior: AEGIS SIEGE OVERDRIVE
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetAegisOverdriveClip(), 0.95f);
                aegisTimeRemaining = 8.0f;
                InstantReloadEquippedWeapon();

                // +75 Shield/HP Reinforcement (allows up to +50 Overshield above maxHealth)
                if (OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.currentHealth = Mathf.Min(
                        OasisShooterManager.Instance.maxHealth + 50f,
                        OasisShooterManager.Instance.currentHealth + 75f);
                    OasisShooterManager.Instance.UpdateHealthUI();
                }

                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1.0f, Vector3.up, false);
                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#26D9FF>[F: AEGIS SIEGE OVERDRIVE] +75 SHIELD // 40% DMG REDUCTION & +25% FIRE RATE (8s)</color>", 3.5f);
                break;
            }

            case 1: // Desert Tactical Commando: ORBITAL PRECISION STRIKE
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetOrbitalStrikeClip(), 0.95f);
                Camera cam = Camera.main;
                Vector3 rayOrigin = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.6f;
                Vector3 rayDir = cam != null ? cam.transform.forward : transform.forward;

                int mask = Physics.AllLayers & ~((1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << gameObject.layer) | (1 << 8));
                Vector3 strikePoint = rayOrigin + rayDir * 45f;
                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, 250f, mask, QueryTriggerInteraction.Ignore))
                {
                    strikePoint = hit.point;
                }
                else
                {
                    // Project down onto terrain at 65m ahead if aiming into sky
                    Vector3 probe = rayOrigin + new Vector3(rayDir.x, 0f, rayDir.z).normalized * 65f;
                    if (Physics.Raycast(new Vector3(probe.x, 40f, probe.z), Vector3.down, out RaycastHit gHit, 80f, mask, QueryTriggerInteraction.Ignore))
                    {
                        strikePoint = gHit.point;
                    }
                }

                StartCoroutine(ExecuteOrbitalPrecisionStrikeRoutine(strikePoint, localTeam, localId));
                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#FF9933>[F: ORBITAL PRECISION STRIKE] TARGET LOCKED // 3-WAVE ORBITAL PLASMA BARRAGE INBOUND!</color>", 3.5f);
                break;
            }

            case 2: // Cyborg Special Operative: PHANTOM BLINK & CLOAK
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetPhantomBlinkClip(), 0.95f);
                cloakTimeRemaining = 6.0f;
                InstantReloadEquippedWeapon();

                // Instant 9m forward cyber-blink along aim/movement direction
                Vector3 blinkDir = Camera.main != null ? Camera.main.transform.forward : transform.forward;
                blinkDir.y = 0.08f;
                blinkDir.Normalize();
                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, -blinkDir, false);
                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(blinkDir * 9.0f);
                }
                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, blinkDir, false);
                SyncControllerSpeeds();

                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#33FFFF>[F: PHANTOM BLINK & CLOAK] 9M BLINK // STEALTH CAMO, +35% SPEED & +25% AMBUSH DMG (6s)</color>", 3.5f);
                break;
            }

            case 3: // Unit-07 Combat Android: DIRECTED EMP SHOCKWAVE
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetEMPClip(), 1.0f);
                empOverdriveTimeRemaining = 6.0f;
                InstantReloadEquippedWeapon();

                Camera cam = Camera.main;
                Vector3 rayOrigin = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.6f;
                Vector3 rayDir = cam != null ? cam.transform.forward : transform.forward;
                int mask = Physics.AllLayers & ~((1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << gameObject.layer) | (1 << 8));

                Vector3 remoteEmpCenter = rayOrigin + rayDir * 35f;
                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, 180f, mask, QueryTriggerInteraction.Ignore))
                {
                    remoteEmpCenter = hit.point;
                }

                // Visual EMP beam from muzzle to remote detonation center
                BlasterVFX.SpawnLaserBeam(transform.position + Vector3.up * 1.35f, remoteEmpCenter, new Color(0.20f, 0.95f, 1.0f), 0.18f, 0.30f);
                BlasterVFX.SpawnBombExplosion(remoteEmpCenter, 8.0f);
                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, Vector3.up, false);

                int stunnedCount = 0;
                foreach (var bot in OasisCombatAI.ActiveBots)
                {
                    if (bot == null || bot.isDead || bot.isDowned || bot.teamId == localTeam) continue;
                    float distSelf = Vector3.Distance(transform.position, bot.transform.position);
                    float distTarget = Vector3.Distance(remoteEmpCenter, bot.transform.position);
                    if (distSelf <= 20.0f || distTarget <= 14.0f)
                    {
                        bot.ApplyEMPStun(4.0f);
                        bot.TakeDamage(55f, bot.transform.position + Vector3.up * 1.1f, Vector3.up, localId, localTeam, Mathf.Min(distSelf, distTarget));
                        BlasterVFX.SpawnDamagePopup(bot.transform.position + Vector3.up * 1.4f, 55f, false, false);
                        TriggerHitmarker();
                        stunnedCount++;
                    }
                }

                // Also hit enemy real players in EMP radius
                if (OasisNetworkManager.Instance != null)
                {
                    var remList = OasisNetworkManager.Instance.GetRemotePlayerInstances();
                    for (int i = 0; i < remList.Count; i++)
                    {
                        var rp = remList[i];
                        if (rp == null || rp.isDead || rp.teamId == localTeam || rp.transform == null) continue;
                        float distSelf = Vector3.Distance(transform.position, rp.transform.position);
                        float distTarget = Vector3.Distance(remoteEmpCenter, rp.transform.position);
                        if (distSelf <= 20.0f || distTarget <= 14.0f)
                        {
                            OasisNetworkManager.Instance.SendPlayerHit(rp.id, 55f, localTeam);
                            BlasterVFX.SpawnDamagePopup(rp.transform.position + Vector3.up * 1.4f, 55f, false, false);
                            TriggerHitmarker();
                            stunnedCount++;
                        }
                    }
                }

                OasisShooterManager.Instance?.ShowNotificationBanner(
                    $"<color=#26D9FF>[F: DIRECTED EMP SHOCKWAVE] {stunnedCount} HOSTILES STUNNED (55 DMG) // INFINITE BEAM ENERGY (6s)</color>", 3.5f);
                break;
            }

            case 4: // Wasteland Nomad: ORBITAL RECON & LOCK
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetScanPingClip(), 0.95f);
                scanReconTimeRemaining = 8.0f;
                InstantReloadEquippedWeapon();
                SyncControllerSpeeds();
                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#55FF55>[F: ORBITAL RECON & LOCK] ALL HOSTILES LOCKED // +20% SPEED & +15% MARKSMAN DMG (8s)</color>", 3.5f);
                break;
            }

            case 5: // Combat Medic: NANO-MENDER SANCTUARY
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHealPulseClip(), 0.95f);
                medicFieldTimeRemaining = 7.0f;
                medicFieldPosition = transform.position;
                SyncControllerSpeeds();

                // Immediate +40 HP burst heal on cast
                if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isPlayerDead)
                {
                    OasisShooterManager.Instance.currentHealth = Mathf.Min(
                        OasisShooterManager.Instance.maxHealth,
                        OasisShooterManager.Instance.currentHealth + 40f);
                    OasisShooterManager.Instance.UpdateHealthUI();
                }

                // Immediate emergency revive of any downed AI or real teammate within 15m
                foreach (var bot in OasisCombatAI.ActiveBots)
                {
                    if (bot == null || bot.teamId != localTeam || !bot.isDowned) continue;
                    float d = Vector3.Distance(transform.position, bot.transform.position);
                    if (d <= 15.0f)
                    {
                        bot.Revive();
                        OasisShooterManager.Instance?.AddPlayerScore(75);
                        OasisShooterManager.Instance?.ShowNotificationBanner(
                            $"<color=#FF5566>NANO-MENDER SANCTUARY RESURRECTED {bot.botName.ToUpper()}!</color>", 3.0f);
                    }
                }
                if (OasisNetworkManager.Instance != null)
                {
                    var remList = OasisNetworkManager.Instance.GetRemotePlayerInstances();
                    for (int i = 0; i < remList.Count; i++)
                    {
                        var rp = remList[i];
                        if (rp == null || rp.teamId != localTeam || !rp.isDowned || rp.transform == null) continue;
                        if (Vector3.Distance(transform.position, rp.transform.position) <= 15.0f)
                        {
                            OasisNetworkManager.Instance.BroadcastPlayerRevived(rp.id, localId);
                            rp.isDowned = false;
                            rp.reviveProgress = 0f;
                            OasisShooterManager.Instance?.AddPlayerScore(75);
                        }
                    }
                }

                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#FF5566>[F: NANO-MENDER SANCTUARY] +40 HP BURST // REGENERATING 22 HP/S & +20% SPEED (7s)</color>", 3.5f);
                break;
            }

            case 6: // Cyber Outlaw Specialist: DEADSHOT OVERCLOCK
            default:
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetDeadshotOverclockClip(), 0.95f);
                deadshotTimeRemaining = 7.0f;
                InstantReloadEquippedWeapon();
                SyncControllerSpeeds();
                OasisShooterManager.Instance?.ShowNotificationBanner(
                    "<color=#EE55FF>[F: DEADSHOT OVERCLOCK] +60% FIRE RATE // SMART-LOCK & +25 HP SIPHON ON HIT (7s)</color>", 3.5f);
                break;
            }
        }
    }

    private IEnumerator ExecuteOrbitalPrecisionStrikeRoutine(Vector3 centerPoint, int localTeam, int localId)
    {
        for (int wave = 0; wave < 3; wave++)
        {
            Vector2 offset2D = (wave == 0) ? Vector2.zero : Random.insideUnitCircle * 3.8f;
            Vector3 impactPos = centerPoint + new Vector3(offset2D.x, 0f, offset2D.y);
            Vector3 skyOrigin = impactPos + new Vector3(0f, 48f, 0f);

            // Sky-to-ground orbital plasma beam
            BlasterVFX.SpawnLaserBeam(skyOrigin, impactPos, new Color(1.0f, 0.72f, 0.18f), 0.36f, 0.32f);
            BlasterVFX.SpawnBombExplosion(impactPos, 9.0f);
            AudioSource.PlayClipAtPoint(ProceduralAudio.GetBombExplosionClip(), impactPos, 0.85f);

            // Damage enemy AI bots within 9.5m blast radius
            foreach (var bot in OasisCombatAI.ActiveBots)
            {
                if (bot == null || bot.isDead || bot.teamId == localTeam) continue;
                float dist = Vector3.Distance(impactPos, bot.transform.position);
                if (dist <= 9.5f)
                {
                    float falloff = Mathf.Lerp(1.0f, 0.55f, dist / 9.5f);
                    float dmg = Mathf.Round(75f * falloff);
                    Vector3 hitPos = bot.transform.position + Vector3.up * 1.1f;
                    OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(bot, dmg, dist);
                    bot.TakeDamage(dmg, hitPos, (bot.transform.position - impactPos).normalized, localId, localTeam, dist);
                    BlasterVFX.SpawnDamagePopup(hitPos, dmg, false, false);
                    TriggerHitmarker(false);
                }
            }

            // Damage enemy real players within 9.5m blast radius
            if (OasisNetworkManager.Instance != null)
            {
                var remList = OasisNetworkManager.Instance.GetRemotePlayerInstances();
                for (int i = 0; i < remList.Count; i++)
                {
                    var rp = remList[i];
                    if (rp == null || rp.isDead || rp.teamId == localTeam || rp.transform == null) continue;
                    float dist = Vector3.Distance(impactPos, rp.transform.position);
                    if (dist <= 9.5f)
                    {
                        float falloff = Mathf.Lerp(1.0f, 0.55f, dist / 9.5f);
                        float dmg = Mathf.Round(75f * falloff);
                        OasisNetworkManager.Instance.SendPlayerHit(rp.id, dmg, localTeam);
                        BlasterVFX.SpawnDamagePopup(rp.transform.position + Vector3.up * 1.3f, dmg, false, false);
                        TriggerHitmarker(false);
                    }
                }
            }

            yield return new WaitForSeconds(0.28f);
        }
    }

    private void UpdateActiveAbility()
    {
        if (abilityCooldownRemaining > 0f)
        {
            abilityCooldownRemaining -= Time.deltaTime;
        }

        if (abilityActiveRemaining > 0f)
        {
            abilityActiveRemaining -= Time.deltaTime;
        }

        if (aegisTimeRemaining > 0f)
        {
            aegisTimeRemaining -= Time.deltaTime;
        }

        if (empOverdriveTimeRemaining > 0f)
        {
            empOverdriveTimeRemaining -= Time.deltaTime;
        }

        if (deadshotTimeRemaining > 0f)
        {
            deadshotTimeRemaining -= Time.deltaTime;
            if (deadshotTimeRemaining <= 0f) SyncControllerSpeeds();
        }

        // 1. Cloak handling
        if (cloakTimeRemaining > 0f)
        {
            cloakTimeRemaining -= Time.deltaTime;
            if (cloakTimeRemaining <= 0f)
            {
                SyncControllerSpeeds();
            }
        }

        // 2. Scan recon handling
        if (scanReconTimeRemaining > 0f)
        {
            scanReconTimeRemaining -= Time.deltaTime;
            if (scanReconTimeRemaining <= 0f)
            {
                SyncControllerSpeeds();
            }
        }

        // 3. Medic Field healing aura
        if (medicFieldTimeRemaining > 0f)
        {
            medicFieldTimeRemaining -= Time.deltaTime;

            // Regenerate player health/shield at 22 HP/s
            if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isPlayerDowned && !OasisShooterManager.Instance.isPlayerDead)
            {
                float healTick = Time.deltaTime * 22f;
                OasisShooterManager.Instance.currentHealth = Mathf.Min(OasisShooterManager.Instance.maxHealth, OasisShooterManager.Instance.currentHealth + healTick);
                OasisShooterManager.Instance.UpdateHealthUI();
            }

            // Heal friendly bots in 12m radius
            int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            foreach (var bot in OasisCombatAI.ActiveBots)
            {
                if (bot == null || bot.isDead || bot.teamId != localTeam) continue;
                float d = Vector3.Distance(transform.position, bot.transform.position);
                if (d <= 12.0f)
                {
                    if (bot.isDowned)
                    {
                        bot.reviveProgress += Time.deltaTime * 0.55f;
                        if (bot.reviveProgress >= 1f) bot.Revive();
                    }
                    else
                    {
                        bot.currentHealth = Mathf.Min(bot.maxHealth, bot.currentHealth + Time.deltaTime * 22f);
                    }
                }
            }

            if (medicFieldTimeRemaining <= 0f)
            {
                SyncControllerSpeeds();
            }
        }
    }

    private void UpdateCrouchAndSlide()
    {
        isSliding = false;

        // Smooth height and camera interpolation
        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        float targetCamY = isCrouching ? crouchCameraY : standingCameraY;

        _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, Time.deltaTime * 10f);
        _currentCameraY = Mathf.Lerp(_currentCameraY, targetCamY, Time.deltaTime * 10f);

        _characterController.height = _currentHeight;
        _characterController.center = new Vector3(_initialCenter.x, _initialCenter.y * (_currentHeight / standingHeight), _initialCenter.z);

        if (_cameraTarget != null)
        {
            Vector3 camPos = _cameraTarget.localPosition;
            camPos.x = 0f;
            camPos.y = _currentCameraY;
            camPos.z = 0f;
            _cameraTarget.localPosition = camPos;
        }
    }

    private void LateUpdate()
    {
        if (InGamePauseManager.Instance != null && InGamePauseManager.Instance.IsPaused)
        {
            return;
        }

        // Process mouse look rotation immediately BEFORE reading _cameraTarget so there is zero 1-frame camera lag
        if (_fpc != null)
        {
            _fpc.UpdateCameraRotation();
        }

        Camera cam = Camera.main;
        if (cam == null || _cameraTarget == null) return;

        if (!isThirdPerson)
        {
            // Natural First-Person FPS view: place camera at eye-level with slight forward head offset
            cam.transform.position = _cameraTarget.position + transform.forward * 0.06f;
            cam.transform.rotation = _cameraTarget.rotation;
        }
        else
        {
            // Natural Over-The-Shoulder Third-Person view (toggled via V or scroll wheel):
            currentCameraDistance = Mathf.Lerp(currentCameraDistance, _targetCameraDistance, Time.deltaTime * 16f);

            float activeDist = isAimingThirdPerson
                ? Mathf.Min(currentCameraDistance * 0.62f, 1.45f)
                : currentCameraDistance;

            // Balanced over-the-shoulder framing with minimal horizontal parallax
            Vector3 shoulderOffset = isAimingThirdPerson
                ? new Vector3(0.34f, 0.16f, -activeDist)
                : new Vector3(0.42f, 0.20f, -activeDist);

            Vector3 desiredPos = _cameraTarget.position + _cameraTarget.rotation * shoulderOffset;
            Vector3 origin = _cameraTarget.position;
            Vector3 dir = desiredPos - origin;
            float maxDist = dir.magnitude;

            int mask = Physics.AllLayers & ~((1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << gameObject.layer) | (1 << 8));
            if (Physics.SphereCast(origin, 0.20f, dir.normalized, out RaycastHit hit, maxDist, mask, QueryTriggerInteraction.Ignore))
            {
                cam.transform.position = origin + dir.normalized * Mathf.Max(0.50f, hit.distance - 0.15f);
            }
            else
            {
                cam.transform.position = desiredPos;
            }

            cam.transform.rotation = _cameraTarget.rotation;

            // Failsafe: If camera gets too close to the character's head/body, hide mesh so it never clips through camera
            float distToHead = Vector3.Distance(cam.transform.position, _cameraTarget.position);
            bool tooClose = distToHead < 0.85f;
            if (robotHeroVisual != null)
            {
                var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    rends[i].enabled = !tooClose;
                }
            }
        }

        // Apply Aim IK so upper body and weapon point dead straight at the crosshair target
        ApplyAimIK(cam);
    }

    private void HandleCameraZoomInput()
    {
        float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            scroll = Mouse.current.scroll.ReadValue().y;
        }
#else
        scroll = Input.GetAxis("Mouse ScrollWheel");
#endif
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Normalize scroll: new input system returns ~120 per notch; legacy returns ~0.1
            float normScroll = Mathf.Sign(scroll) * (Mathf.Abs(scroll) > 1f ? 0.35f : scroll * 3.5f);

            if (isThirdPerson)
            {
                // Scroll up (positive) = zoom in; Scroll down (negative) = zoom out
                _targetCameraDistance -= normScroll * cameraZoomSpeed;

                if (_targetCameraDistance < minCameraDistance - 0.25f)
                {
                    // Seamlessly zoom directly into first person mode
                    SetThirdPersonMode(false);
                    _targetCameraDistance = minCameraDistance;
                    currentCameraDistance = minCameraDistance;
                }
                else
                {
                    _targetCameraDistance = Mathf.Clamp(_targetCameraDistance, minCameraDistance, maxCameraDistance);
                }
            }
            else
            {
                // In first person: scrolling down (zoom out) seamlessly transitions back into third person mode
                if (normScroll < -0.05f)
                {
                    SetThirdPersonMode(true);
                    _targetCameraDistance = minCameraDistance + 0.6f;
                    currentCameraDistance = _targetCameraDistance;
                }
            }
        }
    }

    private void ApplyAimIK(Camera cam)
    {
        if (cam == null || robotHeroVisual == null || !robotHeroVisual.activeInHierarchy) return;

        bool isDowned = (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned);
        if (isDowned) return;

        // Determine reticle aim target in world space
        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimTarget;
        int aimMask = Physics.AllLayers & ~((1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << gameObject.layer));
        if (Physics.SphereCast(aimRay, 0.15f, out RaycastHit aimHit, 200f, aimMask, QueryTriggerInteraction.Ignore))
        {
            aimTarget = aimHit.point;
        }
        else
        {
            aimTarget = aimRay.GetPoint(100f);
        }

        // Camera pitch angle (-89 deg looking up to +89 deg looking down)
        float camPitch = _cameraTarget != null ? _cameraTarget.localEulerAngles.x : cam.transform.eulerAngles.x;
        if (camPitch > 180f) camPitch -= 360f;

        // Decay procedural 3rd-person recoil and reticle bloom smoothly
        _tpRecoilPitch = Mathf.Lerp(_tpRecoilPitch, 0f, Time.deltaTime * 15f);
        _tpRecoilYaw = Mathf.Lerp(_tpRecoilYaw, 0f, Time.deltaTime * 15f);
        _reticleBloom = Mathf.Lerp(_reticleBloom, 0f, Time.deltaTime * 12f);

        // Blend down arm IK override while reloading so the two-handed magazine swap animation plays naturally
        float ikWeight = (_reloadAnimTimer > 0f) ? 0.25f : 1.0f;

        // 1. Spine pitch, recoil kick & horizontal parallax convergence
        if (_spineBone != null)
        {
            float spinePitch = (camPitch * 0.55f - _tpRecoilPitch * 0.45f) * ikWeight;
            _spineBone.Rotate(cam.transform.right, spinePitch, Space.World);

            if (isThirdPerson)
            {
                Vector3 toTarget = (aimTarget - _spineBone.position).normalized;
                Vector3 localDir = transform.InverseTransformDirection(toTarget);
                float yawOffset = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                yawOffset = Mathf.Clamp(yawOffset, -25f, 25f);
                float meleeTwist = (_meleeAnimTimer > 0f) ? Mathf.Sin((_meleeAnimTimer / 0.28f) * Mathf.PI) * -32f : 0f;
                _spineBone.Rotate(Vector3.up, (yawOffset * 0.45f + _tpRecoilYaw * 0.4f) * ikWeight + meleeTwist, Space.World);
            }
        }

        // 2. Gun arm pitch, recoil kick & aim convergence
        if (_gunArmBone != null && ikWeight > 0.3f)
        {
            float armPitch = (camPitch * 0.45f - _tpRecoilPitch * 0.85f) * ikWeight;
            _gunArmBone.Rotate(cam.transform.right, armPitch, Space.World);

            if (isThirdPerson)
            {
                Vector3 toTarget = (aimTarget - _gunArmBone.position).normalized;
                Vector3 localDir = transform.InverseTransformDirection(toTarget);
                float yawOffset = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                yawOffset = Mathf.Clamp(yawOffset, -30f, 30f);
                _gunArmBone.Rotate(Vector3.up, (yawOffset * 0.55f + _tpRecoilYaw * 0.6f) * ikWeight, Space.World);
            }
        }
    }

    private void HandleCameraViewToggle()
    {
        bool vPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            vPressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.V))
        {
            vPressed = true;
        }
#endif
        if (vPressed)
        {
            SetThirdPersonMode(!isThirdPerson);
        }

        // Mouse sensitivity quick-adjust hotkeys: [ (decrease) and ] (increase)
        if (_fpc != null)
        {
            bool sensDown = false;
            bool sensUp = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                sensDown = Keyboard.current.leftBracketKey.wasPressedThisFrame || Keyboard.current.minusKey.wasPressedThisFrame;
                sensUp = Keyboard.current.rightBracketKey.wasPressedThisFrame || Keyboard.current.equalsKey.wasPressedThisFrame;
            }
#else
            sensDown = Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.Minus);
            sensUp = Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.Equals);
#endif
            if (sensDown) _fpc.AdjustMouseSensitivity(-0.10f);
            if (sensUp) _fpc.AdjustMouseSensitivity(0.10f);
        }
    }

    private void HandleAimCameraInput()
    {
        bool leftHeld = false;
        bool rightHeld = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            leftHeld = Mouse.current.leftButton.isPressed;
            rightHeld = Mouse.current.rightButton.isPressed;
        }
#else
        leftHeld = Input.GetMouseButton(0);
        rightHeld = Input.GetMouseButton(1);
#endif

        isHoldingLeftClickAim = leftHeld;
        isAimingThirdPerson = rightHeld;

    }

    public void TriggerWeaponShootAnim(float kickIntensity = 1.0f, bool isHeavySingleShot = false)
    {
        _shootAnimTimer = isHeavySingleShot ? 0.34f : 0.22f;
        _shootAnimClip = isHeavySingleShot ? "Shoot_SingleShot_AR" : "Shoot_Autoshot_AR";
        _tpRecoilPitch = Mathf.Min(22f, _tpRecoilPitch + 8.5f * kickIntensity);
        _tpRecoilYaw = Random.Range(-3.2f, 3.2f) * kickIntensity;
        _reticleBloom = Mathf.Min(18f, _reticleBloom + 4.8f * kickIntensity);

        if (robotHeroAnimator != null && _reloadAnimTimer <= 0f && !isCrouching)
        {
            float horizontalSpeed = _characterController != null
                ? new Vector3(_characterController.velocity.x, 0f, _characterController.velocity.z).magnitude
                : 0f;
            if (horizontalSpeed <= 0.35f)
            {
                robotHeroAnimator.speed = 1.0f;
                robotHeroAnimator.Play(_shootAnimClip, 0, 0f);
            }
        }
    }

    public void TriggerWeaponReloadAnim(float duration)
    {
        _reloadTotalDuration = Mathf.Max(0.45f, duration);
        _reloadAnimTimer = _reloadTotalDuration;
        _shootAnimTimer = 0f;

        if (robotHeroAnimator != null && !isCrouching)
        {
            robotHeroAnimator.speed = 1.35f / _reloadTotalDuration;
            robotHeroAnimator.Play("Reload", 0, 0f);
        }
    }

    private void HandleMeleeInput()
    {
        if (_meleeCooldownTimer > 0f)
        {
            _meleeCooldownTimer -= Time.deltaTime;
        }
        if (_meleeAnimTimer > 0f)
        {
            _meleeAnimTimer -= Time.deltaTime;
        }

        if (_meleeCooldownTimer > 0f) return;
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned) return;

        bool meleePressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            meleePressed = true;
        }
        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
        {
            meleePressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(2))
        {
            meleePressed = true;
        }
#endif
        if (meleePressed)
        {
            ExecutePlasmaMeleeStrike();
        }
    }

    private void ExecutePlasmaMeleeStrike()
    {
        _meleeCooldownTimer = 0.68f;
        _meleeAnimTimer = 0.28f;
        _tpRecoilPitch = -6f;

        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetDashClip(), 0.95f);
        }

        Camera cam = Camera.main;
        Vector3 strikeOrigin = (cam != null && !isThirdPerson)
            ? cam.transform.position + cam.transform.forward * 0.45f - cam.transform.up * 0.15f
            : transform.position + Vector3.up * 1.25f + transform.forward * 0.45f;
        Vector3 strikeForward = (cam != null) ? cam.transform.forward : transform.forward;
        Vector3 strikeRight = (cam != null) ? cam.transform.right : transform.right;

        // Forward combat lunge
        if (_characterController != null)
        {
            Vector3 lungeDir = new Vector3(strikeForward.x, 0f, strikeForward.z).normalized;
            _characterController.Move(lungeDir * 0.85f);
        }

        BlasterVFX.SpawnMeleeArc(strikeOrigin, strikeForward, new Color(0.2f, 0.95f, 1.0f));

        if (robotHeroAnimator != null && !isCrouching)
        {
            robotHeroAnimator.speed = 1.85f;
            robotHeroAnimator.Play("Shoot_SingleShot_AR", 0, 0f);
        }

        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        int localId = (OasisNetworkManager.Instance != null) ? OasisNetworkManager.Instance.myPlayerId : 0;
        bool landedHit = false;

        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null || bot.isDead || bot.teamId == localTeam) continue;
            Vector3 toBot = (bot.transform.position + Vector3.up * 1.0f) - transform.position;
            float dist = toBot.magnitude;
            if (dist <= 3.8f)
            {
                Vector3 flatToBot = new Vector3(toBot.x, 0f, toBot.z).normalized;
                Vector3 flatFwd = new Vector3(strikeForward.x, 0f, strikeForward.z).normalized;
                if (Vector3.Dot(flatFwd, flatToBot) >= 0.25f || dist <= 1.8f)
                {
                    float meleeDmg = 65f;
                    Vector3 hitPos = bot.transform.position + Vector3.up * 1.2f;
                    OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(bot, meleeDmg, dist);
                    bot.TakeDamage(meleeDmg, hitPos, -flatToBot, localId, localTeam, dist);
                    BlasterVFX.SpawnDamagePopup(hitPos, meleeDmg, true, false);
                    TriggerHitmarker(true);
                    landedHit = true;
                }
            }
        }

        if (OasisNetworkManager.Instance != null)
        {
            var remList = OasisNetworkManager.Instance.GetRemotePlayerInstances();
            for (int i = 0; i < remList.Count; i++)
            {
                var rp = remList[i];
                if (rp == null || rp.isDead || rp.teamId == localTeam || rp.transform == null) continue;
                Vector3 toRp = (rp.transform.position + Vector3.up * 1.0f) - transform.position;
                float dist = toRp.magnitude;
                if (dist <= 3.8f)
                {
                    Vector3 flatToRp = new Vector3(toRp.x, 0f, toRp.z).normalized;
                    Vector3 flatFwd = new Vector3(strikeForward.x, 0f, strikeForward.z).normalized;
                    if (Vector3.Dot(flatFwd, flatToRp) >= 0.25f || dist <= 1.8f)
                    {
                        float meleeDmg = 65f;
                        Vector3 hitPos = rp.transform.position + Vector3.up * 1.2f;
                        OasisNetworkManager.Instance.SendPlayerHit(rp.id, meleeDmg, localTeam);
                        BlasterVFX.SpawnDamagePopup(hitPos, meleeDmg, true, false);
                        TriggerHitmarker(true);
                        landedHit = true;
                    }
                }
            }
        }

        if (landedHit && OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.currentHealth = Mathf.Min(
                OasisShooterManager.Instance.maxHealth,
                OasisShooterManager.Instance.currentHealth + 20f);
            OasisShooterManager.Instance.AddPlayerScore(25);
        }
    }

    private void UpdateCharacterAnimation()
    {
        if (robotHeroAnimator == null) return;

        if (_shootAnimTimer > 0f) _shootAnimTimer -= Time.deltaTime;
        if (_reloadAnimTimer > 0f) _reloadAnimTimer -= Time.deltaTime;

        Vector3 vel = _characterController != null ? _characterController.velocity : Vector3.zero;
        float horizontalSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;

        // Priority 0: Airborne & Jetpack flight state - legs MUST NOT move whilst in the air!
        bool isAirborne = _fpc != null && !_fpc.Grounded;
        bool isJetpackFlight = _fpc != null && _fpc.IsJetpackActive;

        if (isAirborne || isJetpackFlight)
        {
            if (robotHeroVisual != null)
            {
                robotHeroVisual.transform.localRotation = Quaternion.identity;
            }

            // Hold legs completely still in the authentic suspended airborne flight pose
            robotHeroAnimator.speed = 0f;
            if (!robotHeroAnimator.GetCurrentAnimatorStateInfo(0).IsName("Jump"))
            {
                robotHeroAnimator.Play("Jump", 0, 0.42f);
            }
            return;
        }

        if (isCrouching)
        {
            if (horizontalSpeed < 0.25f)
            {
                // Stationary crouch pose (Idle_Ducking_AR was imported facing backwards, so rotate visual mesh 180 deg to face aim)
                if (robotHeroVisual != null)
                {
                    robotHeroVisual.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                }
                robotHeroAnimator.speed = 1.0f;
                robotHeroAnimator.Play("Idle_Ducking_AR");
            }
            else
            {
                // Actively walking while crouching: physically move legs and step!
                if (robotHeroVisual != null)
                {
                    robotHeroVisual.transform.localRotation = Quaternion.identity;
                }
                robotHeroAnimator.speed = 0.85f;
                robotHeroAnimator.Play("WalkFront_Shoot_AR");
            }
            return;
        }

        if (robotHeroVisual != null)
        {
            robotHeroVisual.transform.localRotation = Quaternion.identity;
        }

        // Priority 1: Close-quarters Melee Slash animation
        if (_meleeAnimTimer > 0f)
        {
            robotHeroAnimator.speed = 1.85f;
            robotHeroAnimator.Play("Shoot_SingleShot_AR");
            return;
        }

        // Priority 2: Active Weapon Reload animation (when stationary or walking)
        if (_reloadAnimTimer > 0f && horizontalSpeed < 4.5f)
        {
            robotHeroAnimator.speed = Mathf.Clamp(1.35f / _reloadTotalDuration, 0.75f, 1.8f);
            robotHeroAnimator.Play("Reload");
            return;
        }

        // Priority 3: Directional Aimed Locomotion & Firing Animations
        bool isCombatPosture = isAimingThirdPerson || isHoldingLeftClickAim || _shootAnimTimer > 0f;

        if (horizontalSpeed > 0.35f)
        {
            Vector3 localVel = transform.InverseTransformDirection(vel);
            robotHeroAnimator.speed = 1.0f;

            if (isCombatPosture || horizontalSpeed <= 5.2f)
            {
                if (Mathf.Abs(localVel.x) > Mathf.Abs(localVel.z) * 0.85f)
                {
                    robotHeroAnimator.Play(localVel.x < 0f ? "WalkLeft_Shoot_AR" : "WalkRight_Shoot_AR");
                }
                else if (localVel.z < -0.35f)
                {
                    robotHeroAnimator.Play("WalkBack_Shoot_AR");
                }
                else if (horizontalSpeed > 3.8f)
                {
                    robotHeroAnimator.Play("Run_gunMiddle_AR");
                }
                else
                {
                    robotHeroAnimator.Play("WalkFront_Shoot_AR");
                }
            }
            else
            {
                // Full sprint out of combat
                robotHeroAnimator.Play("Run_guard_AR");
            }
        }
        else
        {
            robotHeroAnimator.speed = 1.0f;
            if (_shootAnimTimer > 0f || isHoldingLeftClickAim)
            {
                robotHeroAnimator.Play(_shootAnimClip);
            }
            else
            {
                robotHeroAnimator.Play("Idle_Shoot_Ar");
            }
        }
    }

    private void SetupLocalRobotHero()
    {
        GameObject robotPrefab = Resources.Load<GameObject>("RobotHero");
#if UNITY_EDITOR
        if (robotPrefab == null)
        {
            robotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab");
        }
#endif
        if (robotPrefab != null)
        {
            robotHeroVisual = Instantiate(robotPrefab, transform);
            robotHeroVisual.name = "Local_RobotHero";
            robotHeroVisual.transform.localPosition = Vector3.zero;
            robotHeroVisual.transform.localRotation = Quaternion.identity;
            robotHeroVisual.transform.localScale = Vector3.one;

            var cols = robotHeroVisual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);

            // Set tag to "Player" and layer to "Ignore Raycast" so Cinemachine AvoidObstacles ignores the player mesh
            int ignoreRayLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRayLayer < 0) ignoreRayLayer = 2;

            var allTransforms = robotHeroVisual.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                t.gameObject.tag = "Player";
                t.gameObject.layer = ignoreRayLayer;
            }

            // Ensure character's authentic sci-fi blaster (AssaultRifle) is active and visible
            Transform rifleTransform = robotHeroVisual.transform.Find("AssaultRifle");
            if (rifleTransform == null)
            {
                foreach (var t in allTransforms)
                {
                    if (t.name == "AssaultRifle")
                    {
                        rifleTransform = t;
                        break;
                    }
                }
            }
            if (rifleTransform != null)
            {
                rifleTransform.gameObject.SetActive(true);
                var rRend = rifleTransform.GetComponent<Renderer>();
                if (rRend != null) rRend.enabled = true;
            }

            Material urpMat = Resources.Load<Material>("RobotHero_URP");
#if UNITY_EDITOR
            if (urpMat == null) urpMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/SciFiWarriorPBRHPPolyart/Materials/RobotHero_URP.mat");
#endif
            if (urpMat != null)
            {
                var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++) rends[i].sharedMaterial = urpMat;
            }

            RuntimeAnimatorController animCtrl = Resources.Load<RuntimeAnimatorController>("RobotHero");
            if (animCtrl == null) animCtrl = Resources.Load<RuntimeAnimatorController>("SciFiWarrior");
#if UNITY_EDITOR
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/RobotHero.controller");
            if (animCtrl == null) animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/SciFiWarriorPBRHPPolyart/Animators/SciFiWarrior.controller");
#endif
            robotHeroAnimator = robotHeroVisual.GetComponent<Animator>();
            if (robotHeroAnimator == null) robotHeroAnimator = robotHeroVisual.AddComponent<Animator>();
            if (robotHeroAnimator != null)
            {
                robotHeroAnimator.applyRootMotion = false;
                if (animCtrl != null) robotHeroAnimator.runtimeAnimatorController = animCtrl;
            }

            // Find Spine / Chest and Hand / Arm bones to enable aim IK
            _spineBone = null;
            _gunArmBone = null;
            _gunHandBone = null;
            var allBones = robotHeroVisual.GetComponentsInChildren<Transform>(true);
            foreach (var b in allBones)
            {
                if (b.name == "ArmPosition_Right") _gunArmBone = b;
                if (b.name == "Hand_Right" || b.name == "RightHand") _gunHandBone = b;
                if (b.name == "Chest" && _spineBone == null) _spineBone = b;
                if (b.name == "Spine" && _spineBone == null) _spineBone = b;
            }

            if (_gunArmBone == null && _gunHandBone != null)
            {
                _gunArmBone = _gunHandBone;
            }

            if (_spineBone == null && robotHeroAnimator != null && robotHeroAnimator.isHuman)
            {
                _spineBone = robotHeroAnimator.GetBoneTransform(HumanBodyBones.Chest) 
                           ?? robotHeroAnimator.GetBoneTransform(HumanBodyBones.Spine);
            }

            SetMeshVisibility(!isThirdPerson);
        }
    }

    public void SetMeshVisibility(bool firstPerson)
    {
        if (robotHeroVisual != null)
        {
            var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (firstPerson)
                {
                    // In First-Person: set full-body character mesh to ShadowsOnly so the player casts a realistic
                    // shadow on the ground without third-person shoulder armor or waist rifle clipping into the camera!
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                else
                {
                    // In Third-Person: enable full authentic body mesh, weapon, and cast shadows
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
        }

        // Disable default primitive capsule mesh if any exists on the player hierarchy
        Transform capsuleChild = transform.Find("Capsule");
        if (capsuleChild != null)
        {
            var cr = capsuleChild.GetComponent<Renderer>();
            if (cr != null) cr.enabled = false;
        }

        // Toggle First-Person Blaster Weapon model visibility (hidden in 3rd person, visible in 1st person)
        var weapon = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
        if (weapon == null) weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon != null)
        {
            weapon.SetWeaponVisualActive(firstPerson);
        }
    }

    [System.NonSerialized] public Material currentCharacterMaterial;

    public void ApplyCharacterSkin(Texture2D texture, Color accentColor, string skinName)
    {
        if (robotHeroVisual == null) SetupLocalRobotHero();
        if (robotHeroVisual == null) return;

        Material baseMat = Resources.Load<Material>("RobotHero_URP");
        if (baseMat == null) baseMat = new Material(OasisShaderHelper.GetLitShader());

        Material customMat = new Material(baseMat);
        customMat.name = $"{skinName}_RuntimeMat";

        if (texture != null)
        {
            if (customMat.HasProperty("_BaseMap")) customMat.SetTexture("_BaseMap", texture);
            if (customMat.HasProperty("_MainTex")) customMat.SetTexture("_MainTex", texture);
        }
        customMat.color = Color.white;

        if (customMat.HasProperty("_EmissionColor"))
        {
            customMat.EnableKeyword("_EMISSION");
            customMat.SetColor("_EmissionColor", accentColor * 1.5f);
        }

        currentCharacterMaterial = customMat;

        var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            r.sharedMaterial = customMat;
        }

        // Sync the selected character skin material directly to the First-Person weapon mesh
        var weapon = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
        if (weapon == null) weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon != null)
        {
            weapon.SyncWeaponMaterial(customMat);
        }

        SetMeshVisibility(!isThirdPerson);
    }

    public void SetThirdPersonMode(bool thirdPerson)
    {
        isThirdPerson = thirdPerson;
        SetMeshVisibility(!isThirdPerson);
        OasisShooterManager.Instance?.ShowNotificationBanner(
            isThirdPerson ? "THIRD-PERSON CAMERA (PRESS V TO TOGGLE)" : "FIRST-PERSON CAMERA (PRESS V TO TOGGLE)", 2f);
    }

    private bool _wasGroundedLastFrame = true;
    private void HandleJumpEffect()
    {
        if (_fpc == null) return;

        // Ensure configured Jump key is directly captured and passed to StarterAssetsInputs jump flag
        bool jumpKeyPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Jump);
        if (jumpKeyPressed && _inputs != null)
        {
            _inputs.JumpInput(true);
        }

        if (_wasGroundedLastFrame && !_fpc.Grounded && _inputs != null && _inputs.jump)
        {
            // Just launched into a jump
            if (_audioSource != null)
            {
                _audioSource.PlayOneShot(ProceduralAudio.GetJumpClip(), 0.6f);
            }
            if (robotHeroAnimator != null)
            {
                robotHeroAnimator.Play("Jump", 0, 0f);
            }
        }

        _wasGroundedLastFrame = _fpc.Grounded;
    }

    private void HandleObstacleJumpAssist()
    {
        if (!enableObstacleJump || _characterController == null || _fpc == null) return;
        if (!_characterController.isGrounded)
        {
            _playerStuckTimer = 0f;
            return;
        }

        // Check if player is attempting forward movement
        bool movingForward = (_inputs != null && _inputs.move.y > 0.25f) ||
                             OasisKeybindManager.IsPressed(OasisAction.MoveForward) ||
                             OasisKeybindManager.IsKeyCodePressed(KeyCode.UpArrow);

        if (!movingForward)
        {
            _playerStuckTimer = 0f;
            _playerLastSampledPos = transform.position;
            return;
        }

        // Forward raycast at shin/knee level (0.35m up)
        Vector3 kneePos = transform.position + Vector3.up * 0.35f;
        bool lowObstacleHit = Physics.Raycast(kneePos, transform.forward, out RaycastHit hit, 0.95f);

        // Check if head/chest level is clear (vaultable obstacle, not an enormous wall)
        Vector3 chestPos = transform.position + Vector3.up * 1.5f;
        bool chestClear = !Physics.Raycast(chestPos, transform.forward, 1.1f);

        // Check if horizontal progress is blocked
        float horizontalSpeed = new Vector2(_characterController.velocity.x, _characterController.velocity.z).magnitude;
        bool isImpeded = (horizontalSpeed < 0.8f) && ((transform.position - _playerLastSampledPos).sqrMagnitude < 0.05f);

        if (lowObstacleHit && chestClear && hit.collider != null && !hit.collider.isTrigger)
        {
            _playerStuckTimer += Time.deltaTime;
            // Trigger vault jump if pushing against low obstacle for >0.25s or if impeded
            if (_playerStuckTimer >= 0.25f || isImpeded)
            {
                if (_inputs != null)
                {
                    _inputs.JumpInput(true);
                }
                _characterController.Move(transform.forward * (obstacleVaultForwardBoost * Time.deltaTime));

                _playerStuckTimer = 0f;
                _playerLastSampledPos = transform.position;
            }
        }
        else if (isImpeded)
        {
            // General stuck fallback (caught in rock crevices/terrain seams):
            _playerStuckTimer += Time.deltaTime;
            if (_playerStuckTimer >= 0.75f)
            {
                if (_inputs != null) _inputs.JumpInput(true);
                _characterController.Move((transform.forward + Vector3.up * 0.5f) * (obstacleVaultForwardBoost * Time.deltaTime));
                _playerStuckTimer = 0f;
                _playerLastSampledPos = transform.position;
            }
        }
        else
        {
            _playerStuckTimer = 0f;
            _playerLastSampledPos = transform.position;
        }
    }

    private Texture2D _crosshairWhiteTex;
    private float _hitmarkerDisplayTimer = 0f;

    public void TriggerHitmarker(bool isCrit = false)
    {
        _hitmarkerDisplayTimer = isCrit ? 0.24f : 0.16f;
        _isCritHitmarker = isCrit;
    }

    private void OnGUI()
    {
        // Only draw aiming reticle during active gameplay (not while character customizer, pause overlay, or post-match modal is open)
        var customizer = OasisCharacterCustomizer.Instance;
        if (customizer != null && customizer.isSelectionActive) return;
        if (InGamePauseManager.Instance != null && InGamePauseManager.Instance.IsPaused) return;

        var mgr = OasisShooterManager.Instance;
        if (mgr != null && (mgr.isSimulationMode || mgr.isMatchFinished))
        {
            if (mgr.isSimulationMode && !mgr.isMatchFinished)
            {
                float cx = Screen.width * 0.5f;
                DrawTopScoreboardHUD(cx);
            }
            return;
        }

        if (_crosshairWhiteTex == null)
        {
            _crosshairWhiteTex = new Texture2D(1, 1);
            _crosshairWhiteTex.SetPixel(0, 0, Color.white);
            _crosshairWhiteTex.Apply();
        }

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        // If aiming (Right click 3rd person or Left click 1st person), color cyan/red, tighter reticle
        Color reticleColor = isAimingThirdPerson 
            ? new Color(1.0f, 0.35f, 0.25f, 0.95f) 
            : new Color(0.2f, 0.95f, 1.0f, 0.85f);

        if (_hitmarkerDisplayTimer > 0f)
        {
            _hitmarkerDisplayTimer -= Time.deltaTime;
            reticleColor = Color.red;
        }

        GUI.color = reticleColor;

        // 1. Center Aim Dot (4x4)
        GUI.DrawTexture(new Rect(centerX - 2, centerY - 2, 4, 4), _crosshairWhiteTex);

        // 2. Dynamic Aiming Reticle Ticks (expands with firing bloom & movement velocity)
        float moveSpeedSpread = (_characterController != null)
            ? Mathf.Min(6f, new Vector2(_characterController.velocity.x, _characterController.velocity.z).magnitude * 0.55f)
            : 0f;
        float baseRadius = isAimingThirdPerson ? 10f : 16f;
        float radius = baseRadius + _reticleBloom + (isAimingThirdPerson ? moveSpeedSpread * 0.4f : moveSpeedSpread);
        float tickLen = isAimingThirdPerson ? 5f : 7f;
        float tickThick = 2f;

        // Top tick
        GUI.DrawTexture(new Rect(centerX - (tickThick * 0.5f), centerY - radius - tickLen, tickThick, tickLen), _crosshairWhiteTex);
        // Bottom tick
        GUI.DrawTexture(new Rect(centerX - (tickThick * 0.5f), centerY + radius, tickThick, tickLen), _crosshairWhiteTex);
        // Left tick
        GUI.DrawTexture(new Rect(centerX - radius - tickLen, centerY - (tickThick * 0.5f), tickLen, tickThick), _crosshairWhiteTex);
        // Right tick
        GUI.DrawTexture(new Rect(centerX + radius, centerY - (tickThick * 0.5f), tickLen, tickThick), _crosshairWhiteTex);

        // 3. Hitmarker Ticks (Gold double-bracket for Headshot/Melee CRIT, Crimson for standard hit)
        if (_hitmarkerDisplayTimer > 0f)
        {
            GUI.color = _isCritHitmarker ? new Color(1f, 0.88f, 0.15f, 1.0f) : new Color(1f, 0.18f, 0.18f, 0.95f);
            float hmDist = _isCritHitmarker ? 11f : 9f;
            float hmLen = _isCritHitmarker ? 8f : 6f;
            float hmThick = _isCritHitmarker ? 3f : 2f;
            GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY - hmDist - hmLen, hmLen, hmThick), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX + hmDist, centerY - hmDist - hmLen, hmLen, hmThick), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY + hmDist, hmLen, hmThick), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX + hmDist, centerY + hmDist, hmLen, hmThick), _crosshairWhiteTex);

            if (_isCritHitmarker)
            {
                // Vertical cross bars on diagonal corners for CRIT
                GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY - hmDist - hmLen, hmThick, hmLen), _crosshairWhiteTex);
                GUI.DrawTexture(new Rect(centerX + hmDist + hmLen - hmThick, centerY - hmDist - hmLen, hmThick, hmLen), _crosshairWhiteTex);
                GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY + hmDist - hmLen + hmThick, hmThick, hmLen), _crosshairWhiteTex);
                GUI.DrawTexture(new Rect(centerX + hmDist + hmLen - hmThick, centerY + hmDist - hmLen + hmThick, hmThick, hmLen), _crosshairWhiteTex);
            }
        }

        GUI.color = Color.white;

        // 4. Downed Player HUD Overlay (Vignette + Crawl Status)
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned)
        {
            // Fullscreen pulsing red/dark vignette around screen edges to communicate critical chassis state
            float pulse = 0.35f + Mathf.Sin(Time.time * 4f) * 0.15f;
            GUI.color = new Color(0.6f, 0.05f, 0.05f, pulse);
            float edgeThick = 45f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, edgeThick), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(0, Screen.height - edgeThick, Screen.width, edgeThick), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(0, 0, edgeThick, Screen.height), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(Screen.width - edgeThick, 0, edgeThick, Screen.height), _crosshairWhiteTex);

            float bw = 460f;
            float bh = 95f;
            float bx = centerX - (bw * 0.5f);
            float by = Screen.height * 0.58f;

            // Dark Backdrop
            GUI.color = new Color(0.08f, 0.04f, 0.04f, 0.92f);
            GUI.DrawTexture(new Rect(bx, by, bw, bh), _crosshairWhiteTex);

            // Red glowing border
            GUI.color = new Color(1f, 0.25f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(bx, by, bw, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(bx, by + bh - 2, bw, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(bx, by, 2, bh), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(bx + bw - 2, by, 2, bh), _crosshairWhiteTex);

            GUIStyle downedStyle = new GUIStyle(GUI.skin.label);
            downedStyle.alignment = TextAnchor.MiddleCenter;
            downedStyle.fontSize = 16;
            downedStyle.fontStyle = FontStyle.Bold;
            downedStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);

            float remaining = Mathf.Max(0f, OasisShooterManager.Instance.playerDownedTimer);
            GUI.Label(new Rect(bx, by + 8, bw, 24), $"CHASSIS CRITICAL // DOWNED ({remaining:F1}s)", downedStyle);

            float pProg = OasisShooterManager.Instance.playerReviveProgress;
            if (pProg > 0f)
            {
                downedStyle.fontSize = 13;
                downedStyle.normal.textColor = new Color(0.25f, 0.95f, 1f);
                GUI.Label(new Rect(bx, by + 34, bw, 20), $"BEING RESURRECTED BY SQUADMATE: {Mathf.RoundToInt(pProg * 100)}%", downedStyle);

                // Progress Bar
                float barW = bw - 40f;
                GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                GUI.DrawTexture(new Rect(bx + 20, by + 60, barW, 16), _crosshairWhiteTex);
                GUI.color = new Color(0.2f, 0.95f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(bx + 20, by + 60, barW * Mathf.Clamp01(pProg), 16), _crosshairWhiteTex);
            }
            else
            {
                downedStyle.fontSize = 12;
                downedStyle.fontStyle = FontStyle.Normal;
                downedStyle.normal.textColor = new Color(0.9f, 0.85f, 0.85f);
                GUI.Label(new Rect(bx, by + 36, bw, 42), "AI SQUADMATE RUSHING TO YOUR AID\nHOLD FOR EXTRACTION AND RESURRECTION", downedStyle);
            }

            GUI.color = Color.white;
            return;
        }

        // 5. Teammate Revival Prompt [HOLD E]
        if (_downedTeammateNearby != null || _downedRemotePlayerNearby != null)
        {
            string downedName = _downedTeammateNearby != null ? _downedTeammateNearby.botName : _downedRemotePlayerNearby.name;
            float downedTimer = _downedTeammateNearby != null ? _downedTeammateNearby.downedTimer : _downedRemotePlayerNearby.downedTimer;
            float rProg = _downedTeammateNearby != null ? _downedTeammateNearby.reviveProgress : _downedRemotePlayerNearby.reviveProgress;

            float pw = 380f;
            float ph = 85f;
            float px = centerX - (pw * 0.5f);
            float py = centerY + 50f;

            // Backdrop
            GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
            GUI.DrawTexture(new Rect(px, py, pw, ph), _crosshairWhiteTex);

            // Cyan border
            GUI.color = new Color(0.2f, 0.95f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(px, py, pw, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(px, py + ph - 2, pw, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(px, py, 2, ph), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(px + pw - 2, py, 2, ph), _crosshairWhiteTex);

            GUIStyle promptStyle = new GUIStyle(GUI.skin.label);
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.fontSize = 15;
            promptStyle.fontStyle = FontStyle.Bold;
            promptStyle.normal.textColor = new Color(0.2f, 0.95f, 1f);

            string revKey = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Revive));
            GUI.Label(new Rect(px, py + 8, pw, 22), $"[HOLD {revKey}] RESURRECT SQUADMATE", promptStyle);

            promptStyle.fontSize = 12;
            promptStyle.fontStyle = FontStyle.Normal;
            promptStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(px, py + 30, pw, 18), $"{downedName} (Bleedout: {downedTimer:F1}s)", promptStyle);

            float barW = pw - 30f;
            GUI.color = new Color(0.15f, 0.2f, 0.25f, 0.8f);
            GUI.DrawTexture(new Rect(px + 15, py + 54, barW, 14), _crosshairWhiteTex);

            if (rProg > 0f)
            {
                GUI.color = new Color(0.2f, 0.95f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(px + 15, py + 54, barW * Mathf.Clamp01(rProg), 14), _crosshairWhiteTex);
            }

            GUI.color = Color.white;
        }

        // 6. Top Match Scoreboard Header
        DrawTopScoreboardHUD(centerX);

        // 7. Bottom Left Health & Shield Display
        DrawBottomLeftHealthHUD();

        // 8. Bottom Right Tactical Weapon & Ammo Display
        DrawBottomRightAmmoHUD();

        // Mech Thruster Jet Boost HUD
        DrawThrusterJetHUD(centerX);

        // Operative Special Ability HUD
        DrawBottomCenterAbilityHUD(centerX);

        // 9. Floating Kill / Notification Banner
        DrawNotificationBannerHUD(centerX);
    }

    private void DrawTopScoreboardHUD(float centerX)
    {
        var mgr = OasisShooterManager.Instance;
        if (mgr == null || !mgr.isTeamBattleActive) return;

        int nTeams = Mathf.Clamp(mgr.activeNumTeams, 2, 4);
        float topY = 12f;

        // 1. Center Prominent Match Timer Box
        float timerW = 136f;
        float timerH = 50f;
        float timerX = centerX - (timerW * 0.5f);

        GUI.color = new Color(0.03f, 0.06f, 0.11f, 0.95f);
        GUI.DrawTexture(new Rect(timerX, topY, timerW, timerH), _crosshairWhiteTex);

        bool lowTime = mgr.matchTimeRemaining <= 30f;
        Color timerAccent = lowTime ? new Color(1.0f, 0.30f, 0.25f, 0.95f) : new Color(0.20f, 0.90f, 1.0f, 0.95f);
        GUI.color = timerAccent;
        GUI.DrawTexture(new Rect(timerX, topY, timerW, 3f), _crosshairWhiteTex);
        GUI.color = new Color(0.18f, 0.32f, 0.45f, 0.9f);
        GUI.DrawTexture(new Rect(timerX, topY + timerH - 2f, timerW, 2f), _crosshairWhiteTex);

        int minutes = Mathf.FloorToInt(mgr.matchTimeRemaining / 60f);
        int seconds = Mathf.FloorToInt(mgr.matchTimeRemaining % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        GUI.color = Color.white;
        GUIStyle timerStyle = new GUIStyle(GUI.skin.label);
        timerStyle.alignment = TextAnchor.MiddleCenter;
        timerStyle.fontSize = 22;
        timerStyle.fontStyle = FontStyle.Bold;
        timerStyle.normal.textColor = lowTime ? new Color(1.0f, 0.38f, 0.32f) : Color.white;
        GUI.Label(new Rect(timerX, topY + 3f, timerW, 28f), timeStr, timerStyle);

        // Subtitle under countdown timer
        GUIStyle goalStyle = new GUIStyle(GUI.skin.label);
        goalStyle.alignment = TextAnchor.MiddleCenter;
        goalStyle.fontSize = 10;
        goalStyle.fontStyle = FontStyle.Bold;
        goalStyle.normal.textColor = new Color(0.45f, 0.90f, 1.0f);
        GUI.Label(new Rect(timerX, topY + 30f, timerW, 16f), "3:00 TIMED MATCH", goalStyle);

        // Helper local method to draw each Team Score Card cleanly
        void DrawTeamCard(float cardX, float cardW, string teamName, int score, Color accent, bool alignRight)
        {
            float cardY = topY + 3f;
            float cardH = 44f;

            GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
            GUI.DrawTexture(new Rect(cardX, cardY, cardW, cardH), _crosshairWhiteTex);

            GUI.color = accent;
            if (alignRight)
                GUI.DrawTexture(new Rect(cardX + cardW - 4f, cardY, 4f, cardH), _crosshairWhiteTex);
            else
                GUI.DrawTexture(new Rect(cardX, cardY, 4f, cardH), _crosshairWhiteTex);

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.35f);
            GUI.DrawTexture(new Rect(cardX, cardY + cardH - 2f, cardW, 2f), _crosshairWhiteTex);

            GUI.color = Color.white;

            GUIStyle nameStyle = new GUIStyle(GUI.skin.label);
            nameStyle.fontSize = 13;
            nameStyle.fontStyle = FontStyle.Bold;
            nameStyle.normal.textColor = accent;
            nameStyle.alignment = alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

            GUIStyle scoreStyle = new GUIStyle(GUI.skin.label);
            scoreStyle.fontSize = 22;
            scoreStyle.fontStyle = FontStyle.Bold;
            scoreStyle.normal.textColor = Color.white;
            scoreStyle.alignment = alignRight ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            if (alignRight)
            {
                GUI.Label(new Rect(cardX + 12f, cardY + 4f, 56f, 36f), score.ToString(), scoreStyle);
                GUI.Label(new Rect(cardX + 54f, cardY + 4f, cardW - 66f, 36f), teamName, nameStyle);
            }
            else
            {
                GUI.Label(new Rect(cardX + 12f, cardY + 4f, cardW - 66f, 36f), teamName, nameStyle);
                GUI.Label(new Rect(cardX + cardW - 68f, cardY + 4f, 56f, 36f), score.ToString(), scoreStyle);
            }
        }

        float gap = 8f;
        float cardWidth = nTeams > 2 ? 132f : 154f;

        // Left of Timer: ALPHA (inner left) and CHARLIE (outer left if 3+ teams)
        DrawTeamCard(timerX - gap - cardWidth, cardWidth, "ALPHA", mgr.alphaKills, new Color(0.15f, 0.85f, 1.0f), false);
        if (nTeams >= 3)
        {
            DrawTeamCard(timerX - (gap * 2f) - (cardWidth * 2f), cardWidth, "CHARLIE", mgr.charlieKills, new Color(0.22f, 0.94f, 0.49f), false);
        }

        // Right of Timer: BRAVO (inner right) and DELTA (outer right if 4 teams)
        DrawTeamCard(timerX + timerW + gap, cardWidth, "BRAVO", mgr.bravoKills, new Color(1.0f, 0.35f, 0.27f), true);
        if (nTeams >= 4)
        {
            DrawTeamCard(timerX + timerW + (gap * 2f) + cardWidth, cardWidth, "DELTA", mgr.deltaKills, new Color(1.0f, 0.74f, 0.18f), true);
        }

        // 2. Slim Sub-Header Bar below Timer & Team Cards (ESC Hint + Relay Code)
        bool hasRelay = OasisNetworkManager.Instance != null && !string.IsNullOrEmpty(OasisNetworkManager.Instance.activeRelayJoinCode);
        string subText = hasRelay
            ? $"[ESC: PAUSE / SETTINGS]   •   RELAY JOIN CODE: {OasisNetworkManager.Instance.activeRelayJoinCode} [F10 COPY]"
            : "3:00 TIMED MATCH   •   [ESC: PAUSE / SETTINGS]";

        float subW = hasRelay ? 460f : 310f;
        float subH = 20f;
        float subX = centerX - (subW * 0.5f);
        float subY = topY + timerH + 4f;

        GUI.color = new Color(0.03f, 0.06f, 0.10f, 0.88f);
        GUI.DrawTexture(new Rect(subX, subY, subW, subH), _crosshairWhiteTex);

        GUI.color = Color.white;
        GUIStyle subBarStyle = new GUIStyle(GUI.skin.label);
        subBarStyle.fontSize = 11;
        subBarStyle.fontStyle = FontStyle.Bold;
        subBarStyle.alignment = TextAnchor.MiddleCenter;
        subBarStyle.normal.textColor = hasRelay ? new Color(0.40f, 1.0f, 0.65f) : new Color(0.78f, 0.88f, 0.96f);
        GUI.Label(new Rect(subX, subY, subW, subH), subText, subBarStyle);

        GUI.color = Color.white;
    }

    private void DrawBottomLeftHealthHUD()
    {
        var mgr = OasisShooterManager.Instance;
        if (mgr == null) return;

        float hpW = 280f;
        float hpH = 68f;
        float hpX = 36f;
        float hpY = Screen.height - 96f;

        // Dark tactical container
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);
        GUI.DrawTexture(new Rect(hpX, hpY, hpW, hpH), _crosshairWhiteTex);

        // Cyan left accent bar
        GUI.color = new Color(0.2f, 0.95f, 1.0f, 0.9f);
        GUI.DrawTexture(new Rect(hpX, hpY, 3, hpH), _crosshairWhiteTex);

        GUI.color = Color.white;

        // 1. Shield Bar (Top)
        float curHp = mgr.currentHealth;
        float maxHp = mgr.maxHealth > 0f ? mgr.maxHealth : 100f;
        float shieldPercent = Mathf.Clamp01(curHp / maxHp);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 11;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleLeft;
        labelStyle.normal.textColor = new Color(0.3f, 0.85f, 1.0f);
        GUI.Label(new Rect(hpX + 12, hpY + 8, 80, 16), "SHIELD", labelStyle);

        labelStyle.alignment = TextAnchor.MiddleRight;
        labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(hpX + hpW - 90, hpY + 8, 76, 16), $"{Mathf.CeilToInt(curHp)} / {Mathf.CeilToInt(maxHp)}", labelStyle);

        // Shield Track & Fill
        float barX = hpX + 12;
        float barW = hpW - 26;
        GUI.color = new Color(0.12f, 0.16f, 0.22f, 0.9f);
        GUI.DrawTexture(new Rect(barX, hpY + 26, barW, 8), _crosshairWhiteTex);

        GUI.color = new Color(0.15f, 0.85f, 1.0f, 0.95f);
        GUI.DrawTexture(new Rect(barX, hpY + 26, barW * shieldPercent, 8), _crosshairWhiteTex);

        GUI.color = Color.white;

        // 2. Health Bar (Bottom)
        labelStyle.alignment = TextAnchor.MiddleLeft;
        Color hpColor = (curHp > 60f) ? new Color(0.2f, 0.95f, 0.45f) : ((curHp > 30f) ? new Color(1.0f, 0.8f, 0.2f) : new Color(1.0f, 0.25f, 0.25f));
        labelStyle.normal.textColor = hpColor;
        GUI.Label(new Rect(hpX + 12, hpY + 38, 80, 16), "HEALTH", labelStyle);

        labelStyle.alignment = TextAnchor.MiddleRight;
        labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(hpX + hpW - 90, hpY + 38, 76, 16), $"{Mathf.CeilToInt(curHp)}%", labelStyle);

        // Health Track & Fill
        GUI.color = new Color(0.12f, 0.16f, 0.22f, 0.9f);
        GUI.DrawTexture(new Rect(barX, hpY + 54, barW, 6), _crosshairWhiteTex);

        GUI.color = hpColor;
        GUI.DrawTexture(new Rect(barX, hpY + 54, barW * (curHp / maxHp), 6), _crosshairWhiteTex);

        GUI.color = Color.white;
    }

    private void DrawBottomRightAmmoHUD()
    {
        var weapon = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
        if (weapon == null) weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon == null) return;

        float amW = 210f;
        float amH = 68f;
        float amX = Screen.width - amW - 36f;
        float amY = Screen.height - 96f;

        // Dark tactical container
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);
        GUI.DrawTexture(new Rect(amX, amY, amW, amH), _crosshairWhiteTex);

        // Right accent bar
        GUI.color = new Color(0.2f, 0.85f, 1.0f, 0.9f);
        GUI.DrawTexture(new Rect(amX + amW - 3, amY, 3, amH), _crosshairWhiteTex);

        GUI.color = Color.white;

        bool isDowned = OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned;

        // Weapon Label
        GUIStyle wStyle = new GUIStyle(GUI.skin.label);
        wStyle.fontSize = 11;
        wStyle.fontStyle = FontStyle.Bold;
        wStyle.alignment = TextAnchor.MiddleLeft;
        wStyle.normal.textColor = isDowned ? new Color(1.0f, 0.72f, 0.22f) : new Color(0.3f, 0.85f, 1.0f);
        GUI.Label(new Rect(amX + 14, amY + 8, 130, 16), isDowned ? "SINGLE-ROUND FIRE" : "PULSE BLASTER", wStyle);

        // Controls hint
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 10;
        hintStyle.alignment = TextAnchor.MiddleRight;
        hintStyle.normal.textColor = isDowned ? new Color(1.0f, 0.72f, 0.22f) : new Color(0.75f, 0.85f, 0.95f);
        string relKey = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Reload));
        GUI.Label(new Rect(amX + amW - 86, amY + 8, 72, 16), isDowned ? "[SEMI-AUTO]" : $"[{relKey}: RELOAD]", hintStyle);

        // Ammo Numbers
        GUIStyle ammoStyle = new GUIStyle(GUI.skin.label);
        ammoStyle.alignment = TextAnchor.MiddleCenter;
        ammoStyle.fontSize = 24;
        ammoStyle.fontStyle = FontStyle.Bold;

        if (weapon.isReloading)
        {
            ammoStyle.fontSize = 16;
            ammoStyle.normal.textColor = new Color(1.0f, 0.4f, 0.3f);
            GUI.Label(new Rect(amX + 14, amY + 28, amW - 28, 30), "RELOADING...", ammoStyle);
        }
        else
        {
            ammoStyle.normal.textColor = weapon.currentAmmo <= 3 ? new Color(1.0f, 0.35f, 0.35f) : Color.white;
            GUI.Label(new Rect(amX + 14, amY + 26, amW - 28, 32), $"{weapon.currentAmmo} / {weapon.maxAmmo}", ammoStyle);
        }

        GUI.color = Color.white;
    }

    private void DrawNotificationBannerHUD(float centerX)
    {
        var mgr = OasisShooterManager.Instance;
        if (mgr == null || string.IsNullOrEmpty(mgr.ActiveNotificationBanner)) return;

        float bnW = 560f;
        float bnH = 44f;
        float bnX = centerX - (bnW * 0.5f);
        float bnY = 92f;

        GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
        GUI.DrawTexture(new Rect(bnX, bnY, bnW, bnH), _crosshairWhiteTex);

        GUI.color = new Color(1.0f, 0.85f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(bnX, bnY, bnW, 2), _crosshairWhiteTex);
        GUI.DrawTexture(new Rect(bnX, bnY + bnH - 2, bnW, 2), _crosshairWhiteTex);

        GUI.color = Color.white;
        GUIStyle bannerStyle = new GUIStyle(GUI.skin.label);
        bannerStyle.alignment = TextAnchor.MiddleCenter;
        bannerStyle.fontSize = 14;
        bannerStyle.fontStyle = FontStyle.Bold;
        bannerStyle.richText = true;
        bannerStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(bnX + 10, bnY + 4, bnW - 20, bnH - 8), mgr.ActiveNotificationBanner, bannerStyle);
        GUI.color = Color.white;
    }

    private void DrawBottomCenterAbilityHUD(float centerX)
    {
        float abW = 270f;
        float abH = 62f;
        float abX = centerX - (abW * 0.5f);
        float abY = Screen.height - 78f;

        // Container background
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.90f);
        GUI.DrawTexture(new Rect(abX, abY, abW, abH), _crosshairWhiteTex);

        bool ready = abilityCooldownRemaining <= 0f;
        bool active = abilityActiveRemaining > 0f;

        Color borderCol = active ? new Color(1.0f, 0.85f, 0.2f, 0.95f) : (ready ? new Color(0.2f, 0.95f, 1.0f, 0.95f) : new Color(0.55f, 0.65f, 0.75f, 0.8f));

        // Top accent line
        GUI.color = borderCol;
        GUI.DrawTexture(new Rect(abX, abY, abW, 2), _crosshairWhiteTex);

        GUI.color = Color.white;

        string abilityKey = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Ability));

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 11;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleLeft;
        titleStyle.normal.textColor = borderCol;
        GUI.Label(new Rect(abX + 12, abY + 6, 170, 16), $"[{abilityKey}] {abilityName}", titleStyle);

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 10;
        statusStyle.fontStyle = FontStyle.Bold;
        statusStyle.alignment = TextAnchor.MiddleRight;

        if (active)
        {
            statusStyle.normal.textColor = new Color(1.0f, 0.85f, 0.2f);
            GUI.Label(new Rect(abX + abW - 95, abY + 6, 83, 16), $"ACTIVE {abilityActiveRemaining:F1}s", statusStyle);
        }
        else if (ready)
        {
            statusStyle.normal.textColor = new Color(0.2f, 0.95f, 1.0f);
            GUI.Label(new Rect(abX + abW - 95, abY + 6, 83, 16), $"READY [{abilityKey}]", statusStyle);
        }
        else
        {
            statusStyle.normal.textColor = new Color(0.85f, 0.90f, 0.95f);
            GUI.Label(new Rect(abX + abW - 95, abY + 6, 83, 16), $"{abilityCooldownRemaining:F1}s", statusStyle);
        }

        // Subtitle / Effect description
        GUIStyle descStyle = new GUIStyle(GUI.skin.label);
        descStyle.fontSize = 9;
        descStyle.fontStyle = FontStyle.Normal;
        descStyle.alignment = TextAnchor.MiddleLeft;
        descStyle.normal.textColor = new Color(0.80f, 0.86f, 0.92f);
        GUI.Label(new Rect(abX + 12, abY + 23, abW - 24, 15), abilityDescription, descStyle);

        // Progress / Cooldown bar
        float barX = abX + 12;
        float barY = abY + 42;
        float barW = abW - 24;
        float barH = 7;

        GUI.color = new Color(0.12f, 0.16f, 0.22f, 0.9f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _crosshairWhiteTex);

        if (active)
        {
            float pct = Mathf.Clamp01(abilityActiveRemaining / (abilityActiveDuration > 0 ? abilityActiveDuration : 1f));
            GUI.color = new Color(1.0f, 0.85f, 0.2f, 0.95f);
            GUI.DrawTexture(new Rect(barX, barY, barW * pct, barH), _crosshairWhiteTex);
        }
        else if (ready)
        {
            GUI.color = new Color(0.2f, 0.95f, 1.0f, 0.95f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _crosshairWhiteTex);
        }
        else
        {
            float pct = Mathf.Clamp01(1f - (abilityCooldownRemaining / (abilityCooldown > 0f ? abilityCooldown : 1f)));
            GUI.color = new Color(0.3f, 0.7f, 0.85f, 0.85f);
            GUI.DrawTexture(new Rect(barX, barY, barW * pct, barH), _crosshairWhiteTex);
        }

        GUI.color = Color.white;
    }

    private void DrawThrusterJetHUD(float centerX)
    {
        if (_fpc == null) return;

        float jW = 200f;
        float jH = 22f;
        float jX = centerX - (jW * 0.5f);
        float jY = Screen.height - 108f;

        float fuelPct = Mathf.Clamp01(_fpc.JetpackFuelRemaining / _fpc.JetpackMaxFuel);
        bool isActive = _fpc.IsJetpackActive;
        bool isFull = _fpc.JetpackFuelRemaining >= _fpc.JetpackMaxFuel - 0.05f;
        bool isEmpty = _fpc.JetpackFuelRemaining <= 0.05f;

        // Background
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);
        GUI.DrawTexture(new Rect(jX, jY, jW, jH), _crosshairWhiteTex);

        // Progress line
        if (isActive)
        {
            GUI.color = new Color(0.2f, 0.95f, 1.0f, 0.95f); // Bright active cyan
        }
        else if (isFull)
        {
            GUI.color = new Color(0.25f, 0.90f, 0.65f, 0.95f); // Teal/green ready
        }
        else if (isEmpty)
        {
            GUI.color = new Color(0.95f, 0.35f, 0.25f, 0.90f); // Depleted orange/red
        }
        else
        {
            GUI.color = new Color(0.95f, 0.75f, 0.25f, 0.85f); // Recharging amber
        }
        GUI.DrawTexture(new Rect(jX + 4, jY + jH - 3, (jW - 8) * fuelPct, 2), _crosshairWhiteTex);

        GUI.color = Color.white;
        GUIStyle jetStyle = new GUIStyle(GUI.skin.label);
        jetStyle.fontSize = 10;
        jetStyle.fontStyle = FontStyle.Bold;
        jetStyle.alignment = TextAnchor.MiddleCenter;
        jetStyle.normal.textColor = isActive ? new Color(0.2f, 0.95f, 1.0f) : (isFull ? new Color(0.35f, 0.95f, 0.70f) : (isEmpty ? new Color(0.95f, 0.4f, 0.3f) : new Color(0.95f, 0.85f, 0.45f)));

        string status;
        if (isActive)
        {
            status = $"JETPACK ACTIVE [{Mathf.RoundToInt(fuelPct * 100f)}%]";
        }
        else if (isFull)
        {
            status = "[SPACE HOLD] JETPACK READY";
        }
        else if (isEmpty)
        {
            status = "JETPACK DEPLETED // RECHARGING";
        }
        else
        {
            status = $"JETPACK RECHARGING [{Mathf.RoundToInt(fuelPct * 100f)}%]";
        }

        GUI.Label(new Rect(jX, jY + 1, jW, 16), status, jetStyle);
        GUI.color = Color.white;
    }

    private void HandleUpscalerToggle()
    {
        bool f10Pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null) f10Pressed = kb.f10Key.wasPressedThisFrame;
#else
        f10Pressed = Input.GetKeyDown(KeyCode.F10);
#endif
        if (!f10Pressed) return;

        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null) return;

        string toast = "";
        if (urpAsset.upscalingFilter == UpscalingFilterSelection.STP)
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.FSR;
            urpAsset.renderScale = 0.77f;
            urpAsset.fsrSharpness = 0.92f;
            toast = "[F10] UPSCALER: AMD FSR 1.0 (77% -> NATIVE)";
        }
        else if (urpAsset.upscalingFilter == UpscalingFilterSelection.FSR)
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.Auto;
            urpAsset.renderScale = 1.0f;
            toast = "[F10] UPSCALER: OFF / NATIVE 1:1 (100%)";
        }
        else
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.STP;
            urpAsset.renderScale = 0.77f;
            toast = "[F10] UPSCALER: UNITY 6 STP (77% -> NATIVE)";
        }

        OasisShooterManager.Instance?.ShowNotificationBanner(toast, 2.5f);
    }

    private void HandleFullscreenToggle()
    {
        bool f11Pressed = false;
        bool altEnterPressed = false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            f11Pressed = kb.f11Key.wasPressedThisFrame;
            altEnterPressed = (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed) && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        }
#else
        f11Pressed = Input.GetKeyDown(KeyCode.F11);
        altEnterPressed = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
#endif

        bool f10Pressed = false;
#if ENABLE_INPUT_SYSTEM
        if (kb != null) f10Pressed = kb.f10Key.wasPressedThisFrame;
#else
        f10Pressed = Input.GetKeyDown(KeyCode.F10);
#endif
        if (f10Pressed && OasisNetworkManager.Instance != null && !string.IsNullOrEmpty(OasisNetworkManager.Instance.activeRelayJoinCode))
        {
            GUIUtility.systemCopyBuffer = OasisNetworkManager.Instance.activeRelayJoinCode;
            OasisShooterManager.Instance?.ShowNotificationBanner($"RELAY JOIN CODE {OasisNetworkManager.Instance.activeRelayJoinCode} COPIED TO CLIPBOARD!", 3.5f);
        }

        if (f11Pressed || altEnterPressed)
        {
            if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen || Screen.fullScreen)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
                Screen.fullScreen = false;
            }
            else
            {
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                Screen.fullScreen = true;
            }
        }
    }
}
