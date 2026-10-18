using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oasis Water No-Fire Sanctuary Zone.
/// Designates the desert oasis water pond as a strict ceasefire sanctuary.
/// Neither players nor AI combatants may discharge weapons while wading or swimming in the water.
/// In addition, AI combatants actively steer themselves out of the water back toward dry ground.
/// </summary>
public class OasisWaterZone : MonoBehaviour
{
    private static List<Bounds> _waterBounds = new List<Bounds>();
    private static List<Collider> _waterColliders = new List<Collider>();
    private static bool _initialized = false;
    private static float _lastInitTime = -20f;

    // Canonical central oasis water pond coordinates
    public static readonly Vector3 PondCenter = new Vector3(6.65f, -4.35f, -6.53f);
    public const float PondRadius = 26f;
    public const float WaterSurfaceY = -3.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Initialize()
    {
        _waterBounds.Clear();
        _waterColliders.Clear();

        // 1. Discover all MeshRenderers in scene representing water surfaces
        var allRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
        foreach (var mr in allRenderers)
        {
            if (mr == null) continue;
            string goName = mr.gameObject.name;
            string matName = (mr.sharedMaterial != null) ? mr.sharedMaterial.name : "";

            if (goName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                matName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Bounds b = mr.bounds;
                // Add vertical clearance so standing in the water volume is caught reliably
                b.Expand(new Vector3(1.5f, 4.0f, 1.5f));
                _waterBounds.Add(b);

                var col = mr.GetComponent<Collider>();
                if (col != null)
                {
                    _waterColliders.Add(col);
                }
            }
        }

        // 2. Add canonical oasis pool volume
        Bounds pondBounds = new Bounds(PondCenter + Vector3.up * 0.5f, new Vector3(PondRadius * 2f, 6.0f, PondRadius * 2f));
        _waterBounds.Add(pondBounds);

        _initialized = true;
        _lastInitTime = Time.time;
    }

    /// <summary>
    /// Returns true if the given world position is located inside the water pool / pond.
    /// </summary>
    public static bool IsInWater(Vector3 worldPos)
    {
        if (!_initialized || (Time.time - _lastInitTime > 15f))
        {
            Initialize();
        }

        // 1. Distance & depth test against canonical oasis pond
        float distToPond = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(PondCenter.x, PondCenter.z));
        if (distToPond <= PondRadius && worldPos.y <= WaterSurfaceY)
        {
            return true;
        }

        // 2. Bounds tests against all discovered scene water renderers
        for (int i = 0; i < _waterBounds.Count; i++)
        {
            if (_waterBounds[i].Contains(worldPos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes a horizontal normalized push vector pointing from the water center outward toward dry ground.
    /// </summary>
    public static Vector3 GetPushOutDirection(Vector3 worldPos)
    {
        Vector2 delta = new Vector2(worldPos.x - PondCenter.x, worldPos.z - PondCenter.z);
        if (delta.sqrMagnitude < 0.01f)
        {
            return Vector3.forward;
        }
        Vector2 norm = delta.normalized;
        return new Vector3(norm.x, 0f, norm.y);
    }
}
