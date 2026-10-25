using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-Person Blaster Weapon supporting multi-archetype combat:
/// 1. Standard Kinetic Rifle (balanced assault blaster)
/// 2. Plasma Scatter Shotgun (8-pellet wide spread close-range punch)
/// 3. Continuous Phase Laser Beam (sustained tracking heat beam)
/// 4. Micro-Bomb Flak Mortar (ballistic plasma explosive bombs with AoE splash)
/// 
/// Features procedural 3D weapon models tailored to each archetype, procedural sound synthesis,
/// recoil, sway, holographic ADS scope, and dynamic weapon swapping.
/// </summary>
public class BlasterWeapon : MonoBehaviour
{
    public enum WeaponArchetype
    {
        StandardRifle,
        ScatterShotgun,
        ContinuousBeam,
        BombMortar,
        DefenseCarbine,
        DualPistols
    }

    [System.NonSerialized] public WeaponArchetype currentArchetype = WeaponArchetype.StandardRifle;
    [System.NonSerialized] public string weaponDisplayName = "Long-Range Marksman Rifle";

    [Header("Weapon Stats")]
    public float damage = 25f;
    public float fireRate = 0.18f;
    public float range = 150f;
    public int maxAmmo = 24;
    public int currentAmmo;
    public float reloadTime = 1.1f;

    private const int shotgunPelletCount = 8;
    private const float shotgunSpreadAngle = 0.055f;
    private const float beamTickInterval = 0.08f;
    private const float beamTickDamage = 11f;

    [Header("Weapon Positioning")]
    public Vector3 hipPosition = new Vector3(0.28f, -0.24f, 0.55f);
    public Vector3 aimPosition = new Vector3(0f, -0.16f, 0.45f);
    public float swayAmount = 0.02f;
    public float maxSway = 0.06f;
    public float swaySmooth = 8f;

    [Header("Recoil")]
    public float recoilKickback = 0.08f;
    public float recoilRotation = 4f;
    public float recoilRecovery = 12f;

    [Header("Aim Down Sights & Precision Scope")]
    public bool isScoped = false;
    public float defaultFOV = 60f;
    public float scopedFOV = 22f;
    public float scopeZoomSpeed = 16f;

    private Camera _camera;
    private Transform _weaponRoot;
    private Transform _muzzleTransform;
    private Light _muzzleFlashLight;
    private AudioSource _audioSource;
    private AudioSource _beamAudioSource;
    private StarterAssets.FirstPersonController _fpc;
    private float _origRotationSpeed = 1.0f;

    // Continuous Beam Line
    private LineRenderer _continuousBeamLine;
    private float _nextBeamTickTime = 0f;
    private bool _isBeamActive = false;

    // Holographic Scope UI
    private GameObject _scopeCanvas;
    private Text _scopeRangeText;
    private Text _scopeWeaponLabel;

    private float _nextFireTime = 0f;
    private bool _isReloading = false;
    public bool isReloading => _isReloading;
    private Vector3 _currentRecoilPos;
    private Vector3 _currentRecoilRot;

    public void SetWeaponVisualActive(bool active)
    {
        if (_weaponRoot != null)
        {
            bool is3rd = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
            _weaponRoot.gameObject.SetActive(active && !is3rd);
        }
    }

