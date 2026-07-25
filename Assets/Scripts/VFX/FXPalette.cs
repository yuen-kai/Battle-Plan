using UnityEngine;

/// <summary>
/// Every colour, every composite mode and every ground height the FX layer is allowed to use.
/// Direction FIELD DAY. Nothing in Assets/Scripts/VFX may author a colour outside this file.
///
/// WHICH SPACE. Palette §0 governs, and getting it wrong is the standard way a swap looks broken
/// on day one. A <c>Color</c> handed to <c>SetColor</c> or a MaterialPropertyBlock is written
/// straight through, so those constants are LINEAR. A <c>Color</c> handed to a ParticleSystem
/// gradient, a <c>Light.color</c> or an LDR inspector field is converted for us, so those are
/// sRGB. Every constant below says which it is; the sRGB ones carry the <c>Srgb</c> suffix.
///
/// THE THESIS. On a bright board darkness is the scarce resource, so it is the expensive-looking
/// one. Night Range put a white core against black; FIELD DAY puts ink against paper. Almost
/// everything composites Multiply, the impact device is a darkening flash rather than a white one,
/// and only the seven values in the HDR block are permitted to cross the 1.8 bloom threshold.
/// Reaching for an eighth means an effect is trying to solve a silhouette problem with light,
/// which does not work at high key (ArtDirection §9.2, anti-pattern 2).
/// </summary>
public static class FXPalette
{
    // =====================================================================================
    // HDR EMISSIVE — palette §6, LINEAR, paste-as-is.
    //
    // These seven are the complete list of things allowed to bloom. Bloom threshold is 1.8 and
    // intensity is capped at 0.5, so the two entries that do not reach 1.8 are hot colour that
    // deliberately stops short of glowing.
    // =====================================================================================

    /// <summary>Muzzle flash core, 2 frames. #FFF0D2 +2.0. Blooms.</summary>
    public static readonly Color MuzzleCore = new(4f, 3.485f, 2.578f, 1f);

    /// <summary>The one-frame impact flash. #FFFFFF +1.6. Blooms.</summary>
    public static readonly Color ImpactFlash = new(3.031f, 3.031f, 3.031f, 1f);

    /// <summary>Sniper charge core. #FFD24C +2.2. Blooms.</summary>
    public static readonly Color SniperChargeCore = new(4.595f, 2.961f, 0.332f, 1f);

    /// <summary>Beam core, friendly. #7FB6F5 +1.8. Hot colour, deliberately below threshold.</summary>
    public static readonly Color BeamCoreBlue = new(0.739f, 1.629f, 3.18f, 1f);

    /// <summary>Beam core, hostile. #FF9AA1 +1.8. Hot colour, deliberately below threshold.</summary>
    public static readonly Color BeamCoreRed = new(3.482f, 1.125f, 1.241f, 1f);

    /// <summary>Contested objective ring — the only additive ground element. #F08A14 +1.4.</summary>
    public static readonly Color ObjectiveContested = new(2.3f, 0.671f, 0.018f, 1f);

    /// <summary>Deployment mark sweep. #FFF6E4 +1.2. Blooms.</summary>
    public static readonly Color DeployMarkSweep = new(2.297f, 2.117f, 1.782f, 1f);

    /// <summary>Alias kept so impact call sites read naturally. Same value as ImpactFlash.</summary>
    public static readonly Color Core = ImpactFlash;

    /// <summary>Alias kept so muzzle and spark call sites read naturally.</summary>
    public static readonly Color CoreSoft = MuzzleCore;

    // =====================================================================================
    // BOARD TOKENS — LINEAR. For SetColor, MaterialPropertyBlocks and multiply tints.
    //
    // A Multiply tint and an opaque fill want the same number, which is why the team colours are
    // no longer split into a "glow" and a "surface" variant the way they were on the dark board.
    // =====================================================================================

    /// <summary>--bp-ink #151A20. The darkest value in the game, and the one FIELD DAY spends.
    /// Every contour, every darkening flash, the deck pulse.</summary>
    public static readonly Color Ink = new(0.0075f, 0.0103f, 0.0144f, 1f);

