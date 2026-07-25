using System.Collections;
using UnityEngine;

/// <summary>
/// One-call Clash Mini-style impact: an expanding, fading ground ring plus a brief light pop.
/// Purely local and visual — call on each peer at the moment of impact (grenade detonation, Area
/// Lock hit, Pogo landing, shield activation):
///
///   ImpactShockwave.Spawn(position, teamColor);                    // standard hit
///   ImpactShockwave.Spawn(position, teamColor, maxRadius: 5f);     // big boom
///
/// ONE RING, MULTIPLY. The ring is a horizontal quad at FXPalette.YShockwaveRing, which puts it
/// inside the §7.10 ground band, and §9.2.1 rules that a ground-plane effect may multiply because
/// it can only ever composite against deck, painted markings and the fog wash — all of them light.
/// An earlier revision paired it with an additive ring as a dark-host fallback; that is unwound,
/// because the case it guarded against cannot occur down here and the second ring was doubling the
/// draw count of the busiest effect in the game. Multiply is also the better neighbour for the
/// painted deck markings, which it darkens through rather than covering over.
///
/// Rings share one material per composite mode and differ only by MaterialPropertyBlock, so a
/// multi-hit frame still GPU-instances into one draw call per mode. The light pop goes through
/// FXLightPool rather than creating a Light, which is what keeps a ten-pellet Shotgunner burst
/// inside URP's per-object additional-light limit.
/// </summary>
public class ImpactShockwave : MonoBehaviour
{
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // === Ring envelope (ArtDirection §9.1) ===
    private const float StartDiameter = 0.2f;
    private const float StartRingWidth = 0.22f;
    private const float EndRingWidth = 0.06f;

    /// <summary>In multiply mode _Intensity is coverage, not HDR headroom, so the body ring's peak
    /// is full opacity — anything above 1 would clip and lose the fade's first third.</summary>
    private const float BodyPeakCoverage = 1f;

    private const float LightIntensity = 6f;
    private const float LightRangeMultiplier = 3f;
    private const float LightHeight = 1.5f;

    private static Material ringMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ringMaterial = null;
    }

    public static void Spawn(
        Vector3 position,
        Color color,
        float maxRadius = 2.5f,
        float duration = 0.45f,
        bool withLightPop = true
    )
    {
        SpawnRing(position, color, maxRadius, duration, BodyPeakCoverage);

        // Gated on fog: a light pop is a bright, unmistakable announcement that something happened
        // at a place the local player may not be allowed to see (§9.5). The ring itself is only
        // spawned by call sites that already run on visible events, but the light travels further.
        if (withLightPop && FXPalette.IsVisibleToLocalTeam(position))
        {
            FXLightPool.TryFlash(
                position + Vector3.up * LightHeight,
                Color.Lerp(color, Color.white, 0.5f),
                maxRadius * LightRangeMultiplier,
                LightIntensity,
                duration
            );
        }
    }

    private static void SpawnRing(
        Vector3 position,
        Color color,
        float maxRadius,
        float duration,
        float peakIntensity
    )
    {
        Material shared = ResolveMaterial();
        if (shared == null)
            return;

        GameObject ringObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ringObject.name = "ImpactShockwave";
        ringObject.hideFlags = HideFlags.DontSave;
        Destroy(ringObject.GetComponent<Collider>());
        ringObject.transform.position = new Vector3(
            position.x,
            FXPalette.YShockwaveRing,
            position.z
        );
        ringObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ringObject.transform.localScale = Vector3.one * StartDiameter;

        Renderer ringRenderer = ringObject.GetComponent<Renderer>();
        ringRenderer.sharedMaterial = shared;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        ImpactShockwave shockwave = ringObject.AddComponent<ImpactShockwave>();
        shockwave.StartCoroutine(shockwave.Animate(maxRadius, duration, color, peakIntensity));
    }

    /// <summary>
    /// One shared material, created once. Blend state is render state and cannot come from a
    /// MaterialPropertyBlock, so it has to live on the material — everything else rides the
    /// property block, which is what keeps a multi-hit frame down to a single draw call.
    /// </summary>
    private static Material ResolveMaterial()
    {
        if (ringMaterial != null)
            return ringMaterial;

        Shader glowShader = Shader.Find("BattlePlan/GroundGlow");
        if (glowShader == null)
        {
            Debug.LogWarning(
                "[ImpactShockwave] BattlePlan/GroundGlow shader not found, skipping shockwave"
            );
            return null;
        }

        ringMaterial = new Material(glowShader)
        {
            name = "ImpactShockwave (runtime)",
            hideFlags = HideFlags.HideAndDontSave,
            enableInstancing = true,
        };
        FXPalette.Composite.Apply(ringMaterial, FXPalette.GroundPlaneComposite);
        return ringMaterial;
    }

    private IEnumerator Animate(
        float maxRadius,
        float duration,
        Color color,
        float peakIntensity
    )
    {
        Renderer ringRenderer = GetComponent<Renderer>();
        MaterialPropertyBlock properties = new();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            // Fast start, decelerating expansion (ease-out) reads punchier
            float eased = 1f - (1f - progress) * (1f - progress);
            float radius = Mathf.Lerp(0.1f, maxRadius, eased);
            transform.localScale = Vector3.one * (radius * 2f);

            float fade = 1f - progress;
            ringRenderer.GetPropertyBlock(properties);
            properties.SetColor(GlowColorId, color);
            properties.SetFloat(IntensityId, peakIntensity * fade);
            // Ring thins as it expands, like a real shockwave
            properties.SetFloat(
                RingWidthId,
                Mathf.Lerp(StartRingWidth, EndRingWidth, progress)
            );
            ringRenderer.SetPropertyBlock(properties);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }
}