    private void Start()
    {
        _camera = GetComponentInParent<Camera>() ?? Camera.main;
        if (_camera != null) defaultFOV = _camera.fieldOfView;

        _fpc = GetComponentInParent<StarterAssets.FirstPersonController>();
        if (_fpc != null) _origRotationSpeed = _fpc.RotationSpeed;

        // Create main audio source
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 0f;

        // Create beam audio source
        _beamAudioSource = gameObject.AddComponent<AudioSource>();
        _beamAudioSource.spatialBlend = 0f;
        _beamAudioSource.loop = true;

        // Create beam LineRenderer
        GameObject beamObj = new GameObject("ContinuousBeamRenderer");
        beamObj.transform.SetParent(transform, false);
        _continuousBeamLine = beamObj.AddComponent<LineRenderer>();
        _continuousBeamLine.material = OasisShaderHelper.CreateUnlitMaterial(new Color(0.1f, 0.95f, 1f, 1f));
        _continuousBeamLine.startColor = new Color(0.2f, 0.95f, 1f, 0.95f);
        _continuousBeamLine.endColor = new Color(0.85f, 0.35f, 1f, 0.85f);
        _continuousBeamLine.startWidth = 0.08f;
        _continuousBeamLine.endWidth = 0.05f;
        _continuousBeamLine.positionCount = 2;
        _continuousBeamLine.enabled = false;
        _continuousBeamLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _continuousBeamLine.receiveShadows = false;

        // Configure initial archetype based on selected operative chassis
        int opIndex = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
        ApplyOperativeWeapon(opIndex);

        // Build scope overlay
        BuildScopeOverlayUI();

        // Sync initial visibility based on third person mode
        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson)
        {
            SetWeaponVisualActive(false);
        }

        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
    }

    /// <summary>
    /// Configures weapon stats, displays, and 3D procedural chassis for the chosen archetype.
    /// </summary>
    public void SetWeaponArchetype(WeaponArchetype archetype, bool showBanner = true)
    {
        currentArchetype = archetype;

        switch (archetype)
        {
            case WeaponArchetype.ScatterShotgun:
                weaponDisplayName = "Plasma Scatter Shotgun";
                damage = 14f; // 14 x 8 pellets = 112 potential burst damage
                fireRate = 0.65f;
                range = 55f;
                maxAmmo = 12;
                reloadTime = 1.35f;
                recoilKickback = 0.20f;
                recoilRotation = 7.5f;
                recoilRecovery = 10f;
                break;

            case WeaponArchetype.ContinuousBeam:
                weaponDisplayName = "Continuous Phase Laser";
                damage = beamTickDamage; // 150 DPS
                fireRate = beamTickInterval;
                range = 85f;
                maxAmmo = 90;
                reloadTime = 1.2f;
                recoilKickback = 0.008f;
                recoilRotation = 0.4f;
                recoilRecovery = 16f;
                break;

            case WeaponArchetype.BombMortar:
                weaponDisplayName = "High-Velocity Flak Cannon";
                damage = 110f; // High-velocity direct impact + airburst AoE
                fireRate = 0.65f;
                range = 160f;
                maxAmmo = 10;
                reloadTime = 1.3f;
                recoilKickback = 0.18f;
                recoilRotation = 6.0f;
                recoilRecovery = 12f;
                break;

            case WeaponArchetype.DefenseCarbine:
                weaponDisplayName = "Tactical Defense Carbine";
                damage = 24f;
                fireRate = 0.12f; // Rapid suppressive fire
                range = 160f;
                maxAmmo = 32;
                reloadTime = 0.95f;
                recoilKickback = 0.04f;
                recoilRotation = 2.0f;
                recoilRecovery = 16f;
                break;

            case WeaponArchetype.DualPistols:
                weaponDisplayName = "High-Caliber Dual Pistols";
                damage = 34f;
                fireRate = 0.14f;
                range = 110f;
                maxAmmo = 24;
                reloadTime = 0.85f;
                recoilKickback = 0.06f;
                recoilRotation = 3.0f;
                recoilRecovery = 18f;
                break;

            case WeaponArchetype.StandardRifle:
            default:
                weaponDisplayName = "Long-Range Marksman Rifle";
                damage = 38f; // Heavy precision marksman caliber
                fireRate = 0.16f;
                range = 350f;
                maxAmmo = 28;
                reloadTime = 1.05f;
                recoilKickback = 0.05f;
                recoilRotation = 2.2f;
                recoilRecovery = 15f;
                break;
        }

        currentAmmo = maxAmmo;
        _isReloading = false;

        // Stop beam if active
        UpdateContinuousBeam(false);

        // Rebuild 3D visual gun model
        BuildProceduralGunModel();

        // Update HUD
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);

        if (showBanner && OasisShooterManager.Instance != null)
        {
            string colorHex = archetype == WeaponArchetype.BombMortar ? "#FFAA00" :
                             archetype == WeaponArchetype.ContinuousBeam ? "#00E5FF" :
                             archetype == WeaponArchetype.ScatterShotgun ? "#FF5533" : "#44DDFF";

            OasisShooterManager.Instance.ShowNotificationBanner(
                $"<color={colorHex}>WEAPON EQUIPPED // {weaponDisplayName.ToUpper()}</color>", 1.5f);
        }
    }

    private void OnDisable()
    {
        if (_fpc != null)
        {
            _fpc.RotationSpeed = _origRotationSpeed;
        }
        UpdateContinuousBeam(false);
    }

    private void Update()
    {
        bool inactiveCombat =
            (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isMatchFinished || OasisShooterManager.Instance.isSimulationMode)) ||
            (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive);

        if (inactiveCombat)
        {
            isScoped = false;
            UpdateContinuousBeam(false);
            if (_scopeCanvas != null && _scopeCanvas.activeSelf) _scopeCanvas.SetActive(false);
            if (_camera != null) _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, defaultFOV, Time.deltaTime * scopeZoomSpeed);
            if (_fpc != null) _fpc.RotationSpeed = _origRotationSpeed;
            return;
        }

        HandleInput();
        UpdateScopeState();
        HandleSwayAndBob();
        RecoverRecoil();
    }

    public void ApplyOperativeWeapon(int opIndex)
    {
        switch (opIndex)
        {
            case 0: // Titanium Heavy Warrior -> Micro-Bomb Flak Mortar
                SetWeaponArchetype(WeaponArchetype.BombMortar, false);
                break;
            case 1: // Desert Tactical Commando -> Plasma Scatter Shotgun
                SetWeaponArchetype(WeaponArchetype.ScatterShotgun, false);
                break;
            case 2: // Cyborg Special Operative -> Continuous Phase Laser
                SetWeaponArchetype(WeaponArchetype.ContinuousBeam, false);
                break;
            case 3: // Unit-07 Combat Android -> Continuous Phase Laser
                SetWeaponArchetype(WeaponArchetype.ContinuousBeam, false);
                break;
            case 4: // Wasteland Nomad -> Long-Range Precision Marksman Rifle
                SetWeaponArchetype(WeaponArchetype.StandardRifle, false);
                break;
            case 5: // Combat Medic -> Tactical Defense Carbine
                SetWeaponArchetype(WeaponArchetype.DefenseCarbine, false);
                break;
            case 6: // Cyber Outlaw Specialist -> High-Caliber Dual Pistols
                SetWeaponArchetype(WeaponArchetype.DualPistols, false);
                break;
            default:
                SetWeaponArchetype(WeaponArchetype.StandardRifle, false);
                break;
        }
    }

    private void HandleInput()
    {
        bool fireHeld = false;
        bool reloadPressed = false;
        bool scopeHeld = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            fireHeld = Mouse.current.leftButton.isPressed;
            scopeHeld = Mouse.current.rightButton.isPressed;
        }