    /// <summary>--bp-shadow #3E4C55. Cool shadow tint for contact shadows and the match-end
    /// vignette. Warmer and lighter than ink, because a shadow is not a drawn line.</summary>
    public static readonly Color Shadow = new(0.0482f, 0.0723f, 0.0908f, 1f);

    /// <summary>--bp-blue #0F5CB8. Team blue, as a multiply tint and as an opaque fill.</summary>
    public static readonly Color Blue = new(0.0048f, 0.107f, 0.4793f, 1f);

    /// <summary>--bp-blue-hi #0B4A93. Emphasis is darker on a light board, never lighter.</summary>
    public static readonly Color BlueHi = new(0.0033f, 0.0685f, 0.2918f, 1f);

    /// <summary>--bp-red #E23B45. Team red, and the Area Lock line.</summary>
    public static readonly Color Red = new(0.7605f, 0.0437f, 0.0595f, 1f);

    /// <summary>--bp-red-hi #A8142A.</summary>
    public static readonly Color RedHi = new(0.3916f, 0.007f, 0.0232f, 1f);

    /// <summary>--bp-red-deep #780C1C. The darkest red step, and the sniper target-lock beam
    /// (§8.2). A threat that has not landed yet is drawn nearer ink than the attack it precedes.
    /// </summary>
    public static readonly Color RedDeep = new(0.1878f, 0.0037f, 0.0116f, 1f);

    /// <summary>--bp-amber #F08A14. Act-now only: objective, dodge alert, grenade core.</summary>
    public static readonly Color Amber = new(0.8714f, 0.2542f, 0.007f, 1f);

    /// <summary>--bp-amber-deep #5E3103. The grenade's darkening telegraph.</summary>
    public static readonly Color AmberDeep = new(0.1119f, 0.0307f, 0.0009f, 1f);

    /// <summary>--bp-cover #66707A. Cover's vertical faces. The smoke telegraph ring.</summary>
    public static readonly Color Cover = new(0.1329f, 0.162f, 0.1946f, 1f);

    /// <summary>--bp-cover-plate #59626A. Chips knocked off a cover block. Deliberately not
    /// --bp-cover: debris rendered in the host's own colour is invisible against it (§9.2.1).</summary>
    public static readonly Color CoverPlate = new(0.0999f, 0.1221f, 0.1441f, 1f);

    /// <summary>--bp-ground-hill #8E999E. Objective deck, a rung under the checker.</summary>
    public static readonly Color GroundHill = new(0.2705f, 0.3185f, 0.3419f, 1f);

    /// <summary>--bp-deck-0 #FCF9F3. Paper. The contour colour against a dark host — see
    /// <see cref="ContourFor"/>.</summary>
    public static readonly Color Paper = new(0.9734f, 0.9473f, 0.8963f, 1f);

    /// <summary>--bp-deck-4 #B6AB93. Smoke puff cards.</summary>
    public static readonly Color Deck4 = new(0.4678f, 0.4072f, 0.2918f, 1f);

    /// <summary>--bp-ground-line #6E7A80. Pogo launch dust.</summary>
    public static readonly Color GroundLine = new(0.1559f, 0.1946f, 0.2159f, 1f);

    /// <summary>--bp-deck-4 at alpha 0.78. Smoke must be a solid pale mass on a bright board, not
    /// a translucent haze — it has to actually obscure (§9.4).</summary>
    public static readonly Color WorldSmoke = new(0.4678f, 0.4072f, 0.2918f, 0.78f);

    // =====================================================================================
    // BOARD TOKENS — sRGB. For ParticleSystem gradients, Light.color and LDR inspector fields.
    // =====================================================================================

    /// <summary>--bp-ink #151A20.</summary>
    public static readonly Color InkSrgb = new(0.0824f, 0.102f, 0.1255f, 1f);

    /// <summary>--bp-blue #0F5CB8.</summary>
    public static readonly Color BlueSrgb = new(0.0588f, 0.3608f, 0.7216f, 1f);

    /// <summary>--bp-blue-hi #0B4A93. Tracer head; a tracer must be darker than the deck.</summary>
    public static readonly Color BlueHiSrgb = new(0.0431f, 0.2902f, 0.5765f, 1f);

    /// <summary>--bp-red #E23B45.</summary>
    public static readonly Color RedSrgb = new(0.8863f, 0.2314f, 0.2706f, 1f);

