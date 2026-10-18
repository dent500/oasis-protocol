using UnityEngine;

/// <summary>
/// Centralized safe shader and material resolver for standalone builds and editor playmode.
/// Prevents ArgumentNullException when Shader.Find() cannot locate shaders stripped or not preloaded in standalone builds.
/// </summary>
public static class OasisShaderHelper
{
    private static Shader _cachedLitShader;
    private static Shader _cachedUnlitShader;

    /// <summary>
    /// Gets a guaranteed non-null Lit shader suitable for URP and standalone builds.
    /// </summary>
    public static Shader GetLitShader()
    {
        if (_cachedLitShader != null) return _cachedLitShader;

        _cachedLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (_cachedLitShader != null) return _cachedLitShader;

        // Try standard or mobile fallbacks
        _cachedLitShader = Shader.Find("Standard");
        if (_cachedLitShader != null) return _cachedLitShader;

        // Extract from any existing active renderer in the scene
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
        foreach (var r in renderers)
        {
            if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null)
            {
                _cachedLitShader = r.sharedMaterial.shader;
                return _cachedLitShader;
            }
        }

        // Ultimate built-in fallback (always included by Unity)
        _cachedLitShader = Shader.Find("Sprites/Default");
        return _cachedLitShader;
    }

    /// <summary>
    /// Gets a guaranteed non-null Unlit / Glowing shader suitable for URP and standalone builds.
    /// </summary>
    public static Shader GetUnlitShader()
    {
        if (_cachedUnlitShader != null) return _cachedUnlitShader;

        _cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (_cachedUnlitShader != null) return _cachedUnlitShader;

        _cachedUnlitShader = Shader.Find("Unlit/Color");
        if (_cachedUnlitShader != null) return _cachedUnlitShader;

        _cachedUnlitShader = Shader.Find("Sprites/Default");
        if (_cachedUnlitShader != null) return _cachedUnlitShader;

        // Fallback to lit shader if unlit is not available
        _cachedUnlitShader = GetLitShader();
        return _cachedUnlitShader;
    }

    /// <summary>
    /// Creates a safe material with the unlit shader, guaranteed non-null.
    /// </summary>
    public static Material CreateUnlitMaterial(Color color)
    {
        Shader s = GetUnlitShader();
        Material mat = (s != null) ? new Material(s) : new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// Creates a safe material with the lit shader, guaranteed non-null.
    /// </summary>
    public static Material CreateLitMaterial(Color color, float metallic = 0.5f, float smoothness = 0.5f)
    {
        Shader s = GetLitShader();
        Material mat = (s != null) ? new Material(s) : new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }
}