#else
        fireHeld = Input.GetMouseButton(0);
        scopeHeld = Input.GetMouseButton(1);
#endif
        reloadPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Reload);

        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        bool is3rdAiming = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isAimingThirdPerson;

        isScoped = scopeHeld;

        if (reloadPressed && currentAmmo < maxAmmo && !_isReloading)
        {
            StartCoroutine(Reload());
        }

        // Water sanctuary restriction
        if (OasisWaterZone.IsInWater(transform.position) && fireHeld)
        {
            UpdateContinuousBeam(false);
            OasisShooterManager.Instance?.ShowNotificationBanner("<color=#26D9FF>WATER NO-FIRE SANCTUARY // WEAPONS DISABLED IN WATER</color>", 1.5f);
            return;
        }

        // Firing handling per archetype
        if (currentArchetype == WeaponArchetype.ContinuousBeam)
        {
            if (fireHeld && currentAmmo > 0 && !_isReloading)
            {
                UpdateContinuousBeam(true);
            }
            else
            {
                UpdateContinuousBeam(false);
                if (fireHeld && currentAmmo <= 0 && !_isReloading)
                {
                    StartCoroutine(Reload());
                }
            }
        }
        else
        {
            UpdateContinuousBeam(false);
            if (fireHeld && Time.time >= _nextFireTime)
            {
                if (currentAmmo > 0 && !_isReloading)
                {
                    Fire();
                }
                else if (currentAmmo <= 0 && !_isReloading)
                {
                    StartCoroutine(Reload());
                }
            }
        }
    }

    private void UpdateScopeState()
    {
        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        bool is3rdAiming = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isAimingThirdPerson;

        float targetFOV = defaultFOV;
        if (!is3rdPerson)
        {
            if (isScoped)
            {
                switch (currentArchetype)
                {
                    case WeaponArchetype.StandardRifle:
                        targetFOV = 18f; // High-powered 6x marksman optic magnification!
                        break;
                    case WeaponArchetype.DefenseCarbine:
                        targetFOV = 36f;
                        break;
                    case WeaponArchetype.BombMortar:
                        targetFOV = 42f;
                        break;
                    case WeaponArchetype.ScatterShotgun:
                        targetFOV = 45f;
                        break;
                    case WeaponArchetype.DualPistols:
                        targetFOV = 42f;
                        break;
                    case WeaponArchetype.ContinuousBeam:
                    default:
                        targetFOV = 35f;
                        break;
                }
            }
        }
        else if (is3rdAiming)
        {
            targetFOV = currentArchetype == WeaponArchetype.StandardRifle ? 36f : 48f;
        }

        if (_camera != null)
        {
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFOV, Time.deltaTime * scopeZoomSpeed);
        }

        if (_weaponRoot != null)
        {
            if (_weaponRoot.gameObject.activeSelf == is3rdPerson)
            {
                _weaponRoot.gameObject.SetActive(!is3rdPerson);
            }
            if (!is3rdPerson)
            {
                Vector3 targetPos = isScoped
                    ? (currentArchetype == WeaponArchetype.StandardRifle ? new Vector3(0f, -0.42f, 0.40f) : aimPosition)
                    : hipPosition;
                _weaponRoot.localPosition = Vector3.Lerp(_weaponRoot.localPosition, targetPos, Time.deltaTime * scopeZoomSpeed);
            }
        }

        if (_fpc != null)
        {
            bool isPrecisionAim = (!is3rdPerson && isScoped) || (is3rdPerson && is3rdAiming);
            float aimSensitivity = currentArchetype == WeaponArchetype.StandardRifle ? 0.25f : 0.45f;
            _fpc.RotationSpeed = isPrecisionAim ? (_origRotationSpeed * aimSensitivity) : _origRotationSpeed;
        }

        if (_scopeCanvas != null)
        {
            _scopeCanvas.SetActive(!is3rdPerson && isScoped);
            if (!is3rdPerson && _scopeRangeText != null && _camera != null)
            {
                Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (Physics.Raycast(ray, out RaycastHit hit, 350f))
                {
                    _scopeRangeText.text = $"RANGE: {hit.distance:F1}M";
                }
                else
                {
                    _scopeRangeText.text = "RANGE: ---";
                }
            }
            if (_scopeWeaponLabel != null)
            {
                _scopeWeaponLabel.text = currentArchetype == WeaponArchetype.StandardRifle
                    ? "[ 6X LONG-RANGE PRECISION OPTIC // CALIBRATED ]"
                    : $"[ TACTICAL HOLOSIGHT // {weaponDisplayName.ToUpper()} ]";
            }
        }
    }

    private void Fire()
    {
        _nextFireTime = Time.time + fireRate;
        currentAmmo--;
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
        OasisAITelemetryLogger.Instance?.RecordPlayerShotFired();

        // Apply recoil impulse
        _currentRecoilPos += new Vector3(0, 0.015f, -recoilKickback);
        _currentRecoilRot += new Vector3(-recoilRotation, Random.Range(-1.2f, 1.2f), 0);

        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.robotHeroAnimator != null)
        {
            OasisCombatMovement.Instance.robotHeroAnimator.Play("Shoot_Autoshot_AR", 0, 0f);
        }

        if (_muzzleFlashLight != null)
        {
            StartCoroutine(FlashMuzzleLight());
        }

        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        Vector3 muzzlePos = GetMuzzlePosition();
        int localId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        Transform playerRoot = OasisCombatMovement.Instance != null ? OasisCombatMovement.Instance.transform : null;

        switch (currentArchetype)
        {
            case WeaponArchetype.ScatterShotgun:
                FireScatterShotgun(ray, muzzlePos, playerRoot, localTeam, localId);
                break;

            case WeaponArchetype.BombMortar:
                FireBombMortar(ray, muzzlePos, localTeam, localId);
                break;

            case WeaponArchetype.DefenseCarbine:
                FireDefenseCarbine(ray, muzzlePos, playerRoot, localTeam, localId);
                break;

            case WeaponArchetype.DualPistols:
                FireDualPistols(ray, muzzlePos, playerRoot, localTeam, localId);
                break;

            case WeaponArchetype.StandardRifle:
            default:
                FireStandardRifle(ray, muzzlePos, playerRoot, localTeam, localId);
                break;
        }
    }

    private Vector3 GetMuzzlePosition()
    {
        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        if (is3rdPerson && OasisCombatMovement.Instance.robotHeroVisual != null)
        {
            return OasisCombatMovement.Instance.GetThirdPersonMuzzlePosition();
        }
        return _muzzleTransform != null ? _muzzleTransform.position : transform.position + transform.forward * 0.5f;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 1: Long-Range Precision Marksman Rifle
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireStandardRifle(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.44f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        BlasterVFX.SpawnLaserBeam(muzzlePos, targetPoint, new Color(0.25f, 0.95f, 1f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(muzzlePos, targetPoint, hitHostile);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 2: Plasma Scatter Shotgun (8 Pellets)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireScatterShotgun(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetShotgunShotClip(), 0.55f);
        }

        Color shotgunColor = new Color(1f, 0.45f, 0.1f);
        bool anyHostileHit = false;

        for (int p = 0; p < shotgunPelletCount; p++)
        {
            Vector2 spreadCircle = Random.insideUnitCircle * shotgunSpreadAngle;
            Vector3 pelletDir = (ray.direction + _camera.transform.right * spreadCircle.x + _camera.transform.up * spreadCircle.y).normalized;
            Ray pelletRay = new Ray(ray.origin, pelletDir);

            Vector3 hitPt = PerformRaycastShot(pelletRay, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile, true);
            if (hitHostile) anyHostileHit = true;

            BlasterVFX.SpawnLaserBeam(muzzlePos, hitPt, shotgunColor);
        }

        OasisNetworkManager.Instance?.BroadcastLaserFire(muzzlePos, ray.GetPoint(range), anyHostileHit);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 3: High-Velocity Flak Cannon (Airburst & Cluster Fragmentation)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireBombMortar(Ray ray, Vector3 muzzlePos, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBombLaunchClip(), 0.75f);
        }

        // High-velocity rocket-assisted flak trajectory (80 m/s) with proximity airburst
        Vector3 launchVel = ray.direction * 80f;
        OasisPlasmaBomb.SpawnBomb(muzzlePos, launchVel, localTeam, localId);

        OasisShooterManager.Instance?.TriggerCameraShake(0.22f, 0.15f);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 5: Tactical Defense Carbine (Rapid Fire Support)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireDefenseCarbine(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.32f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        BlasterVFX.SpawnLaserBeam(muzzlePos, targetPoint, new Color(0.2f, 0.95f, 0.5f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(muzzlePos, targetPoint, hitHostile);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 6: High-Caliber Dual Pistols (High Mobility Gunslinger)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireDualPistols(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.42f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        BlasterVFX.SpawnLaserBeam(muzzlePos, targetPoint, new Color(0.92f, 0.35f, 0.95f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(muzzlePos, targetPoint, hitHostile);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 4: Continuous Phase Laser Beam
    // ─────────────────────────────────────────────────────────────────────────────
    private void UpdateContinuousBeam(bool active)
    {
        if (!active)
        {
            if (_isBeamActive)
            {
                _isBeamActive = false;
                if (_continuousBeamLine != null) _continuousBeamLine.enabled = false;
                if (_beamAudioSource != null && _beamAudioSource.isPlaying) _beamAudioSource.Stop();
                if (_weaponRoot != null) _weaponRoot.localRotation = Quaternion.identity;
            }
            return;
        }

        _isBeamActive = true;
        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        int localId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        Transform playerRoot = OasisCombatMovement.Instance != null ? OasisCombatMovement.Instance.transform : null;

        // Sound
        if (_beamAudioSource != null && !_beamAudioSource.isPlaying)
        {
            _beamAudioSource.clip = ProceduralAudio.GetBeamHumClip();
            _beamAudioSource.volume = 0.35f;
            _beamAudioSource.Play();
        }

        // Trace hit point
        Vector3 targetPoint = ray.GetPoint(range);
        RaycastHit[] hits = Physics.RaycastAll(ray, range, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float closestDist = range;
        RaycastHit bestHit = default;
        bool hasHit = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.collider.isTrigger) continue;
            if (playerRoot != null && (h.collider.transform.IsChildOf(playerRoot) || h.collider.transform == playerRoot)) continue;

            OasisCombatAI aiCheck = h.collider.GetComponentInParent<OasisCombatAI>();
            if (aiCheck != null && aiCheck.teamId == localTeam) continue;

            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                bestHit = h;
                hasHit = true;
            }
        }

        if (hasHit)
        {
            targetPoint = bestHit.point;
            BlasterVFX.SpawnImpact(bestHit.point, bestHit.normal, bestHit.collider.GetComponentInParent<OasisCombatAI>() != null);
        }

        // Align weapon model bore straight at targetPoint so laser shoots dead straight along the gun
        bool is3rd = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        Vector3 muzzlePos;
        if (!is3rd && _weaponRoot != null && _muzzleTransform != null)
        {
            Vector3 aimDir = (targetPoint - _weaponRoot.position).normalized;
            _weaponRoot.rotation = Quaternion.LookRotation(aimDir, _camera.transform.up);
            muzzlePos = _muzzleTransform.position;
        }
        else
        {
            muzzlePos = GetMuzzlePosition();
        }

        // Update LineRenderer directly from muzzle point along the physical gun line
        if (_continuousBeamLine != null)
        {
            _continuousBeamLine.enabled = true;
            _continuousBeamLine.SetPosition(0, muzzlePos);
            _continuousBeamLine.SetPosition(1, targetPoint);
        }

        // Beam damage & ammo tick
        if (Time.time >= _nextBeamTickTime)
        {
            _nextBeamTickTime = Time.time + beamTickInterval;
            currentAmmo--;
            OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);

            if (hasHit)
            {
                OasisCombatAI hitAI = bestHit.collider.GetComponentInParent<OasisCombatAI>();
                if (hitAI != null && hitAI.teamId != localTeam && !hitAI.isDead)
                {
                    hitAI.TakeDamage(damage, bestHit.point, bestHit.normal, localId, localTeam);
                    OasisCombatMovement.Instance?.TriggerHitmarker();
                    OasisShooterManager.Instance?.OnTargetHit(damage, hitAI.isDowned);
                    OasisShooterManager.Instance?.AddPlayerScore(5);
                    if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.35f);
                }
                else
                {
                    TargetDrone drone = bestHit.collider.GetComponentInParent<TargetDrone>();
                    if (drone != null)
                    {
                        drone.TakeDamage(damage, bestHit.point, bestHit.normal, localId);
                        OasisCombatMovement.Instance?.TriggerHitmarker();
                    }
                }
            }

            if (currentAmmo <= 0 && !_isReloading)
            {
                UpdateContinuousBeam(false);
                StartCoroutine(Reload());
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core Hitscan Resolver
    // ─────────────────────────────────────────────────────────────────────────────
    private Vector3 PerformRaycastShot(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId, float dmg, out bool hitHostile, bool isPellet = false)
    {
        hitHostile = false;
        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.scanReconTimeRemaining > 0f)
        {
            dmg *= 1.25f;
        }
        Vector3 targetPoint;
        RaycastHit[] hits = Physics.RaycastAll(ray, range, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        RaycastHit bestHit = default;
        bool hasHit = false;
        float closestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.collider.isTrigger) continue;
            if (playerRoot != null && (h.collider.transform.IsChildOf(playerRoot) || h.collider.transform == playerRoot)) continue;

            OasisCombatAI aiCheck = h.collider.GetComponentInParent<OasisCombatAI>();
            if (aiCheck != null && aiCheck.teamId == localTeam) continue;

            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                bestHit = h;
                hasHit = true;
            }
        }

        if (!hasHit && !isPellet)
        {
            // Sphere-cast grace window for single shot rifles
            RaycastHit[] sphereHits = Physics.SphereCastAll(ray, 0.34f, range, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < sphereHits.Length; i++)
            {
                RaycastHit sh = sphereHits[i];
                if (sh.collider == null || sh.collider.isTrigger) continue;
                OasisCombatAI candidateAI = sh.collider.GetComponentInParent<OasisCombatAI>();
                if (candidateAI != null && !candidateAI.isDead && candidateAI.teamId != localTeam && sh.distance < closestDist)
                {
                    closestDist = sh.distance;
                    bestHit = sh;
                    hasHit = true;
                }
            }
        }

        if (hasHit)
        {
            targetPoint = bestHit.point;
            OasisCombatAI hitAI = bestHit.collider.GetComponentInParent<OasisCombatAI>();
            if (hitAI != null && hitAI.teamId != localTeam && !hitAI.isDead)
            {
                hitHostile = true;
                hitAI.TakeDamage(dmg, bestHit.point, bestHit.normal, localId, localTeam);
                OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(hitAI, dmg, bestHit.distance);
                OasisCombatMovement.Instance?.TriggerHitmarker();
                OasisShooterManager.Instance?.OnTargetHit(dmg, hitAI.isDowned);
                OasisShooterManager.Instance?.AddPlayerScore(isPellet ? 2 : 15);
                if (_audioSource != null && (!isPellet || Random.value < 0.3f))
                {
                    _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.65f);
                }
            }
            else
            {
                TargetDrone drone = bestHit.collider.GetComponentInParent<TargetDrone>();
                if (drone != null)
                {
                    hitHostile = true;
                    drone.TakeDamage(dmg, bestHit.point, bestHit.normal, localId);
                    OasisCombatMovement.Instance?.TriggerHitmarker();
                }
                else
                {
                    BlasterVFX.SpawnImpact(bestHit.point, bestHit.normal, false);
                    if (_audioSource != null && (!isPellet || Random.value < 0.25f))
                    {
                        _audioSource.PlayOneShot(ProceduralAudio.GetHitWorldClip(), 0.5f);
                    }
                }
            }
        }
        else
        {
            targetPoint = ray.GetPoint(range);
        }

        return targetPoint;
    }

    private IEnumerator FlashMuzzleLight()
    {
        if (_muzzleFlashLight != null)
        {
            _muzzleFlashLight.enabled = true;
            yield return new WaitForSeconds(0.04f);
            if (_muzzleFlashLight != null) _muzzleFlashLight.enabled = false;
        }
    }

    private IEnumerator Reload()
    {
        _isReloading = true;
        UpdateContinuousBeam(false);
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
        OasisAITelemetryLogger.Instance?.RecordPlayerReload();

        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetReloadClip(), 0.8f);
        }

        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.robotHeroAnimator != null)
        {
            OasisCombatMovement.Instance.robotHeroAnimator.Play("Reload", 0, 0f);
        }

        Vector3 origRecoil = _currentRecoilPos;
        _currentRecoilPos += new Vector3(0, -0.15f, -0.05f);

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = maxAmmo;
        _isReloading = false;
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
    }

    private void HandleSwayAndBob()
    {
        if (_weaponRoot == null || !_weaponRoot.gameObject.activeInHierarchy) return;

        float mouseX = 0f;
        float mouseY = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * 0.05f;
            mouseX = delta.x;
            mouseY = delta.y;
        }