    /// <summary>--bp-red-hi #A8142A.</summary>
    public static readonly Color RedHiSrgb = new(0.6588f, 0.0784f, 0.1647f, 1f);

    /// <summary>--bp-amber #F08A14.</summary>
    public static readonly Color AmberSrgb = new(0.9412f, 0.5412f, 0.0784f, 1f);

    /// <summary>--bp-cover #66707A.</summary>
    public static readonly Color CoverSrgb = new(0.4f, 0.4392f, 0.4784f, 1f);

    /// <summary>--bp-cover-plate #59626A. Debris chips and grenade shrapnel.</summary>
    public static readonly Color CoverPlateSrgb = new(0.349f, 0.3843f, 0.4157f, 1f);

    /// <summary>--bp-deck-0 #FCF9F3. The paper contour, for effects on a dark host.</summary>
    public static readonly Color PaperSrgb = new(0.9882f, 0.9765f, 0.9529f, 1f);

    /// <summary>--bp-deck-3 #D2C9B6. Grenade aftermath dust.</summary>
    public static readonly Color Deck3Srgb = new(0.8235f, 0.7882f, 0.7137f, 1f);

    /// <summary>--bp-deck-4 #B6AB93.</summary>
    public static readonly Color Deck4Srgb = new(0.7137f, 0.6706f, 0.5765f, 1f);

    /// <summary>--bp-deck-5 #877E6C.</summary>
    public static readonly Color Deck5Srgb = new(0.5294f, 0.4941f, 0.4235f, 1f);

    /// <summary>--bp-ground-line #6E7A80. Dust kicked off painted concrete.</summary>
    public static readonly Color GroundLineSrgb = new(0.4314f, 0.4784f, 0.502f, 1f);

    /// <summary>--bp-deck-4, the LDR face of WorldSmoke, for gradients.</summary>
    public static readonly Color SmokeSrgb = Deck4Srgb;

    // =====================================================================================
    // COMPOSITE MODES
    //
    // Which way an effect composites is a palette decision, not a per-effect one, so it lives here
    // beside the colours and swaps atomically with them. Changing the colours without changing the
    // modes produces effects that are the right hue and invisible.
    //
    // Values must match BP_COMPOSITE_* in Assets/Shaders/BP_FXComposite.hlsl.
    // =====================================================================================

    public static class Composite
    {
        public const float Additive = 0f;
        public const float Alpha = 1f;
        public const float Multiply = 2f;

        // UnityEngine.Rendering.BlendMode, as the shaders' [Enum] properties expect them.
        public const float BlendOne = 1f;
        public const float BlendZero = 0f;
        public const float BlendDstColor = 2f;
        public const float BlendSrcAlpha = 5f;
        public const float BlendOneMinusSrcAlpha = 10f;

        /// <summary>Applies a composite mode and its matching blend state together. They are not
        /// independently meaningful — a mode without its blend factors renders wrong, not just
        /// different — so no caller is given the chance to set one without the other.</summary>
        public static void Apply(Material material, float mode)
        {
            if (material == null)
                return;
            material.SetFloat(CompositeModeId, mode);
            material.SetFloat(SrcBlendId, SrcBlendFor(mode));
            material.SetFloat(DstBlendId, DstBlendFor(mode));
        }

        public static float SrcBlendFor(float mode)
        {
            if (mode >= Multiply)
                return BlendDstColor;
            return mode >= Alpha ? BlendSrcAlpha : BlendOne;
        }

        public static float DstBlendFor(float mode)
        {
            if (mode >= Multiply)
                return BlendZero;
            return mode >= Alpha ? BlendOneMinusSrcAlpha : BlendOne;
        }
    }

    public static readonly int CompositeModeId = Shader.PropertyToID("_CompositeMode");
    public static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    public static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

