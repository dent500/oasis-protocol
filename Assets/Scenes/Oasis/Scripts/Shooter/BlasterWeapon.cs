using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-Person Blaster Weapon with 3D procedural model, weapon sway, bobbing,
/// recoil, raycast shooting, laser tracer VFX, ammo/reload, and audio.
/// </summary>
public class BlasterWeapon : MonoBehaviour
{
    [Header("Weapon Stats")]
    public float damage = 25f;
    public float fireRate = 0.18f; // ~330 RPM
    public float range = 150f;
    public int maxAmmo = 20;
    public int currentAmmo;
    public float reloadTime = 1.1f;

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
    private StarterAssets.FirstPersonController _fpc;
    private float _origRotationSpeed = 1.0f;

    // Holographic Scope UI
    private GameObject _scopeCanvas;
    private Text _scopeRangeText;

    private float _nextFireTime = 0f;
    private bool _isReloading = false;
    public bool isReloading => _isReloading;
    private Vector3 _currentRecoilPos;
    private Vector3 _currentRecoilRot;

    public void SetWeaponVisualActive(bool active)
    {
        if (_weaponRoot != null)
        {
            _weaponRoot.gameObject.SetActive(active);
        }
    }

    private void Start()
    {
        currentAmmo = maxAmmo;
        _camera = GetComponentInParent<Camera>() ?? Camera.main;
        if (_camera != null) defaultFOV = _camera.fieldOfView;

        _fpc = GetComponentInParent<StarterAssets.FirstPersonController>();
        if (_fpc != null) _origRotationSpeed = _fpc.RotationSpeed;

        // Create audio source
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 0f; // 2D weapon sound for player

        // Build procedural 3D blaster gun model
        BuildProceduralGunModel();

        // Build holographic precision scope UI
        BuildScopeOverlayUI();

        // Sync initial visibility based on third person camera mode
        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson)
        {
            SetWeaponVisualActive(false);
        }

