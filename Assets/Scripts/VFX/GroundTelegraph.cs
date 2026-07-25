using System.Collections;
using UnityEngine;

/// <summary>
/// A ground ring or disc drawn with BattlePlan/GroundGlow — the anticipation beat of every
/// ability (ArtDirection §9.1) and the shape a player reads the danger zone off.
///
/// The footprint rule (§9.2) is enforced here rather than trusted to call sites: a telegraph is
/// created with the ability's exact rules footprint in world units and can never be animated past
/// 1.35× it. An effect wider than its rules footprint "reads as generosity and plays as a bug
/// report", so the clamp is deliberate and silent-by-design at the boundary.
///
/// All instances share one material and differ only by MaterialPropertyBlock, so Area Lock's
/// per-cell discs — up to fourteen of them — still GPU-instance into a single draw call.
/// </summary>
public sealed class GroundTelegraph : MonoBehaviour
{
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

    private Renderer quadRenderer;
    private MaterialPropertyBlock properties;
    private float footprintDiameter;
    private Color glowColor = Color.white;
    private float intensity = 1.5f;

    /// <summary>The rules footprint this telegraph was created against, in world units.</summary>
    public float FootprintDiameter => footprintDiameter;

    /// <summary>
    /// Creates a telegraph centred on <paramref name="worldPosition"/>.
    /// <paramref name="footprintDiameter"/> is the ability's actual rules footprint in world
    /// units and becomes the ceiling every later size change is measured against.
    /// </summary>
    public static GroundTelegraph Create(
        Vector3 worldPosition,
        float footprintDiameter,
        float ringWidth,
        Color glow,
        float intensity = 1.5f,
        float pulseSpeed = 0f,
        float edgeSoftness = 0.5f,
        float height = FXPalette.YAbilityTelegraph
    )
    {
        GameObject spawned = FXAssets.Spawn(
            FXAssets.GroundGlow,
            new Vector3(worldPosition.x, height, worldPosition.z),
            Quaternion.Euler(90f, 0f, 0f)
        );
        if (spawned == null)
            return null;

        GroundTelegraph telegraph = spawned.GetComponent<GroundTelegraph>();
        if (telegraph == null)
            telegraph = spawned.AddComponent<GroundTelegraph>();

        telegraph.footprintDiameter = Mathf.Max(0.01f, footprintDiameter);
        telegraph.glowColor = glow;
        telegraph.intensity = intensity;
        telegraph.Initialise(ringWidth, pulseSpeed, edgeSoftness);
        telegraph.SetDiameter(footprintDiameter);
        return telegraph;
    }

    private void Initialise(float ringWidth, float pulseSpeed, float edgeSoftness)
    {
        quadRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
        quadRenderer.GetPropertyBlock(properties);
        properties.SetColor(GlowColorId, glowColor);
        properties.SetFloat(RingWidthId, Mathf.Clamp(ringWidth, 0.02f, 1f));
        properties.SetFloat(IntensityId, intensity);
        properties.SetFloat(PulseSpeedId, pulseSpeed);
        properties.SetFloat(PulseAmountId, pulseSpeed > 0f ? 0.35f : 0f);
        properties.SetFloat(EdgeSoftnessId, edgeSoftness);
        quadRenderer.SetPropertyBlock(properties);
    }

    /// <summary>Sets the on-screen diameter, clamped to 1.35× the rules footprint (§9.2).</summary>
    public void SetDiameter(float diameter)
    {
        float ceiling = footprintDiameter * FXPalette.MaxFootprintOvershoot;
        transform.localScale = Vector3.one * Mathf.Clamp(diameter, 0f, ceiling);
    }

    public void SetGlow(Color glow)
    {
        glowColor = glow;
        Apply(GlowColorId, glow);
    }

    public void SetIntensity(float value)
    {
        intensity = value;
        Apply(IntensityId, value);
    }

    public void SetRingWidth(float value)
    {
        Apply(RingWidthId, Mathf.Clamp(value, 0.02f, 1f));
    }

    public void SetPulseSpeed(float value)
    {
        Apply(PulseSpeedId, value);
        Apply(PulseAmountId, value > 0f ? 0.35f : 0f);
    }

    private void Apply(int propertyId, float value)
    {
        if (quadRenderer == null)
            return;
        quadRenderer.GetPropertyBlock(properties);
        properties.SetFloat(propertyId, value);
        quadRenderer.SetPropertyBlock(properties);
    }

    private void Apply(int propertyId, Color value)
    {
        if (quadRenderer == null)
            return;
        quadRenderer.GetPropertyBlock(properties);
        properties.SetColor(propertyId, value);
        quadRenderer.SetPropertyBlock(properties);
    }

    /// <summary>Contracts (or expands) to a target diameter — the standard anticipation move.</summary>
    public IEnumerator AnimateDiameter(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && this != null)
        {
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
            SetDiameter(Mathf.Lerp(from, to, eased));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (this != null)
            SetDiameter(to);
    }

    /// <summary>Ramps the pulse rate, which is how the grenade fuse reads as running out.</summary>
    public IEnumerator RampPulse(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && this != null)
        {
            SetPulseSpeed(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (this != null)
            SetPulseSpeed(to);
    }

    /// <summary>Fades the glow out and destroys the telegraph.</summary>
    public IEnumerator FadeAndDestroy(float duration)
    {
        float elapsed = 0f;
        float startIntensity = intensity;
        while (elapsed < duration && this != null)
        {
            SetIntensity(Mathf.Lerp(startIntensity, 0f, Mathf.Clamp01(elapsed / duration)));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (this != null)
            Destroy(gameObject);
    }

    /// <summary>Fire-and-forget cleanup for callers that are not in a coroutine.</summary>
    public void FadeOut(float duration)
    {
        if (isActiveAndEnabled)
            StartCoroutine(FadeAndDestroy(duration));
        else if (this != null)
            Destroy(gameObject);
    }
}