#else
        mouseX = Input.GetAxis("Mouse X");
        mouseY = Input.GetAxis("Mouse Y");
#endif

        float moveX = Mathf.Clamp(-mouseX * swayAmount, -maxSway, maxSway);
        float moveY = Mathf.Clamp(-mouseY * swayAmount, -maxSway, maxSway);

        float bobX = Mathf.Sin(Time.time * 8f) * 0.005f;
        float bobY = Mathf.Cos(Time.time * 16f) * 0.005f;

        Vector3 targetPos = hipPosition + new Vector3(moveX + bobX, moveY + bobY, 0) + _currentRecoilPos;
        _weaponRoot.localPosition = Vector3.Lerp(_weaponRoot.localPosition, targetPos, Time.deltaTime * swaySmooth);
        _weaponRoot.localRotation = Quaternion.Euler(_currentRecoilRot);
    }

    private void RecoverRecoil()
    {
        _currentRecoilPos = Vector3.Lerp(_currentRecoilPos, Vector3.zero, Time.deltaTime * recoilRecovery);
        _currentRecoilRot = Vector3.Lerp(_currentRecoilRot, Vector3.zero, Time.deltaTime * recoilRecovery);
    }

    /// <summary>
    /// Loads and displays the authentic weapon mesh held by the character model.
    /// Completely avoids blocky primitive cubes/cylinders ("polygon gun").
    /// </summary>
    private void BuildProceduralGunModel()
    {
        if (_weaponRoot != null)
        {
            Destroy(_weaponRoot.gameObject);
        }

        GameObject root = new GameObject("BlasterModelRoot");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = hipPosition;
        _weaponRoot = root.transform;

        bool is3rd = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        _weaponRoot.gameObject.SetActive(!is3rd);

        Color energyColor = currentArchetype == WeaponArchetype.BombMortar ? new Color(1f, 0.45f, 0.1f) :
                            currentArchetype == WeaponArchetype.ContinuousBeam ? new Color(0.2f, 0.95f, 1f) :
                            currentArchetype == WeaponArchetype.ScatterShotgun ? new Color(1f, 0.35f, 0.15f) :
                            currentArchetype == WeaponArchetype.DefenseCarbine ? new Color(0.3f, 1f, 0.6f) :
                            currentArchetype == WeaponArchetype.DualPistols ? new Color(1f, 0.85f, 0.2f) :
                            new Color(0.1f, 0.95f, 1f);

        // Find authentic AssaultRifle mesh from character model / RobotHero
        Mesh rifleMesh = null;
        Material rifleMat = null;

        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.robotHeroVisual != null)
        {
            var smrs = OasisCombatMovement.Instance.robotHeroVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.name == "AssaultRifle" || smr.name.Contains("Rifle"))
                {
                    rifleMesh = smr.sharedMesh;
                    rifleMat = smr.sharedMaterial;
                    break;
                }
            }
        }

        if (rifleMesh == null)
        {
            GameObject robotPrefab = Resources.Load<GameObject>("RobotHero");
            if (robotPrefab != null)
            {
                var smrs = robotPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (smr.name == "AssaultRifle" || smr.name.Contains("Rifle"))
                    {
                        rifleMesh = smr.sharedMesh;
                        rifleMat = smr.sharedMaterial;
                        break;
                    }
                }
            }
        }

        if (rifleMat == null)
        {
            rifleMat = Resources.Load<Material>("RobotHero_URP");
        }

        Vector3 muzzleLocalPos = new Vector3(0.02f, 0.04f, 0.58f);

        if (rifleMesh != null)
        {
            GameObject authenticRifle = new GameObject("Authentic_AssaultRifle");
            authenticRifle.transform.SetParent(_weaponRoot, false);
            // Position the authentic rifle cleanly in the lower-right first person view
            authenticRifle.transform.localPosition = new Vector3(0.06f, -0.14f, 0.18f);
            authenticRifle.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            authenticRifle.transform.localScale = Vector3.one * 0.01f;

            MeshFilter mf = authenticRifle.AddComponent<MeshFilter>();
            mf.sharedMesh = rifleMesh;

            MeshRenderer mr = authenticRifle.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rifleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Muzzle Point
        GameObject muzzle = new GameObject("MuzzlePoint");
        muzzle.transform.SetParent(_weaponRoot, false);
        muzzle.transform.localPosition = muzzleLocalPos;
        _muzzleTransform = muzzle.transform;

        // Muzzle Flash Point Light
        GameObject flashLightObj = new GameObject("MuzzleFlashLight");
        flashLightObj.transform.SetParent(muzzle.transform, false);
        _muzzleFlashLight = flashLightObj.AddComponent<Light>();
        _muzzleFlashLight.type = LightType.Point;
        _muzzleFlashLight.color = energyColor;
        _muzzleFlashLight.range = 6f;
        _muzzleFlashLight.intensity = 5f;
        _muzzleFlashLight.enabled = false;
    }

    private void BuildScopeOverlayUI()
    {
        _scopeCanvas = new GameObject("Oasis_PrecisionScope_Canvas");
        Canvas canvas = _scopeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;

        CanvasScaler scaler = _scopeCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Vignette
        GameObject vignette = new GameObject("Scope_PeripheralVignette");
        vignette.transform.SetParent(_scopeCanvas.transform, false);
        RectTransform vRT = vignette.AddComponent<RectTransform>();
        vRT.anchorMin = Vector2.zero;
        vRT.anchorMax = Vector2.one;
        vRT.offsetMin = Vector2.zero;
        vRT.offsetMax = Vector2.zero;
        Image vImg = vignette.AddComponent<Image>();
        vImg.color = new Color(0.01f, 0.02f, 0.04f, 0.82f);

        // Center Lens
        GameObject lensAperture = new GameObject("Scope_LensFrame");
        lensAperture.transform.SetParent(_scopeCanvas.transform, false);
        RectTransform lRT = lensAperture.AddComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0.5f, 0.5f);
        lRT.anchorMax = new Vector2(0.5f, 0.5f);
        lRT.sizeDelta = new Vector2(640f, 640f);
        lRT.anchoredPosition = Vector2.zero;

        // Outer Ring
        GameObject ring1 = new GameObject("Ring_Outer");
        ring1.transform.SetParent(lensAperture.transform, false);
        RectTransform r1RT = ring1.AddComponent<RectTransform>();
        r1RT.anchorMin = Vector2.zero;
        r1RT.anchorMax = Vector2.one;
        Image r1Img = ring1.AddComponent<Image>();
        r1Img.color = new Color(0.15f, 0.85f, 1.0f, 0.65f);

        // Glass
        GameObject lensGlass = new GameObject("Lens_Glass");
        lensGlass.transform.SetParent(lensAperture.transform, false);
        RectTransform lgRT = lensGlass.AddComponent<RectTransform>();
        lgRT.anchorMin = Vector2.zero;
        lgRT.anchorMax = Vector2.one;
        lgRT.offsetMin = new Vector2(3, 3);
        lgRT.offsetMax = new Vector2(-3, -3);
        Image lgImg = lensGlass.AddComponent<Image>();
        lgImg.color = new Color(0.02f, 0.06f, 0.12f, 0.28f);

        Color reticleCyan = new Color(0.2f, 0.95f, 1.0f, 0.9f);
        Color milDotColor = new Color(0.35f, 1.0f, 0.85f, 0.8f);

        CreateScopeLine(lensAperture.transform, new Vector2(-190, 0), new Vector2(240, 2), reticleCyan);
        CreateScopeLine(lensAperture.transform, new Vector2(190, 0), new Vector2(240, 2), reticleCyan);
        CreateScopeLine(lensAperture.transform, new Vector2(0, 190), new Vector2(2, 240), reticleCyan);
        CreateScopeLine(lensAperture.transform, new Vector2(0, -190), new Vector2(2, 240), reticleCyan);

        for (int m = 1; m <= 4; m++)
        {
            float offset = m * 38f;
            CreateScopeLine(lensAperture.transform, new Vector2(-offset, 0), new Vector2(2, 10), milDotColor);
            CreateScopeLine(lensAperture.transform, new Vector2(offset, 0), new Vector2(2, 10), milDotColor);
            CreateScopeLine(lensAperture.transform, new Vector2(0, -offset), new Vector2(10 + m * 3, 2), milDotColor);
        }

        // Center Precision Dot
        GameObject centerDot = new GameObject("Center_PrecisionDot");
        centerDot.transform.SetParent(lensAperture.transform, false);
        RectTransform cdRT = centerDot.AddComponent<RectTransform>();
        cdRT.anchorMin = new Vector2(0.5f, 0.5f);
        cdRT.anchorMax = new Vector2(0.5f, 0.5f);
        cdRT.sizeDelta = new Vector2(4f, 4f);
        cdRT.anchoredPosition = Vector2.zero;
        Image cdImg = centerDot.AddComponent<Image>();
        cdImg.color = new Color(1.0f, 0.25f, 0.2f, 1.0f);

        // Rangefinder
        GameObject rangeObj = new GameObject("Scope_RangeText");
        rangeObj.transform.SetParent(lensAperture.transform, false);
        RectTransform rngRT = rangeObj.AddComponent<RectTransform>();
        rngRT.anchorMin = new Vector2(0.5f, 0.5f);
        rngRT.anchorMax = new Vector2(0.5f, 0.5f);
        rngRT.sizeDelta = new Vector2(200, 30);
        rngRT.anchoredPosition = new Vector2(125, -60);
        _scopeRangeText = rangeObj.AddComponent<Text>();
        _scopeRangeText.font = font;
        _scopeRangeText.fontSize = 14;
        _scopeRangeText.fontStyle = FontStyle.Bold;
        _scopeRangeText.text = "RANGE: ---";
        _scopeRangeText.color = new Color(0.2f, 0.95f, 1.0f, 0.95f);
        _scopeRangeText.alignment = TextAnchor.MiddleLeft;

        // Weapon Label
        GameObject topLabel = new GameObject("Scope_TopLabel");
        topLabel.transform.SetParent(lensAperture.transform, false);
        RectTransform tlRT = topLabel.AddComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.5f, 0.5f);
        tlRT.anchorMax = new Vector2(0.5f, 0.5f);
        tlRT.sizeDelta = new Vector2(320, 24);
        tlRT.anchoredPosition = new Vector2(0, 160);
        _scopeWeaponLabel = topLabel.AddComponent<Text>();
        _scopeWeaponLabel.font = font;
        _scopeWeaponLabel.fontSize = 12;
        _scopeWeaponLabel.fontStyle = FontStyle.Bold;
        _scopeWeaponLabel.text = "[ HOLOGRAPHIC TARGETING SYS ]";
        _scopeWeaponLabel.color = new Color(0.2f, 0.95f, 1.0f, 0.75f);
        _scopeWeaponLabel.alignment = TextAnchor.MiddleCenter;

        _scopeCanvas.SetActive(false);
    }

    private void CreateScopeLine(Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject line = new GameObject("ReticleLine");
        line.transform.SetParent(parent, false);
        RectTransform rt = line.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        Image img = line.AddComponent<Image>();
        img.color = color;
    }
}
