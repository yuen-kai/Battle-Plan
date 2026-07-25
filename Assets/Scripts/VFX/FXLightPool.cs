using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The whole game's budget for FX point lights, capped at four concurrent (ArtDirection §7.6,
/// risk 10). The Shotgunner fires ten pellets at 0.01 s intervals; ten muzzle lights inside one
/// frame would blow URP's per-object additional-light limit and start popping lights off units at
/// random. Above the cap a request is simply refused and the caller keeps its quad, which is the
/// behaviour §8.5 asks for — the flash is the read, the light is the garnish.
///
/// Purely local and presentation-only. Lights are reused rather than created per shot so a burst
/// does not churn the scene graph.
/// </summary>
public sealed class FXLightPool : MonoBehaviour
{
    /// <summary>Hard ceiling on simultaneously lit FX lights. Do not raise without re-measuring.</summary>
    public const int MaxConcurrentLights = 4;

    private static FXLightPool instance;

    private readonly List<Light> lights = new(MaxConcurrentLights);
    private readonly float[] expiresAt = new float[MaxConcurrentLights];
    private readonly float[] peakIntensity = new float[MaxConcurrentLights];
    private readonly float[] startedAt = new float[MaxConcurrentLights];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    /// <summary>
    /// Lights the pop if the budget allows. Returns false when all four lights are already busy,
    /// which callers must treat as "draw the flash without the light", never as a reason to skip
    /// the whole effect.
    /// </summary>
    public static bool TryFlash(
        Vector3 position,
        Color color,
        float range,
        float intensity,
        float lifetime
    )
    {
        return Ensure().Request(position, color, range, intensity, lifetime);
    }

    private static FXLightPool Ensure()
    {
        if (instance != null)
            return instance;

        GameObject host = new("FXLightPool");
        instance = host.AddComponent<FXLightPool>();
        return instance;
    }

    private bool Request(Vector3 position, Color color, float range, float intensity, float lifetime)
    {
        int slot = AcquireSlot();
        if (slot < 0)
            return false;

        Light pooled = lights[slot];
        pooled.transform.position = position;
        pooled.color = color;
        pooled.range = range;
        pooled.intensity = intensity;
        pooled.enabled = true;

        peakIntensity[slot] = intensity;
        startedAt[slot] = Time.time;
        expiresAt[slot] = Time.time + Mathf.Max(0.01f, lifetime);
        return true;
    }

    private int AcquireSlot()
    {
        for (int slot = 0; slot < lights.Count; slot++)
        {
            if (lights[slot] != null && !lights[slot].enabled)
                return slot;
        }

        if (lights.Count >= MaxConcurrentLights)
            return -1;

        GameObject lightObject = new($"FXLight{lights.Count}");
        lightObject.transform.SetParent(transform, worldPositionStays: false);
        Light created = lightObject.AddComponent<Light>();
        created.type = LightType.Point;
        created.shadows = LightShadows.None;
        created.enabled = false;
        lights.Add(created);
        return lights.Count - 1;
    }

    private void Update()
    {
        float now = Time.time;
        for (int slot = 0; slot < lights.Count; slot++)
        {
            Light pooled = lights[slot];
            if (pooled == null || !pooled.enabled)
                continue;

            if (now >= expiresAt[slot])
            {
                pooled.enabled = false;
                continue;
            }

            // Squared falloff: a muzzle pop should be gone before the eye resolves it.
            float remaining = 1f - Mathf.InverseLerp(startedAt[slot], expiresAt[slot], now);
            pooled.intensity = peakIntensity[slot] * remaining * remaining;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
