using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns and manages visual effects for raycast shooting and combat:
/// Dual-layer plasma laser beam tracers, 3D volumetric muzzle flashes, ejecting plasma casings,
/// floating 3D combat damage popups, shield shatter bursts, melee energy arcs, and explosions.
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
    /// Draws a dual-layered plasma laser beam tracer (hot white core + colored outer plasma sheath)
    /// and triggers a 3D volumetric muzzle flash burst at the barrel.
    /// </summary>
    public static void SpawnLaserBeam(Vector3 start, Vector3 end, Color color)
    {
        Vector3 dir = (end - start);
        if (dir.sqrMagnitude > 0.001f)
        {
            SpawnMuzzleFlash(start, dir.normalized, color);
        }

        // Outer Plasma Sheath
        GameObject beamObj = new GameObject("LaserBeam_Outer");
        LineRenderer lr = beamObj.AddComponent<LineRenderer>();
        lr.material = OasisShaderHelper.CreateUnlitMaterial(color);
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0.65f);
        lr.startWidth = 0.085f;
        lr.endWidth = 0.042f;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Inner Hot-White Core
        GameObject coreObj = new GameObject("LaserBeam_Core");
        coreObj.transform.SetParent(beamObj.transform, false);
        LineRenderer coreLr = coreObj.AddComponent<LineRenderer>();
        Color hotCore = Color.Lerp(color, Color.white, 0.78f);
        coreLr.material = OasisShaderHelper.CreateUnlitMaterial(hotCore);
        coreLr.startColor = hotCore;
        coreLr.endColor = hotCore;
        coreLr.startWidth = 0.032f;
        coreLr.endWidth = 0.016f;
        coreLr.positionCount = 2;
        coreLr.SetPosition(0, start);
        coreLr.SetPosition(1, end);
        coreLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coreLr.receiveShadows = false;

        BlasterVFXRunner.Instance.StartCoroutine(FadeAndDestroyDualBeam(beamObj, lr, coreLr, 0.095f));
    }

    private static IEnumerator FadeAndDestroyDualBeam(GameObject obj, LineRenderer outerLr, LineRenderer coreLr, float duration)
    {
        float elapsed = 0f;
        Color initialOuter = outerLr != null ? outerLr.startColor : Color.cyan;
        Color initialCore = coreLr != null ? coreLr.startColor : Color.white;

        while (elapsed < duration && obj != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(1f - (elapsed / duration));
            if (outerLr != null)
            {
                outerLr.startColor = new Color(initialOuter.r, initialOuter.g, initialOuter.b, t);
                outerLr.endColor = new Color(initialOuter.r, initialOuter.g, initialOuter.b, t * 0.7f);
                outerLr.startWidth = 0.085f * t;
                outerLr.endWidth = 0.042f * t;
            }
            if (coreLr != null)
            {
                coreLr.startColor = new Color(initialCore.r, initialCore.g, initialCore.b, t);
                coreLr.endColor = new Color(initialCore.r, initialCore.g, initialCore.b, t);
                coreLr.startWidth = 0.032f * t;
                coreLr.endWidth = 0.016f * t;
            }
            yield return null;
        }

        if (obj != null) Destroy(obj);
    }

    /// <summary>
    /// Spawns a 3D volumetric star-flare muzzle flash and point light burst at the gun barrel.
    /// Visible in both 1st-person and 3rd-person perspectives.
    /// </summary>
    public static void SpawnMuzzleFlash(Vector3 muzzlePos, Vector3 forwardDir, Color color)
    {
        GameObject flashRoot = new GameObject("MuzzleFlashVFX");
        flashRoot.transform.position = muzzlePos;
        if (forwardDir.sqrMagnitude > 0.001f)
        {
            flashRoot.transform.rotation = Quaternion.LookRotation(forwardDir) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }

        Color coreCol = Color.Lerp(color, Color.white, 0.55f);
        Material flashMat = OasisShaderHelper.CreateUnlitMaterial(coreCol);

        // Forward muzzle flame spike
        GameObject spike = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(spike.GetComponent<Collider>());
        spike.transform.SetParent(flashRoot.transform, false);
        spike.transform.localPosition = new Vector3(0f, 0f, 0.14f);
        spike.transform.localScale = new Vector3(0.09f, 0.09f, 0.34f);
        var sRend = spike.GetComponent<Renderer>();
        sRend.sharedMaterial = flashMat;
        sRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Cross star-flare fins
        GameObject finA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(finA.GetComponent<Collider>());
        finA.transform.SetParent(flashRoot.transform, false);
        finA.transform.localPosition = Vector3.zero;
        finA.transform.localScale = new Vector3(0.28f, 0.035f, 0.035f);
        var faRend = finA.GetComponent<Renderer>();
        faRend.sharedMaterial = flashMat;
        faRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GameObject finB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(finB.GetComponent<Collider>());
        finB.transform.SetParent(flashRoot.transform, false);
        finB.transform.localPosition = Vector3.zero;
        finB.transform.localScale = new Vector3(0.035f, 0.28f, 0.035f);
        var fbRend = finB.GetComponent<Renderer>();
        fbRend.sharedMaterial = flashMat;
        fbRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Point light flash
        Light l = flashRoot.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 4.5f;
        l.intensity = 3.8f;

        Destroy(flashRoot, 0.055f);
    }

    /// <summary>
    /// Ejects a spinning hot plasma shell casing from the weapon receiver.
    /// </summary>
    public static void SpawnShellCasing(Vector3 ejectPos, Vector3 rightDir, Color glowColor)
    {
        GameObject casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        casing.name = "SpentPlasmaCasing";
        Destroy(casing.GetComponent<Collider>());
        casing.transform.position = ejectPos;
        casing.transform.localScale = new Vector3(0.016f, 0.028f, 0.016f);

        Color brassGlow = Color.Lerp(new Color(1f, 0.78f, 0.25f), glowColor, 0.35f);
        Material casingMat = OasisShaderHelper.CreateUnlitMaterial(brassGlow);
        var rend = casing.GetComponent<Renderer>();
        rend.sharedMaterial = casingMat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 vel = (rightDir.normalized * Random.Range(1.8f, 2.8f)) + (Vector3.up * Random.Range(1.4f, 2.2f)) + (Random.insideUnitSphere * 0.35f);
        Vector3 angVel = new Vector3(Random.Range(360f, 720f), Random.Range(360f, 720f), Random.Range(360f, 720f));
        BlasterVFXRunner.Instance.StartCoroutine(AnimateShellCasing(casing, vel, angVel, 0.65f));
    }

    private static IEnumerator AnimateShellCasing(GameObject casing, Vector3 velocity, Vector3 angularVel, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && casing != null)
        {
            elapsed += Time.deltaTime;
            velocity.y -= 12.5f * Time.deltaTime;
            casing.transform.position += velocity * Time.deltaTime;
            casing.transform.Rotate(angularVel * Time.deltaTime, Space.Self);
            yield return null;
        }
        if (casing != null) Destroy(casing);
    }

    /// <summary>
    /// Spawns a floating 3D combat damage number that rises and fades at the hit location.
    /// </summary>
    public static void SpawnDamagePopup(Vector3 worldPos, float damage, bool isCrit, bool isShield)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;

        GameObject popupObj = new GameObject("CombatDamagePopup");
        popupObj.transform.position = worldPos + Vector3.up * 0.35f + Random.insideUnitSphere * 0.18f;

        Canvas canvas = popupObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 220;
        RectTransform rt = popupObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 50f);
        popupObj.transform.localScale = Vector3.one * 0.012f;

        GameObject txtObj = new GameObject("DmgText");
        txtObj.transform.SetParent(popupObj.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        Text txt = txtObj.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.fontStyle = FontStyle.Bold;
        txt.fontSize = isCrit ? 24 : 18;
        txt.text = isCrit ? $"{Mathf.RoundToInt(damage)} CRIT!" : $"{Mathf.RoundToInt(damage)}";

        Color baseCol = isCrit
            ? new Color(1.0f, 0.85f, 0.15f, 1f) // Gold Critical Headshot
            : (isShield ? new Color(0.25f, 0.92f, 1.0f, 1f) : new Color(1.0f, 0.38f, 0.28f, 1f));
        txt.color = baseCol;

        Outline outline = txtObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(1.4f, -1.4f);

        BlasterVFXRunner.Instance.StartCoroutine(AnimateDamagePopup(popupObj, txt, baseCol, isCrit ? 0.85f : 0.65f));
    }

    private static IEnumerator AnimateDamagePopup(GameObject popupObj, Text txt, Color baseCol, float duration)
    {
        float elapsed = 0f;
        Vector3 startPos = popupObj.transform.position;
        Vector3 drift = new Vector3(Random.Range(-0.35f, 0.35f), 1.35f, Random.Range(-0.35f, 0.35f));

        while (elapsed < duration && popupObj != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            popupObj.transform.position = startPos + drift * Mathf.Sin(t * Mathf.PI * 0.5f);

            Camera cam = Camera.main;
            if (cam != null)
            {
                popupObj.transform.rotation = cam.transform.rotation;
                float dist = Vector3.Distance(cam.transform.position, popupObj.transform.position);
                float scale = Mathf.Clamp(dist * 0.00085f, 0.009f, 0.025f);
                float popBump = t < 0.2f ? Mathf.Lerp(1.35f, 1.0f, t / 0.2f) : 1.0f;
                popupObj.transform.localScale = Vector3.one * (scale * popBump);
            }

            if (txt != null && t > 0.5f)
            {
                float alpha = 1f - ((t - 0.5f) / 0.5f);
                txt.color = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);
            }
            yield return null;
        }

        if (popupObj != null) Destroy(popupObj);
    }

    /// <summary>
    /// Spawns an electric cyan shield-shatter shockwave sphere when an operative's energy shield breaks.
    /// </summary>
    public static void SpawnShieldBreak(Vector3 position)
    {
        GameObject root = new GameObject("ShieldBreakVFX");
        root.transform.position = position;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(root.transform, false);
        sphere.transform.localPosition = Vector3.zero;
        Material shieldMat = OasisShaderHelper.CreateUnlitMaterial(new Color(0.18f, 0.92f, 1.0f, 0.85f));
        sphere.GetComponent<Renderer>().sharedMaterial = shieldMat;
        BlasterVFXRunner.Instance.StartCoroutine(AnimateFireballSphere(sphere, 2.4f, 0.28f));

        SpawnImpact(position, Vector3.up, false);
        Destroy(root, 0.5f);
    }

    /// <summary>
    /// Spawns a high-energy plasma blade slash arc for close-quarters melee combat.
    /// </summary>
    public static void SpawnMeleeArc(Vector3 origin, Vector3 forward, Color color)
    {
        GameObject arcObj = new GameObject("PlasmaMeleeArc");
        arcObj.transform.position = origin;
        if (forward.sqrMagnitude > 0.001f)
        {
            arcObj.transform.rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, -22f);
        }

        LineRenderer lr = arcObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.material = OasisShaderHelper.CreateUnlitMaterial(Color.Lerp(color, Color.white, 0.4f));
        lr.startColor = new Color(color.r, color.g, color.b, 0.2f);
        lr.endColor = Color.white;
        lr.startWidth = 0.04f;
        lr.endWidth = 0.14f;
        int segments = 12;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float frac = (float)i / (segments - 1);
            float angle = Mathf.Lerp(-55f, 55f, frac) * Mathf.Deg2Rad;
            float r = 1.45f;
            lr.SetPosition(i, new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r));
        }

        BlasterVFXRunner.Instance.StartCoroutine(AnimateMeleeArc(arcObj, lr, 0.16f));
    }

    private static IEnumerator AnimateMeleeArc(GameObject obj, LineRenderer lr, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && obj != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            obj.transform.localScale = Vector3.Lerp(Vector3.one * 0.7f, Vector3.one * 1.35f, t);
            if (lr != null)
            {
                lr.startWidth = 0.04f * (1f - t);
                lr.endWidth = 0.16f * (1f - t);
            }
            yield return null;
        }
        if (obj != null) Destroy(obj);
    }

    /// <summary>
    /// Spawns an impact particle burst at the hit location.
    /// </summary>
    public static void SpawnImpact(Vector3 position, Vector3 normal, bool isEnemy)
    {
        GameObject impact = new GameObject("BlasterImpact");
        impact.transform.position = position;
        impact.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 0.001f ? normal : Vector3.up);

        ParticleSystem ps = impact.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer renderer = impact.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetSparkMaterial();

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = 0.25f;
        main.startSpeed = isEnemy ? 9.5f : 6.5f;
        main.startSize = isEnemy ? 0.095f : 0.08f;
        main.startColor = isEnemy ? new Color(1f, 0.32f, 0.12f) : new Color(0.95f, 0.88f, 0.5f);
        main.gravityModifier = 2.5f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, isEnemy ? 28 : 16) });

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
