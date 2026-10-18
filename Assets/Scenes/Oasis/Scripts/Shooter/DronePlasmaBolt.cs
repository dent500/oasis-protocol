using UnityEngine;

/// <summary>
/// Energy bolt projectile fired by combat drones towards the player.
/// </summary>
public class DronePlasmaBolt : MonoBehaviour
{
    public float speed = 18f;
    public float damage = 15f;
    public float lifetime = 5f;

    private Vector3 _direction;

    public void Launch(Vector3 direction)
    {
        _direction = direction.normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // Safe Spawn Shelter deflection: bolts cannot penetrate into the shelter
        if (OasisShooterManager.IsPositionInSafeShelter(transform.position))
        {
            BlasterVFX.SpawnImpact(transform.position, -_direction, false);
            Destroy(gameObject);
            return;
        }

        Vector3 step = _direction * speed * Time.deltaTime;
        float dist = step.magnitude;

        // 1. Direct proximity check to Player (handles CharacterControllers and Cinemachine camera targets reliably)
        var player = OasisShooterManager.Instance != null ? OasisShooterManager.Instance.GetPlayerTransform() : null;
        if (player != null)
        {
            Vector3 playerCenter = player.position + Vector3.up * 1.0f;
            float distToPlayer = Vector3.Distance(transform.position, playerCenter);
            if (distToPlayer <= 1.4f)
            {
                OasisShooterManager.Instance.DamagePlayer(damage);
                BlasterVFX.SpawnImpact(transform.position, -_direction, true);
                Destroy(gameObject);
                return;
            }
        }

        // 2. Continuous raycast along trajectory each frame to ensure terrain, rocks, and obstacles are never bypassed
        RaycastHit hit;
        if (Physics.Raycast(transform.position, _direction, out hit, dist, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.CompareTag("Player") || hit.collider.GetComponentInParent<StarterAssets.FirstPersonController>() != null || hit.collider.GetComponent<CharacterController>() != null)
            {
                OasisShooterManager.Instance?.DamagePlayer(damage);
                BlasterVFX.SpawnImpact(hit.point, hit.normal, true);
                Destroy(gameObject);
                return;
            }
            else if (!hit.collider.GetComponent<TargetDrone>())
            {
                // Hit rock, mountain, or canyon terrain
                BlasterVFX.SpawnImpact(hit.point, hit.normal, false);
                Destroy(gameObject);
                return;
            }
        }

        transform.position += step;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.GetComponentInParent<StarterAssets.FirstPersonController>() != null || other.GetComponent<CharacterController>() != null)
        {
            OasisShooterManager.Instance?.DamagePlayer(damage);
            BlasterVFX.SpawnImpact(transform.position, -_direction, true);
            Destroy(gameObject);
        }
        else if (!other.isTrigger && !other.GetComponent<TargetDrone>())
        {
            BlasterVFX.SpawnImpact(transform.position, -_direction, false);
            Destroy(gameObject);
        }
    }
}