    // =====================================================================================
    // COMPOSITE SELECTION — ArtDirection §9.2.1, and the resolution of risk 11.
    //
    // Multiply over an already-dark host composites to nothing, so an effect that can cross cover
    // needs an answer. The answer is NOT a runtime fallback. A beam that multiplies over deck and
    // switches to alpha where it crosses a cover block has a seam at that boundary, and a player
    // reads a seam as information — as the beam doing something different there. A rendering
    // artefact that looks like a game rule is worse than an inconsistent effect or an invisible
    // one, so the composite mode is fixed per instance and never varies along an effect's length.
    //
    // The choice falls out of the Y-order contract (§7.10), which already sorts every element by
    // whether it lies on the ground:
    //
    //     y <= YContactShadow   Multiply permitted. It can only ever composite against deck,
    //                           painted markings and the fog wash, and all three are light.
    //     above that            Alpha with a contour. At a 73 degree camera it WILL overlap cover,
    //                           units and the rail in screen space even when it does not touch
    //                           them in world space.
    //
    // Static and authored, so there is no surface query, no mode switching and no fallback layer.
    // Over a pale deck the two are close to indistinguishable anyway; multiply's real advantage is
    // that it preserves the painted markings underneath, which is a reason to keep ground effects
    // on it and no reason to want it above the plane.
    // =====================================================================================

    /// <summary>Ground telegraphs, ability rings, range discs, shockwaves, contact shadows.</summary>
    public const float GroundGlowComposite = Composite.Multiply;

    /// <summary>The body of any element lying on the ground plane. Darkens the deck it sits on
    /// without covering the paint.</summary>
    public const float GroundPlaneComposite = Composite.Multiply;

    /// <summary>Everything drawn above the ground plane: beams, cores, sparks, shields, smoke.
    /// Alpha, so the element looks the same over deck, cover, a unit and the rail.</summary>
    public const float AbovePlaneComposite = Composite.Alpha;

    /// <summary>Contours, slash decals, debris. Straight alpha, so a drawn line keeps its exact
    /// authored value instead of being scaled by whatever is underneath.</summary>
    public const float ContourComposite = Composite.Alpha;

    /// <summary>
    /// The two-frame HDR flashes, and the only things still additive: the muzzle flash core, the
    /// impact flash frame, the contested objective ring. §6 names these explicitly and §9.2.1's
    /// alpha rule is aimed at persistent elements — an additive flash over cover is more visible
    /// rather than less, and at two frames there is no time to read a seam.
    /// </summary>
    public const float FlashComposite = Composite.Additive;

    /// <summary>Contact shadows, dropped beneath projectiles and effect elements.</summary>
    public const float ShadowComposite = Composite.Multiply;

    /// <summary>
    /// The contour colour for an effect drawn against <paramref name="darkHost"/> — a cover face,
    /// the rail, a unit's shaded side. §4.3's contour law inverts rather than failing: ink against
    /// paper, paper against ink, whichever wins against the host.
    ///
    /// Authored per call site, not sampled. The only effects that pass true are the ones that
    /// land ON a dark object and are known to at their call site — a wall impact, a shield
    /// deflection — and those are allowed to look different from their floor equivalents, because
    /// a wall is visibly a different object and the difference reads as material response.
    /// </summary>
    public static Color ContourFor(bool darkHost) => darkHost ? Paper : Ink;

    /// <summary>Multiply tint for contact shadows. Coverage does the modulation, not the tint —
    /// BP_Composite lerps from white to this by coverage, so a shadow at 0.4 coverage darkens to
    /// 40% of the way toward --bp-shadow rather than slamming to it.</summary>
    public static readonly Color ShadowTint = Shadow;

    /// <summary>Multiply tint for a darkening flash — the FIELD DAY impact device, an inverted
    /// white flash. Ink rather than shadow, because an impact frame should reach the darkest value
    /// the game owns (§9.2).</summary>
    public static readonly Color DarkeningFlashTint = Ink;

    /// <summary>
    /// Whether darkening layers actually darken anything. A multiply of white is a no-op, so a
    /// board whose shadow tint is white skips every darkening layer outright rather than drawing
    /// it invisibly. On FIELD DAY this is true and those layers are the primary read.
    /// </summary>
    public static bool ShadowLayersEnabled =>
        ShadowTint.r < 0.999f || ShadowTint.g < 0.999f || ShadowTint.b < 0.999f;

    // === Contact shadow and two-tone ring geometry ===

    /// <summary>Diameter of a projectile's ground contact shadow, in world units.</summary>
    public const float ContactShadowDiameter = 0.35f;

