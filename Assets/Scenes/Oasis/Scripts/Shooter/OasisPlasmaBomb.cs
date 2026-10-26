using System.Collections;
using UnityEngine;

/// <summary>
/// Physical ballistic plasma bomb launched by the Titanium Heavy Warrior's Flak Mortar.
/// Detonates on impact with terrain or enemies, dealing heavy direct + AoE splash damage and knockback.
/// </summary>
public class OasisPlasmaBomb : MonoBehaviour
{
    [Header("Explosive Stats")]
    public float directDamage = 125f;
    public float splashDamage = 90f;
    public float explosionRadius = 9.5f;
    public float explosionForce = 650f;
    public int shooterTeamId = 0;
    public int shooterPlayerId = 0;

    private Rigidbody _rb;
    private bool _hasExploded = false;
    private Light _glowLight;
    private float _flightTime = 0f;
    private const float MortarGravity = 7.35f;

    private void FixedUpdate()
    {
        if (_hasExploded || _rb == null) return;

        // Apply smooth ballistic mortar downward gravity
        _rb.AddForce(Vector3.down * MortarGravity, ForceMode.Acceleration);

        // Turn projectile to follow flight velocity
        if (_rb.linearVelocity.sqrMagnitude > 1f)
        {
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
        }
    }

    private void Update()
    {
        if (_hasExploded) return;

        _flightTime += Time.deltaTime;

        // Arm safety: Do not airburst within first 0.12s of leaving the muzzle
        if (_flightTime < 0.12f) return;

        // Proximity Airburst Sensor: Detonates automatically when passing within 5.0m of any hostile enemy or drone
        Collider[] nearby = Physics.OverlapSphere(transform.position, 5.0f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < nearby.Length; i++)
        {
            var ai = nearby[i].GetComponentInParent<OasisCombatAI>();
            if (ai != null && !ai.isDead && ai.teamId != shooterTeamId)
            {
                Explode(transform.position);
                return;
            }

            var drone = nearby[i].GetComponentInParent<TargetDrone>();
            if (drone != null)
            {
                Explode(transform.position);
                return;
            }
        }
    }

