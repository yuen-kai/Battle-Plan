using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves the FX prefabs, which live under Assets/Resources/FX and are built by
/// Battle Plan ▸ FX ▸ Build FX Prefabs. Loading by path rather than by serialized
/// reference is what keeps the whole FX layer callable from plain static helpers, with no scene
/// wiring and nothing for a designer to leave unassigned.
///
/// Anything spawned through here is local, non-networked and self-destructing. If a prefab is
/// missing the call is a no-op with one warning — a missing effect must never take a match down.
/// </summary>
public static class FXAssets
{
    public const string SparkBurst = "FX/Burst_Sparks";
    public const string DebrisBurst = "FX/Burst_Debris";
    public const string DustBurst = "FX/Burst_Dust";
    public const string MuzzleFlash = "FX/Quad_MuzzleFlash";
    public const string CoreSphere = "FX/Sphere_Core";
    public const string GroundGlow = "FX/Quad_GroundGlow";
    public const string ScorchDecal = "FX/Quad_Scorch";
    public const string SlashDecal = "FX/Quad_Slash";
    public const string SmokePuffCard = "FX/Quad_SmokePuff";

    private static readonly Dictionary<string, GameObject> cache = new();
    private static readonly HashSet<string> warned = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        warned.Clear();
    }

    /// <summary>The prefab at <paramref name="resourcePath"/>, or null once warned.</summary>
    public static GameObject Load(string resourcePath)
    {
        if (cache.TryGetValue(resourcePath, out GameObject cached))
            return cached;

        GameObject loaded = Resources.Load<GameObject>(resourcePath);
        if (loaded == null && warned.Add(resourcePath))
        {
            Debug.LogWarning(
                $"[FXAssets] '{resourcePath}' is missing. Run "
                    + "Battle Plan ▸ FX ▸ Build FX Prefabs. Effect skipped."
            );
        }

        cache[resourcePath] = loaded;
        return loaded;
    }

    /// <summary>Instantiates an FX prefab unparented, at world position and rotation.</summary>
    public static GameObject Spawn(string resourcePath, Vector3 position, Quaternion rotation)
    {
        GameObject prefab = Load(resourcePath);
        if (prefab == null)
            return null;

        GameObject spawned = Object.Instantiate(prefab, position, rotation);
        spawned.hideFlags = HideFlags.DontSave;
        return spawned;
    }

    /// <summary>Instantiates an FX prefab and destroys it after <paramref name="lifetime"/>.</summary>
    public static GameObject SpawnTimed(
        string resourcePath,
        Vector3 position,
        Quaternion rotation,
        float lifetime
    )
    {
        GameObject spawned = Spawn(resourcePath, position, rotation);
        if (spawned != null)
            Object.Destroy(spawned, lifetime);
        return spawned;
    }
}
