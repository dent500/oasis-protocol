using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns and manages visual effects for raycast shooting:
/// Laser beam tracer, muzzle flash, impact spark bursts, and drone explosion particles.
/// </summary>
public class BlasterVFX : MonoBehaviour
{
    private static Material _laserMat;
    private static Material _sparkMat;

    public static Material GetLaserMaterial()
    {
        if (_laserMat == null)
        {
            _laserMat = OasisShaderHelper.CreateUnlitMaterial(new Color(0.1f, 0.85f, 1f, 1f)); // Neon Cyan
        }
        return _laserMat;
    }

    public static Material GetSparkMaterial()
    {
        if (_sparkMat == null)
        {
            _sparkMat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.6f, 0.1f, 1f)); // Orange plasma
        }
        return _sparkMat;
    }

    /// <summary>
    /// Draws a laser beam line tracer from barrel to impact point.
    /// </summary>
    public static void SpawnLaserBeam(Vector3 start, Vector3 end, Color color)
    {
        GameObject beamObj = new GameObject("LaserBeam");
        LineRenderer lr = beamObj.AddComponent<LineRenderer>();
        lr.material = GetLaserMaterial();
        lr.startColor = color;
        lr.endColor = color * 0.7f;
        lr.startWidth = 0.06f;
        lr.endWidth = 0.03f;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Auto destroy with fade
        BlasterVFXRunner.Instance.StartCoroutine(FadeAndDestroyBeam(beamObj, lr, 0.08f));
    }

    private static IEnumerator FadeAndDestroyBeam(GameObject obj, LineRenderer lr, float duration)
    {
        float elapsed = 0f;
        Color initialStart = lr.startColor;
        Color initialEnd = lr.endColor;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration);
            lr.startColor = new Color(initialStart.r, initialStart.g, initialStart.b, t);
            lr.endColor = new Color(initialEnd.r, initialEnd.g, initialEnd.b, t);
            lr.startWidth = 0.06f * t;
            lr.endWidth = 0.03f * t;
            yield return null;
        }

        Destroy(obj);
    }

    /// <summary>
    /// Spawns an impact particle burst at the hit location.
    /// </summary>
    public static void SpawnImpact(Vector3 position, Vector3 normal, bool isEnemy)
    {
        GameObject impact = new GameObject("BlasterImpact");
        impact.transform.position = position;
        impact.transform.rotation = Quaternion.LookRotation(normal);

        ParticleSystem ps = impact.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer renderer = impact.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetSparkMaterial();

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = 0.25f;
        main.startSpeed = isEnemy ? 9f : 6f;
        main.startSize = 0.08f;
        main.startColor = isEnemy ? new Color(1f, 0.3f, 0.1f) : new Color(0.9f, 0.85f, 0.5f);
        main.gravityModifier = 2.5f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, isEnemy ? 25 : 15) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 45f;
        shape.radius = 0.02f;

        ps.Play();
        Destroy(impact, 0.6f);
    }

    /// <summary>
    /// Spawns an explosion when a drone is destroyed.
    /// </summary>
    public static void SpawnExplosion(Vector3 position)
    {
        GameObject exp = new GameObject("DroneExplosionVFX");
        exp.transform.position = position;

        // Spark explosion
        ParticleSystem ps = exp.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer renderer = exp.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetSparkMaterial();

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 12f;
        main.startSize = 0.16f;
        main.startColor = new Color(1f, 0.5f, 0.1f);
        main.gravityModifier = 3f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 40) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        // Flash Light
        GameObject lightObj = new GameObject("ExplosionLight");
        lightObj.transform.SetParent(exp.transform, false);
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.65f, 0.2f);
        l.range = 14f;
        l.intensity = 8f;

        ps.Play();
        BlasterVFXRunner.Instance.StartCoroutine(FadeLight(l, 0.35f));
        Destroy(exp, 1.2f);
    }

    /// <summary>
    /// Spawns an electromagnetic warp-in flash beacon when a drone materializes.
    /// </summary>
    public static void SpawnWarpIn(Vector3 position)
    {
        GameObject warp = new GameObject("DroneWarpVFX");
        warp.transform.position = position;

        ParticleSystem ps = warp.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer renderer = warp.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetSparkMaterial();

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.35f;
        main.startSpeed = 8f;
        main.startSize = 0.12f;
        main.startColor = new Color(0.2f, 0.8f, 1f, 1f); // Neon Cyan warp flash

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 25) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;

        // Flash Light
        GameObject lightObj = new GameObject("WarpLight");
        lightObj.transform.SetParent(warp.transform, false);
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.2f, 0.85f, 1f);
        l.range = 10f;
        l.intensity = 6f;

        ps.Play();
        BlasterVFXRunner.Instance.StartCoroutine(FadeLight(l, 0.25f));
        Destroy(warp, 0.7f);
    }

    private static IEnumerator FadeLight(Light l, float duration)
    {
        float elapsed = 0f;
        float startIntensity = l.intensity;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (l != null)
            {
                l.intensity = Mathf.Lerp(startIntensity, 0f, elapsed / duration);
            }
            yield return null;
        }
    }
}

/// <summary>
/// Helper MonoBehaviour runner to execute Coroutines for static VFX methods.
/// </summary>
public class BlasterVFXRunner : MonoBehaviour
{
    private static BlasterVFXRunner _instance;
    public static BlasterVFXRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject runner = new GameObject("BlasterVFXRunner");
                _instance = runner.AddComponent<BlasterVFXRunner>();
            }
            return _instance;
        }
    }
}
