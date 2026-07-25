using System.Collections;
using UnityEngine;

/// <summary>
/// The impact frame on the victim: every tintable renderer on the unit lifts toward
/// <c>--bp-deck-0</c> for a few frames, with a small scale punch. Purely local/visual, written
/// through MaterialPropertyBlocks so the shared team/character materials are never touched.
///
/// WHY THIS IS STILL A BRIGHTENING FLASH ON A BRIGHT BOARD. FIELD DAY inverts almost every effect
/// to darkening, and this one looks like an obvious candidate — it was the Night Range white-hot
/// flash. It is not, for two reasons that only show up when you look at what else fires in the same
/// frame.
///
/// The darkening flash §9.2 calls "the single most effective impact device available" is a
/// *ground* element: a filled multiply disc in <see cref="FXPalette.DarkeningFlashTint"/> that takes
/// the board out from under the blast. That device exists and is correct — see
/// <c>AbilityFX.DarkeningFlash</c>. <c>AbilityFX.AreaLockImpact</c> calls it and this in the same
/// frame, which is the whole design: the ground drops to ink and the victim lifts off it. Darken the
/// victim too and the figure merges into its own hole, so the impact frame that cost two effects
/// reads as one dark smudge.
///
/// The contour is the second reason, and it is measurable. Darkening the body toward ink collapses
/// its contrast against its own ink outline to about 1.05:1 — the outline is eaten by the body and
/// the unit becomes a featureless blob. Lifting takes it the other way: with the contour held (see
/// <see cref="ContourLuminanceCeiling"/>) the puck rim goes from a measured 2.08:1 against the puck
/// it outlines at rest to roughly 7:1 under flash. Lift the rim too and it collapses instead, to a
/// measured 1.09:1 — worse than doing nothing. §9.2 pattern 3 licenses
/// exactly this — "bright shape, ink contour", reserved for the impact frame on single-target
/// moments where context is unambiguous — and the ink contour is the half of that pattern only a
/// brightening flash can keep, and only if the contour is excluded from the lift.
///
/// What WAS wrong is that it brightened illegally. It wrote <c>_BaseColor</c> at white x(1 + 3xmult)
/// — 5.2 for a bullet, 8.5 on death — plus white <c>_EmissionColor</c> at 4.2 to 7.5. All of those
/// are past URP's effective bloom threshold, which made a unit body an eighth emitter on top of
/// §6's seven, and clipping that far past 1.0 flattens every material on the unit to the same white
/// so team colour and internal form both vanish. This lerps between two in-gamut palette colours
/// instead — both linear, which is not free, see <see cref="AuthoredAlbedoToLinear"/> — so no channel
/// can leave LDR by construction and nothing on a unit emits.
///
/// Add to the unit root (or call HitFlash.FlashTarget(gameObject) for one-off use) and trigger
/// from damage handling on each peer:
///
///   GetComponent&lt;HitFlash&gt;()?.Flash();          // ordinary hit
///   GetComponent&lt;HitFlash&gt;()?.Flash(0.18f, 2f); // heavy hit (Area Lock, backstab)
/// </summary>
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Tooltip("How far an ordinary hit lifts the unit toward --bp-deck-0. 0 = no flash, 1 = paper.")]
    public float flashLift = 0.5f;

    [Tooltip("Scale punch at peak (1 = none). Small values read best top-down.")]
    public float scalePunch = 1.12f;

    /// <summary>
    /// Ceiling on the lift a caller's multiplier can reach. The multipliers in use run to 2.5
    /// (<c>CombatFX.DeathFlashStrength</c>), which would otherwise land on flat paper — and at a
    /// full lift every material on the unit converges to the same value, so the unit stops being a
    /// figure and becomes a paper cut-out. 0.85 leaves the darkest and lightest character materials
    /// still measurably apart while a heavy hit is unmistakably heavier than a bullet.
    /// </summary>
    const float MaxFlashLift = 0.85f;

    /// <summary>
    /// Authored-albedo linear luminance at or below which a renderer is the CONTOUR and holds at
    /// rest while the body lifts. Without this the flash lifted the ink rim along with the puck it
    /// outlines, and the contour got *weaker* on the one frame it is doing the most work — measured
    /// 2.33:1 at rest falling to 1.88:1 under flash. Contour survival is the whole reason this
    /// effect brightens rather than darkens, so it has to be true rather than argued.
    ///
    /// WHY A PALETTE VALUE AND NOT A NAME. The rim is <c>BasePuckRim</c> on <c>Char_Black</c> today,
    /// but a predicate that knows that breaks the moment the prefab is reorganised — the same
    /// assumption broke the fuse lookup and the team material list. What actually makes something a
    /// contour is the value it holds, so that is what gets tested: rename the child, reparent it,
    /// duplicate it onto a new mesh, or ink a new part, and the test still lands correctly with no
    /// maintenance.
    ///
    /// 0.028 sits in the gap between the contour band and the darkest body material — ink is 0.0100
    /// and <c>Char_Black</c> 0.0201, while the next value up, <c>Char_CoatNavy</c>, is 0.0384. The
    /// consequence to know about is that ink linework anywhere on a unit holds, not just the puck
    /// rim: twelve renderers on the Sniper, ten on the Shotgunner. That is §4.3 working as intended —
    /// the ink detailing stays ink while the body travels to paper — but it does mean the flash is
    /// deliberately not uniform across the model.
    ///
    /// This threshold is in LINEAR luminance, matching the palette, and the measurement is converted
    /// to meet it — see <see cref="AuthoredAlbedoToLinear"/>, which is where the space change lives
    /// and where the earlier wrong assumption about it is written down. The predicate's only failure
    /// mode is silence, so the separation it depends on is pinned by
    /// <c>HitFlashContourEditModeTests</c> rather than trusted here. That tripwire has already caught
    /// this once.
    /// </summary>
    public const float ContourLuminanceCeiling = 0.028f;

    Renderer[] renderers;

    /// <summary>
    /// Each renderer's authored <c>_BaseColor</c>, refreshed at the start of every flash rather than
    /// cached once. <c>Unit.SetTeamIndicators</c> swaps a viewer-relative team material onto the
    /// indicator props after Awake, so a colour captured at Awake would have the flash on a red
    /// unit lerping up from blue.
    /// </summary>
    Color[] restingBaseColors;

    /// <summary>
    /// Per-renderer, whether <see cref="restingBaseColors"/> came out at contour value. Decided
    /// alongside the colour rather than at Awake so it re-reads through the team material swap.
    /// </summary>
    bool[] holdsAtContour;

    MaterialPropertyBlock propertyBlock;
    Coroutine flashRoutine;
    Vector3 baseScale;
    bool allowScalePunch;

    void Awake()
    {
        renderers = CollectTintableRenderers();
        restingBaseColors = new Color[renderers.Length];
        holdsAtContour = new bool[renderers.Length];
        propertyBlock = new MaterialPropertyBlock();
        baseScale = transform.localScale;
        // A NetworkTransform may replicate scale; punching it locally would fight the sync.
        allowScalePunch = GetComponent<Unity.Netcode.Components.NetworkTransform>() == null;
    }

    /// <summary>
    /// Only renderers that actually consume <c>_BaseColor</c>. A unit's children include FX on the
    /// BattlePlan shaders — the vision cone on <c>_ConeColor</c>, the base puck and its halo on
    /// <c>BattlePlan/GroundGlow</c> — and writing a lift they cannot read was harmless only by
    /// accident. Filtering here also keeps the per-frame loop to the unit's actual body and means a
    /// hit can never brighten a team halo or a ground element.
    /// </summary>
    Renderer[] CollectTintableRenderers()
    {
        Renderer[] candidates = GetComponentsInChildren<Renderer>(includeInactive: false);
        int kept = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            Renderer candidate = candidates[i];
            if (
                candidate == null
                || candidate is LineRenderer
                || candidate.sharedMaterial == null
                || !candidate.sharedMaterial.HasProperty(BaseColorId)
            )
                continue;
            candidates[kept++] = candidate;
        }

        Renderer[] tintable = new Renderer[kept];
        System.Array.Copy(candidates, tintable, kept);
        return tintable;
    }

    /// <summary>Convenience for objects without the component pre-attached.</summary>
    public static void FlashTarget(
        GameObject target,
        float duration = 0.12f,
        float intensityMultiplier = 1f
    )
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
        float peakLift = Mathf.Clamp(flashLift * intensityMultiplier, 0f, MaxFlashLift);
        CaptureRestingColors();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Hold near-full lift briefly, then decay — the "impact frame" then falloff. Timing is
            // unchanged from the dark-board version; the beat is short by design.
            float progress = elapsed / duration;
            float strength = progress < 0.3f ? 1f : 1f - (progress - 0.3f) / 0.7f;
            float lift = peakLift * strength;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer unitRenderer = renderers[i];
                // The contour is not written at all rather than written back at its resting value.
                // Skipping is the stronger guarantee: it holds at the material's own colour without
                // depending on this code having read that colour correctly, and it keeps the ink
                // renderers in the SRP batch.
                if (unitRenderer == null || holdsAtContour[i])
                    continue;

                // Clear before Get so one renderer's write cannot leak into the next through the
                // reused block, and Get after it so any foreign property on this renderer survives.
                propertyBlock.Clear();
                unitRenderer.GetPropertyBlock(propertyBlock);

                Color resting = restingBaseColors[i];
                Color lifted = Color.Lerp(resting, FXPalette.Paper, lift);
                // Paper is opaque; a unit material that is not stays as authored.
                lifted.a = resting.a;
                propertyBlock.SetColor(BaseColorId, lifted);
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

    /// <summary>
    /// <c>sharedMaterial</c> reads the material asset and never the property block, so this is the
    /// authored colour even when called with a previous flash still applied.
    /// </summary>
    void CaptureRestingColors()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer unitRenderer = renderers[i];
            Color authored =
                unitRenderer != null && unitRenderer.sharedMaterial != null
                    ? unitRenderer.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;

            // Both the contour test and the lift target are linear, so the space change happens once,
            // here, at the only point in this component that reads a material.
            restingBaseColors[i] = AuthoredAlbedoToLinear(authored);
            holdsAtContour[i] = IsContourValue(authored);
        }
    }

    /// <summary>
    /// Whether an authored albedo is at contour value, and so held at rest through a flash. Takes the
    /// colour exactly as <c>Material.GetColor</c> returns it; the conversion is this method's job.
    ///
    /// Public because the tripwire in <c>HitFlashContourEditModeTests</c> has to call the real
    /// decision rather than rebuild it. A test that reimplemented the weighting, the ceiling, or the
    /// direction of the comparison could pass while this drifted, which for a predicate whose only
    /// failure mode is silence would be worse than having no test.
    /// </summary>
    public static bool IsContourValue(Color authoredAlbedo) =>
        LinearLuminance(AuthoredAlbedoToLinear(authoredAlbedo)) <= ContourLuminanceCeiling;

    /// <summary>
    /// The sRGB-to-linear step between reading a material colour and doing anything numeric with it.
    ///
    /// <c>Material.GetColor</c> and <c>MaterialPropertyBlock.SetColor</c> ARE NOT IN THE SAME SPACE,
    /// which is the trap this exists to name. Get returns the sRGB value serialised in the
    /// <c>.mat</c> — <c>Char_Black</c> comes back as <c>(0.137, 0.153, 0.180)</c>, the literal
    /// #23272E — while a property-block Set is written straight through and reaches the shader as
    /// linear, which is why every <see cref="FXPalette"/> constant in the project is authored linear.
    /// Reading with one and writing with the other therefore needs an explicit conversion; it is not
    /// a round trip.
    ///
    /// This code previously assumed Get returned linear, on the reasoning that both ends share one
    /// property store. They do not. That single assumption broke two separate things: the contour
    /// predicate compared a gamma measurement against a linear ceiling and so matched nothing, and
    /// the lift interpolated from a gamma start toward a linear <see cref="FXPalette.Paper"/>, which
    /// put the resting end of the ramp about three stops too pale.
    ///
    /// The ceiling stays canonical at linear 0.028 and the measurement is what moves to meet it,
    /// because the palette states the whole gap in linear — ink 0.0100, Char_Black 0.0201,
    /// Char_CoatNavy 0.0384. Re-siting the constant into the gamma gap would leave the number in the
    /// code corresponding to nothing in the document, so the next person computing a new material's
    /// luminance from the palette would get a wrong answer with nothing to warn them.
    ///
    /// Public for the same reason as <see cref="IsContourValue"/>: the tripwire reports both readings
    /// side by side, and it has to get the converted one from here rather than applying its own
    /// <c>.linear</c>, or the space step becomes a third thing that can drift.
    /// </summary>
    public static Color AuthoredAlbedoToLinear(Color authoredAlbedo) => authoredAlbedo.linear;

    /// <summary>Rec. 709 luminance, matching the weighting every contrast figure in the art
    /// direction is quoted against. Expects a linear colour — see
    /// <see cref="AuthoredAlbedoToLinear"/>.</summary>
    public static float LinearLuminance(Color linear) =>
        0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;

    void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        ClearFlash();
    }

    /// <summary>
    /// Drops the block entirely rather than writing the resting colour back. A renderer that keeps a
    /// MaterialPropertyBlock is excluded from SRP batching for as long as it has one, and this fires
    /// on every damage event, so leaving an identity override behind would quietly un-batch every
    /// unit that has ever been shot.
    ///
    /// ⚠ The cost of that choice: this is destructive to the whole block, not just to
    /// <c>_BaseColor</c>. Nothing else writes a property block to a unit's own renderers today — the
    /// team swap assigns materials (which a block overrides regardless, so the two do not collide),
    /// and the world-space health fill is a <c>UnityEngine.UI.Image</c> on a CanvasRenderer, which
    /// <c>GetComponentsInChildren&lt;Renderer&gt;</c> does not return. If a per-unit property ever
    /// does arrive — a fog fade, an outline width — it will be wiped on every hit unless this clears
    /// selectively instead.
    /// </summary>
    void ClearFlash()
    {
        foreach (Renderer unitRenderer in renderers)
        {
            if (unitRenderer == null)
                continue;
            unitRenderer.SetPropertyBlock(null);
        }
        if (allowScalePunch)
            transform.localScale = baseScale;
    }
}
