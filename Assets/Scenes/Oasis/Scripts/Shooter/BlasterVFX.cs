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
    /// Spawns a massive, high-visibility cinematic plasma mortar explosion:
    /// Expanding glowing fireball shockwave dome, towering billowing smoke plume,
    /// fiery shrapnel shower, expanding ground shockwave ring, and charred impact crater.
    /// </summary>
    public static void SpawnMortarBlast(Vector3 position, float blastRadius = 9.5f)
    {
        GameObject root = new GameObject("MortarBlastVFX");
        root.transform.position = position;

        // 1. Expanding Glowing Fireball Shockwave Sphere
        GameObject fireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fireball.name = "FireballSphere";
        fireball.transform.SetParent(root.transform, false);
        fireball.transform.position = position;
        Destroy(fireball.GetComponent<Collider>());
        Material fireballMat = OasisShaderHelper.CreateUnlitMaterial(new Color(1f, 0.65f, 0.15f, 1f));
        fireball.GetComponent<Renderer>().sharedMaterial = fireballMat;
        BlasterVFXRunner.Instance.StartCoroutine(AnimateFireballSphere(fireball, blastRadius * 1.6f, 0.65f));

        // 2. High-Impact Detonation Flash Light
        GameObject lightObj = new GameObject("DetonationFlash");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.position = position;
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.65f, 0.2f);
        l.range = 42f;
        l.intensity = 24f;
        BlasterVFXRunner.Instance.StartCoroutine(FadeLight(l, 0.65f));

        // 3. Multi-Layer Particle System (Fire Cloud + Smoke Plume + Shrapnel)
        // Layer A: Smoke & Fire Clouds
        ParticleSystem psSmoke = root.AddComponent<ParticleSystem>();
        psSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer smokeRend = root.GetComponent<ParticleSystemRenderer>();
        smokeRend.material = GetSparkMaterial();

        var main = psSmoke.main;
        main.duration = 2.5f;
        main.loop = false;
        main.startLifetime = 2.8f;
        main.startSpeed = 12f;
        main.startSize = 4.5f;
        main.startColor = new Color(1f, 0.50f, 0.12f, 0.95f);
        main.gravityModifier = -0.5f; // Billows upward into the sky

        var emission = psSmoke.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 55) });

        var shape = psSmoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.5f;

        // Layer B: Fiery Shrapnel & Spark Streaks (Child GameObject)
        GameObject sparksObj = new GameObject("ShrapnelSparks");
        sparksObj.transform.SetParent(root.transform, false);
        sparksObj.transform.position = position;
        ParticleSystem psSparks = sparksObj.AddComponent<ParticleSystem>();
        psSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer sparksRend = sparksObj.GetComponent<ParticleSystemRenderer>();
        sparksRend.material = GetSparkMaterial();

        var sMain = psSparks.main;
        sMain.duration = 1.8f;
        sMain.loop = false;
        sMain.startLifetime = 1.4f;
        sMain.startSpeed = 32f;
        sMain.startSize = 0.35f;
        sMain.startColor = new Color(1f, 0.85f, 0.3f, 1f);
        sMain.gravityModifier = 3.2f;

        var sEmission = psSparks.emission;
        sEmission.rateOverTime = 0;
        sEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 75) });

        var sShape = psSparks.shape;
        sShape.shapeType = ParticleSystemShapeType.Sphere;
        sShape.radius = 0.8f;

        psSmoke.Play();
        psSparks.Play();

        // 4. Ground Shockwave Ring & Scorched Impact Crater
        if (Physics.Raycast(position + Vector3.up * 1f, Vector3.down, out RaycastHit gHit, 8f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 groundPos = gHit.point + Vector3.up * 0.05f;

            // Expanding Shockwave Ring
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "GroundShockwaveRing";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.position = groundPos;
            ring.transform.localScale = new Vector3(1f, 0.02f, 1f);
            ring.GetComponent<Renderer>().sharedMaterial = GetSparkMaterial();
            BlasterVFXRunner.Instance.StartCoroutine(AnimateGroundRing(ring, blastRadius * 2.2f, 0.45f));

            // Charred Ground Crater Decal
            GameObject crater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crater.name = "ScorchedCrater";
            Destroy(crater.GetComponent<Collider>());
            crater.transform.position = groundPos;
            crater.transform.localScale = new Vector3(blastRadius * 0.85f, 0.01f, blastRadius * 0.85f);
            Material craterMat = OasisShaderHelper.CreateUnlitMaterial(new Color(0.09f, 0.08f, 0.07f, 0.85f));
            crater.GetComponent<Renderer>().sharedMaterial = craterMat;
            BlasterVFXRunner.Instance.StartCoroutine(FadeAndDestroyObject(crater, craterMat, 7.0f));
        }

        Destroy(root, 4.0f);
    }

    private static IEnumerator AnimateFireballSphere(GameObject fireball, float targetDiameter, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 0.5f;
        Vector3 maxScale = Vector3.one * targetDiameter;
        Renderer rend = fireball != null ? fireball.GetComponent<Renderer>() : null;
        Material mat = rend != null ? rend.material : null;
        Color initialColor = mat != null ? mat.color : new Color(1f, 0.65f, 0.15f, 1f);

        while (elapsed < duration && fireball != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            if (t < 0.35f)
            {
                // Rapid explosive expansion
                float expandT = t / 0.35f;
                fireball.transform.localScale = Vector3.Lerp(initialScale, maxScale, Mathf.Sin(expandT * Mathf.PI * 0.5f));
            }
            else
            {
                // Dissipate & fade
                float fadeT = (t - 0.35f) / 0.65f;
                fireball.transform.localScale = Vector3.Lerp(maxScale, maxScale * 1.25f, fadeT);
                if (mat != null)
                {
                    mat.color = Color.Lerp(initialColor, new Color(initialColor.r, initialColor.g * 0.5f, 0f, 0f), fadeT);
                }
            }
            yield return null;
        }

        if (fireball != null) Destroy(fireball);
    }

    private static IEnumerator AnimateGroundRing(GameObject ring, float targetDiameter, float duration)
    {
        float elapsed = 0f;
        Vector3 startScale = new Vector3(1f, 0.02f, 1f);
        Vector3 endScale = new Vector3(targetDiameter, 0.02f, targetDiameter);
        Renderer rend = ring != null ? ring.GetComponent<Renderer>() : null;
        Material mat = rend != null ? rend.material : null;
        Color startColor = mat != null ? mat.color : Color.white;

        while (elapsed < duration && ring != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            ring.transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sqrt(t));
            if (mat != null)
            {
                mat.color = Color.Lerp(startColor, new Color(startColor.r, startColor.g, startColor.b, 0f), t);
            }
            yield return null;
        }

        if (ring != null) Destroy(ring);
    }

    private static IEnumerator FadeAndDestroyObject(GameObject obj, Material mat, float duration)
    {
        float elapsed = 0f;
        Color initialColor = mat != null ? mat.color : Color.white;
        float fadeStart = duration * 0.65f;

        while (elapsed < duration && obj != null)
        {
            elapsed += Time.deltaTime;
            if (elapsed > fadeStart && mat != null)
            {
                float t = (elapsed - fadeStart) / (duration - fadeStart);
                mat.color = Color.Lerp(initialColor, new Color(initialColor.r, initialColor.g, initialColor.b, 0f), t);
            }
            yield return null;
        }

        if (obj != null) Destroy(obj);
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