    /// <summary>Shares the light pool's cap. Above it, projectiles keep the tracer and lose the
    /// shadow rather than smearing the deck during a Shotgunner burst.</summary>
    public const int MaxConcurrentContactShadows = FXLightPool.MaxConcurrentLights;

    /// <summary>--bp-fog #C5C7C3. The unpainted-board wash, for effects that must reckon with it
    /// as a host. Lighter than the deck, so a multiply reads better over fog than over paint.
    /// </summary>
    public static readonly Color Fog = new(0.5583f, 0.5711f, 0.5457f, 1f);

    // =====================================================================================
    // Y-ORDER CONTRACT — ArtDirection §7.10. Every ground-plane element the FX layer draws picks
    // its height from here and nowhere else.
    // =====================================================================================

    /// <summary>Temporary blast scorch. Above painted deck decals, below the hill groove.</summary>
    public const float YScorchDecal = 0.025f;

    /// <summary>Existing slot used by ImpactShockwave.</summary>
    public const float YShockwaveRing = 0.08f;

    /// <summary>Ability telegraphs and dodge markers.</summary>
    public const float YAbilityTelegraph = 0.09f;

    /// <summary>Unit and projectile contact shadow blob — the top of the ground stack.</summary>
    public const float YContactShadow = 0.13f;

    // =====================================================================================
    // FOOTPRINT CONTRACT — ArtDirection §9.3.
    // 1 unit-width = 1.3 world units. 1 cell = 2.7 world units.
    // =====================================================================================

    /// <summary>Nothing may exceed this multiple of its rules footprint on any frame.</summary>
    public const float MaxFootprintOvershoot = 1.35f;

    /// <summary>No ability's full anticipation → impact → aftermath may outlast this.</summary>
    public const float MaxAbilitySequenceSeconds = 1.4f;

    /// <summary>
    /// The viewer-relative team colour, matching the contract Unit.SetTeamIndicators and
    /// GameLoop.GetTeamColorForViewer already hold to: the local player's crew is always blue.
    /// </summary>
    public static Color TeamGlow(int teamIndex)
    {
        return GameLoop.IsTeamFriendlyToLocalPlayer(teamIndex) ? Blue : Red;
    }

    /// <summary>The emphasis step of <see cref="TeamGlow"/>. Darker, never lighter.</summary>
    public static Color TeamGlowHot(int teamIndex)
    {
        return GameLoop.IsTeamFriendlyToLocalPlayer(teamIndex) ? BlueHi : RedHi;
    }

    /// <summary>The one hot core a team-coloured beam is allowed. Below the bloom threshold.</summary>
    public static Color TeamBeamCore(int teamIndex)
    {
        return GameLoop.IsTeamFriendlyToLocalPlayer(teamIndex) ? BeamCoreBlue : BeamCoreRed;
    }

    /// <summary>Viewer-relative team colour in sRGB, for ParticleSystem gradients and Lights.</summary>
    public static Color TeamSrgb(int teamIndex)
    {
        return GameLoop.IsTeamFriendlyToLocalPlayer(teamIndex) ? BlueSrgb : RedSrgb;
    }

    /// <summary>Viewer-relative emphasis colour in sRGB. Tracers use this, not the base: a tracer
    /// has to be darker than the deck to be seen at all (§8.1).</summary>
    public static Color TeamHiSrgb(int teamIndex)
    {
        return GameLoop.IsTeamFriendlyToLocalPlayer(teamIndex) ? BlueHiSrgb : RedHiSrgb;
    }

    /// <summary>The team index of a unit GameObject, or -1 when it is not a unit.</summary>
    public static int TeamIndexOf(GameObject unit)
    {
        if (unit == null)
            return -1;
        return unit.TryGetComponent(out Unit identity) ? identity.TeamIndex : -1;
    }

    /// <summary>
    /// Whether the local player's team can see the cell under <paramref name="worldPosition"/>.
    /// Effects that would otherwise announce an enemy action the player has not been shown are
    /// gated on this; see AbilityFX for which effects deliberately pierce fog instead.
    /// </summary>
    public static bool IsVisibleToLocalTeam(Vector3 worldPosition)
    {
        if (GameLoop.Instance == null)
            return true;
        return GameLoop.Instance.IsCellVisibleToLocalTeam(
            GridSystem.ConvertToGridCoords(worldPosition)
        );
    }
}
