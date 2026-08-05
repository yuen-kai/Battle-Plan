using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colour half of an impact frame: the unit's own material saying what just happened to it.
/// <para>
/// A hit sears the lit surfaces toward a molten version of their own value and then lets them cool
/// through a shallow bruise. It never brightens them, and it is gone in a quarter of a second:
/// damage lands on the same body several times in a round, so paint that stays warm or stays dark
/// for a whole second stops being an event and becomes the colour the unit is.
/// This board's floor sits at luminance 182 of
/// 255 and the crew's paint already renders within two percent of the scene's bloom threshold, so
/// there is no headroom upward at all: a gain above one puts the surface into the bloom prefilter,
/// and the halo that comes back eats the unit's black outline and every trace of its team colour.
/// Saturation and value are the two axes this board still has, so the sear spends both — the
/// surface keeps its peak channel and loses the other two, which reads as heat without a single
/// clipped pixel.
/// </para>
/// <para>
/// The character's dark materials — the helmet rim, the outline, the gun — are never written at
/// all. They are the drawing, not the paint: without them the unit has no silhouette, no facing and
/// no identity, and a tint that swallows them turns a hurt unit into a deleted one.
/// </para>
/// <para>
/// Written through MaterialPropertyBlocks on the model's renderers only, so the shared team and
/// character materials are never touched and the health bar, base puck and vision cone keep their
/// own colours. This only ever writes colour; the body's motion belongs to <see cref="HitReaction"/>,
/// which means both can run on one unit without arguing over its transform.
/// </para>
/// <code>
///   HitFlash.FlashTarget(unit);                 // ability activation, or a generic pop
///   HitFlash.FlashDamage(unit, 0.7f);           // took a heavy hit
/// </code>
/// </summary>
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>
    /// Below this brightest-channel value a material is structure rather than surface. The crew's
    /// palette splits cleanly here — everything drawn is at or under 0.42, everything painted is at
    /// or over 0.72 — so the line has a wide margin and holds for every unit in the roster.
    /// </summary>
    const float StructureValue = 0.5f;

    // Absolute seconds, not fractions of the flash: the sear has to be the same length whether the
    // hit was a graze or nearly lethal, or severity quietly becomes duration. Fifty milliseconds is
    // still two frames at the 30Hz the strip samples at, so the peak survives capture, and it is
    // short enough that the next round of fire lands on a body that has already cooled.
    const float SearSeconds = 0.05f;
    const float CoolSeconds = 0.07f;
    const float ReleaseSeconds = 0.1f;

    /// <summary>Contact heat: one channel alive, the other two nearly gone.</summary>
    static readonly Color EmberColor = new(1f, 0.26f, 0.055f, 1f);

    /// <summary>Where the surfaces cool to. Dark first, and only incidentally a colour.</summary>
    static readonly Color BruiseColor = new(0.2f, 0.09f, 0.1f, 1f);

    /// <summary>Activation heat. Warm and almost colourless, so it cannot repaint the unit's team.</summary>
    static readonly Color StampColor = new(1f, 0.965f, 0.878f, 1f);

    const float BruiseBlend = 0.12f;

    /// <summary>
    /// Albedo multiplier under the bruise. A hit that arrives this often cannot take the unit's
    /// paint down to half and hold it there: the body dips by a seventh and is back, which reads
    /// as being hit without ever reading as a different unit.
    /// </summary>
    const float BruiseGain = 0.86f;

    [Tooltip("Albedo gain at the hottest frame, before each slot's own ceiling clamps it.")]
    public float flashIntensity = 2.6f;

    /// <summary>
    /// One material slot of the body. A unit's mesh carries several materials, and a block set on
    /// the whole renderer would flatten every submesh to the first one's colour — which is exactly
    /// the "the unit turned into a single bright shape" read this is trying to get away from.
    /// </summary>
    readonly struct BodySlot
    {
        public readonly Renderer Renderer;
        public readonly int MaterialIndex;
        public readonly Color BaseColor;
        public readonly Color BaseEmission;

        /// <summary>
        /// The brightest channel this slot is ever allowed to reach. A material is not blooming at
        /// rest, so its own rest peak is the one gain this effect can prove is safe on every unit
        /// in the roster without knowing how hot the board's key light is.
        /// </summary>
        public readonly float Ceiling;

        /// <summary>False for the dark materials that draw the unit rather than paint it.</summary>
        public readonly bool Surface;

        public BodySlot(Renderer bodyRenderer, int materialIndex, Material material)
        {
            Renderer = bodyRenderer;
            MaterialIndex = materialIndex;
            BaseColor =
                material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;
            BaseEmission =
                material != null && material.HasProperty(EmissionColorId)
                    ? material.GetColor(EmissionColorId)
                    : Color.black;
            Ceiling = Mathf.Max(BaseColor.r, Mathf.Max(BaseColor.g, BaseColor.b));
            Surface = Ceiling >= StructureValue;
        }
    }

    BodySlot[] slots;
    MaterialPropertyBlock propertyBlock;
    Coroutine flashRoutine;
    float damageForce;
    bool damageRamp;
    bool tinted;

    void Awake()
    {
        List<Transform> parts = new();
        List<Renderer> found = new();
        HitBody.Collect(transform, parts, found);

        if (found.Count == 0)
        {
            // Not a unit. Fall back to everything a flat tint would not ruin.
            foreach (Renderer candidate in GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (DiveRecoveryPulse.IsCharacterRenderer(candidate))
                    found.Add(candidate);
            }
        }

        // Each slot burns from its own colour rather than being handed one, so a unit under a hit
        // still reads as its team and its character underneath the heat.
        List<BodySlot> built = new();
        foreach (Renderer bodyRenderer in found)
        {
            Material[] materials = bodyRenderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                built.Add(new BodySlot(bodyRenderer, index, materials[index]));
        }

        slots = built.ToArray();
        propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>Convenience for objects without the component pre-attached.</summary>
    public static void FlashTarget(
        GameObject target,
        float duration = 0.12f,
        float intensityMultiplier = 1f
    )
    {
        if (target == null || !target.activeInHierarchy)
            return;
        if (!target.TryGetComponent(out HitFlash hitFlash))
            hitFlash = target.AddComponent<HitFlash>();
        hitFlash.Flash(duration, intensityMultiplier);
    }

    /// <summary>
    /// The damage-weighted sear, driven by <see cref="HitReaction"/>. A heavier hit drives the
    /// surface further toward the ember and bruises for longer, so the colour agrees with the
    /// body's reaction about how bad it was. Only the depth moves with severity; the sear is the
    /// same length either way.
    /// </summary>
    /// <param name="force">Damage as a fraction of maximum health.</param>
    public static void FlashDamage(GameObject target, float force)
    {
        force = Mathf.Clamp01(force);
        if (target == null || !target.activeInHierarchy)
            return;
        if (!target.TryGetComponent(out HitFlash hitFlash))
            hitFlash = target.AddComponent<HitFlash>();
        hitFlash.damageForce = force;
        hitFlash.Begin(0.24f + 0.18f * force, 1f, damage: true);
    }

    public void Flash(float duration = 0.12f, float intensityMultiplier = 1f)
    {
        // An activation pop landing on a unit that is still bruised would read as the hit healing
        // it, so the longer, more specific ramp keeps the body.
        if (damageRamp && flashRoutine != null)
            return;
        Begin(duration, intensityMultiplier, damage: false);
    }

    void Begin(float duration, float intensityMultiplier, bool damage)
    {
        if (!isActiveAndEnabled || slots == null)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        damageRamp = damage;
        flashRoutine = StartCoroutine(
            FlashRoutine(Mathf.Max(0.02f, duration), Mathf.Max(0f, intensityMultiplier))
        );
    }

    IEnumerator FlashRoutine(float duration, float intensityMultiplier)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            Apply(elapsed, duration, intensityMultiplier);
            elapsed += Time.deltaTime;
            yield return null;
        }
        ClearFlash();
        damageRamp = false;
        flashRoutine = null;
    }

    void Apply(float elapsed, float duration, float intensityMultiplier)
    {
        Color hue;
        float gain;
        float blend;

        if (!damageRamp)
        {
            // Activation: hot the whole way, and back to normal. Nothing was damaged.
            hue = StampColor;
            float progress = Mathf.Clamp01(elapsed / duration);
            gain = Mathf.Lerp(flashIntensity * intensityMultiplier, 1f, progress * progress);
            blend = Mathf.Lerp(0.72f, 0f, progress);
        }
        else
        {
            // A graze barely warms the paint; a near-lethal hit pushes a third of the way to the
            // ember. The slot ceiling means neither can ever exceed the value the surface had.
            float searBlend = 0.06f + 0.28f * damageForce;
            float coolEnd = SearSeconds + CoolSeconds;
            float holdEnd = Mathf.Max(coolEnd + 0.03f, duration - ReleaseSeconds);

            if (elapsed < SearSeconds)
            {
                // Flat across the whole sear. The knockback peaks inside this window and the
                // contact mass is at full through all of it, so light, matter and colour land on
                // one frame instead of three different ones.
                hue = EmberColor;
                blend = searBlend;
                gain = 1f - 0.02f * Mathf.Clamp01(elapsed / SearSeconds);
            }
            else if (elapsed < coolEnd)
            {
                float cooling = Mathf.Pow((elapsed - SearSeconds) / CoolSeconds, 0.8f);
                hue = Color.Lerp(EmberColor, BruiseColor, cooling);
                blend = Mathf.Lerp(searBlend, BruiseBlend, cooling);
                gain = Mathf.Lerp(0.98f, BruiseGain, cooling);
            }
            else if (elapsed < holdEnd)
            {
                // The bruise lifts by a hair as it is held, so no two frames of a long recovery are
                // the same image with a different timestamp.
                float held = Mathf.InverseLerp(coolEnd, holdEnd, elapsed);
                hue = BruiseColor;
                blend = BruiseBlend;
                gain = BruiseGain + 0.035f * held * held;
            }
            else
            {
                float releasing = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((elapsed - holdEnd) / Mathf.Max(0.0001f, duration - holdEnd))
                );
                hue = BruiseColor;
                blend = BruiseBlend * (1f - releasing);
                gain = Mathf.Lerp(BruiseGain + 0.035f, 1f, releasing);
            }
        }

        foreach (BodySlot slot in slots)
        {
            if (slot.Renderer == null || !slot.Surface)
                continue;

            Color tint = Color.Lerp(slot.BaseColor, hue, blend) * gain;
            float peak = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            if (peak > slot.Ceiling)
                tint *= slot.Ceiling / peak;
            tint.a = slot.BaseColor.a;

            // Emission is scaled down with the surface and never up: a team material's own glow
            // has to dim with the bruise instead of holding the body's value from underneath, and
            // it is the one channel that could still push a capped albedo over the bloom knee.
            slot.Renderer.GetPropertyBlock(propertyBlock, slot.MaterialIndex);
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(EmissionColorId, slot.BaseEmission * Mathf.Min(1f, gain));
            slot.Renderer.SetPropertyBlock(propertyBlock, slot.MaterialIndex);
        }
        tinted = true;
    }

    void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        damageRamp = false;
        ClearFlash();
    }

    void ClearFlash()
    {
        if (!tinted || slots == null)
            return;
        tinted = false;
        foreach (BodySlot slot in slots)
        {
            if (slot.Renderer != null && slot.Surface)
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
        }
    }
}
