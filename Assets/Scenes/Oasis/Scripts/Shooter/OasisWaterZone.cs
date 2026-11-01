using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oasis Water No-Fire Sanctuary Zone & Swimming Physics.
/// Designates the desert oasis water pond as a strict ceasefire sanctuary.
/// Neither players nor AI combatants may discharge weapons while wading or swimming in the water.
/// In addition, provides buoyancy to float combatants at the water surface and gentle steering toward dry ground.
/// </summary>
public class OasisWaterZone : MonoBehaviour
{
    private static List<Bounds> _waterBounds = new List<Bounds>();
    private static List<Collider> _waterColliders = new List<Collider>();
    private static bool _initialized = false;
    private static float _lastInitTime = -20f;
    private static Transform _waterTransform = null;

    // Canonical central oasis water pond coordinates in world space:
    // (Parent root in OasisScene is at Z = -157.44, child Water local is at (6.65, -4.35, -6.53) => World: (6.65, -4.35, -163.97))
    public static Vector3 PondCenter = new Vector3(6.65f, -4.35f, -163.97f);
    public static float WaterSurfaceY = -4.35f;
    public const float PondRadius = 28f;
    public const float PondRadiusX = 35f;
    public const float PondRadiusZ = 22f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Initialize()
    {
        _waterBounds.Clear();
        _waterColliders.Clear();

        // 1. Discover MeshRenderers representing water surfaces
        var allRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
        foreach (var mr in allRenderers)
        {
            if (mr == null) continue;
            string goName = mr.gameObject.name;
            string matName = (mr.sharedMaterial != null) ? mr.sharedMaterial.name : "";

            if (goName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                matName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _waterTransform = mr.transform;
                PondCenter = mr.transform.position;
                WaterSurfaceY = mr.transform.position.y;

                Bounds b = mr.bounds;
                // Generously expand downward and outward so standing anywhere in the pond (surface to bed) is captured
                b.Expand(new Vector3(4.0f, 16.0f, 4.0f));
                b.center -= new Vector3(0f, 4.0f, 0f);
                _waterBounds.Add(b);

                var col = mr.GetComponent<Collider>();
                if (col != null)
                {
                    _waterColliders.Add(col);
                }
            }
        }

        // 2. Add canonical oasis pool volume as safety fallback
        Bounds pondBounds = new Bounds(PondCenter - Vector3.up * 3.0f, new Vector3(PondRadiusX * 2f, 14.0f, PondRadiusZ * 2f));
        _waterBounds.Add(pondBounds);

        _initialized = true;
        _lastInitTime = Time.time;
    }

    /// <summary>
    /// Returns true if the horizontal position is within the oasis pond boundary.
    /// </summary>
    public static bool IsInWaterHorizontal(Vector3 worldPos)
    {
        if (!_initialized || (Time.time - _lastInitTime > 15f))
        {
            Initialize();
        }

        // Rotated ellipse test aligned with water plane rotation (-29 degrees)
        float dx = worldPos.x - PondCenter.x;
        float dz = worldPos.z - PondCenter.z;

        // Rotate delta by +29 degrees to align with ellipse local axes
        float angleRad = 29.018f * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRad);
        float sin = Mathf.Sin(angleRad);
        float localX = dx * cos - dz * sin;
        float localZ = dx * sin + dz * cos;

        float normDist = (localX * localX) / (PondRadiusX * PondRadiusX) + (localZ * localZ) / (PondRadiusZ * PondRadiusZ);
        if (normDist <= 1.15f) return true;

        // Fallback simple radius test
        float simpleDist = Mathf.Sqrt(dx * dx + dz * dz);
        if (simpleDist <= PondRadius) return true;

        return false;
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

        // 1. Distance & depth test against canonical oasis pond (from surface down to lakebed)
        if (worldPos.y <= WaterSurfaceY + 0.35f && IsInWaterHorizontal(worldPos))
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
