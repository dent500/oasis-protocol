using UnityEngine;

/// <summary>
/// Helper to reliably find the active local player Transform and CharacterController in the Oasis scene.
/// Avoids issues where parent or nested child objects are untagged or instantiated in different hierarchies.
/// </summary>
public static class OasisPlayerHelper
{
    public static Transform FindPlayerTransform()
    {
        // 1. Check tag
        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.transform;

        // 2. Check FirstPersonController
        var fpc = Object.FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            if (!fpc.gameObject.CompareTag("Player"))
            {
                try { fpc.gameObject.tag = "Player"; } catch { }
            }
            return fpc.transform;
        }

        // 3. Check CharacterController
        var cc = Object.FindAnyObjectByType<CharacterController>();
        if (cc != null)
        {
            if (!cc.gameObject.CompareTag("Player"))
            {
                try { cc.gameObject.tag = "Player"; } catch { }
            }
            return cc.transform;
        }

        // 4. Check known GameObject names
        var pc = GameObject.Find("PlayerCapsule");
        if (pc != null) return pc.transform;

        var fpsRoot = GameObject.Find("FPS_Controller");
        if (fpsRoot != null) return fpsRoot.transform;

        return null;
    }

    public static CharacterController FindPlayerCharacterController()
    {
        var trans = FindPlayerTransform();
        if (trans != null)
        {
            var cc = trans.GetComponent<CharacterController>();
            if (cc != null) return cc;
            cc = trans.GetComponentInChildren<CharacterController>();
            if (cc != null) return cc;
        }
        return Object.FindAnyObjectByType<CharacterController>();
    }
}
