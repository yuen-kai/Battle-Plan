using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clash Mini-style impact frame: flashes every renderer on the unit white-hot for a few frames
/// with a small scale punch. Uses MaterialPropertyBlocks (URP _BaseColor/_EmissionColor) so the
/// shared team/character materials are never touched. Purely local/visual.
///
/// Add to the unit root (or call HitFlash.FlashTarget(gameObject) for one-off use) and trigger
/// from damage handling on each peer:
///
///   GetComponent<HitFlash>()?.Flash();          // ordinary hit
///   GetComponent<HitFlash>()?.Flash(0.18f, 2f); // heavy hit (Area Lock, backstab)
/// </summary>
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Tooltip("HDR white multiplier at peak flash.")]
    public float flashIntensity = 3f;

    [Tooltip("Scale punch at peak (1 = none). Small values read best top-down.")]
    public float scalePunch = 1.12f;

    Renderer[] renderers;
    MaterialPropertyBlock propertyBlock;
    Coroutine flashRoutine;
    Vector3 baseScale;
    bool allowScalePunch;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
        propertyBlock = new MaterialPropertyBlock();
        baseScale = transform.localScale;
        // A NetworkTransform may replicate scale; punching it locally would fight the sync.
        allowScalePunch = GetComponent<Unity.Netcode.Components.NetworkTransform>() == null;
    }

    /// <summary>Convenience for objects without the component pre-attached.</summary>
    public static void FlashTarget(GameObject target, float duration = 0.12f, float intensityMultiplier = 1f)
    {
        if (target == null)
            return;
        if (!target.TryGetComponent(out HitFlash hitFlash))
            hitFlash = target.AddComponent<HitFlash>();
        hitFlash.Flash(duration, intensityMultiplier);
    }

    public void Flash(float duration = 0.12f, float intensityMultiplier = 1f)
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            ClearFlash();
        }
        flashRoutine = StartCoroutine(FlashRoutine(duration, intensityMultiplier));
    }

    IEnumerator FlashRoutine(float duration, float intensityMultiplier)
    {
        float peak = flashIntensity * intensityMultiplier;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Hold near-full white briefly, then decay — the "impact frame" then falloff
            float progress = elapsed / duration;
            float strength = progress < 0.3f ? 1f : 1f - (progress - 0.3f) / 0.7f;

            Color flashColor = Color.white * (peak * strength);
            foreach (Renderer unitRenderer in renderers)
            {
                if (unitRenderer == null || unitRenderer is LineRenderer)
                    continue;
                unitRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, Color.white * (1f + peak * strength));
                propertyBlock.SetColor(EmissionColorId, flashColor);
                unitRenderer.SetPropertyBlock(propertyBlock);
            }

            if (allowScalePunch)
            {
                float punch = Mathf.Lerp(scalePunch, 1f, progress);
                transform.localScale = baseScale * punch;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
        ClearFlash();
        flashRoutine = null;
    }

    void ClearFlash()
    {
        foreach (Renderer unitRenderer in renderers)
        {
            if (unitRenderer == null || unitRenderer is LineRenderer)
                continue;
            unitRenderer.SetPropertyBlock(null);
        }
        if (allowScalePunch)
            transform.localScale = baseScale;
    }
}