    public static OasisPlasmaBomb SpawnBomb(Vector3 position, Vector3 velocity, int teamId, int playerId)
    {
        GameObject bombObj = new GameObject("OasisPlasmaBomb");
        bombObj.transform.position = position;

        // 1. Prominent Core Visual Sphere (0.65m for clear flight visibility)
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(bombObj.transform, false);
        sphere.transform.localScale = Vector3.one * 0.65f;
        Destroy(sphere.GetComponent<Collider>());

        Material mat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.60f, 0.15f, 1f));
        sphere.GetComponent<Renderer>().sharedMaterial = mat;

        // 2. Outer Plasma Glow Ring
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.transform.SetParent(bombObj.transform, false);
        ring.transform.localScale = new Vector3(0.75f, 0.08f, 0.75f);
        ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
        Destroy(ring.GetComponent<Collider>());
        ring.GetComponent<Renderer>().sharedMaterial = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.85f, 0.25f, 1f));

        // 3. Dynamic Glow Light
        Light lt = bombObj.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = new Color(1f, 0.65f, 0.2f);
        lt.range = 14f;
        lt.intensity = 6f;

        // 4. Thick Burning Rocket Trail
        TrailRenderer tr = bombObj.AddComponent<TrailRenderer>();
        tr.material = mat;
        tr.startColor = new Color(1f, 0.75f, 0.2f, 1f);
        tr.endColor = new Color(1f, 0.25f, 0.05f, 0f);
        tr.startWidth = 0.50f;
        tr.endWidth = 0.05f;
        tr.time = 0.85f;

        // 5. In-flight Smoke Particle Contrail
        GameObject smokeTrail = new GameObject("SmokeContrail");
        smokeTrail.transform.SetParent(bombObj.transform, false);
        ParticleSystem ps = smokeTrail.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer psr = smokeTrail.GetComponent<ParticleSystemRenderer>();
        psr.material = mat;

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 0.65f;
        main.startSpeed = 1.5f;
        main.startSize = 0.85f;
        main.startColor = new Color(1f, 0.55f, 0.15f, 0.85f);
        main.gravityModifier = -0.1f;

        var emission = ps.emission;
        emission.rateOverTime = 40;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        ps.Play();

        // 6. Physics
        SphereCollider col = bombObj.AddComponent<SphereCollider>();
        col.radius = 0.40f;

        Rigidbody rb = bombObj.AddComponent<Rigidbody>();
        rb.mass = 1.0f;
        rb.useGravity = false; // Custom gravity driven in FixedUpdate
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;

        OasisPlasmaBomb bomb = bombObj.AddComponent<OasisPlasmaBomb>();
        bomb._rb = rb;
        bomb._glowLight = lt;
        bomb.shooterTeamId = teamId;
        bomb.shooterPlayerId = playerId;

        // Auto-fuse safety detonation in 6 seconds
        Destroy(bombObj, 6f);
        return bomb;
    }

    public static OasisPlasmaBomb SpawnBomb(Vector3 position, Vector3 velocity, float dmg, float radius, int teamId, int playerId, float fuseSeconds = 6f)
    {
        var bomb = SpawnBomb(position, velocity, teamId, playerId);
        bomb.directDamage = dmg;
        bomb.splashDamage = dmg * 0.72f;
        bomb.explosionRadius = radius;
        if (fuseSeconds > 0f)
        {
            bomb.StartCoroutine(bomb.TimedFuseRoutine(fuseSeconds));
        }
        return bomb;
    }

    private IEnumerator TimedFuseRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!_hasExploded)
        {
            Explode(transform.position);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasExploded) return;
        Explode(collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
    }

    public void Explode(Vector3 center)
    {
        if (_hasExploded) return;
        _hasExploded = true;

        // 1. Primary Massive Cinematic Mortar Blast & Secondary Clusters
        BlasterVFX.SpawnMortarBlast(center, explosionRadius);
        for (int k = 0; k < 2; k++)
        {
            Vector3 clusterPos = center + Random.insideUnitSphere * 3.5f;
            clusterPos.y = Mathf.Max(center.y, clusterPos.y);
            BlasterVFX.SpawnExplosion(clusterPos);
        }

        // 2. Deep Sub-Bass Explosive Audio
        AudioSource.PlayClipAtPoint(ProceduralAudio.GetBombExplosionClip(), center, 0.95f);

        // 3. High-Impact Screen Shake
        Camera cam = Camera.main;
        if (cam != null)
        {
            float distToPlayer = Vector3.Distance(cam.transform.position, center);
            if (distToPlayer < 45f)
            {
                float shakeIntensity = Mathf.Clamp01(1f - (distToPlayer / 45f)) * 0.45f;
                OasisShooterManager.Instance?.TriggerCameraShake(0.40f, shakeIntensity);
            }
        }

        // 4. AoE Radius Damage & Knockback
        Collider[] hits = Physics.OverlapSphere(center, explosionRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        int localTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;

            // Damage AI enemies
            OasisCombatAI ai = c.GetComponentInParent<OasisCombatAI>();
            if (ai != null && !ai.isDead && ai.teamId != shooterTeamId)
            {
                float dist = Vector3.Distance(center, ai.transform.position);
                float falloff = Mathf.Clamp01(1f - (dist / explosionRadius));
                float dmg = splashDamage * falloff;
                if (dist < 2.5f) dmg = directDamage; // Direct hit bonus

                ai.TakeDamage(dmg, c.ClosestPoint(center), Vector3.up, shooterPlayerId, shooterTeamId);
                int localId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
                if (shooterPlayerId == localId)
                {
                    OasisCombatMovement.Instance?.TriggerHitmarker();
                    OasisShooterManager.Instance?.OnTargetHit(dmg, ai.isDowned);
                }
            }

            // Damage drones
            TargetDrone drone = c.GetComponentInParent<TargetDrone>();
            if (drone != null)
            {
                drone.TakeDamage(splashDamage, c.ClosestPoint(center), Vector3.up);
            }

            // Physics push
            Rigidbody targetRb = c.attachedRigidbody;
            if (targetRb != null && !targetRb.isKinematic)
            {
                targetRb.AddExplosionForce(explosionForce, center, explosionRadius, 1.2f, ForceMode.Impulse);
            }
        }

        Destroy(gameObject);
    }
}
