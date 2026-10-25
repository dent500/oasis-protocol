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
    public Vector3 medicFieldPosition;

    private CharacterController _characterController;
    private StarterAssets.FirstPersonController _fpc;
    private StarterAssets.StarterAssetsInputs _inputs;
    private Transform _cameraTarget;
    private AudioSource _audioSource;
    private Transform _spineBone;
    private Transform _gunArmBone;
    public Transform gunArmBone => _gunArmBone;

    public Vector3 GetThirdPersonMuzzlePosition()
    {
        if (_gunArmBone != null)
        {
            // The rifle barrel extends forward along the gun arm bone's aim axis
            // In the FBX rig, the rifle tip is ~0.65m forward and ~0.05m up from the grip/bone pivot
            return _gunArmBone.position + _gunArmBone.forward * 0.65f + _gunArmBone.up * 0.05f;
        }
        // Fallback if bone not found
        return transform.position + transform.forward * 0.7f + transform.up * 1.2f + transform.right * 0.35f;
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
            (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isSimulationMode || OasisShooterManager.Instance.isMatchFinished)) ||
            (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive);

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
            SetThirdPersonMode(false); // Seamlessly enter gameplay in unobstructed first-person mode
        }

        HandleCameraViewToggle();

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
            if (_cameraTarget != null)
            {
                Vector3 camPos = _cameraTarget.localPosition;
                camPos.y = Mathf.Lerp(camPos.y, 0.45f, Time.deltaTime * 6f);
                _cameraTarget.localPosition = camPos;
            }
            float crawlSpeed = _characterController != null
                ? new Vector3(_characterController.velocity.x, 0f, _characterController.velocity.z).magnitude
                : 0f;
            bool isCrawling = crawlSpeed > 0.1f;

            if (robotHeroVisual != null)
            {
                float crawlSway = isCrawling ? Mathf.Sin(Time.time * 5.2f) * 6.0f : 0f;
                robotHeroVisual.transform.localPosition = Vector3.Lerp(robotHeroVisual.transform.localPosition, new Vector3(0f, 0.22f, -0.40f), Time.deltaTime * 10f);
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

    public void ApplyCrouchSpeed()
    {
        if (_fpc == null) return;
        if (isCrouching)
        {
            _fpc.MoveSpeed = baseRunSpeed * crouchSpeedMultiplier; // ~3.5 m/s tactical crouch walk
            _fpc.SprintSpeed = baseRunSpeed * crouchSpeedMultiplier; // disallow sprint during crouch
        }
        else
        {
            _fpc.MoveSpeed = baseRunSpeed;
            _fpc.SprintSpeed = baseSprintSpeed;
        }
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
        switch (opIndex)
        {
            case 0: // Titanium Heavy Warrior
                abilityName = "TITANIUM SLAM";
                abilityDescription = "Kinetic shockwave (45 AoE dmg) + 60 HP Overshield";
                abilityCooldown = 14f;
                abilityActiveDuration = 0f;
                break;
            case 1: // Desert Tactical Commando
                abilityName = "CLUSTER MORTAR";
                abilityDescription = "4-bomblet incendiary cluster barrage (50 dmg each)";
                abilityCooldown = 12f;
                abilityActiveDuration = 0f;
                break;
            case 2: // Cyborg Special Operative
                abilityName = "TACTICAL CLOAK";
                abilityDescription = "Stealth camo, breaks AI aggro, +35% speed & instant reload";
                abilityCooldown = 15f;
                abilityActiveDuration = 6f;
                break;
            case 3: // Unit-07 Combat Android
                abilityName = "CORE EMP PULSE";
                abilityDescription = "14m EMP wave stuns enemies (3.5s), strips shields & maxes ammo";
                abilityCooldown = 16f;
                abilityActiveDuration = 0f;
                break;
            case 4: // Wasteland Nomad
                abilityName = "ORBITAL RECON";
                abilityDescription = "Exposes all enemy locations through walls + 25% blaster dmg";
                abilityCooldown = 14f;
                abilityActiveDuration = 8f;
                break;
            case 5: // Combat Medic
                abilityName = "NANO-MENDER FIELD";
                abilityDescription = "AoE healing aura (18 HP/s for 6s) + instant squadmate revive";
                abilityCooldown = 18f;
                abilityActiveDuration = 6f;
                break;
            default:
                abilityName = "TACTICAL OVERCHARGE";
                abilityDescription = "Restores 50 shield & reloads blaster instantly";
                abilityCooldown = 12f;
                abilityActiveDuration = 0f;
                break;
        }
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

    private void ActivateSpecialAbility()
    {
        abilityCooldownRemaining = abilityCooldown;
        abilityActiveRemaining = abilityActiveDuration;

        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        int localId = (OasisNetworkManager.Instance != null) ? OasisNetworkManager.Instance.myPlayerId : 0;

        switch (currentOperativeIndex)
        {
            case 0: // Titanium Heavy Warrior: TITANIUM SLAM
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetBombExplosionClip(), 0.9f);
                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 0.2f, Vector3.up, false);

                // +60 Overshield
                if (OasisShooterManager.Instance != null)
                {
                    OasisShooterManager.Instance.currentHealth = Mathf.Min(OasisShooterManager.Instance.maxHealth + 60f, OasisShooterManager.Instance.currentHealth + 60f);
                }

                // Concussive shockwave (deal 45 damage to all enemy bots within 9m)
                foreach (var bot in OasisCombatAI.ActiveBots)
                {
                    if (bot == null || bot.isDead || bot.isDowned || bot.teamId == localTeam) continue;
                    float d = Vector3.Distance(transform.position, bot.transform.position);
                    if (d <= 9.0f)
                    {
                        Vector3 push = (bot.transform.position - transform.position).normalized;
                        bot.TakeDamage(45f, bot.transform.position + Vector3.up * 1f, push, localId, localTeam);
                        TriggerHitmarker();
                    }
                }
                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#26D9FF>[F: TITANIUM SLAM] SHOCKWAVE DISCHARGED // +60 OVERSHIELD</color>", 3.0f);
                break;
            }

            case 1: // Desert Tactical Commando: CLUSTER MORTAR
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetBombLaunchClip(), 0.95f);
                Vector3 launchOrigin = Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 0.6f : transform.position + Vector3.up * 1.5f;
                Vector3 fwd = Camera.main != null ? Camera.main.transform.forward : transform.forward;

                // Launch 4 bomblets in a cluster fan
                for (int i = 0; i < 4; i++)
                {
                    Vector3 spreadDir = Quaternion.Euler(Random.Range(-5f, 2f), Random.Range(-18f, 18f), 0f) * fwd;
                    Vector3 initialVel = spreadDir * Random.Range(16f, 22f) + Vector3.up * Random.Range(3f, 6f);
                    OasisPlasmaBomb.SpawnBomb(launchOrigin, initialVel, 50f, 4.5f, localTeam, localId, 0.45f + i * 0.15f);
                }
                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#FF9933>[F: CLUSTER MORTAR] 4-BOMBLET BARRAGE DEPLOYED!</color>", 3.0f);
                break;
            }

            case 2: // Cyborg Special Operative: TACTICAL CLOAK
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetAbilityChargeClip(), 0.9f);
                cloakTimeRemaining = 6.0f;

                // Boost speed by 35%
                if (_fpc != null)
                {
                    _fpc.MoveSpeed = baseRunSpeed * 1.35f;
                    _fpc.SprintSpeed = baseSprintSpeed * 1.35f;
                }

                // Instant blaster reload
                var wpn = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
                if (wpn == null) wpn = FindAnyObjectByType<BlasterWeapon>();
                if (wpn != null)
                {
                    wpn.currentAmmo = wpn.maxAmmo;
                    OasisShooterManager.Instance?.UpdateAmmo(wpn.currentAmmo, wpn.maxAmmo, false);
                }

                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, Vector3.up, false);
                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#33FFFF>[F: TACTICAL CLOAK] CAMOUFLAGE ACTIVE // +35% SPEED & AI AGGRO BROKEN (6s)</color>", 3.5f);
                break;
            }

            case 3: // Unit-07 Combat Android: CORE EMP PULSE
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetEMPClip(), 1.0f);
                BlasterVFX.SpawnImpact(transform.position + Vector3.up * 1f, Vector3.up, false);

                // EMP stuns all enemy bots within 14m and strips 50 shields
                int stunnedCount = 0;
                foreach (var bot in OasisCombatAI.ActiveBots)
                {
                    if (bot == null || bot.isDead || bot.isDowned || bot.teamId == localTeam) continue;
                    float d = Vector3.Distance(transform.position, bot.transform.position);
                    if (d <= 14.0f)
                    {
                        bot.ApplyEMPStun(3.5f);
                        stunnedCount++;
                    }
                }

                // Instantly refill blaster ammo
                var wpn = Camera.main != null ? Camera.main.GetComponent<BlasterWeapon>() : null;
                if (wpn == null) wpn = FindAnyObjectByType<BlasterWeapon>();
                if (wpn != null)
                {
                    wpn.currentAmmo = wpn.maxAmmo;
                    OasisShooterManager.Instance?.UpdateAmmo(wpn.currentAmmo, wpn.maxAmmo, false);
                }

                OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>[F: CORE EMP PULSE] {stunnedCount} HOSTILES STUNNED // SHIELDS STRIPPED</color>", 3.5f);
                break;
            }

            case 4: // Wasteland Nomad: ORBITAL RECON
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetScanPingClip(), 0.9f);
                scanReconTimeRemaining = 8.0f;
                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#55FF55>[F: ORBITAL RECON] ALL HOSTILES DETECTED & +25% BLASTER DAMAGE (8s)</color>", 3.5f);
                break;
            }

            case 5: // Combat Medic: NANO-MENDER FIELD
            {
                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHealPulseClip(), 0.95f);
                medicFieldTimeRemaining = 6.0f;
                medicFieldPosition = transform.position;

                // Immediate emergency revive of any downed teammate within 7m
                foreach (var bot in OasisCombatAI.ActiveBots)
                {
                    if (bot == null || bot.teamId != localTeam || !bot.isDowned) continue;
                    float d = Vector3.Distance(transform.position, bot.transform.position);
                    if (d <= 7.0f)
                    {
                        bot.Revive();
                        OasisShooterManager.Instance?.AddPlayerScore(75);
                        OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#FF5566>NANO-MENDER FIELD INSTANTLY RESURRECTED {bot.botName.ToUpper()}!</color>", 3.0f);
                    }
                }

                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#FF5566>[F: NANO-MENDER FIELD] FIELD DEPLOYED // REGENERATING 18 HP/S (6s)</color>", 3.5f);
                break;
            }
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

        // 1. Cloak handling
        if (cloakTimeRemaining > 0f)
        {
            cloakTimeRemaining -= Time.deltaTime;
            if (cloakTimeRemaining <= 0f && _fpc != null)
            {
                _fpc.MoveSpeed = baseRunSpeed;
                _fpc.SprintSpeed = baseSprintSpeed;
            }
        }

        // 2. Scan recon handling
        if (scanReconTimeRemaining > 0f)
        {
            scanReconTimeRemaining -= Time.deltaTime;
        }

        // 3. Medic Field healing aura
        if (medicFieldTimeRemaining > 0f)
        {
            medicFieldTimeRemaining -= Time.deltaTime;

            // Regenerate player health/shield
            if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isPlayerDowned)
            {
                float healTick = Time.deltaTime * 18f;
                OasisShooterManager.Instance.currentHealth = Mathf.Min(OasisShooterManager.Instance.maxHealth, OasisShooterManager.Instance.currentHealth + healTick);
            }

            // Heal friendly bots in 8m radius
            int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            foreach (var bot in OasisCombatAI.ActiveBots)
            {
                if (bot == null || bot.isDead || bot.teamId != localTeam) continue;
                float d = Vector3.Distance(transform.position, bot.transform.position);
                if (d <= 8.0f)
                {
                    if (bot.isDowned)
                    {
                        bot.reviveProgress += Time.deltaTime * 0.45f;
                        if (bot.reviveProgress >= 1f) bot.Revive();
                    }
                    else
                    {
                        bot.currentHealth = Mathf.Min(bot.maxHealth, bot.currentHealth + Time.deltaTime * 18f);
                    }
                }
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
        Camera cam = Camera.main;
        if (cam == null || _cameraTarget == null) return;

        if (!isThirdPerson)
        {
            // First-person view: place camera directly at eye-level target
            cam.transform.position = _cameraTarget.position;
            cam.transform.rotation = _cameraTarget.rotation;
        }
        else
        {
            // Third-person over-the-shoulder view:
            Vector3 shoulderOffset = isAimingThirdPerson
                ? new Vector3(0.48f, 0.10f, -1.6f)
                : new Vector3(0.58f, 0.18f, -2.5f);

            Vector3 desiredPos = _cameraTarget.position + _cameraTarget.rotation * shoulderOffset;
            Vector3 origin = _cameraTarget.position;
            Vector3 dir = desiredPos - origin;
            float maxDist = dir.magnitude;

            int mask = Physics.AllLayers & ~((1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << gameObject.layer) | (1 << 8));
            if (Physics.SphereCast(origin, 0.20f, dir.normalized, out RaycastHit hit, maxDist, mask, QueryTriggerInteraction.Ignore))
            {
                cam.transform.position = origin + dir.normalized * Mathf.Max(0.45f, hit.distance - 0.15f);
            }
            else
            {
                cam.transform.position = desiredPos;
            }

            cam.transform.rotation = _cameraTarget.rotation;

            // Failsafe: If camera gets too close to the character's head, hide mesh so it never blocks the screen
            float distToHead = Vector3.Distance(cam.transform.position, _cameraTarget.position);
            bool tooClose = distToHead < 0.75f;
            if (robotHeroVisual != null)
            {
                var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    rends[i].enabled = !tooClose;
                }
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

    private void UpdateCharacterAnimation()
    {
        if (robotHeroAnimator == null) return;

        float horizontalSpeed = _characterController != null 
            ? new Vector3(_characterController.velocity.x, 0f, _characterController.velocity.z).magnitude 
            : 0f;

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
        }
        else
        {
            robotHeroAnimator.speed = 1.0f;
            if (robotHeroVisual != null)
            {
                robotHeroVisual.transform.localRotation = Quaternion.identity;
            }

            if (horizontalSpeed > 2.0f)
            {
                robotHeroAnimator.Play("Run_guard_AR");
            }
            else if (horizontalSpeed > 0.35f)
            {
                robotHeroAnimator.Play("WalkFront_Shoot_AR");
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

            // Find Spine / Chest bone to enable vertical aim pitch (aiming up/down)
            _spineBone = null;
            _gunArmBone = null;
            var allBones = robotHeroVisual.GetComponentsInChildren<Transform>(true);
            foreach (var b in allBones)
            {
                if (b.name == "ArmPosition_Right") _gunArmBone = b;
                if (b.name == "Chest" && _spineBone == null) _spineBone = b;
                if (b.name == "Spine" && _spineBone == null) _spineBone = b;
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
                    // In First-Person: completely disable character body mesh so it NEVER blocks the forward camera
                    r.enabled = false;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                else
                {
                    // In Third-Person: enable full body mesh and cast shadows
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

        var rends = robotHeroVisual.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            r.sharedMaterial = customMat;
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

    public void TriggerHitmarker()
    {
        _hitmarkerDisplayTimer = 0.15f;
    }

    private void OnGUI()
    {
        // Only draw aiming reticle during active gameplay (not while character customizer or post-match modal is open)
        var customizer = OasisCharacterCustomizer.Instance;
        if (customizer != null && customizer.isSelectionActive) return;

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

        // 2. Aiming Reticle Ticks / Ring (Circle aesthetic)
        float radius = isAimingThirdPerson ? 12f : 18f;
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

        // 3. Diagonal Hitmarker ticks if hit enemy
        if (_hitmarkerDisplayTimer > 0f)
        {
            GUI.color = new Color(1f, 0.15f, 0.15f, 0.95f);
            float hmDist = 10f;
            float hmLen = 6f;
            // Draw diagonal hit indicators
            GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY - hmDist - hmLen, hmLen, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX + hmDist, centerY - hmDist - hmLen, hmLen, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX - hmDist - hmLen, centerY + hmDist, hmLen, 2), _crosshairWhiteTex);
            GUI.DrawTexture(new Rect(centerX + hmDist, centerY + hmDist, hmLen, 2), _crosshairWhiteTex);
        }

        GUI.color = Color.white;

        // 4. Downed Player HUD Overlay
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned)
        {
            float bw = 420f;
            float bh = 95f;
            float bx = centerX - (bw * 0.5f);
            float by = Screen.height * 0.62f;

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

        float sbW = 440f;
        float sbH = 44f;
        float sbX = centerX - (sbW * 0.5f);
        float sbY = 16f;

        // Dark tactical backdrop
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.90f);
        GUI.DrawTexture(new Rect(sbX, sbY, sbW, sbH), _crosshairWhiteTex);

        // Sleek top & bottom accent borders
        GUI.color = new Color(0.2f, 0.85f, 1.0f, 0.8f);
        GUI.DrawTexture(new Rect(sbX, sbY, sbW, 2), _crosshairWhiteTex);
        GUI.color = new Color(0.12f, 0.20f, 0.28f, 0.9f);
        GUI.DrawTexture(new Rect(sbX, sbY + sbH - 2, sbW, 2), _crosshairWhiteTex);

        // Styles
        GUIStyle teamStyle = new GUIStyle(GUI.skin.label);
        teamStyle.fontSize = 18;
        teamStyle.fontStyle = FontStyle.Bold;

        // ALPHA Score (Left)
        teamStyle.alignment = TextAnchor.MiddleLeft;
        teamStyle.normal.textColor = new Color(0.15f, 0.85f, 1.0f);
        GUI.Label(new Rect(sbX + 18, sbY + 4, 130, 24), $"ALPHA  {mgr.alphaKills}", teamStyle);

        // BRAVO Score (Right)
        teamStyle.alignment = TextAnchor.MiddleRight;
        teamStyle.normal.textColor = new Color(1.0f, 0.35f, 0.25f);
        GUI.Label(new Rect(sbX + sbW - 148, sbY + 4, 130, 24), $"{mgr.bravoKills}  BRAVO", teamStyle);

        // Center Countdown Timer
        int minutes = Mathf.FloorToInt(mgr.matchTimeRemaining / 60f);
        int seconds = Mathf.FloorToInt(mgr.matchTimeRemaining % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        GUIStyle timerStyle = new GUIStyle(GUI.skin.label);
        timerStyle.alignment = TextAnchor.MiddleCenter;
        timerStyle.fontSize = 19;
        timerStyle.fontStyle = FontStyle.Bold;
        timerStyle.normal.textColor = mgr.matchTimeRemaining <= 30f ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.95f, 0.95f, 0.95f);
        GUI.Label(new Rect(sbX + (sbW * 0.5f) - 60, sbY + 3, 120, 24), timeStr, timerStyle);

        // Subtitle: Goal
        GUIStyle subStyle = new GUIStyle(GUI.skin.label);
        subStyle.alignment = TextAnchor.MiddleCenter;
        subStyle.fontSize = 10;
        subStyle.fontStyle = FontStyle.Bold;
        subStyle.richText = true;
        subStyle.normal.textColor = new Color(0.65f, 0.75f, 0.85f);
        GUI.Label(new Rect(sbX + 20, sbY + 25, sbW - 40, 16), $"FIRST TO {mgr.targetKills} ELIMINATIONS", subStyle);

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

        // Weapon Label
        GUIStyle wStyle = new GUIStyle(GUI.skin.label);
        wStyle.fontSize = 11;
        wStyle.fontStyle = FontStyle.Bold;
        wStyle.alignment = TextAnchor.MiddleLeft;
        wStyle.normal.textColor = new Color(0.3f, 0.85f, 1.0f);
        GUI.Label(new Rect(amX + 14, amY + 8, 120, 16), "PULSE BLASTER", wStyle);

        // Controls hint
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 10;
        hintStyle.alignment = TextAnchor.MiddleRight;
        hintStyle.normal.textColor = new Color(0.6f, 0.7f, 0.8f);
        string relKey = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Reload));
        GUI.Label(new Rect(amX + amW - 86, amY + 8, 72, 16), $"[{relKey}: RELOAD]", hintStyle);

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
            ammoStyle.normal.textColor = weapon.currentAmmo <= 3 ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.2f, 0.95f, 1.0f);
            GUI.Label(new Rect(amX + 14, amY + 26, amW - 28, 32), $"{weapon.currentAmmo} / {weapon.maxAmmo}", ammoStyle);
        }

        GUI.color = Color.white;
    }

    private void DrawNotificationBannerHUD(float centerX)
    {
        var mgr = OasisShooterManager.Instance;
        if (mgr == null || string.IsNullOrEmpty(mgr.ActiveNotificationBanner)) return;

        float bnW = 560f;
        float bnH = 48f;
        float bnX = centerX - (bnW * 0.5f);
        float bnY = 82f;

        GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
        GUI.DrawTexture(new Rect(bnX, bnY, bnW, bnH), _crosshairWhiteTex);

        GUI.color = new Color(1.0f, 0.85f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(bnX, bnY, bnW, 2), _crosshairWhiteTex);
        GUI.DrawTexture(new Rect(bnX, bnY + bnH - 2, bnW, 2), _crosshairWhiteTex);

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

        Color borderCol = active ? new Color(1.0f, 0.85f, 0.2f, 0.95f) : (ready ? new Color(0.2f, 0.95f, 1.0f, 0.95f) : new Color(0.35f, 0.4f, 0.45f, 0.6f));

        // Top accent line
        GUI.color = borderCol;
        GUI.DrawTexture(new Rect(abX, abY, abW, 2), _crosshairWhiteTex);

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
            GUI.Label(new Rect(abX + abW - 95, abY + 6, 83, 16), "READY [F]", statusStyle);
        }
        else
        {
            statusStyle.normal.textColor = new Color(0.75f, 0.8f, 0.85f);
            GUI.Label(new Rect(abX + abW - 95, abY + 6, 83, 16), $"{abilityCooldownRemaining:F1}s", statusStyle);
        }

        // Subtitle / Effect description
        GUIStyle descStyle = new GUIStyle(GUI.skin.label);
        descStyle.fontSize = 9;
        descStyle.fontStyle = FontStyle.Normal;
        descStyle.alignment = TextAnchor.MiddleLeft;
        descStyle.normal.textColor = new Color(0.7f, 0.75f, 0.82f);
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

        float jW = 190f;
        float jH = 22f;
        float jX = centerX - (jW * 0.5f);
        float jY = Screen.height - 108f;

        bool ready = _fpc.thrusterCooldownRemaining <= 0f;
        float cdPct = ready ? 1f : Mathf.Clamp01(1f - (_fpc.thrusterCooldownRemaining / _fpc.ThrusterCooldown));

        // Background
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);
        GUI.DrawTexture(new Rect(jX, jY, jW, jH), _crosshairWhiteTex);

        // Progress line
        GUI.color = ready ? new Color(0.2f, 0.95f, 1.0f, 0.95f) : new Color(0.95f, 0.55f, 0.15f, 0.85f);
        GUI.DrawTexture(new Rect(jX + 4, jY + jH - 3, (jW - 8) * cdPct, 2), _crosshairWhiteTex);

        GUIStyle jetStyle = new GUIStyle(GUI.skin.label);
        jetStyle.fontSize = 10;
        jetStyle.fontStyle = FontStyle.Bold;
        jetStyle.alignment = TextAnchor.MiddleCenter;
        jetStyle.normal.textColor = ready ? new Color(0.2f, 0.95f, 1.0f) : new Color(0.7f, 0.75f, 0.8f);

        string status = ready ? "[SPACE x2] JET BOOST READY" : $"JET CHARGING {_fpc.thrusterCooldownRemaining:F1}s";
        GUI.Label(new Rect(jX, jY + 1, jW, 16), status, jetStyle);
        GUI.color = Color.white;
    }

    private string _upscalerToastText = "";
    private float _upscalerToastTimer = 0f;

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

        if (urpAsset.upscalingFilter == UpscalingFilterSelection.STP)
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.FSR;
            urpAsset.renderScale = 0.77f;
            urpAsset.fsrSharpness = 0.92f;
            _upscalerToastText = "[F10] UPSCALER: AMD FSR 1.0 (77% -> NATIVE)";
        }
        else if (urpAsset.upscalingFilter == UpscalingFilterSelection.FSR)
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.Auto;
            urpAsset.renderScale = 1.0f;
            _upscalerToastText = "[F10] UPSCALER: OFF / NATIVE 1:1 (100%)";
        }
        else
        {
            urpAsset.upscalingFilter = UpscalingFilterSelection.STP;
            urpAsset.renderScale = 0.77f;
            _upscalerToastText = "[F10] UPSCALER: UNITY 6 STP (77% -> NATIVE)";
        }

        _upscalerToastTimer = 2.5f;
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
