using System.Collections;
using UnityEngine;

/// <summary>
/// Physical ballistic plasma bomb launched by the Titanium Heavy Warrior's Flak Mortar.
/// Detonates on impact with terrain or enemies, dealing heavy direct + AoE splash damage and knockback.
/// </summary>
public class OasisPlasmaBomb : MonoBehaviour
{
    [Header("Explosive Stats")]
    public float directDamage = 85f;
    public float splashDamage = 55f;
    public float explosionRadius = 6.5f;
    public float explosionForce = 450f;
    public int shooterTeamId = 0;
    public int shooterPlayerId = 0;

    private Rigidbody _rb;
    private bool _hasExploded = false;
    private Light _glowLight;

    public static OasisPlasmaBomb SpawnBomb(Vector3 position, Vector3 velocity, int teamId, int playerId)
    {
        GameObject bombObj = new GameObject("OasisPlasmaBomb");
        bombObj.transform.position = position;

        // Core visual sphere
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(bombObj.transform, false);
        sphere.transform.localScale = Vector3.one * 0.32f;
        Destroy(sphere.GetComponent<Collider>()); // Main bomb uses its own sphere collider

        Material mat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.45f, 0.1f, 1f));
        sphere.GetComponent<Renderer>().sharedMaterial = mat;

        // Glow light
        Light lt = bombObj.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = new Color(1f, 0.55f, 0.15f);
        lt.range = 7f;
        lt.intensity = 3.5f;

        // Trail renderer
        TrailRenderer tr = bombObj.AddComponent<TrailRenderer>();
        tr.material = mat;
        tr.startColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        tr.endColor = new Color(1f, 0.2f, 0.05f, 0f);
        tr.startWidth = 0.22f;
        tr.endWidth = 0.02f;
        tr.time = 0.35f;

        // Physics
        SphereCollider col = bombObj.AddComponent<SphereCollider>();
        col.radius = 0.2f;

        Rigidbody rb = bombObj.AddComponent<Rigidbody>();
        rb.mass = 1.2f;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasExploded) return;
        Explode(collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
    }

    public void Explode(Vector3 center)
    {
        if (_hasExploded) return;
        _hasExploded = true;

        // 1. VFX
        BlasterVFX.SpawnExplosion(center);

        // 2. Audio
        AudioSource.PlayClipAtPoint(ProceduralAudio.GetBombExplosionClip(), center, 0.75f);

        // 3. Screen Shake if near player
        Camera cam = Camera.main;
        if (cam != null)
        {
            float distToPlayer = Vector3.Distance(cam.transform.position, center);
            if (distToPlayer < 25f)
            {
                float shakeIntensity = Mathf.Clamp01(1f - (distToPlayer / 25f)) * 0.35f;
                OasisShooterManager.Instance?.TriggerCameraShake(0.35f, shakeIntensity);
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
                if (dist < 1.8f) dmg = directDamage; // Direct hit bonus

                ai.TakeDamage(dmg, c.ClosestPoint(center), Vector3.up, shooterPlayerId, shooterTeamId);
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
