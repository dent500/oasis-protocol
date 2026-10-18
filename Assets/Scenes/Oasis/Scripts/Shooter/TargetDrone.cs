using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyType
{
    ScoutDrone = 0,
    HeavySiegeDrone = 1,
    GroundWalkerMech = 2
}

/// <summary>
/// Sci-Fi Enemy Unit supporting multiple spawn archetypes:
/// - Scout Drone: Fast, agile, twin blaster counter-fire.
/// - Heavy Siege Drone: Quad heavy blasters, armored chassis, durable shielding.
/// - Ground Walker Mech: Armored bipedal walker patrolling dunes with heavy pulse cannons.
/// Includes comprehensive bunker & shelter avoidance so units never clip inside or through bunker roofs.
/// </summary>
public class TargetDrone : MonoBehaviour
{
    [Header("Enemy Classification")]
    public EnemyType enemyType = EnemyType.ScoutDrone;

    [Header("Drone Stats")]
    public float maxHealth = 60f;
    public float currentHealth;
    public int scoreValue = 150;
    public bool isHostile = true;
    public float attackInterval = 3.5f;

    [Header("Hover & Movement")]
    public float hoverAmplitude = 0.4f;
    public float hoverFrequency = 1.6f;
    public float moveSpeed = 2.4f;         // Smooth, steady combat fly speed
    public float idealCombatDistance = 18f; // Closes distance towards player until reaching comfortable combat range
    public float minCombatDistance = 11f;   // Backs off smoothly if player gets too close
    public float minTerrainHeight = 3.5f;   // Keeps drone floating safely above dunes and rocks

    [Header("Network Sync")]
    public int networkDroneId = -1;
    public bool isNetworkPuppet = false;
    public Vector3 networkTargetPosition;
    public Quaternion networkTargetRotation;

    private Vector3 _spawnPosition;
    private float _hoverOffset;
    private Transform _playerTransform;
    private float _attackTimer;
    private MeshRenderer[] _renderers;
    private Color[] _originalColors;
    private bool _isDestroyed = false;
    private float _strafeAngle;
    private float _strafeChangeTimer;
    private float _retargetTimer = 0f;
    public int lastAttackerPlayerId = 0; // Tracks which operative dealt the fatal or most recent blow

    private AudioSource _audioSource;
    private Transform _eyeTransform;
    private Transform _ringTransform;
    private Transform _ring2Transform;
    private Transform _leftCannonTip;
    private Transform _rightCannonTip;
    private Transform _leftCannonTip2;
    private Transform _rightCannonTip2;
    private Transform _leftLegPivot;
    private Transform _rightLegPivot;
    private Material _eyeMaterial;
    private Light _thrusterLight;
    private float _pulseTimer;
    private bool _isSpawningIn = true;
    private float _spawnProgress = 0f;
    private float _mechWalkCycle = 0f;

    private void Start()
    {
        currentHealth = maxHealth;
        _spawnPosition = transform.position;
        _hoverOffset = Random.Range(0f, Mathf.PI * 2f);
        _attackTimer = Random.Range(2.5f, attackInterval);
        _strafeAngle = Random.Range(-1f, 1f) > 0 ? 1f : -1f;
        _strafeChangeTimer = Random.Range(3f, 5f);

        // Find player
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
        }

