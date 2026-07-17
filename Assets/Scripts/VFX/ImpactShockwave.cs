using System.Collections;
using UnityEngine;

/// <summary>
/// One-call Clash Mini-style impact: an expanding, fading ground ring (BattlePlan/GroundGlow in
/// ring mode) plus a brief point-light pop. Purely local/visual — call on each peer at the
/// moment of impact (grenade detonation, Area Lock hit, Pogo landing, shield activation):
///
///   ImpactShockwave.Spawn(position, teamColor);                    // standard hit
///   ImpactShockwave.Spawn(position, teamColor, maxRadius: 5f);     // big boom
/// </summary>
public class ImpactShockwave : MonoBehaviour
{
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    public static void Spawn(
        Vector3 position,
        Color color,
        float maxRadius = 2.5f,
        float duration = 0.45f,
        bool withLightPop = true
    )
    {
        Shader glowShader = Shader.Find("BattlePlan/GroundGlow");
        if (glowShader == null)
        {
            Debug.LogWarning("[ImpactShockwave] BattlePlan/GroundGlow shader not found, skipping shockwave");
            return;
        }

        GameObject ringObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ringObject.name = "ImpactShockwave";
        Destroy(ringObject.GetComponent<Collider>());
        // 0.08: above floor and the fog overlay tiles (y 0.05) to avoid z-fighting
        ringObject.transform.position = new Vector3(position.x, 0.08f, position.z);
        ringObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ringObject.transform.localScale = Vector3.one * 0.1f;

        Renderer ringRenderer = ringObject.GetComponent<Renderer>();
        Material ringMaterial = new(glowShader);
        ringMaterial.SetFloat(RingWidthId, 0.22f);
        ringMaterial.SetColor(GlowColorId, color);
        ringMaterial.SetFloat(IntensityId, 3f);
        ringRenderer.material = ringMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ImpactShockwave shockwave = ringObject.AddComponent<ImpactShockwave>();
        shockwave.StartCoroutine(shockwave.Animate(maxRadius, duration, withLightPop, color, position));
    }

    IEnumerator Animate(float maxRadius, float duration, bool withLightPop, Color color, Vector3 position)
    {
        Light popLight = null;
        if (withLightPop)
        {
            GameObject lightObject = new("ImpactLight");
            lightObject.transform.position = position + Vector3.up * 1.5f;
            popLight = lightObject.AddComponent<Light>();
            popLight.type = LightType.Point;
            popLight.color = Color.Lerp(color, Color.white, 0.5f);
            popLight.range = maxRadius * 3f;
            popLight.intensity = 6f;
        }

        Renderer ringRenderer = GetComponent<Renderer>();
        Material ringMaterial = ringRenderer.material;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            // Fast start, decelerating expansion (ease-out) reads punchier
            float eased = 1f - (1f - progress) * (1f - progress);
            float diameter = Mathf.Lerp(0.2f, maxRadius * 2f, eased);
            transform.localScale = Vector3.one * diameter;

            float fade = 1f - progress;
            ringMaterial.SetFloat(IntensityId, 3f * fade);
            // Ring thins as it expands, like a real shockwave
            ringMaterial.SetFloat(RingWidthId, Mathf.Lerp(0.22f, 0.06f, progress));

            if (popLight != null)
                popLight.intensity = 6f * fade * fade;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (popLight != null)
            Destroy(popLight.gameObject);
        Destroy(ringMaterial);
        Destroy(gameObject);
    }
}
