using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-Person & Third-Person Blaster Weapon supporting multi-archetype combat:
/// 1. Standard Kinetic Rifle (balanced precision marksman blaster)
/// 2. Plasma Scatter Shotgun (8-pellet wide spread close-range punch)
/// 3. Continuous Phase Laser Beam (sustained tracking heat beam)
/// 4. Micro-Bomb Flak Mortar (ballistic plasma explosive bombs with AoE splash)
/// 5. Tactical Defense Carbine (rapid-fire support carbine)
/// 6. High-Caliber Dual Pistols (high-mobility gunslinger sidearms)
///
/// Features velocity-driven stride bobbing, sprint canting, multi-stage tactical reload animation,
/// 3rd-person upper-body recoil synchronization, ejecting plasma casings, headshot critical multipliers,
/// floating 3D damage popups, and holographic ADS optics.
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
    public Vector3 hipPosition = new Vector3(0.20f, -0.19f, 0.42f);
    public Vector3 aimPosition = new Vector3(0f, -0.125f, 0.32f);
    public float swayAmount = 0.016f;
    public float maxSway = 0.045f;
    public float swaySmooth = 14f;

    [Header("Recoil")]
    public float recoilKickback = 0.065f;
    public float recoilRotation = 3.2f;
    public float recoilRecovery = 14f;

    [Header("Aim Down Sights & Precision Scope")]
    public bool isScoped = false;
    public float defaultFOV = 75f;
    public float scopedFOV = 34f;
    public float scopeZoomSpeed = 18f;

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
    private float _reloadStartTime = 0f;
    private bool _reloadCasingEjected = false;
    private bool _reloadBoltSlammed = false;

    private Vector3 _currentRecoilPos;
    private Vector3 _currentRecoilRot;
    private Vector3 _smoothedBasePos;
    private Quaternion _smoothedBaseRot = Quaternion.identity;
    private float _stridePhase = 0f;
    private float _verticalVelocitySway = 0f;

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
        defaultFOV = 75f;
        if (_camera != null) _camera.fieldOfView = defaultFOV;

        _fpc = GetComponentInParent<StarterAssets.FirstPersonController>();
        if (_fpc != null) _origRotationSpeed = _fpc.RotationSpeed;

        _smoothedBasePos = hipPosition;

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

    public Color GetWeaponEnergyColor()
    {
        switch (currentArchetype)
        {
            case WeaponArchetype.BombMortar: return new Color(1f, 0.45f, 0.1f);
            case WeaponArchetype.ContinuousBeam: return new Color(0.2f, 0.95f, 1f);
            case WeaponArchetype.ScatterShotgun: return new Color(1f, 0.38f, 0.14f);
            case WeaponArchetype.DefenseCarbine: return new Color(0.25f, 1f, 0.55f);
            case WeaponArchetype.DualPistols: return new Color(0.95f, 0.38f, 0.95f);
            case WeaponArchetype.StandardRifle:
            default: return new Color(0.2f, 0.95f, 1f);
        }
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
                fireRate = 0.62f;
                range = 55f;
                maxAmmo = 12;
                reloadTime = 1.30f;
                recoilKickback = 0.22f;
                recoilRotation = 8.5f;
                recoilRecovery = 10f;
                break;

            case WeaponArchetype.ContinuousBeam:
                weaponDisplayName = "Continuous Phase Laser";
                damage = beamTickDamage; // 150 DPS
                fireRate = beamTickInterval;
                range = 85f;
                maxAmmo = 90;
                reloadTime = 1.2f;
                recoilKickback = 0.010f;
                recoilRotation = 0.5f;
                recoilRecovery = 16f;
                break;

            case WeaponArchetype.BombMortar:
                weaponDisplayName = "Heavy Plasma Mortar Cannon";
                damage = 125f; // Heavy direct impact + devastating AoE
                fireRate = 0.65f;
                range = 220f;
                maxAmmo = 10;
                reloadTime = 1.3f;
                recoilKickback = 0.24f;
                recoilRotation = 7.8f;
                recoilRecovery = 11f;
                break;

            case WeaponArchetype.DefenseCarbine:
                weaponDisplayName = "Tactical Defense Carbine";
                damage = 24f;
                fireRate = 0.12f; // Rapid suppressive fire
                range = 160f;
                maxAmmo = 32;
                reloadTime = 0.95f;
                recoilKickback = 0.055f;
                recoilRotation = 2.6f;
                recoilRecovery = 16f;
                break;

            case WeaponArchetype.DualPistols:
                weaponDisplayName = "High-Caliber Dual Pistols";
                damage = 34f;
                fireRate = 0.14f;
                range = 110f;
                maxAmmo = 24;
                reloadTime = 0.85f;
                recoilKickback = 0.075f;
                recoilRotation = 3.6f;
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
                recoilKickback = 0.075f;
                recoilRotation = 3.2f;
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

        isScoped = scopeHeld && !_isReloading;

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
                        targetFOV = 34f; // Crisp 2.2x Tactical Marksman Optic
                        break;
                    case WeaponArchetype.DefenseCarbine:
                        targetFOV = 46f;
                        break;
                    case WeaponArchetype.BombMortar:
                        targetFOV = 52f;
                        break;
                    case WeaponArchetype.ScatterShotgun:
                        targetFOV = 56f;
                        break;
                    case WeaponArchetype.DualPistols:
                        targetFOV = 52f;
                        break;
                    case WeaponArchetype.ContinuousBeam:
                    default:
                        targetFOV = 48f;
                        break;
                }
            }
        }
        else if (is3rdAiming)
        {
            targetFOV = currentArchetype == WeaponArchetype.StandardRifle ? 44f : 56f;
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
        }

        if (_fpc != null)
        {
            bool isPrecisionAim = (!is3rdPerson && isScoped) || (is3rdPerson && is3rdAiming);
            // Scale ADS sensitivity proportionally to FOV zoom so tracking feels natural and never sluggish
            float currentFov = _camera != null ? _camera.fieldOfView : targetFOV;
            float fovRatio = Mathf.Clamp(currentFov / Mathf.Max(45f, defaultFOV), 0.50f, 1.0f);
            _fpc.RotationSpeed = isPrecisionAim ? (_origRotationSpeed * fovRatio) : _origRotationSpeed;
        }

        if (_scopeCanvas != null)
        {
            bool showHudOptic = (!is3rdPerson && isScoped) || (is3rdPerson && is3rdAiming);
            if (_scopeCanvas.activeSelf != showHudOptic)
            {
                _scopeCanvas.SetActive(showHudOptic);
            }
            if (showHudOptic && _scopeRangeText != null && _camera != null)
            {
                Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (Physics.Raycast(ray, out RaycastHit hit, 350f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    _scopeRangeText.text = $"TARGET RANGE: {hit.distance:F1}M";
                }
                else
                {
                    _scopeRangeText.text = "TARGET RANGE: ---";
                }
            }
            if (showHudOptic && _scopeWeaponLabel != null)
            {
                _scopeWeaponLabel.text = currentArchetype == WeaponArchetype.StandardRifle
                    ? $"[ 2.2X MARKSMAN OPTIC // {weaponDisplayName.ToUpper()} ]"
                    : $"[ REFLEX HOLOSIGHT // {weaponDisplayName.ToUpper()} ]";
            }
        }
    }

    private void Fire()
    {
        _nextFireTime = Time.time + fireRate;
        currentAmmo--;
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
        OasisAITelemetryLogger.Instance?.RecordPlayerShotFired();

        // Apply multi-axis physical gun recoil impulse (tighter & more controllable when aiming down sights)
        float adsMultiplier = isScoped ? 0.35f : 1.0f;
        _currentRecoilPos += new Vector3(Random.Range(-0.005f, 0.005f) * adsMultiplier, 0.014f * adsMultiplier, -recoilKickback * adsMultiplier);
        _currentRecoilRot += new Vector3(-recoilRotation * adsMultiplier, Random.Range(-1.1f, 1.1f) * adsMultiplier, Random.Range(-1.4f, 1.4f) * adsMultiplier);

        if (_fpc != null)
        {
            float camPitchKick = (currentArchetype == WeaponArchetype.ScatterShotgun || currentArchetype == WeaponArchetype.BombMortar) ? 0.75f : 0.32f;
            _fpc.AddWeaponRecoilPitchYaw(camPitchKick * (isScoped ? 0.55f : 1.0f), Random.Range(-0.15f, 0.15f) * adsMultiplier);
        }

        bool isHeavyShot = (currentArchetype == WeaponArchetype.ScatterShotgun ||
                            currentArchetype == WeaponArchetype.BombMortar ||
                            currentArchetype == WeaponArchetype.StandardRifle);
        float tpKickScale = (currentArchetype == WeaponArchetype.ScatterShotgun || currentArchetype == WeaponArchetype.BombMortar) ? 1.55f : 1.0f;

        if (OasisCombatMovement.Instance != null)
        {
            OasisCombatMovement.Instance.TriggerWeaponShootAnim(tpKickScale, isHeavyShot);
        }

        if (_muzzleFlashLight != null)
        {
            StartCoroutine(FlashMuzzleLight());
        }

        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        Vector3 muzzlePos = GetMuzzlePosition();

        // Eject a spinning hot plasma shell casing from the weapon receiver
        Vector3 ejectRight = _camera != null ? _camera.transform.right : transform.right;
        Vector3 ejectPos = muzzlePos - (_camera != null ? _camera.transform.forward * 0.28f : transform.forward * 0.28f) + ejectRight * 0.08f;
        BlasterVFX.SpawnShellCasing(ejectPos, ejectRight, GetWeaponEnergyColor());

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

    private Vector3 GetMuzzlePosition(Vector3 targetPoint)
    {
        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        if (is3rdPerson && OasisCombatMovement.Instance.robotHeroVisual != null)
        {
            return OasisCombatMovement.Instance.GetThirdPersonMuzzlePosition(targetPoint);
        }
        return _muzzleTransform != null ? _muzzleTransform.position : transform.position + transform.forward * 0.5f;
    }

    private Vector3 GetMuzzlePosition()
    {
        Vector3 defaultTarget = _camera != null ? _camera.transform.position + _camera.transform.forward * 50f : transform.position + transform.forward * 50f;
        return GetMuzzlePosition(defaultTarget);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 1: Long-Range Precision Marksman Rifle
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireStandardRifle(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.46f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        Vector3 trueMuzzle = GetMuzzlePosition(targetPoint);
        BlasterVFX.SpawnLaserBeam(trueMuzzle, targetPoint, new Color(0.25f, 0.95f, 1f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(trueMuzzle, targetPoint, hitHostile);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 2: Plasma Scatter Shotgun (8 Pellets)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireScatterShotgun(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetShotgunShotClip(), 0.58f);
        }

        Color shotgunColor = new Color(1f, 0.45f, 0.1f);
        bool anyHostileHit = false;
        OasisCombatAI primaryHitAI = null;
        float totalPelletDamageOnAI = 0f;
        float primaryHitDist = 0f;

        for (int p = 0; p < shotgunPelletCount; p++)
        {
            Vector2 spreadCircle = Random.insideUnitCircle * shotgunSpreadAngle;
            Vector3 pelletDir = (ray.direction + _camera.transform.right * spreadCircle.x + _camera.transform.up * spreadCircle.y).normalized;
            Ray pelletRay = new Ray(ray.origin, pelletDir);

            Vector3 hitPt = PerformRaycastShot(pelletRay, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile, true, out OasisCombatAI pelletHitAI, out float pelletHitDist);
            if (hitHostile)
            {
                anyHostileHit = true;
                if (pelletHitAI != null)
                {
                    primaryHitAI = pelletHitAI;
                    totalPelletDamageOnAI += damage;
                    primaryHitDist = pelletHitDist;
                }
            }

            Vector3 trueMuzzle = GetMuzzlePosition(hitPt);
            BlasterVFX.SpawnLaserBeam(trueMuzzle, hitPt, shotgunColor);
        }

        if (primaryHitAI != null && totalPelletDamageOnAI > 0f)
        {
            OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(primaryHitAI, totalPelletDamageOnAI, primaryHitDist);
            BlasterVFX.SpawnDamagePopup(primaryHitAI.transform.position + Vector3.up * 1.4f, totalPelletDamageOnAI, totalPelletDamageOnAI >= 55f, primaryHitAI.currentShield > 0f);
        }

        Vector3 fallbackMuzzle = GetMuzzlePosition(ray.GetPoint(range));
        OasisNetworkManager.Instance?.BroadcastLaserFire(fallbackMuzzle, ray.GetPoint(range), anyHostileHit);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 3: Heavy Plasma Mortar Cannon (Guided Ballistic Arc & Airburst)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireBombMortar(Ray ray, Vector3 muzzlePos, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBombLaunchClip(), 0.85f);
        }

        // Raycast from crosshair to find exact target point
        Ray aimRay = _camera != null ? _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)) : ray;
        Vector3 targetPoint;
        if (Physics.Raycast(aimRay, out RaycastHit hit, 250f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            targetPoint = hit.point;
        }
        else
        {
            targetPoint = aimRay.GetPoint(135f);
        }

        Vector3 trueMuzzle = GetMuzzlePosition(targetPoint);
        BlasterVFX.SpawnMuzzleFlash(trueMuzzle, (targetPoint - trueMuzzle).normalized, new Color(1f, 0.55f, 0.15f));

        // Calculate smooth ballistic mortar arc towards targetPoint (speed ~55 m/s)
        Vector3 toTarget = targetPoint - trueMuzzle;
        float dist = toTarget.magnitude;
        float speed = 55f;
        float flightTime = Mathf.Max(0.25f, dist / speed);

        float mortarGravity = 7.35f; // Matches bomb's custom downward gravity
        Vector3 horizontalVel = new Vector3(toTarget.x, 0, toTarget.z) / flightTime;
        float verticalVel = (toTarget.y + 0.5f * mortarGravity * flightTime * flightTime) / flightTime;
        Vector3 launchVel = horizontalVel + Vector3.up * verticalVel;

        OasisPlasmaBomb.SpawnBomb(trueMuzzle, launchVel, localTeam, localId);

        OasisShooterManager.Instance?.TriggerCameraShake(0.28f, 0.22f);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 5: Tactical Defense Carbine (Rapid Fire Support)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireDefenseCarbine(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.34f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        Vector3 trueMuzzle = GetMuzzlePosition(targetPoint);
        BlasterVFX.SpawnLaserBeam(trueMuzzle, targetPoint, new Color(0.2f, 0.95f, 0.5f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(trueMuzzle, targetPoint, hitHostile);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ARCHETYPE 6: High-Caliber Dual Pistols (High Mobility Gunslinger)
    // ─────────────────────────────────────────────────────────────────────────────
    private void FireDualPistols(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId)
    {
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.44f);
        }

        Vector3 targetPoint = PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, damage, out bool hitHostile);
        Vector3 trueMuzzle = GetMuzzlePosition(targetPoint);
        BlasterVFX.SpawnLaserBeam(trueMuzzle, targetPoint, new Color(0.92f, 0.35f, 0.95f));
        OasisNetworkManager.Instance?.BroadcastLaserFire(trueMuzzle, targetPoint, hitHostile);
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
            if (aiCheck != null && (aiCheck.teamId == localTeam || aiCheck.isDowned || aiCheck.isDead)) continue;

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

        bool is3rd = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        Vector3 muzzlePos = (!is3rd && _muzzleTransform != null) ? _muzzleTransform.position : GetMuzzlePosition(targetPoint);

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
            OasisCombatMovement.Instance?.TriggerWeaponShootAnim(0.35f, false);

            if (hasHit)
            {
                OasisCombatAI hitAI = bestHit.collider.GetComponentInParent<OasisCombatAI>();
                if (hitAI != null && hitAI.teamId != localTeam && !hitAI.isDead && !hitAI.isDowned)
                {
                    bool isHeadshot = (bestHit.point.y - hitAI.transform.position.y) >= 1.42f;
                    float finalDmg = isHeadshot ? damage * 1.4f : damage;
                    bool hadShield = hitAI.currentShield > 0f;
                    hitAI.TakeDamage(finalDmg, bestHit.point, bestHit.normal, localId, localTeam, bestHit.distance);
                    BlasterVFX.SpawnDamagePopup(bestHit.point, finalDmg, isHeadshot, hadShield);
                    OasisCombatMovement.Instance?.TriggerHitmarker(isHeadshot);
                    OasisShooterManager.Instance?.OnTargetHit(finalDmg, hitAI.isDowned);
                    OasisShooterManager.Instance?.AddPlayerScore(5);
                    if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.35f);
                }
                else
                {
                    TargetDrone drone = bestHit.collider.GetComponentInParent<TargetDrone>();
                    if (drone != null)
                    {
                        drone.TakeDamage(damage, bestHit.point, bestHit.normal, localId);
                        OasisCombatMovement.Instance?.TriggerHitmarker(false);
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
    // Core Hitscan Resolver (with Headshot Crit Detection & 3D Damage Popups)
    // ─────────────────────────────────────────────────────────────────────────────
    private Vector3 PerformRaycastShot(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId, float dmg, out bool hitHostile, bool isPellet = false)
    {
        return PerformRaycastShot(ray, muzzlePos, playerRoot, localTeam, localId, dmg, out hitHostile, isPellet, out _, out _);
    }

    private Vector3 PerformRaycastShot(Ray ray, Vector3 muzzlePos, Transform playerRoot, int localTeam, int localId, float dmg, out bool hitHostile, bool isPellet, out OasisCombatAI outHitAI, out float outHitDist)
    {
        hitHostile = false;
        outHitAI = null;
        outHitDist = 0f;
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
            if (aiCheck != null && (aiCheck.teamId == localTeam || aiCheck.isDowned || aiCheck.isDead)) continue;

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
                if (candidateAI != null && !candidateAI.isDead && !candidateAI.isDowned && candidateAI.teamId != localTeam && sh.distance < closestDist)
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
            if (hitAI != null && hitAI.teamId != localTeam && !hitAI.isDead && !hitAI.isDowned)
            {
                hitHostile = true;
                bool isHeadshot = !isPellet && ((bestHit.point.y - hitAI.transform.position.y) >= 1.42f);
                float finalDmg = isHeadshot ? (dmg * 1.5f) : dmg;
                bool hadShield = hitAI.currentShield > 0f;

                outHitAI = hitAI;
                outHitDist = bestHit.distance;
                if (!isPellet)
                {
                    OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(hitAI, finalDmg, bestHit.distance);
                    BlasterVFX.SpawnDamagePopup(bestHit.point, finalDmg, isHeadshot, hadShield);
                }
                hitAI.TakeDamage(finalDmg, bestHit.point, bestHit.normal, localId, localTeam, bestHit.distance);
                OasisCombatMovement.Instance?.TriggerHitmarker(isHeadshot);
                OasisShooterManager.Instance?.OnTargetHit(finalDmg, hitAI.isDowned);
                OasisShooterManager.Instance?.AddPlayerScore(isPellet ? 2 : (isHeadshot ? 25 : 15));
                if (_audioSource != null && (!isPellet || Random.value < 0.3f))
                {
                    _audioSource.pitch = isHeadshot ? 1.28f : 1.0f;
                    _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), isHeadshot ? 0.85f : 0.65f);
                    _audioSource.pitch = 1.0f;
                }
            }
            else
            {
                TargetDrone drone = bestHit.collider.GetComponentInParent<TargetDrone>();
                if (drone != null)
                {
                    hitHostile = true;
                    drone.TakeDamage(dmg, bestHit.point, bestHit.normal, localId);
                    BlasterVFX.SpawnDamagePopup(bestHit.point, dmg, false, false);
                    OasisCombatMovement.Instance?.TriggerHitmarker(false);
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
        _reloadStartTime = Time.time;
        _reloadCasingEjected = false;
        _reloadBoltSlammed = false;
        isScoped = false;

        UpdateContinuousBeam(false);
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
        OasisAITelemetryLogger.Instance?.RecordPlayerReload();

        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetReloadClip(), 0.85f);
        }

        if (OasisCombatMovement.Instance != null)
        {
            OasisCombatMovement.Instance.TriggerWeaponReloadAnim(reloadTime);
        }

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

        float swayScale = isScoped ? 0.25f : 1.0f;
        float moveX = Mathf.Clamp(-mouseX * swayAmount * swayScale, -maxSway, maxSway);
        float moveY = Mathf.Clamp(-mouseY * swayAmount * swayScale, -maxSway, maxSway);

        // Velocity-driven stride bobbing & vertical jump/land inertia
        float horizSpeed = 0f;
        float vertVel = 0f;
        if (OasisCombatMovement.Instance != null)
        {
            var cc = OasisCombatMovement.Instance.GetComponent<CharacterController>();
            if (cc != null)
            {
                horizSpeed = new Vector2(cc.velocity.x, cc.velocity.z).magnitude;
                vertVel = cc.velocity.y;
            }
        }

        _verticalVelocitySway = Mathf.Lerp(_verticalVelocitySway, Mathf.Clamp(-vertVel * 0.008f, -0.055f, 0.055f), Time.deltaTime * 10f);

        float bobFreq = horizSpeed > 0.35f ? Mathf.Lerp(7.5f, 13.5f, Mathf.Clamp01(horizSpeed / 10f)) : 1.8f;
        _stridePhase += Time.deltaTime * bobFreq;

        float bobAmp = horizSpeed > 0.35f
            ? Mathf.Lerp(0.006f, 0.022f, Mathf.Clamp01(horizSpeed / 10f)) * swayScale
            : 0.002f * swayScale;

        float bobX = Mathf.Cos(_stridePhase) * bobAmp;
        float bobY = Mathf.Abs(Mathf.Sin(_stridePhase)) * (-bobAmp * 1.35f) + _verticalVelocitySway;
        float bobRoll = Mathf.Sin(_stridePhase) * (horizSpeed > 0.35f ? 1.8f * swayScale : 0.2f);

        // Determine base stance position & rotation (ADS centers weapon reflex sight directly on screen center)
        Vector3 desiredBasePos = isScoped ? aimPosition : hipPosition;
        Quaternion desiredBaseRot = Quaternion.Euler(0f, 0f, bobRoll - mouseX * 1.4f * swayScale);

        bool isSprinting = (horizSpeed > 7.8f && !isScoped && (Time.time - (_nextFireTime - fireRate)) > 0.25f && !_isBeamActive && !_isReloading);
        if (isSprinting)
        {
            // Tactical sprint ready cant: tuck rifle slightly inward and angled across chest
            desiredBasePos = hipPosition + new Vector3(-0.08f, -0.06f, -0.04f);
            desiredBaseRot = Quaternion.Euler(12f, -24f, 18f + bobRoll * 1.5f);
        }

        if (_isReloading)
        {
            float rFrac = Mathf.Clamp01((Time.time - _reloadStartTime) / Mathf.Max(0.2f, reloadTime));
            if (rFrac < 0.32f)
            {
                // Stage 1: Tilt weapon 36 deg to inspect chamber & drop empty plasma cell
                float t = rFrac / 0.32f;
                desiredBasePos = hipPosition + Vector3.Lerp(Vector3.zero, new Vector3(-0.06f, -0.14f, -0.06f), Mathf.Sin(t * Mathf.PI * 0.5f));
                desiredBaseRot = Quaternion.Euler(Mathf.Lerp(0f, 24f, t), Mathf.Lerp(0f, -18f, t), Mathf.Lerp(0f, 38f, t));

                if (rFrac > 0.16f && !_reloadCasingEjected)
                {
                    _reloadCasingEjected = true;
                    Vector3 ejectRight = _camera != null ? _camera.transform.right : transform.right;
                    BlasterVFX.SpawnShellCasing(_weaponRoot.position, -ejectRight, GetWeaponEnergyColor());
                }
            }
            else if (rFrac < 0.72f)
            {
                // Stage 2: Insert fresh plasma cell with upward slam impulse at 60%
                float t = (rFrac - 0.32f) / 0.40f;
                float slamBump = (t > 0.55f && t < 0.85f) ? Mathf.Sin(((t - 0.55f) / 0.30f) * Mathf.PI) * 0.065f : 0f;
                if (t > 0.58f && !_reloadBoltSlammed)
                {
                    _reloadBoltSlammed = true;
                    _currentRecoilPos += new Vector3(0f, 0.045f, 0.025f);
                    _currentRecoilRot += new Vector3(-6.5f, 0f, -8f);
                }
                desiredBasePos = hipPosition + new Vector3(-0.05f, -0.12f + slamBump, -0.05f);
                desiredBaseRot = Quaternion.Euler(22f - slamBump * 140f, -14f, 34f);
            }
            else
            {
                // Stage 3: Rack charging bolt and snap back to level combat stance
                float t = (rFrac - 0.72f) / 0.28f;
                float rackPull = Mathf.Sin(t * Mathf.PI) * -0.055f;
                desiredBasePos = Vector3.Lerp(hipPosition + new Vector3(-0.04f, -0.08f, -0.04f + rackPull), hipPosition, t);
                desiredBaseRot = Quaternion.Slerp(Quaternion.Euler(16f, -10f, 28f), Quaternion.identity, t);
            }
        }

        _smoothedBasePos = Vector3.Lerp(_smoothedBasePos, desiredBasePos, Time.deltaTime * scopeZoomSpeed);
        _smoothedBaseRot = Quaternion.Slerp(_smoothedBaseRot, desiredBaseRot, Time.deltaTime * swaySmooth);

        Vector3 finalPos = _smoothedBasePos + new Vector3(moveX + bobX, moveY + bobY, 0f) + _currentRecoilPos;
        _weaponRoot.localPosition = Vector3.Lerp(_weaponRoot.localPosition, finalPos, Time.deltaTime * (swaySmooth * 1.3f));
        _weaponRoot.localRotation = _smoothedBaseRot * Quaternion.Euler(_currentRecoilRot);
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
    /// <summary>
    /// Transforms the SkinnedMeshRenderer AssaultRifle mesh from character bind-pose space
    /// into centered, forward-aligned first-person weapon space (+Z barrel forward, +Y top rail up, +X right).
    /// </summary>
    private Mesh CreateAlignedFirstPersonRifleMesh(Mesh srcMesh, SkinnedMeshRenderer smr)
    {
        if (srcMesh == null || !srcMesh.isReadable) return srcMesh;

        Mesh aligned = Instantiate(srcMesh);
        aligned.name = "Aligned_FP_AssaultRifle";

        Vector3[] verts = aligned.vertices;
        Vector3[] norms = aligned.normals;
        bool hasNorms = norms != null && norms.Length == verts.Length;

        // Find ArmPosition_Right bindpose matrix (which transforms from character bind-pose to weapon grip space)
        Matrix4x4 toGripSpace = Matrix4x4.identity;
        bool foundBindpose = false;
        if (smr != null && smr.bones != null && srcMesh.bindposes != null && smr.bones.Length == srcMesh.bindposes.Length)
        {
            for (int i = 0; i < smr.bones.Length; i++)
            {
                if (smr.bones[i] != null && smr.bones[i].name == "ArmPosition_Right")
                {
                    toGripSpace = srcMesh.bindposes[i];
                    foundBindpose = true;
                    break;
                }
            }
        }

        if (foundBindpose)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toGripSpace.MultiplyPoint3x4(verts[i]);
                // In ArmPosition_Right space: -Y is barrel forward (+Z), +X is top rail up (+Y), -Z is right (+X)
                verts[i] = new Vector3(-p.z, p.x, -p.y) * 0.01f;
                if (hasNorms)
                {
                    Vector3 n = toGripSpace.MultiplyVector(norms[i]);
                    norms[i] = new Vector3(-n.z, n.x, -n.y).normalized;
                }
            }
        }
        else
        {
            // Fallback using known ArmPosition_Right bind rotation (-56.48 deg Z) and center
            Vector3 c = srcMesh.bounds.center;
            Quaternion invRot = Quaternion.Inverse(new Quaternion(0.0025357f, -0.0073522f, -0.4731384f, 0.8809539f));
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = invRot * (verts[i] - c);
                verts[i] = new Vector3(-p.z, p.x, -p.y) * 0.01f;
                if (hasNorms)
                {
                    Vector3 n = invRot * norms[i];
                    norms[i] = new Vector3(-n.z, n.x, -n.y).normalized;
                }
            }
        }

        aligned.vertices = verts;
        if (hasNorms) aligned.normals = norms;
        aligned.RecalculateBounds();

        // Center the mesh cleanly so X=0 is the bore axis, Y=0 is the upper receiver, and Z=0.14m is forward of grip
        Vector3 bCenter = aligned.bounds.center;
        float topY = aligned.bounds.max.y;
        Vector3 shift = new Vector3(-bCenter.x, -(topY - 0.045f), -bCenter.z + 0.14f);
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] += shift;
        }
        aligned.vertices = verts;
        aligned.RecalculateBounds();
        return aligned;
    }

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

        Color energyColor = GetWeaponEnergyColor();

        // Find authentic AssaultRifle mesh & SkinnedMeshRenderer from character model / RobotHero
        Mesh rifleMesh = null;
        Material rifleMat = null;
        SkinnedMeshRenderer foundSmr = null;

        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.robotHeroVisual != null)
        {
            var smrs = OasisCombatMovement.Instance.robotHeroVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.name == "AssaultRifle" || smr.name.Contains("Rifle"))
                {
                    foundSmr = smr;
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
                        foundSmr = smr;
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

        float opticCenterY = 0.082f;
        Vector3 muzzleLocalPos = new Vector3(0f, 0.01f, 0.56f);

        if (rifleMesh != null)
        {
            Mesh alignedMesh = CreateAlignedFirstPersonRifleMesh(rifleMesh, foundSmr);
            GameObject authenticRifle = new GameObject("Authentic_AssaultRifle");
            authenticRifle.transform.SetParent(_weaponRoot, false);
            authenticRifle.transform.localPosition = Vector3.zero;
            authenticRifle.transform.localRotation = Quaternion.identity;
            authenticRifle.transform.localScale = Vector3.one;

            MeshFilter mf = authenticRifle.AddComponent<MeshFilter>();
            mf.sharedMesh = alignedMesh;

            MeshRenderer mr = authenticRifle.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rifleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            opticCenterY = alignedMesh.bounds.max.y + 0.028f;
            muzzleLocalPos = new Vector3(0f, alignedMesh.bounds.max.y - 0.035f, alignedMesh.bounds.max.z + 0.04f);
        }

        // Mount a 3D Tactical Reflex Holosight on the top rail so ADS looks directly through the sight window
        BuildTopReflexSight(_weaponRoot, opticCenterY, energyColor);

        // Calibrate aimPosition so holding Right-Click aligns the 3D reflex sight dot directly with screen center (0, 0)
        aimPosition = new Vector3(0f, -opticCenterY, 0.30f);

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

    private void BuildTopReflexSight(Transform weaponParent, float centerY, Color energyColor)
    {
        GameObject sightRoot = new GameObject("ReflexHoloSight_3D");
        sightRoot.transform.SetParent(weaponParent, false);
        sightRoot.transform.localPosition = new Vector3(0f, centerY, 0.06f);
        sightRoot.transform.localRotation = Quaternion.identity;

        Material frameMat = new Material(OasisShaderHelper.GetLitShader());
        frameMat.color = new Color(0.10f, 0.12f, 0.15f);

        Material dotMat = OasisShaderHelper.CreateUnlitMaterial(energyColor);

        // Base mount
        GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        basePlate.transform.SetParent(sightRoot.transform, false);
        basePlate.transform.localPosition = new Vector3(0f, -0.022f, 0f);
        basePlate.transform.localScale = new Vector3(0.038f, 0.008f, 0.075f);
        Destroy(basePlate.GetComponent<Collider>());
        basePlate.GetComponent<Renderer>().sharedMaterial = frameMat;
        basePlate.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Left & Right Sight Hood Struts
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject strut = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strut.transform.SetParent(sightRoot.transform, false);
            strut.transform.localPosition = new Vector3(side * 0.020f, 0f, 0f);
            strut.transform.localScale = new Vector3(0.004f, 0.038f, 0.048f);
            Destroy(strut.GetComponent<Collider>());
            strut.GetComponent<Renderer>().sharedMaterial = frameMat;
            strut.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Top Hood Arch
        GameObject topBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        topBar.transform.SetParent(sightRoot.transform, false);
        topBar.transform.localPosition = new Vector3(0f, 0.020f, 0f);
        topBar.transform.localScale = new Vector3(0.044f, 0.004f, 0.048f);
        Destroy(topBar.GetComponent<Collider>());
        topBar.GetComponent<Renderer>().sharedMaterial = frameMat;
        topBar.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Glowing Holographic Reflex Dot inside the lens window
        GameObject holoDot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        holoDot.transform.SetParent(sightRoot.transform, false);
        holoDot.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        holoDot.transform.localRotation = Quaternion.identity;
        holoDot.transform.localScale = new Vector3(0.0055f, 0.0055f, 0.0055f);
        Destroy(holoDot.GetComponent<Collider>());
        holoDot.GetComponent<Renderer>().sharedMaterial = dotMat;
        holoDot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void BuildScopeOverlayUI()
    {
        if (_scopeCanvas != null) Destroy(_scopeCanvas);

        _scopeCanvas = new GameObject("Oasis_PrecisionScope_Canvas");
        Canvas canvas = _scopeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;

        CanvasScaler scaler = _scopeCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Open-Center Holographic Frame (100% transparent center so the target is crystal clear!)
        GameObject lensAperture = new GameObject("Scope_LensFrame");
        lensAperture.transform.SetParent(_scopeCanvas.transform, false);
        RectTransform lRT = lensAperture.AddComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0.5f, 0.5f);
        lRT.anchorMax = new Vector2(0.5f, 0.5f);
        lRT.sizeDelta = new Vector2(520f, 520f);
        lRT.anchoredPosition = Vector2.zero;

        Color reticleCyan = new Color(0.2f, 0.95f, 1.0f, 0.78f);
        Color milDotColor = new Color(0.35f, 1.0f, 0.85f, 0.65f);

        // Corner brackets (open center)
        float bDist = 175f;
        float bLen = 48f;
        float bThick = 2f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                CreateScopeLine(lensAperture.transform, new Vector2(sx * (bDist - bLen * 0.5f), sy * bDist), new Vector2(bLen, bThick), reticleCyan);
                CreateScopeLine(lensAperture.transform, new Vector2(sx * bDist, sy * (bDist - bLen * 0.5f)), new Vector2(bThick, bLen), reticleCyan);
            }
        }

        // Fine precision stadia lines outside the center
        CreateScopeLine(lensAperture.transform, new Vector2(-95, 0), new Vector2(70, 1.5f), milDotColor);
        CreateScopeLine(lensAperture.transform, new Vector2(95, 0), new Vector2(70, 1.5f), milDotColor);
        CreateScopeLine(lensAperture.transform, new Vector2(0, -95), new Vector2(1.5f, 70), milDotColor);

        // Rangefinder readout below reticle
        GameObject rangeObj = new GameObject("Scope_RangeText");
        rangeObj.transform.SetParent(lensAperture.transform, false);
        RectTransform rngRT = rangeObj.AddComponent<RectTransform>();
        rngRT.anchorMin = new Vector2(0.5f, 0.5f);
        rngRT.anchorMax = new Vector2(0.5f, 0.5f);
        rngRT.sizeDelta = new Vector2(260, 26);
        rngRT.anchoredPosition = new Vector2(0, -148);
        _scopeRangeText = rangeObj.AddComponent<Text>();
        _scopeRangeText.font = font;
        _scopeRangeText.fontSize = 13;
        _scopeRangeText.fontStyle = FontStyle.Bold;
        _scopeRangeText.text = "TARGET RANGE: ---";
        _scopeRangeText.color = new Color(0.25f, 0.95f, 1.0f, 0.92f);
        _scopeRangeText.alignment = TextAnchor.MiddleCenter;

        // Weapon Optic Header Label
        GameObject topLabel = new GameObject("Scope_TopLabel");
        topLabel.transform.SetParent(lensAperture.transform, false);
        RectTransform tlRT = topLabel.AddComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.5f, 0.5f);
        tlRT.anchorMax = new Vector2(0.5f, 0.5f);
        tlRT.sizeDelta = new Vector2(460, 26);
        tlRT.anchoredPosition = new Vector2(0, 198);
        _scopeWeaponLabel = topLabel.AddComponent<Text>();
        _scopeWeaponLabel.font = font;
        _scopeWeaponLabel.fontSize = 12;
        _scopeWeaponLabel.fontStyle = FontStyle.Bold;
        _scopeWeaponLabel.text = "[ REFLEX HOLOSIGHT // CALIBRATED ]";
        _scopeWeaponLabel.color = new Color(0.25f, 0.95f, 1.0f, 0.80f);
        _scopeWeaponLabel.alignment = TextAnchor.MiddleCenter;

        _scopeCanvas.SetActive(false);
    }

    private void CreateScopeLine(Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject line = new GameObject("ScopeReticleLine");
        line.transform.SetParent(parent, false);
        RectTransform rt = line.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        Image img = line.AddComponent<Image>();
        img.color = color;
    }
}