        // Cache renderers for damage flash
        _renderers = GetComponentsInChildren<MeshRenderer>();
        if (_renderers != null && _renderers.Length > 0)
        {
            _originalColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i].material.HasProperty("_BaseColor"))
                    _originalColors[i] = _renderers[i].material.GetColor("_BaseColor");
                else if (_renderers[i].material.HasProperty("_Color"))
                    _originalColors[i] = _renderers[i].material.GetColor("_Color");
                else
                    _originalColors[i] = Color.white;
            }
        }

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 0.85f;
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.maxDistance = 75f;

        // If no model exists under this GameObject, build procedural model based on enemy archetype!
        if (transform.childCount == 0)
        {
            switch (enemyType)
            {
                case EnemyType.HeavySiegeDrone:
                    BuildHeavySiegeDroneModel();
                    break;
                case EnemyType.GroundWalkerMech:
                    BuildGroundMechModel();
                    break;
                case EnemyType.ScoutDrone:
                default:
                    BuildProceduralDroneModel();
                    break;
            }
        }

        // Materialize warp animation: start small and scale up smoothly with audio and flash VFX
        transform.localScale = Vector3.zero;
        BlasterVFX.SpawnWarpIn(transform.position);
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetWarpSpawnClip(), 0.7f);
        }
    }

    private void Update()
    {
        if (_isDestroyed) return;

        // Smooth spawn scale-in animation (takes 0.6 seconds so they materialize clearly into existence)
        if (_isSpawningIn)
        {
            _spawnProgress += Time.deltaTime * 1.8f;
            float scale = Mathf.SmoothStep(0f, 1f, _spawnProgress);
            transform.localScale = Vector3.one * scale;
            if (_spawnProgress >= 1f)
            {
                _isSpawningIn = false;
                transform.localScale = Vector3.one;
            }
        }

        // NETWORK PUPPET MODE: Smoothly follow Host's synced position and rotation
        if (isNetworkPuppet)
        {
            transform.position = Vector3.Lerp(transform.position, networkTargetPosition, Time.deltaTime * 12f);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkTargetRotation, Time.deltaTime * 12f);

            // Counter-rotating rings still rotate visually on puppet
            if (_ringTransform != null) _ringTransform.Rotate(Vector3.forward, 150f * Time.deltaTime, Space.Self);
            if (_ring2Transform != null) _ring2Transform.Rotate(Vector3.forward, -190f * Time.deltaTime, Space.Self);

            // Core glow pulsing
            _pulseTimer += Time.deltaTime * 4f;
            float pPulse = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;
            if (_eyeMaterial != null)
            {
                Color baseEye = isHostile ? new Color(1f, 0.15f, 0.05f) : new Color(0.1f, 0.8f, 1f);
                Color glowColor = Color.Lerp(baseEye * 1.2f, baseEye * 2.8f, pPulse);
                if (_eyeMaterial.HasProperty("_EmissionColor")) _eyeMaterial.SetColor("_EmissionColor", glowColor);
                _eyeMaterial.color = glowColor;
            }
            if (_thrusterLight != null)
            {
                _thrusterLight.intensity = Mathf.Lerp(2.2f, 4.0f, pPulse);
            }

            return; // Puppet does not run local movement AI or local attack decisions
        }

        // Re-acquire and evaluate best target operative (Local or Remote Co-Op Operatives)
        _retargetTimer -= Time.deltaTime;
        if (_playerTransform == null || _retargetTimer <= 0f)
        {
            _retargetTimer = Random.Range(1.8f, 2.8f);
            AcquireBestTargetOperative();
        }

        Vector3 targetMovePos = transform.position;

        // 1. FLY TOWARDS PLAYER & MAINTAIN CLEAR VISUAL LINE OF SIGHT
        if (_playerTransform != null)
        {
            Vector3 playerTarget = _playerTransform.position + Vector3.up * 1.5f;
            Vector3 toPlayer = playerTarget - transform.position;
            Vector3 toPlayerXZ = new Vector3(toPlayer.x, 0f, toPlayer.z);
            float distToPlayer = toPlayerXZ.magnitude;
            Vector3 dirToPlayerXZ = distToPlayer > 0.001f ? toPlayerXZ.normalized : transform.forward;

            // Timer to periodically switch strafe drift direction
            _strafeChangeTimer -= Time.deltaTime;
            if (_strafeChangeTimer <= 0f)
            {
                _strafeChangeTimer = Random.Range(3.5f, 6.0f);
                _strafeAngle = -_strafeAngle;
            }

            Vector3 strafeDir = Vector3.Cross(Vector3.up, dirToPlayerXZ) * _strafeAngle;
            Vector3 moveDirection = Vector3.zero;

            // If further than ideal combat range, pursue directly towards the player
            if (distToPlayer > idealCombatDistance)
            {
                moveDirection = (dirToPlayerXZ * 0.8f + strafeDir * 0.3f).normalized;
            }
            // If too close, back off smoothly
            else if (distToPlayer < minCombatDistance)
            {
                moveDirection = (-dirToPlayerXZ * 0.6f + strafeDir * 0.4f).normalized;
            }
            // Within combat sweet spot: orbit and hover in sight
            else
            {
                moveDirection = strafeDir.normalized;
            }

            // Safe Spawn Shelter Exclusion Zone:
            // Drones are strictly repelled from the safe tent shelter radius so the player is never swamped at spawn
            Vector3 tentCenter = OasisWorldExpander.TentShelterCenter;
            Vector3 droneXZ = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 tentXZ = new Vector3(tentCenter.x, 0, tentCenter.z);
            float distToTent = Vector3.Distance(droneXZ, tentXZ);
            float shelterRadius = OasisWorldExpander.TentShelterRadius;

            bool playerInShelter = Vector3.Distance(new Vector3(playerTarget.x, 0, playerTarget.z), tentXZ) < (shelterRadius - 3f);
            if (playerInShelter && distToTent < (shelterRadius + 4f))
            {
                // Localize patrol outside the tent entrance in the open canyon
                Vector3 awayFromTent = (droneXZ - tentXZ).normalized;
                if (awayFromTent.sqrMagnitude < 0.01f) awayFromTent = Vector3.forward;
                Vector3 patrolDir = Vector3.Cross(Vector3.up, awayFromTent);
                moveDirection = (awayFromTent * 0.7f + patrolDir * _strafeAngle * 0.7f).normalized;
            }
            else if (distToTent < shelterRadius)
            {
                Vector3 repelDir = (droneXZ - tentXZ).normalized;
                if (repelDir.sqrMagnitude < 0.01f) repelDir = Vector3.forward;
                float pushWeight = Mathf.Clamp01((shelterRadius - distToTent) / shelterRadius);
                moveDirection = Vector3.Lerp(moveDirection, repelDir, pushWeight * 0.85f).normalized;
            }

            // Mountain / Obstacle Avoidance:
            RaycastHit hit;
            if (Physics.SphereCast(transform.position, 1.2f, moveDirection, out hit, 4.0f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.CompareTag("Player") && !hit.collider.GetComponent<TargetDrone>())
                {
                    Vector3 reflect = Vector3.Reflect(moveDirection, hit.normal);
                    reflect.y = 0f;
                    moveDirection = (reflect * 0.6f + dirToPlayerXZ * 0.7f).normalized;
                }
            }

            // Apply smooth horizontal motion (damped during spawn-in)
            float activeSpeed = _isSpawningIn ? moveSpeed * 0.4f : moveSpeed;
            targetMovePos += moveDirection * activeSpeed * Time.deltaTime;

            // Maintain visible altitude relative to terrain and player
            float desiredY = playerTarget.y + 2.5f + Mathf.Sin(Time.time * hoverFrequency + _hoverOffset) * hoverAmplitude;

            if (Physics.Raycast(new Vector3(targetMovePos.x, targetMovePos.y + 20f, targetMovePos.z), Vector3.down, out hit, 40f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != gameObject)
                {
                    desiredY = Mathf.Max(desiredY, hit.point.y + minTerrainHeight);
                }
            }

            // Bunker Roof & Perimeter Barrier: ensure enemies never dip into or pass through bunker roof or walls
            if (OasisWorldExpander.HasBunker)
            {
                Bounds b = OasisWorldExpander.BunkerBounds;
                // Add horizontal margin buffer
                float margin = 2.0f;
                bool insideBunkerX = (targetMovePos.x >= (b.min.x - margin) && targetMovePos.x <= (b.max.x + margin));
                bool insideBunkerZ = (targetMovePos.z >= (b.min.z - margin) && targetMovePos.z <= (b.max.z + margin));

                if (insideBunkerX && insideBunkerZ)
                {
                    if (enemyType == EnemyType.GroundWalkerMech)
                    {
                        // Mechs are pushed out horizontally around bunker perimeter
                        Vector3 bCenter = b.center;
                        Vector3 pushOut = (new Vector3(targetMovePos.x, 0, targetMovePos.z) - new Vector3(bCenter.x, 0, bCenter.z)).normalized;
                        if (pushOut.sqrMagnitude < 0.01f) pushOut = Vector3.forward;
                        targetMovePos += pushOut * 8f * Time.deltaTime;
                    }
                    else
                    {
                        // Flying drones strictly fly above the bunker roof with clearance
                        desiredY = Mathf.Max(desiredY, b.max.y + 3.5f);

                        // If too low, repel outward away from bunker center
                        if (targetMovePos.y <= b.max.y + 2.0f)
                        {
                            Vector3 bCenter = b.center;
                            Vector3 pushOut = (new Vector3(targetMovePos.x, 0, targetMovePos.z) - new Vector3(bCenter.x, 0, bCenter.z)).normalized;
                            if (pushOut.sqrMagnitude < 0.01f) pushOut = Vector3.forward;
                            targetMovePos += pushOut * 6f * Time.deltaTime;
                        }
                    }
                }
            }

            // Ground Walker Mech vs Flying Drone altitude behavior
            if (enemyType == EnemyType.GroundWalkerMech)
            {
                // Walker Mech hugs the ground surface
                if (Physics.Raycast(new Vector3(targetMovePos.x, targetMovePos.y + 15f, targetMovePos.z), Vector3.down, out hit, 40f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.gameObject != gameObject)
                    {
                        desiredY = hit.point.y + 0.9f;
                    }
                }
                targetMovePos.y = Mathf.Lerp(transform.position.y, desiredY, Time.deltaTime * 10f);

                // Animate mechanical biped legs while moving
                if (activeSpeed > 0.1f)
                {
                    _mechWalkCycle += Time.deltaTime * 7.5f;
                    float legAngle = Mathf.Sin(_mechWalkCycle) * 28f;
                    if (_leftLegPivot != null) _leftLegPivot.localRotation = Quaternion.Euler(legAngle, 0f, 0f);
                    if (_rightLegPivot != null) _rightLegPivot.localRotation = Quaternion.Euler(-legAngle, 0f, 0f);
                }
            }
            else
            {
                targetMovePos.y = Mathf.Lerp(transform.position.y, desiredY, Time.deltaTime * 2.8f);
            }
        }
        else
        {
            // Fallback idle hover
            float hoverY = Mathf.Sin(Time.time * hoverFrequency + _hoverOffset) * hoverAmplitude;
            targetMovePos = _spawnPosition + Vector3.up * hoverY;
        }

        transform.position = targetMovePos;

        // 2. Rotate towards player with smooth turning
        if (_playerTransform != null)
        {
            Vector3 lookDir = (_playerTransform.position + Vector3.up * 1.2f - transform.position).normalized;
            if (lookDir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 3.5f);
            }
        }

        // 3. Counter-rotating Dual Kinetic/Energy Rings
        if (_ringTransform != null)
        {
            _ringTransform.Rotate(Vector3.forward, 150f * Time.deltaTime, Space.Self);
        }
        if (_ring2Transform != null)
        {
            _ring2Transform.Rotate(Vector3.forward, -190f * Time.deltaTime, Space.Self);
        }

        // 4. Pulsing Sci-Fi Core Glow & Engine flicker
        _pulseTimer += Time.deltaTime * 4f;
        float pulse = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f; // 0..1
        if (_eyeMaterial != null)
        {
            Color baseEye = isHostile ? new Color(1f, 0.15f, 0.05f) : new Color(0.1f, 0.8f, 1f);
            Color glowColor = Color.Lerp(baseEye * 1.2f, baseEye * 2.8f, pulse);
            if (_eyeMaterial.HasProperty("_EmissionColor")) _eyeMaterial.SetColor("_EmissionColor", glowColor);
            _eyeMaterial.color = glowColor;
        }
        if (_thrusterLight != null)
        {
            _thrusterLight.intensity = Mathf.Lerp(2.2f, 4.0f, pulse);
        }

        // 5. Hostile Counter-Fire with alternating twin blasters (STRICT LINE OF SIGHT CHECK)
        if (isHostile && _playerTransform != null && !_isSpawningIn)
        {
            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                _attackTimer = attackInterval + Random.Range(-0.4f, 0.6f);
                if (HasLineOfSightToPlayer())
                {
                    FireAtPlayer();
                }
            }
        }
    }

    /// <summary>
    /// Intelligently selects the highest-priority operative target:
    /// - Evaluates both the local player and any connected remote operatives.
    /// - Prioritizes operatives that have direct visual Line of Sight (unobstructed by rocks/dunes).
    /// - Breaks ties using 3D Euclidean distance (engaging the closest threat).
    /// </summary>
    private void AcquireBestTargetOperative()
    {
        List<Transform> candidates = null;
        if (OasisNetworkManager.Instance != null)
        {
            candidates = OasisNetworkManager.Instance.GetAllPlayerTransforms();
        }

        if (candidates == null || candidates.Count == 0)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _playerTransform = p.transform;
            return;
        }

        Transform bestTarget = null;
        float bestScore = float.MaxValue;

        Vector3 eyePos = _eyeTransform != null ? _eyeTransform.position : transform.position;

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform t = candidates[i];
            if (t == null) continue;

            float dist = Vector3.Distance(eyePos, t.position);
            bool hasLOS = CheckLineOfSightToTransform(t, eyePos);

            // Cost metric: Prioritize clear line of sight heavily (add 100m penalty if behind a mountain)
            float score = dist + (hasLOS ? 0f : 100f);

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = t;
            }
        }

        if (bestTarget != null)
        {
            _playerTransform = bestTarget;
        }
    }

    private bool CheckLineOfSightToTransform(Transform target, Vector3 eyePos)
    {
        if (target == null) return false;

        // If target operative is inside the Safe Spawn Shelter, drones cannot acquire line of sight
        if (OasisShooterManager.IsPositionInSafeShelter(target.position))
        {
            return false;
        }

        Vector3 targetPos = target.position + Vector3.up * 1.4f;
        Vector3 dir = targetPos - eyePos;
        float dist = dir.magnitude;
        if (dist > 60f) return false;

        RaycastHit hit;
        if (Physics.Raycast(eyePos, dir.normalized, out hit, dist, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            // If hit target directly or any player root/child
            if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
            if (hit.collider.CompareTag("Player")) return true;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if a direct unobstructed line of sight exists between the drone and its current target.
    /// Prevents drones from firing through mountains, rocks, cliffs, or sand dunes.
    /// </summary>
    public bool HasLineOfSightToPlayer()
    {
        if (_playerTransform == null) return false;
        Vector3 eyePos = _eyeTransform != null ? _eyeTransform.position : transform.position + transform.forward * 0.5f;
        return CheckLineOfSightToTransform(_playerTransform, eyePos);
    }

    private void FireAtPlayer()
    {
        if (_playerTransform == null) return;
        Vector3 aimPos = _playerTransform.position + Vector3.up * 1.2f;

        // Alternate fire between left/right wing cannons or central eye
        Vector3 firePos = transform.position + transform.forward * 0.7f;
        if (_leftCannonTip != null && _rightCannonTip != null)
        {
            firePos = (Random.value > 0.5f) ? _leftCannonTip.position : _rightCannonTip.position;
        }
        else if (_eyeTransform != null)
        {
            firePos = _eyeTransform.position;
        }

        Vector3 dir = (aimPos - firePos).normalized;

        SpawnPlasmaBolt(firePos, dir);

        // If Host in online co-op, broadcast drone attack to all clients so they see and hear the bolt fired
        if (OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastDroneFire(networkDroneId, firePos, dir);
        }
    }

    /// <summary>
    /// Spawns a glowing plasma bolt projectile in the given direction.
    /// Can be invoked directly by Host or when receiving a DRONE_FIRE network packet on a Client puppet.
    /// </summary>
    public void SpawnPlasmaBolt(Vector3 firePos, Vector3 dir)
    {
        GameObject boltObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boltObj.name = "DronePlasmaBolt";
        boltObj.transform.position = firePos;
        boltObj.transform.localScale = Vector3.one * 0.35f;

        // Glowing red material
        Material boltMat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.25f, 0.1f));
        boltObj.GetComponent<MeshRenderer>().material = boltMat;

        // Trigger collider
        SphereCollider sc = boltObj.GetComponent<SphereCollider>();
        sc.isTrigger = true;

        DronePlasmaBolt bolt = boltObj.AddComponent<DronePlasmaBolt>();
        bolt.Launch(dir);

        // Sound
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetBlasterShotClip(), 0.6f);
        }
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, int attackerId = 0)
    {
        if (_isDestroyed) return;

        lastAttackerPlayerId = attackerId;

        // In multiplayer client mode, report hit to the Host who has health authority
        if (isNetworkPuppet && networkDroneId >= 0 && OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
        {
            int myId = OasisNetworkManager.Instance.myPlayerId;
            // Play local cosmetic hit effects immediately for instant client feedback
            BlasterVFX.SpawnImpact(hitPoint, hitNormal, true);
            if (_audioSource != null) _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 1f);
            StartCoroutine(FlashWhite());
            OasisShooterManager.Instance?.OnTargetHit(amount, false);

            OasisNetworkManager.Instance.SendDroneHitToServer(networkDroneId, amount, hitPoint, myId);
            return;
        }

        currentHealth -= amount;

        // Knockback recoil
        transform.position += -hitNormal * 0.15f;

        // Spark VFX & Audio
        BlasterVFX.SpawnImpact(hitPoint, hitNormal, true);
        if (_audioSource != null)
        {
            _audioSource.PlayOneShot(ProceduralAudio.GetHitTargetClip(), 1f);
        }

        // Damage flash
        StartCoroutine(FlashWhite());

        // Notify manager of successful hit
        OasisShooterManager.Instance?.OnTargetHit(amount, currentHealth <= 0);

        if (currentHealth <= 0f)
        {
            Die(attackerId);
        }
    }

    private IEnumerator FlashWhite()
    {
        if (_renderers == null) yield break;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null && _renderers[i].material != null)
            {
                if (_renderers[i].material.HasProperty("_BaseColor"))
                    _renderers[i].material.SetColor("_BaseColor", Color.white);
                else if (_renderers[i].material.HasProperty("_Color"))
                    _renderers[i].material.SetColor("_Color", Color.white);
            }
        }

        yield return new WaitForSeconds(0.08f);

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null && _renderers[i].material != null && _originalColors != null && i < _originalColors.Length)
            {
                if (_renderers[i].material.HasProperty("_BaseColor"))
                    _renderers[i].material.SetColor("_BaseColor", _originalColors[i]);
                else if (_renderers[i].material.HasProperty("_Color"))
                    _renderers[i].material.SetColor("_Color", _originalColors[i]);
            }
        }
    }

    /// <summary>
    /// Triggered when the drone dies (on Host or in single-player). Broadcasts destruction across network with killer ID.
    /// </summary>
    public void Die(int killerId = 0)
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        if (networkDroneId >= 0 && OasisNetworkManager.Instance != null && OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            OasisNetworkManager.Instance.BroadcastDroneDeath(networkDroneId, transform.position, killerId);
        }

        BlasterVFX.SpawnExplosion(transform.position);

        // Play explosion sound at position
        AudioSource.PlayClipAtPoint(ProceduralAudio.GetDroneExplosionClip(), transform.position, 1.2f);

        // Notify wave manager with killer ID attribution
        OasisShooterManager.Instance?.OnTargetDestroyed(scoreValue, this, killerId);

        Destroy(gameObject);
    }

    /// <summary>
    /// Invoked on Client puppets when the Host sends a DRONE_DIE packet with killer attribution.
    /// </summary>
    public void NetworkDie(Vector3 explosionPos, int killerId = 0)
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        BlasterVFX.SpawnExplosion(explosionPos);
        AudioSource.PlayClipAtPoint(ProceduralAudio.GetDroneExplosionClip(), explosionPos, 1.2f);

        OasisShooterManager.Instance?.OnTargetDestroyed(scoreValue, this, killerId);
        Destroy(gameObject);
    }

    /// <summary>
    /// Constructs a high-tech sci-fi battle drone model:
    /// - Armored Gunmetal stealth fuselage & aerodynamic nose cone
    /// - Carbon-fiber angled armor wings with hazard trim
    /// - Twin underslung heavy laser cannons with muzzle flash tips
    /// - Pulsing multi-layered crimson sensor visor & glowing optic emitter
    /// - Dual counter-rotating anti-gravity rings
    /// - Rear ion thruster turbine with dynamic point light illumination
    /// </summary>
    public void BuildProceduralDroneModel()
    {
        Shader litShader = OasisShaderHelper.GetLitShader();
        Shader unlitShader = OasisShaderHelper.GetUnlitShader();

        // Materials:
        // 1. Sleek Gunmetal Plating
        Material armorHullMat = new Material(litShader);
        armorHullMat.color = new Color(0.18f, 0.20f, 0.25f);
        if (armorHullMat.HasProperty("_Metallic")) armorHullMat.SetFloat("_Metallic", 0.9f);
        if (armorHullMat.HasProperty("_Smoothness")) armorHullMat.SetFloat("_Smoothness", 0.8f);

        // 2. Matte Carbon Wing Struts
        Material carbonMat = new Material(litShader);
        carbonMat.color = new Color(0.08f, 0.09f, 0.11f);
        if (carbonMat.HasProperty("_Metallic")) carbonMat.SetFloat("_Metallic", 0.4f);
        if (carbonMat.HasProperty("_Smoothness")) carbonMat.SetFloat("_Smoothness", 0.5f);

        // 3. Orange / Red Hazard Trim
        Material trimMat = new Material(litShader);
        trimMat.color = new Color(0.85f, 0.35f, 0.08f);
        if (trimMat.HasProperty("_Metallic")) trimMat.SetFloat("_Metallic", 0.7f);
        if (trimMat.HasProperty("_Smoothness")) trimMat.SetFloat("_Smoothness", 0.7f);

        // 4. Glowing Plasma / Visor Material
        _eyeMaterial = new Material(unlitShader);
        Color eyeCol = isHostile ? new Color(1f, 0.2f, 0.05f) : new Color(0.1f, 0.85f, 1f);
        _eyeMaterial.color = eyeCol;
        if (_eyeMaterial.HasProperty("_EmissionColor"))
        {
            _eyeMaterial.EnableKeyword("_EMISSION");
            _eyeMaterial.SetColor("_EmissionColor", eyeCol * 2.5f);
        }

        // 5. Dark Chrome Weapon Barrels
        Material barrelMat = new Material(litShader);
        barrelMat.color = new Color(0.12f, 0.12f, 0.14f);
        if (barrelMat.HasProperty("_Metallic")) barrelMat.SetFloat("_Metallic", 0.95f);
        if (barrelMat.HasProperty("_Smoothness")) barrelMat.SetFloat("_Smoothness", 0.9f);

        // --- ROOT VISUAL CONTAINER ---
        GameObject visualRoot = new GameObject("DroneVisuals");
        visualRoot.transform.SetParent(transform, false);

        // 1. Central Core Pod (Aerodynamic oval chassis)
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "ChassisCore";
        core.transform.SetParent(visualRoot.transform, false);
        core.transform.localScale = new Vector3(0.95f, 0.75f, 1.4f);
        Destroy(core.GetComponent<Collider>());
        core.GetComponent<MeshRenderer>().material = armorHullMat;

        // 2. Chiseled Upper Armor Cowling
        GameObject upperPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        upperPlate.name = "ArmorCowling";
        upperPlate.transform.SetParent(visualRoot.transform, false);
        upperPlate.transform.localPosition = new Vector3(0f, 0.26f, -0.1f);
        upperPlate.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        upperPlate.transform.localScale = new Vector3(0.72f, 0.22f, 1.15f);
        Destroy(upperPlate.GetComponent<Collider>());
        upperPlate.GetComponent<MeshRenderer>().material = trimMat;

        // 3. Glowing Sensor Visor (Horizontally angled front eye)
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        eye.name = "DroneSensorEye";
        eye.transform.SetParent(visualRoot.transform, false);
        eye.transform.localPosition = new Vector3(0f, 0.05f, 0.64f);
        eye.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        eye.transform.localScale = new Vector3(0.42f, 0.12f, 0.22f);
        Destroy(eye.GetComponent<Collider>());
        eye.GetComponent<MeshRenderer>().material = _eyeMaterial;
        _eyeTransform = eye.transform;

        // 4. Swept-Back Aggressive Wings (Left & Right)
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = side == -1 ? "LeftWing" : "RightWing";
            wing.transform.SetParent(visualRoot.transform, false);
            wing.transform.localPosition = new Vector3(side * 0.78f, 0.02f, -0.15f);
            wing.transform.localRotation = Quaternion.Euler(6f, side * -24f, side * 8f);
            wing.transform.localScale = new Vector3(0.85f, 0.08f, 0.55f);
            Destroy(wing.GetComponent<Collider>());
            wing.GetComponent<MeshRenderer>().material = carbonMat;

            // Wingtip Stabilizer Fin
            GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = "WingFin";
            fin.transform.SetParent(wing.transform, false);
            fin.transform.localPosition = new Vector3(side * 0.42f, 0.15f, -0.1f);
            fin.transform.localRotation = Quaternion.Euler(side * 15f, 0f, side * 30f);
            fin.transform.localScale = new Vector3(0.08f, 0.5f, 0.4f);
            Destroy(fin.GetComponent<Collider>());
            fin.GetComponent<MeshRenderer>().material = trimMat;

            // Underslung Heavy Laser Cannon
            GameObject cannon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cannon.name = "HeavyCannon";
            cannon.transform.SetParent(visualRoot.transform, false);
            cannon.transform.localPosition = new Vector3(side * 0.52f, -0.22f, 0.2f);
            cannon.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cannon.transform.localScale = new Vector3(0.14f, 0.65f, 0.14f);
            Destroy(cannon.GetComponent<Collider>());
            cannon.GetComponent<MeshRenderer>().material = barrelMat;

            // Glowing Muzzle Tip
            GameObject muzzle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            muzzle.name = "MuzzleTip";
            muzzle.transform.SetParent(cannon.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            muzzle.transform.localScale = new Vector3(1.2f, 0.4f, 1.2f);
            Destroy(muzzle.GetComponent<Collider>());
            muzzle.GetComponent<MeshRenderer>().material = _eyeMaterial;

            if (side == -1) _leftCannonTip = muzzle.transform;
            else _rightCannonTip = muzzle.transform;
        }

        // 5. Dual Counter-Rotating Anti-Gravity Energy Rings
        GameObject ring1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring1.name = "DroneRingOuter";
        ring1.transform.SetParent(visualRoot.transform, false);
        ring1.transform.localPosition = new Vector3(0f, 0.02f, 0.05f);
        ring1.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring1.transform.localScale = new Vector3(1.85f, 0.035f, 1.85f);
        Destroy(ring1.GetComponent<Collider>());
        ring1.GetComponent<MeshRenderer>().material = carbonMat;
        _ringTransform = ring1.transform;

        GameObject ring2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring2.name = "DroneRingInner";
        ring2.transform.SetParent(visualRoot.transform, false);
        ring2.transform.localPosition = new Vector3(0f, 0.02f, 0.05f);
        ring2.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring2.transform.localScale = new Vector3(1.52f, 0.045f, 1.52f);
        Destroy(ring2.GetComponent<Collider>());
        ring2.GetComponent<MeshRenderer>().material = trimMat;
        _ring2Transform = ring2.transform;

        // 6. Rear Ion Thruster Turbine with dynamic glow light
        GameObject thruster = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        thruster.name = "IonThruster";
        thruster.transform.SetParent(visualRoot.transform, false);
        thruster.transform.localPosition = new Vector3(0f, 0f, -0.68f);
        thruster.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        thruster.transform.localScale = new Vector3(0.48f, 0.22f, 0.48f);
        Destroy(thruster.GetComponent<Collider>());
        thruster.GetComponent<MeshRenderer>().material = barrelMat;

        GameObject thrusterFlame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        thrusterFlame.name = "ThrusterFlame";
        thrusterFlame.transform.SetParent(thruster.transform, false);
        thrusterFlame.transform.localPosition = new Vector3(0f, -0.85f, 0f);
        thrusterFlame.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
        Destroy(thrusterFlame.GetComponent<Collider>());
        thrusterFlame.GetComponent<MeshRenderer>().material = _eyeMaterial;

        // Thruster Point Light
        GameObject lightObj = new GameObject("DroneThrusterLight");
        lightObj.transform.SetParent(visualRoot.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0f, -0.9f);
        _thrusterLight = lightObj.AddComponent<Light>();
        _thrusterLight.type = LightType.Point;
        _thrusterLight.color = eyeCol;
        _thrusterLight.range = 6.5f;
        _thrusterLight.intensity = 3f;

        // 7. Hit Capsule Collider on root
        CapsuleCollider col = gameObject.GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.95f;
        col.height = 2.2f;
        col.direction = 2; // Z-axis

        // 8. Cache renderers for damage flashing
        _renderers = GetComponentsInChildren<MeshRenderer>();
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i].material.HasProperty("_BaseColor"))
                _originalColors[i] = _renderers[i].material.GetColor("_BaseColor");
            else if (_renderers[i].material.HasProperty("_Color"))
                _originalColors[i] = _renderers[i].material.GetColor("_Color");
            else
                _originalColors[i] = Color.white;
        }
    }

    /// <summary>
    /// Constructs a Heavy Siege Drone model:
    /// - Bulky hexagonal armored chassis with reinforced plating
    /// - Quad heavy plasma blasters (2 upper, 2 lower)
    /// - Dual heavy jet turbines with intense orange/crimson engine glow
    /// - Reinforced frontal blast shield visor
    /// </summary>
    public void BuildHeavySiegeDroneModel()
    {
        Shader litShader = OasisShaderHelper.GetLitShader();
        Shader unlitShader = OasisShaderHelper.GetUnlitShader();

        Material heavyArmorMat = new Material(litShader);
        heavyArmorMat.color = new Color(0.24f, 0.22f, 0.20f); // Dark tactical bronze/charcoal
        if (heavyArmorMat.HasProperty("_Metallic")) heavyArmorMat.SetFloat("_Metallic", 0.85f);
        if (heavyArmorMat.HasProperty("_Smoothness")) heavyArmorMat.SetFloat("_Smoothness", 0.65f);

        Material reinforcedPlateMat = new Material(litShader);
        reinforcedPlateMat.color = new Color(0.12f, 0.12f, 0.14f);
        if (reinforcedPlateMat.HasProperty("_Metallic")) reinforcedPlateMat.SetFloat("_Metallic", 0.95f);
        if (reinforcedPlateMat.HasProperty("_Smoothness")) reinforcedPlateMat.SetFloat("_Smoothness", 0.7f);

        Material heavyTrimMat = new Material(litShader);
        heavyTrimMat.color = new Color(0.95f, 0.45f, 0.05f); // Industrial hazard amber
        if (heavyTrimMat.HasProperty("_Metallic")) heavyTrimMat.SetFloat("_Metallic", 0.6f);
        if (heavyTrimMat.HasProperty("_Smoothness")) heavyTrimMat.SetFloat("_Smoothness", 0.8f);

        _eyeMaterial = new Material(unlitShader);
        Color eyeCol = new Color(1f, 0.35f, 0.05f);
        _eyeMaterial.color = eyeCol;
        if (_eyeMaterial.HasProperty("_EmissionColor"))
        {
            _eyeMaterial.EnableKeyword("_EMISSION");
            _eyeMaterial.SetColor("_EmissionColor", eyeCol * 2.8f);
        }

        GameObject visualRoot = new GameObject("HeavySiegeVisuals");
        visualRoot.transform.SetParent(transform, false);

        // 1. Bulky Central Chassis
        GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chassis.name = "HeavyChassis";
        chassis.transform.SetParent(visualRoot.transform, false);
        chassis.transform.localScale = new Vector3(1.6f, 1.1f, 1.8f);
        Destroy(chassis.GetComponent<Collider>());
        chassis.GetComponent<MeshRenderer>().material = heavyArmorMat;

        // Upper Angled Hull Plate
        GameObject topHull = GameObject.CreatePrimitive(PrimitiveType.Cube);
        topHull.name = "TopHullPlate";
        topHull.transform.SetParent(visualRoot.transform, false);
        topHull.transform.localPosition = new Vector3(0f, 0.5f, -0.1f);
        topHull.transform.localScale = new Vector3(1.3f, 0.35f, 1.5f);
        Destroy(topHull.GetComponent<Collider>());
        topHull.GetComponent<MeshRenderer>().material = reinforcedPlateMat;

        // Frontal Armor Ram / Brow
        GameObject frontShield = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontShield.name = "FrontBlastShield";
        frontShield.transform.SetParent(visualRoot.transform, false);
        frontShield.transform.localPosition = new Vector3(0f, 0.1f, 0.88f);
        frontShield.transform.localScale = new Vector3(1.4f, 0.75f, 0.25f);
        Destroy(frontShield.GetComponent<Collider>());
        frontShield.GetComponent<MeshRenderer>().material = heavyTrimMat;

        // Glowing Heavy Visor Slit
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "HeavyVisor";
        visor.transform.SetParent(visualRoot.transform, false);
        visor.transform.localPosition = new Vector3(0f, 0.15f, 1.0f);
        visor.transform.localScale = new Vector3(0.95f, 0.16f, 0.12f);
        Destroy(visor.GetComponent<Collider>());
        visor.GetComponent<MeshRenderer>().material = _eyeMaterial;
        _eyeTransform = visor.transform;

        // 2. Quad Heavy Siege Cannons (2 Left, 2 Right)
        for (int side = -1; side <= 1; side += 2)
        {
            // Heavy Sponson Arm
            GameObject sponson = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sponson.name = (side == -1 ? "Left" : "Right") + "WeaponPod";
            sponson.transform.SetParent(visualRoot.transform, false);
            sponson.transform.localPosition = new Vector3(side * 1.05f, 0f, 0.1f);
            sponson.transform.localScale = new Vector3(0.45f, 0.8f, 1.1f);
            Destroy(sponson.GetComponent<Collider>());
            sponson.GetComponent<MeshRenderer>().material = reinforcedPlateMat;

            // Upper Cannon
            GameObject cUpper = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cUpper.name = "UpperBarrel";
            cUpper.transform.SetParent(sponson.transform, false);
            cUpper.transform.localPosition = new Vector3(0f, 0.32f, 0.55f);
            cUpper.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cUpper.transform.localScale = new Vector3(0.42f, 0.85f, 0.42f);
            Destroy(cUpper.GetComponent<Collider>());
            cUpper.GetComponent<MeshRenderer>().material = heavyArmorMat;

            // Lower Cannon
            GameObject cLower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cLower.name = "LowerBarrel";
            cLower.transform.SetParent(sponson.transform, false);
            cLower.transform.localPosition = new Vector3(0f, -0.32f, 0.55f);
            cLower.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cLower.transform.localScale = new Vector3(0.42f, 0.85f, 0.42f);
            Destroy(cLower.GetComponent<Collider>());
            cLower.GetComponent<MeshRenderer>().material = heavyArmorMat;

            // Glowing muzzle tips
            GameObject mUpper = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mUpper.transform.SetParent(cUpper.transform, false);
            mUpper.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            mUpper.transform.localScale = new Vector3(1.1f, 0.3f, 1.1f);
            Destroy(mUpper.GetComponent<Collider>());
            mUpper.GetComponent<MeshRenderer>().material = _eyeMaterial;

            GameObject mLower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mLower.transform.SetParent(cLower.transform, false);
            mLower.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            mLower.transform.localScale = new Vector3(1.1f, 0.3f, 1.1f);
            Destroy(mLower.GetComponent<Collider>());
            mLower.GetComponent<MeshRenderer>().material = _eyeMaterial;

            if (side == -1)
            {
                _leftCannonTip = mUpper.transform;
                _leftCannonTip2 = mLower.transform;
            }
            else
            {
                _rightCannonTip = mUpper.transform;
                _rightCannonTip2 = mLower.transform;
            }

            // Dual Oversized Jet Turbines
            GameObject turbine = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            turbine.name = "SiegeTurbine";
            turbine.transform.SetParent(visualRoot.transform, false);
            turbine.transform.localPosition = new Vector3(side * 0.65f, 0.15f, -0.95f);
            turbine.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            turbine.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
            Destroy(turbine.GetComponent<Collider>());
            turbine.GetComponent<MeshRenderer>().material = reinforcedPlateMat;

            GameObject thrusterCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            thrusterCore.transform.SetParent(turbine.transform, false);
            thrusterCore.transform.localPosition = new Vector3(0f, -0.8f, 0f);
            thrusterCore.transform.localScale = new Vector3(0.8f, 0.4f, 0.8f);
            Destroy(thrusterCore.GetComponent<Collider>());
            thrusterCore.GetComponent<MeshRenderer>().material = _eyeMaterial;
        }

        // Heavy Point Light
        GameObject lightObj = new GameObject("SiegeThrusterLight");
        lightObj.transform.SetParent(visualRoot.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.15f, -1.2f);
        _thrusterLight = lightObj.AddComponent<Light>();
        _thrusterLight.type = LightType.Point;
        _thrusterLight.color = eyeCol;
        _thrusterLight.range = 8.5f;
        _thrusterLight.intensity = 4.0f;

        // Root collider
        CapsuleCollider col = gameObject.GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.radius = 1.2f;
        col.height = 2.4f;
        col.direction = 2; // Z-axis

        _renderers = GetComponentsInChildren<MeshRenderer>();
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i].material.HasProperty("_BaseColor"))
                _originalColors[i] = _renderers[i].material.GetColor("_BaseColor");
            else if (_renderers[i].material.HasProperty("_Color"))
                _originalColors[i] = _renderers[i].material.GetColor("_Color");
            else
                _originalColors[i] = Color.white;
        }
    }

    /// <summary>
    /// Constructs a Ground Walker Mech model:
    /// - Armored humanoid/mech cockpit torso & shoulder-mounted ammo carapace
    /// - Dual mechanical reverse-joint walking legs with animated pivot transforms
    /// - Left and Right heavy rapid rotary arm cannons
    /// - High-intensity targeting monocle sensor
    /// </summary>
    public void BuildGroundMechModel()
    {
        Shader litShader = OasisShaderHelper.GetLitShader();
        Shader unlitShader = OasisShaderHelper.GetUnlitShader();

        Material mechHullMat = new Material(litShader);
        mechHullMat.color = new Color(0.20f, 0.22f, 0.21f); // Olive drab tactical metal
        if (mechHullMat.HasProperty("_Metallic")) mechHullMat.SetFloat("_Metallic", 0.75f);
        if (mechHullMat.HasProperty("_Smoothness")) mechHullMat.SetFloat("_Smoothness", 0.55f);

        Material mechDarkMat = new Material(litShader);
        mechDarkMat.color = new Color(0.10f, 0.10f, 0.12f);
        if (mechDarkMat.HasProperty("_Metallic")) mechDarkMat.SetFloat("_Metallic", 0.9f);
        if (mechDarkMat.HasProperty("_Smoothness")) mechDarkMat.SetFloat("_Smoothness", 0.6f);

        Material mechTrimMat = new Material(litShader);
        mechTrimMat.color = new Color(0.85f, 0.25f, 0.08f); // Tactical orange/crimson
        if (mechTrimMat.HasProperty("_Metallic")) mechTrimMat.SetFloat("_Metallic", 0.5f);
        if (mechTrimMat.HasProperty("_Smoothness")) mechTrimMat.SetFloat("_Smoothness", 0.7f);

        _eyeMaterial = new Material(unlitShader);
        Color eyeCol = new Color(1f, 0.15f, 0.1f);
        _eyeMaterial.color = eyeCol;
        if (_eyeMaterial.HasProperty("_EmissionColor"))
        {
            _eyeMaterial.EnableKeyword("_EMISSION");
            _eyeMaterial.SetColor("_EmissionColor", eyeCol * 2.8f);
        }

        GameObject visualRoot = new GameObject("GroundMechVisuals");
        visualRoot.transform.SetParent(transform, false);

        // 1. Main Mech Cockpit / Torso
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        torso.name = "MechTorso";
        torso.transform.SetParent(visualRoot.transform, false);
        torso.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        torso.transform.localScale = new Vector3(1.1f, 0.9f, 1.0f);
        Destroy(torso.GetComponent<Collider>());
        torso.GetComponent<MeshRenderer>().material = mechHullMat;

        // Torso Upper Carapace Plate
        GameObject carapace = GameObject.CreatePrimitive(PrimitiveType.Cube);
        carapace.name = "UpperCarapace";
        carapace.transform.SetParent(torso.transform, false);
        carapace.transform.localPosition = new Vector3(0f, 0.5f, -0.05f);
        carapace.transform.localScale = new Vector3(0.85f, 0.3f, 1.1f);
        Destroy(carapace.GetComponent<Collider>());
        carapace.GetComponent<MeshRenderer>().material = mechTrimMat;

        // Front Head / Sensor Eye
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        eye.name = "MechVisorEye";
        eye.transform.SetParent(torso.transform, false);
        eye.transform.localPosition = new Vector3(0f, 0.15f, 0.52f);
        eye.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        eye.transform.localScale = new Vector3(0.45f, 0.12f, 0.35f);
        Destroy(eye.GetComponent<Collider>());
        eye.GetComponent<MeshRenderer>().material = _eyeMaterial;
        _eyeTransform = eye.transform;

        // 2. Heavy Dual Arm Blasters
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject shoulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shoulder.name = (side == -1 ? "Left" : "Right") + "Shoulder";
            shoulder.transform.SetParent(torso.transform, false);
            shoulder.transform.localPosition = new Vector3(side * 0.65f, 0.1f, 0.1f);
            shoulder.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
            Destroy(shoulder.GetComponent<Collider>());
            shoulder.GetComponent<MeshRenderer>().material = mechDarkMat;

            // Arm Cannon
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = "ArmCannon";
            arm.transform.SetParent(shoulder.transform, false);
            arm.transform.localPosition = new Vector3(side * 0.25f, -0.4f, 0.55f);
            arm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arm.transform.localScale = new Vector3(0.35f, 1.1f, 0.35f);
            Destroy(arm.GetComponent<Collider>());
            arm.GetComponent<MeshRenderer>().material = mechDarkMat;

            // Arm Cannon Muzzle Tip
            GameObject muzzle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            muzzle.transform.SetParent(arm.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            muzzle.transform.localScale = new Vector3(1.1f, 0.35f, 1.1f);
            Destroy(muzzle.GetComponent<Collider>());
            muzzle.GetComponent<MeshRenderer>().material = _eyeMaterial;

            if (side == -1) _leftCannonTip = muzzle.transform;
            else _rightCannonTip = muzzle.transform;

            // 3. Reverse-Joint Mechanical Walking Legs (Pivoting root)
            GameObject legPivot = new GameObject((side == -1 ? "Left" : "Right") + "LegPivot");
            legPivot.transform.SetParent(visualRoot.transform, false);
            legPivot.transform.localPosition = new Vector3(side * 0.45f, 0.5f, 0f);

            // Upper Thigh
            GameObject thigh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thigh.name = "Thigh";
            thigh.transform.SetParent(legPivot.transform, false);
            thigh.transform.localPosition = new Vector3(0f, -0.3f, -0.1f);
            thigh.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            thigh.transform.localScale = new Vector3(0.22f, 0.5f, 0.25f);
            Destroy(thigh.GetComponent<Collider>());
            thigh.GetComponent<MeshRenderer>().material = mechHullMat;

            // Lower Shin
            GameObject shin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shin.name = "Shin";
            shin.transform.SetParent(legPivot.transform, false);
            shin.transform.localPosition = new Vector3(0f, -0.65f, 0.1f);
            shin.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            shin.transform.localScale = new Vector3(0.2f, 0.55f, 0.22f);
            Destroy(shin.GetComponent<Collider>());
            shin.GetComponent<MeshRenderer>().material = mechDarkMat;

            // Heavy Foot Pad
            GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foot.name = "FootPad";
            foot.transform.SetParent(legPivot.transform, false);
            foot.transform.localPosition = new Vector3(0f, -0.92f, 0.15f);
            foot.transform.localScale = new Vector3(0.32f, 0.12f, 0.55f);
            Destroy(foot.GetComponent<Collider>());
            foot.GetComponent<MeshRenderer>().material = mechTrimMat;

            if (side == -1) _leftLegPivot = legPivot.transform;
            else _rightLegPivot = legPivot.transform;
        }

        // Mech Core Light
        GameObject lightObj = new GameObject("MechCoreLight");
        lightObj.transform.SetParent(torso.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.15f, 0.7f);
        _thrusterLight = lightObj.AddComponent<Light>();
        _thrusterLight.type = LightType.Point;
        _thrusterLight.color = eyeCol;
        _thrusterLight.range = 5f;
        _thrusterLight.intensity = 2.5f;

        // Root collider
        CapsuleCollider col = gameObject.GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.85f;
        col.height = 2.2f;
        col.center = new Vector3(0f, 0.8f, 0f);
        col.direction = 1; // Y-axis for upright walker

        _renderers = GetComponentsInChildren<MeshRenderer>();
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i].material.HasProperty("_BaseColor"))
                _originalColors[i] = _renderers[i].material.GetColor("_BaseColor");
            else if (_renderers[i].material.HasProperty("_Color"))
                _originalColors[i] = _renderers[i].material.GetColor("_Color");
            else
                _originalColors[i] = Color.white;
        }
    }
}

