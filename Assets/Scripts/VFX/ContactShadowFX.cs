using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dark ellipse a projectile drags across the deck beneath it.
///
/// On a dark board a tracer reads from its own emission. On a lit one it has almost no luminance
/// contrast left to spend, and the eye tracks a fast small object by its contact with the ground
/// instead — which is also the only cue that tells a player how high a grenade currently is, and so
/// where in its one-second arc it has got to. The shadow scales and softens with height, so it
/// doubles as an altimeter.
///
/// BUDGET. Capped at four concurrent, sharing FXLightPool's number for the same reason: the
/// Shotgunner fires ten pellets 0.01 s apart, and ten shadows crossing the same two cells reads as
/// a smear rather than as ten rounds. Over the cap a projectile keeps its tracer and loses its
/// shadow, exactly as it keeps its muzzle flash and loses its light.
///
/// Skipped entirely while FXPalette.ShadowTint is a no-op, so the dark board allocates nothing.
/// Purely local, non-networked, and released when the projectile dies.
/// </summary>
public sealed class ContactShadowFX : MonoBehaviour
{
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

    /// <summary>Height above the deck. Above the scorch decal so a round crossing a burn still
    /// shows its shadow, below the shockwave ring so a ring always wins (ArtDirection §7.8).</summary>
    public const float GroundHeight = 0.03f;

    /// <summary>Height at which the shadow reaches its widest and faintest. A grenade apex.</summary>
    public const float MaxTrackedHeight = 3f;

    /// <summary>Widest the shadow gets, as a multiple of its ground-contact diameter.</summary>
    public const float MaxHeightSpread = 2.2f;

    /// <summary>Coverage at ground contact, falling toward zero at MaxTrackedHeight.</summary>
    public const float ContactCoverage = 1f;
    public const float ApexCoverage = 0.35f;

    private const float ContactEdgeSoftness = 0.35f;
    private const float ApexEdgeSoftness = 0.9f;

    private static ContactShadowFX instance;
    private static Material sharedMaterial;

    private readonly List<Transform> quads = new(FXPalette.MaxConcurrentContactShadows);
    private readonly List<Renderer> renderers = new(FXPalette.MaxConcurrentContactShadows);
    private readonly List<Transform> targets = new(FXPalette.MaxConcurrentContactShadows);
    private readonly List<float> diameters = new(FXPalette.MaxConcurrentContactShadows);
    private MaterialPropertyBlock properties;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        sharedMaterial = null;
    }

    /// <summary>
    /// Starts tracking <paramref name="target"/>. Returns false when the budget is full, which
    /// callers must treat as "no shadow", never as a reason to skip the projectile's other FX.
    /// </summary>
    public static bool TryTrack(Transform target, float diameter = FXPalette.ContactShadowDiameter)
    {
        if (target == null || !FXPalette.ShadowLayersEnabled)
            return false;
        return Ensure().Acquire(target, diameter);
    }

    /// <summary>Releases the slot held for <paramref name="target"/>, if any.</summary>
    public static void Release(Transform target)
    {
        if (instance == null || target == null)
            return;

        int slot = instance.targets.IndexOf(target);
        if (slot >= 0)
            instance.Free(slot);
    }

    private static ContactShadowFX Ensure()
    {
        if (instance != null)
            return instance;

        GameObject host = new("ContactShadowFX") { hideFlags = HideFlags.DontSave };
        instance = host.AddComponent<ContactShadowFX>();
        instance.properties = new MaterialPropertyBlock();
        return instance;
    }

    private bool Acquire(Transform target, float diameter)
    {
        if (targets.Contains(target))
            return true;

        int slot = targets.IndexOf(null);
        if (slot < 0)
        {
            if (targets.Count >= FXPalette.MaxConcurrentContactShadows || !TryCreateSlot())
                return false;
            slot = targets.Count - 1;
        }

        targets[slot] = target;
        diameters[slot] = diameter;
        quads[slot].gameObject.SetActive(true);
        return true;
    }

    private bool TryCreateSlot()
    {
        Material shared = ResolveMaterial();
        if (shared == null)
            return false;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = $"ContactShadow{quads.Count}";
        quad.hideFlags = HideFlags.DontSave;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, worldPositionStays: false);
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.SetActive(false);

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = shared;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        quads.Add(quad.transform);
        renderers.Add(quadRenderer);
        targets.Add(null);
        diameters.Add(FXPalette.ContactShadowDiameter);
        return true;
    }

    private static Material ResolveMaterial()
    {
        if (sharedMaterial != null)
            return sharedMaterial;

        Shader glowShader = Shader.Find("BattlePlan/GroundGlow");
        if (glowShader == null)
            return null;

        sharedMaterial = new Material(glowShader)
        {
            name = "ContactShadow (runtime)",
            hideFlags = HideFlags.HideAndDontSave,
            enableInstancing = true,
        };
        FXPalette.Composite.Apply(sharedMaterial, FXPalette.ShadowComposite);
        // Filled disc, not a ring.
        sharedMaterial.SetFloat(RingWidthId, 1f);
        return sharedMaterial;
    }

    private void Free(int slot)
    {
        targets[slot] = null;
        if (quads[slot] != null)
            quads[slot].gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        for (int slot = 0; slot < targets.Count; slot++)
        {
            Transform target = targets[slot];
            if (target == null)
            {
                // The projectile was destroyed without releasing — reclaim the slot so a burst
                // cannot starve the budget.
                if (quads[slot] != null && quads[slot].gameObject.activeSelf)
                    Free(slot);
                continue;
            }

            Vector3 position = target.position;
            float heightFactor = Mathf.Clamp01(
                (position.y - GroundHeight) / MaxTrackedHeight
            );

            quads[slot].position = new Vector3(position.x, GroundHeight, position.z);
            quads[slot].localScale =
                Vector3.one * (diameters[slot] * Mathf.Lerp(1f, MaxHeightSpread, heightFactor));

            renderers[slot].GetPropertyBlock(properties);
            properties.SetColor(GlowColorId, FXPalette.ShadowTint);
            properties.SetFloat(RingWidthId, 1f);
            properties.SetFloat(
                IntensityId,
                Mathf.Lerp(ContactCoverage, ApexCoverage, heightFactor)
            );
            // A shadow directly under an object has a hard edge and softens with distance, which
            // is the cue that reads as height rather than as size.
            properties.SetFloat(
                EdgeSoftnessId,
                Mathf.Lerp(ContactEdgeSoftness, ApexEdgeSoftness, heightFactor)
            );
            renderers[slot].SetPropertyBlock(properties);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