        // Update HUD
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
    }

    private void OnDisable()
    {
        if (_fpc != null)
        {
            _fpc.RotationSpeed = _origRotationSpeed;
        }
    }

    private void Update()
    {
        bool inactiveCombat =
            (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isMatchFinished || OasisShooterManager.Instance.isSimulationMode)) ||
            (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive);

        if (inactiveCombat)
        {
            isScoped = false;
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

        // Controlled 1st-person scope is active whenever holding left click (1st person mode) OR right click in 1st person
        isScoped = (!is3rdPerson && fireHeld) || (!is3rdPerson && scopeHeld);

        if (reloadPressed && currentAmmo < maxAmmo && !_isReloading)
        {
            StartCoroutine(Reload());
        }

        if (fireHeld && Time.time >= _nextFireTime)
        {
            if (OasisWaterZone.IsInWater(transform.position))
            {
                OasisShooterManager.Instance?.ShowNotificationBanner("<color=#26D9FF>WATER NO-FIRE SANCTUARY // WEAPONS DISABLED IN WATER</color>", 1.5f);
                return;
            }

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

    private void UpdateScopeState()
    {
        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        bool is3rdAiming = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isAimingThirdPerson;

        float targetFOV = defaultFOV;
        if (!is3rdPerson)
        {
            targetFOV = 34f; // Tight precision FOV for 1st-person controlled aimed shot
        }
        else if (is3rdAiming)
        {
            targetFOV = 48f; // Tight over-the-shoulder zoom for 3rd person aim
        }

        // 1. Camera FOV zoom
        if (_camera != null)
        {
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFOV, Time.deltaTime * scopeZoomSpeed);
        }

        // 2. Weapon position in 1st person (centers gun down sights for controlled shot)
        if (_weaponRoot != null && !is3rdPerson)
        {
            Vector3 targetPos = aimPosition;
            _weaponRoot.localPosition = Vector3.Lerp(_weaponRoot.localPosition, targetPos, Time.deltaTime * scopeZoomSpeed);
        }

        // 3. Micro-aim sensitivity
        if (_fpc != null)
        {
            bool isPrecisionAim = !is3rdPerson || is3rdAiming;
            _fpc.RotationSpeed = isPrecisionAim ? (_origRotationSpeed * 0.40f) : _origRotationSpeed;
        }

        // 4. Scope Overlay visibility and telemetry
        if (_scopeCanvas != null)
        {
            _scopeCanvas.SetActive(!is3rdPerson);
            if (!is3rdPerson && _scopeRangeText != null && _camera != null)
            {
                Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (Physics.Raycast(ray, out RaycastHit hit, 250f))
                {
                    _scopeRangeText.text = $"RANGE: {hit.distance:F1}M";
                }
                else
                {
                    _scopeRangeText.text = "RANGE: ---";
                }
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
        _currentRecoilRot += new Vector3(-recoilRotation, Random.Range(-1f, 1f), 0);

        if (OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.robotHeroAnimator != null)
        {
            OasisCombatMovement.Instance.robotHeroAnimator.Play("Shoot_Autoshot_AR", 0, 0f);
        }

        // Sound
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.38f);
        }

        // Muzzle Flash
        if (_muzzleFlashLight != null)
        {
            StartCoroutine(FlashMuzzleLight());
        }

        // Raycast shooting:
        // Crosshair is at viewport center (0.5, 0.5)
        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        Vector3 targetPoint;
        Vector3 muzzlePos;
        bool is3rdPerson = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;

        if (is3rdPerson && OasisCombatMovement.Instance.robotHeroVisual != null)
        {
            // Position laser shot directly from the rifle barrel bone tip in third person
            muzzlePos = OasisCombatMovement.Instance.GetThirdPersonMuzzlePosition();
        }
        else
        {
            muzzlePos = _muzzleTransform != null ? _muzzleTransform.position : transform.position + transform.forward * 0.5f;
        }

        // Raycast from camera center to determine what the crosshair is pointing at in world space.
        // We filter out triggers (volumes, zones), local player colliders, and friendly AI squadmates.
        int localId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

        RaycastHit[] hits = Physics.RaycastAll(ray, range, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        RaycastHit bestHit = default;
        bool hasHit = false;
        float closestDist = float.MaxValue;

        Transform playerRoot = OasisCombatMovement.Instance != null ? OasisCombatMovement.Instance.transform : null;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.collider.isTrigger) continue;
            if (playerRoot != null && (h.collider.transform.IsChildOf(playerRoot) || h.collider.transform == playerRoot))
            {
                continue; // Ignore local player body
            }

            // Pass through friendly AI teammates so they never block shots at enemies
            OasisCombatAI aiCheck = h.collider.GetComponentInParent<OasisCombatAI>();
            if (aiCheck != null && aiCheck.teamId == localTeam)
            {
                continue;
            }

            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                bestHit = h;
                hasHit = true;
            }
        }

        // If the thin raycast missed an enemy AI (e.g. aimed at arms/shoulders/legs outside capsule center),
        // perform a 0.34m sphere cast check to reliably catch enemy CPU operatives before any wall obstacle.
        OasisCombatAI directHitAI = hasHit ? bestHit.collider.GetComponentInParent<OasisCombatAI>() : null;
        if (directHitAI == null)
        {
            float maxCheckDist = hasHit ? closestDist : range;
            RaycastHit[] sphereHits = Physics.SphereCastAll(ray, 0.34f, maxCheckDist, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float bestAIDist = maxCheckDist;
            for (int i = 0; i < sphereHits.Length; i++)
            {
                RaycastHit sh = sphereHits[i];
                if (sh.collider == null || sh.collider.isTrigger) continue;
                OasisCombatAI candidateAI = sh.collider.GetComponentInParent<OasisCombatAI>();
                if (candidateAI != null && !candidateAI.isDead && candidateAI.teamId != localTeam && sh.distance < bestAIDist)
                {
                    bestAIDist = sh.distance;
                    bestHit = sh;
                    hasHit = true;
                }
            }
        }

        TargetDrone hitDrone = null;
        OasisCombatAI hitAI = null;
        if (hasHit)
        {
            targetPoint = bestHit.point;

            // 1. Check if hit humanoid AI combatant
            hitAI = bestHit.collider.GetComponentInParent<OasisCombatAI>();
            if (hitAI != null)
            {
                if (hitAI.teamId != localTeam)
                {
                    hitAI.TakeDamage(damage, bestHit.point, bestHit.normal, localId, localTeam);
                    OasisAITelemetryLogger.Instance?.RecordPlayerHitLanded(hitAI, damage, bestHit.distance);
                    OasisCombatMovement.Instance?.TriggerHitmarker();
                    OasisShooterManager.Instance?.OnTargetHit(damage, hitAI.isDowned);
                    OasisShooterManager.Instance?.AddPlayerScore(15);
                    if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.65f);
                }
            }
            // 2. Check if hit a target drone
            else
            {
                hitDrone = bestHit.collider.GetComponentInParent<TargetDrone>();
                if (hitDrone != null)
                {
                    hitDrone.TakeDamage(damage, bestHit.point, bestHit.normal, localId);
                    OasisCombatMovement.Instance?.TriggerHitmarker();
                }
                else
                {
                    // Hit world (sand, rock, metal)
                    BlasterVFX.SpawnImpact(bestHit.point, bestHit.normal, false);
                    _audioSource.PlayOneShot(ProceduralAudio.GetHitWorldClip(), 0.5f);
                }
            }
        }
        else
        {
            // If no collision within range, project target point along camera look direction
            targetPoint = ray.GetPoint(range);
        }

        // Secondary raycast verification in 3rd person:
        // Ensure nothing immediately obstructs the path from the rifle muzzle to the target point (only if we didn't already confirm an enemy AI hit)
        if (is3rdPerson && hitAI == null)
        {
            Vector3 muzzleToTarget = targetPoint - muzzlePos;
            float dist = muzzleToTarget.magnitude;
            if (dist > 0.1f)
            {
                if (Physics.Raycast(muzzlePos, muzzleToTarget.normalized, out RaycastHit muzzleHit, dist, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    if (playerRoot == null || (!muzzleHit.collider.transform.IsChildOf(playerRoot) && muzzleHit.collider.transform != playerRoot))
                    {
                        OasisCombatAI obstacleAI = muzzleHit.collider.GetComponentInParent<OasisCombatAI>();
                        if (obstacleAI != null && obstacleAI.teamId == localTeam)
                        {
                            // Ignore friendly AI in front of muzzle
                        }
                        else
                        {
                            targetPoint = muzzleHit.point;
                            if (obstacleAI != null && obstacleAI.teamId != localTeam)
                            {
                                hitAI = obstacleAI;
                                obstacleAI.TakeDamage(damage, muzzleHit.point, muzzleHit.normal, localId, localTeam);
                                OasisCombatMovement.Instance?.TriggerHitmarker();
                                OasisShooterManager.Instance?.OnTargetHit(damage, obstacleAI.isDowned);
                                OasisShooterManager.Instance?.AddPlayerScore(15);
                                if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 0.65f);
                            }
                            else
                            {
                                TargetDrone obstacleDrone = muzzleHit.collider.GetComponentInParent<TargetDrone>();
                                if (obstacleDrone != null && hitDrone == null)
                                {
                                    hitDrone = obstacleDrone;
                                    hitDrone.TakeDamage(damage, muzzleHit.point, muzzleHit.normal, localId);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Laser beam tracer
        BlasterVFX.SpawnLaserBeam(muzzlePos, targetPoint, new Color(0.15f, 0.9f, 1f));

        // Broadcast shot over network to sync with all co-op operatives
        OasisNetworkManager.Instance?.BroadcastLaserFire(muzzlePos, targetPoint, hitDrone != null);
    }

    private IEnumerator FlashMuzzleLight()
    {
        _muzzleFlashLight.enabled = true;
        yield return new WaitForSeconds(0.04f);
        if (_muzzleFlashLight != null) _muzzleFlashLight.enabled = false;
    }

    private IEnumerator Reload()
    {
        _isReloading = true;
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

        // Reload animation dip
        Vector3 origRecoil = _currentRecoilPos;
        _currentRecoilPos += new Vector3(0, -0.15f, -0.05f);

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = maxAmmo;
        _isReloading = false;
        OasisShooterManager.Instance?.UpdateAmmo(currentAmmo, maxAmmo, _isReloading);
    }

    private void HandleSwayAndBob()
    {
        if (_weaponRoot == null) return;

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

        // Walking bobbing
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
    /// Constructs a sleek 3D sci-fi blaster gun with barrel, glowing energy cell, and muzzle light.
    /// </summary>
    private void BuildProceduralGunModel()
    {
        GameObject root = new GameObject("BlasterModelRoot");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = hipPosition;
        _weaponRoot = root.transform;

        Material gunMetalMat = OasisShaderHelper.CreateLitMaterial(new Color(0.18f, 0.2f, 0.25f), 0.9f, 0.8f);
        Material neonMat = OasisShaderHelper.CreateUnlitMaterial(new Color(0.1f, 0.95f, 1f)); // Glowing cyan energy

        // 1. Receiver / Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Receiver";
        body.transform.SetParent(_weaponRoot, false);
        body.transform.localPosition = new Vector3(0, 0, 0);
        body.transform.localScale = new Vector3(0.07f, 0.11f, 0.28f);
        body.GetComponent<MeshRenderer>().material = gunMetalMat;
        Destroy(body.GetComponent<Collider>());

        // 2. Barrel
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Barrel";
        barrel.transform.SetParent(_weaponRoot, false);
        barrel.transform.localPosition = new Vector3(0, 0.02f, 0.24f);
        barrel.transform.localScale = new Vector3(0.045f, 0.12f, 0.045f);
        barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
        barrel.GetComponent<MeshRenderer>().material = gunMetalMat;
        Destroy(barrel.GetComponent<Collider>());

        // 3. Glowing Energy Core (Cyan)
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        core.name = "EnergyCore";
        core.transform.SetParent(_weaponRoot, false);
        core.transform.localPosition = new Vector3(0, -0.01f, 0.02f);
        core.transform.localScale = new Vector3(0.055f, 0.08f, 0.055f);
        core.transform.localRotation = Quaternion.Euler(90, 0, 0);
        core.GetComponent<MeshRenderer>().material = neonMat;
        Destroy(core.GetComponent<Collider>());

        // 4. Handle / Grip
        GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        grip.name = "Grip";
        grip.transform.SetParent(_weaponRoot, false);
        grip.transform.localPosition = new Vector3(0, -0.1f, -0.06f);
        grip.transform.localScale = new Vector3(0.05f, 0.12f, 0.06f);
        grip.transform.localRotation = Quaternion.Euler(18, 0, 0);
        grip.GetComponent<MeshRenderer>().material = gunMetalMat;
        Destroy(grip.GetComponent<Collider>());

        // 5. Sight
        GameObject sight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sight.name = "Sight";
        sight.transform.SetParent(_weaponRoot, false);
        sight.transform.localPosition = new Vector3(0, 0.07f, 0.08f);
        sight.transform.localScale = new Vector3(0.015f, 0.025f, 0.04f);
        sight.GetComponent<MeshRenderer>().material = neonMat;
        Destroy(sight.GetComponent<Collider>());

        // 6. Muzzle Point
        GameObject muzzle = new GameObject("MuzzlePoint");
        muzzle.transform.SetParent(_weaponRoot, false);
        muzzle.transform.localPosition = new Vector3(0, 0.02f, 0.38f);
        _muzzleTransform = muzzle.transform;

        // 7. Muzzle Flash Point Light
        GameObject flashLightObj = new GameObject("MuzzleFlashLight");
        flashLightObj.transform.SetParent(muzzle.transform, false);
        _muzzleFlashLight = flashLightObj.AddComponent<Light>();
        _muzzleFlashLight.type = LightType.Point;
        _muzzleFlashLight.color = new Color(0.2f, 0.9f, 1f);
        _muzzleFlashLight.range = 6f;
        _muzzleFlashLight.intensity = 5f;
        _muzzleFlashLight.enabled = false;
    }

    /// <summary>
    /// Constructs a holographic precision sniper scope overlay on screen with reticle,
    /// mil-dots, dynamic rangefinder readout, and peripheral vignette for ADS targeting.
    /// </summary>
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

        // 1. Dark Vignette Outer Mask (blocks peripheral vision for precision ADS feel)
        GameObject vignette = new GameObject("Scope_PeripheralVignette");
        vignette.transform.SetParent(_scopeCanvas.transform, false);
        RectTransform vRT = vignette.AddComponent<RectTransform>();
        vRT.anchorMin = Vector2.zero;
        vRT.anchorMax = Vector2.one;
        vRT.offsetMin = Vector2.zero;
        vRT.offsetMax = Vector2.zero;
        Image vImg = vignette.AddComponent<Image>();
        vImg.color = new Color(0.01f, 0.02f, 0.04f, 0.82f);

        // 2. Clear Center Lens Aperture
        GameObject lensAperture = new GameObject("Scope_LensFrame");
        lensAperture.transform.SetParent(_scopeCanvas.transform, false);
        RectTransform lRT = lensAperture.AddComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0.5f, 0.5f);
        lRT.anchorMax = new Vector2(0.5f, 0.5f);
        lRT.sizeDelta = new Vector2(640f, 640f);
        lRT.anchoredPosition = Vector2.zero;

        // Circular Ring Frame
        GameObject ring1 = new GameObject("Ring_Outer");
        ring1.transform.SetParent(lensAperture.transform, false);
        RectTransform r1RT = ring1.AddComponent<RectTransform>();
        r1RT.anchorMin = Vector2.zero;
        r1RT.anchorMax = Vector2.one;
        r1RT.offsetMin = Vector2.zero;
        r1RT.offsetMax = Vector2.zero;
        Image r1Img = ring1.AddComponent<Image>();
        r1Img.color = new Color(0.15f, 0.85f, 1.0f, 0.65f);

        // Lens inner background - slightly blue-tinted lens glass
        GameObject lensGlass = new GameObject("Lens_Glass");
        lensGlass.transform.SetParent(lensAperture.transform, false);
        RectTransform lgRT = lensGlass.AddComponent<RectTransform>();
        lgRT.anchorMin = Vector2.zero;
        lgRT.anchorMax = Vector2.one;
        lgRT.offsetMin = new Vector2(3, 3);
        lgRT.offsetMax = new Vector2(-3, -3);
        Image lgImg = lensGlass.AddComponent<Image>();
        lgImg.color = new Color(0.02f, 0.06f, 0.12f, 0.28f);

        // 3. Crosshairs & Reticle Lines
        Color reticleCyan = new Color(0.2f, 0.95f, 1.0f, 0.9f);
        Color milDotColor = new Color(0.35f, 1.0f, 0.85f, 0.8f);

        // Horizontal Line Left & Right
        CreateScopeLine(lensAperture.transform, new Vector2(-190, 0), new Vector2(240, 2), reticleCyan);
        CreateScopeLine(lensAperture.transform, new Vector2(190, 0), new Vector2(240, 2), reticleCyan);
        // Vertical Line Top & Bottom
        CreateScopeLine(lensAperture.transform, new Vector2(0, 190), new Vector2(2, 240), reticleCyan);
        CreateScopeLine(lensAperture.transform, new Vector2(0, -190), new Vector2(2, 240), reticleCyan);

        // Mil-dots on horizontal and elevation axes
        for (int m = 1; m <= 4; m++)
        {
            float offset = m * 38f;
            CreateScopeLine(lensAperture.transform, new Vector2(-offset, 0), new Vector2(2, 10), milDotColor);
            CreateScopeLine(lensAperture.transform, new Vector2(offset, 0), new Vector2(2, 10), milDotColor);
            CreateScopeLine(lensAperture.transform, new Vector2(0, -offset), new Vector2(10 + m * 3, 2), milDotColor);
        }

        // Center Precision Aim Point (Small bright dot)
        GameObject centerDot = new GameObject("Center_PrecisionDot");
        centerDot.transform.SetParent(lensAperture.transform, false);
        RectTransform cdRT = centerDot.AddComponent<RectTransform>();
        cdRT.anchorMin = new Vector2(0.5f, 0.5f);
        cdRT.anchorMax = new Vector2(0.5f, 0.5f);
        cdRT.sizeDelta = new Vector2(4f, 4f);
        cdRT.anchoredPosition = Vector2.zero;
        Image cdImg = centerDot.AddComponent<Image>();
        cdImg.color = new Color(1.0f, 0.25f, 0.2f, 1.0f);

        // 4. Tactical Scope Telemetry & Readouts
        // Dynamic Rangefinder Text
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

        // Optic Mode / Zoom Level Text
        GameObject zoomObj = new GameObject("Scope_ZoomText");
        zoomObj.transform.SetParent(lensAperture.transform, false);
        RectTransform zmRT = zoomObj.AddComponent<RectTransform>();
        zmRT.anchorMin = new Vector2(0.5f, 0.5f);
        zmRT.anchorMax = new Vector2(0.5f, 0.5f);
        zmRT.sizeDelta = new Vector2(200, 30);
        zmRT.anchoredPosition = new Vector2(-125, -60);
        Text zmText = zoomObj.AddComponent<Text>();
        zmText.font = font;
        zmText.fontSize = 13;
        zmText.text = "OPTIC // 3.5X ZOOM";
        zmText.color = new Color(0.6f, 0.85f, 1.0f, 0.8f);
        zmText.alignment = TextAnchor.MiddleRight;

        // Header Status Text
        GameObject topLabel = new GameObject("Scope_TopLabel");
        topLabel.transform.SetParent(lensAperture.transform, false);
        RectTransform tlRT = topLabel.AddComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.5f, 0.5f);
        tlRT.anchorMax = new Vector2(0.5f, 0.5f);
        tlRT.sizeDelta = new Vector2(240, 24);
        tlRT.anchoredPosition = new Vector2(0, 160);
        Text tlText = topLabel.AddComponent<Text>();
        tlText.font = font;
        tlText.fontSize = 12;
        tlText.fontStyle = FontStyle.Bold;
        tlText.text = "[ HOLOGRAPHIC TARGETING SYS ]";
        tlText.color = new Color(0.2f, 0.95f, 1.0f, 0.75f);
        tlText.alignment = TextAnchor.MiddleCenter;

        // Hidden by default until player holds right-click
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
