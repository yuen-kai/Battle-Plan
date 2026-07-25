using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The three-beat sequences for all five abilities (ArtDirection §9.3).
///
/// Every combat moment gets anticipation → impact frame → aftermath, and none of these three is
/// optional. Anticipation is what makes the moment readable *in advance*, and it deliberately
/// reuses windows the game already has — the dodge phase, the grenade's one-second arc, Area
/// Lock's armed line — rather than adding delay. Nothing in this file changes when anything
/// happens; it only makes what happens visible.
///
/// THE FOOTPRINT RULE (§9.2) is the constraint that matters most here. Ground rings match the
/// ability's rules footprint *exactly*, and nothing exceeds 1.35× it on any frame. An effect
/// bigger than its rules footprint reads as generosity and plays as a bug report, so the
/// footprint constants below are derived from the ability's own rules values and passed to
/// GroundTelegraph, which clamps against them.
///
/// Everything is client-local presentation. No call here touches simulation state, spawns a
/// NetworkObject, or sends an RPC; each entry point is invoked from a place the engine already
/// runs on every peer.
/// </summary>
public static class AbilityFX
{
    // =====================================================================================
    // CONSTANTS — every timing and dimension in the FX layer. Colours come from FXPalette,
    // which is the single source of truth for the palette; there are no colour literals here.
    // Sources are ArtDirection §9.2 (footprints) and §9.3 (per-ability specs).
    // =====================================================================================

    /// <summary>One board cell in world units. Mirrors GameLoop.cellSize.</summary>
    public const float CellSize = 2.7f;

    // === The darkening flash (§9.2) ===

    /// <summary>Near-hard edge. A soft-edged darkening reads as a shadow the board cast, not as
    /// something that just happened to it.</summary>
    public const float DarkeningFlashEdgeSoftness = 0.12f;

    /// <summary>
    /// The diameter the darkening flash OPENS AT, rather than growing to from a dot.
    ///
    /// §9.4 asks for scale 0 → 3.2, and the first two thirds of that ramp turned out to be
    /// unobservable. The disc draws on the telegraph plane at y 0.09; a unit's <c>BasePuck</c> is an
    /// opaque 1.35-diameter slab spanning y 0.02–0.10, so it is drawn above the disc and hides
    /// everything inside 1.35 world units. Any diameter under that is occluded by the victim itself,
    /// which is why an impact frame that measured correct in every property read as one bright blob:
    /// the disc was only clear of the puck once its coverage had already decayed past legibility.
    ///
    /// 2.2 clears the puck by 0.425 units all round on the first frame, so the darkening is a band
    /// from the outset instead of a hairline that arrives late. The alternative — lifting the
    /// telegraph plane above the puck so the disc darkens it too — would have been a bigger dark
    /// shape, but <c>YAbilityTelegraph</c> is shared by every telegraph in the game and re-sorting
    /// all of them against unit pucks is not a change an impact frame gets to make.
    /// </summary>
    public const float DarkeningFlashStartDiameter = 2.2f;

    /// <summary>
    /// Fraction of the flash spent opening to full diameter. Growth has to finish inside the hold
    /// below, because size and darkness ramping in opposition is what made the old schedule
    /// illegible — the disc was never simultaneously wide enough to see and dark enough to read.
    /// </summary>
    private const float DarkeningFlashGrowthFraction = 0.5f;

    /// <summary>
    /// Fraction of the flash held at full coverage before the fade starts. The old schedule began
    /// fading from t=0, so peak darkness landed while the disc was still inside the victim's puck.
    /// </summary>
    private const float DarkeningFlashHoldFraction = 0.45f;

    /// <summary>§9.4: alpha 0.55 → 0 across the impact frame, opening to 3.2.</summary>
    public const float AreaLockDarkeningDiameter = 3.2f;
    public const float AreaLockDarkeningCoverage = 0.55f;
    public const float AreaLockDarkeningSeconds = 0.08f;

    /// <summary>§9.4: scale 0 → 4.32 (the exact blast radius), alpha 0.6 → 0.</summary>
    public const float GrenadeDarkeningCoverage = 0.6f;

    /// <summary>Lightest of the three. A smoke deploy is a placement, not a hit.</summary>
    public const float SmokeDarkeningCoverage = 0.4f;

    // --- Smoke Screen (Commander, 3 × 3 cells) -------------------------------------------
    /// <summary>3 × 3 cells = 8.10 world units. The ring is this exactly.</summary>
    public const float SmokeFootprintDiameter = 3f * CellSize;
    public const float SmokeTelegraphRingWidth = 0.14f;
    public const float SmokeTelegraphPulseSpeed = 2f;
    public const float SmokeImpactSeconds = 0.08f;
    public const float SmokeCoreDiameter = 1.6f;
    public const float SmokeShockwaveRadius = SmokeFootprintDiameter * 0.5f;
    public const float SmokeShockwaveDuration = 0.5f;
    public const float SmokeTelegraphFadeSeconds = 0.2f;

    // --- Pogo (PogoRider) -----------------------------------------------------------------
    public const float PogoLaunchRingStart = 3.2f;
    public const float PogoLaunchRingEnd = 0.8f;
    public const float PogoAnticipationSeconds = 0.3f;
    public const float PogoLaunchRingWidth = 0.25f;
    public const float PogoLaunchShockwaveRadius = 1.6f;
    public const float PogoLaunchShockwaveDuration = 0.28f;
    public const float PogoLandingShockwaveRadius = 2.2f;
    public const float PogoLandingShockwaveDuration = 0.38f;
    public const float PogoShakeDuration = 0.18f;
    public const float PogoShakeIntensity = 0.06f;
    public const float BackstabFlashDuration = 0.16f;
    public const float BackstabFlashStrength = 2f;
    public const float BackstabSlashSize = 1.1f;
    public const float BackstabSlashSeconds = 0.18f;

    // --- Shield Rush (Shotgunner) — see ShieldFX for the hold and deflection beats --------
    public const float ShieldRaiseShockwaveRadius = 1.5f;
    public const float ShieldRaiseShockwaveDuration = 0.3f;

    // --- Area Lock (Sniper) — the flagship -------------------------------------------------
    public const float AreaLockBeamCoreWidth = 0.05f;
    public const float AreaLockBeamGlowWidth = 0.34f;
    public const float AreaLockPulseSpeed = 1.1f;
    public const float AreaLockPulseAmount = 0.3f;
    public const float AreaLockRushWidthMultiplier = 5f;

    /// <summary>Per-cell telegraph disc. Tile-crisp, not a continuous strip.</summary>
    public const float AreaLockCellDiscDiameter = 1.2f;
    public const float AreaLockCellRingWidth = 0.2f;
    public const float AreaLockCellIntensity = 1.4f;
    public const float AreaLockCellPulseSpeed = 1.6f;

    /// <summary>Ceiling on telegraph discs, so a full-board line cannot flood the draw list.</summary>
    public const int AreaLockMaxCells = 16;

    public const float AreaLockImpactFlashDuration = 0.18f;
    public const float AreaLockImpactFlashStrength = 2f;
    public const float AreaLockShockwaveRadius = 3.2f;
    public const float AreaLockShockwaveDuration = 0.55f;
    public const float AreaLockCoreDiameter = 1.8f;

    /// <summary>Three frames at 60 fps — the whole trick of the impact frame.</summary>
    public const float AreaLockCoreSeconds = 0.05f;
    public const float AreaLockShakeDuration = 0.28f;
    public const float AreaLockShakeIntensity = 0.14f;
    public const float AreaLockHitstopSeconds = 0.06f;
    public const float AreaLockHitstopTimeScale = 0.15f;
    public const float AreaLockBeamFadeSeconds = 0.25f;
    public const float AreaLockTelegraphFadeSeconds = 0.45f;

    // --- Grenade (Soldier, 1.6-cell radius) -------------------------------------------------
    /// <summary>1.6-cell radius = 4.32 world. The ring is 8.64 across, exactly.</summary>
    public const float GrenadeFootprintRadius = 1.6f * CellSize;
    public const float GrenadeFootprintDiameter = GrenadeFootprintRadius * 2f;
    public const float GrenadeTelegraphRingWidth = 0.16f;
    public const float GrenadeTelegraphPulseStart = 1.2f;
    public const float GrenadeTelegraphPulseEnd = 4f;
    public const float GrenadeImpactSeconds = 0.08f;
    public const float GrenadeCoreDiameter = 2.2f;
    public const float GrenadeShockwaveDuration = 0.55f;
    public const float GrenadeShakeDuration = 0.3f;
    public const float GrenadeShakeIntensity = 0.12f;
    public const float GrenadeTelegraphFadeSeconds = 0.25f;

    // =====================================================================================
    // Smoke Screen
    // =====================================================================================

    /// <summary>
    /// Anticipation: the canister is in the air and the ground already shows the exact 3 × 3 the
    /// screen will occupy. Held for the throw, then faded — the puffs take over from here.
    /// </summary>
    public static void SmokeThrow(Vector3 landing, float throwSeconds)
    {
        GroundTelegraph telegraph = GroundTelegraph.Create(
            landing,
            SmokeFootprintDiameter,
            SmokeTelegraphRingWidth,
            // --bp-cover as a multiply tint: the screen's footprint is scored into the deck for the
            // whole throw, and it has to survive being the counterplay a player reads it as.
            FXPalette.Cover,
            intensity: 2f,
            pulseSpeed: SmokeTelegraphPulseSpeed
        );
        if (telegraph == null)
            return;

        FXRunner.Run(FadeAfter(telegraph, throwSeconds, SmokeTelegraphFadeSeconds));
    }

    /// <summary>Impact frame: the canister cracks. A darkening disc on the exact footprint, then a
    /// pale core — the screen is the one effect whose identity is a light mass, so its core is the
    /// one core that stays pale rather than going saturated.</summary>
    public static void SmokeBurst(Vector3 center)
    {
        DarkeningFlash(
            center,
            SmokeShockwaveRadius * 2f,
            SmokeDarkeningCoverage,
            SmokeImpactSeconds
        );
        FXImpactSprite.Core(
            center + Vector3.up * 0.5f,
            SmokeCoreDiameter,
            FXPalette.Deck4,
            SmokeImpactSeconds
        );
        ImpactShockwave.Spawn(
            center,
            FXPalette.Cover,
            SmokeShockwaveRadius,
            SmokeShockwaveDuration
        );
    }

    // =====================================================================================
    // Pogo
    // =====================================================================================

    /// <summary>
    /// Anticipation and launch, fired together at the top of the jump because the rider has no
    /// pre-jump window and adding one would be a gameplay timing change. The ring contracting
    /// under the launch cell while the rider climbs still reads as the ground giving up its
    /// energy, and the shockwave lands on the same frame the rider leaves.
    /// </summary>
    public static void PogoLaunch(Vector3 launchPosition, int teamIndex)
    {
        Color teamGlow = FXPalette.TeamGlow(teamIndex);

        GroundTelegraph ring = GroundTelegraph.Create(
            launchPosition,
            PogoLaunchRingStart,
            PogoLaunchRingWidth,
            teamGlow,
            intensity: 2f
        );
        if (ring != null)
        {
            FXRunner.Run(
                Sequence(
                    ring.AnimateDiameter(
                        PogoLaunchRingStart,
                        PogoLaunchRingEnd,
                        PogoAnticipationSeconds
                    ),
                    ring.FadeAndDestroy(0.15f)
                )
            );
        }

        ImpactShockwave.Spawn(
            launchPosition,
            teamGlow,
            PogoLaunchShockwaveRadius,
            PogoLaunchShockwaveDuration
        );
        ParticleBurstFX.Dust(launchPosition, ParticleBurstFX.PogoLaunchDust);
    }

    /// <summary>Landing slam. Synced to the frame the rider becomes shootable again.</summary>
    public static void PogoLanding(Vector3 landingPosition, int teamIndex)
    {
        ImpactShockwave.Spawn(
            landingPosition,
            FXPalette.TeamGlow(teamIndex),
            PogoLandingShockwaveRadius,
            PogoLandingShockwaveDuration
        );
        ParticleBurstFX.Dust(landingPosition, ParticleBurstFX.PogoLandingDust, spread: 0.9f);
        Shake(PogoShakeDuration, PogoShakeIntensity);
    }

    /// <summary>
    /// The slash over a heavily hit unit (§9.3, Pogo backstab).
    ///
    /// True backstab detection lives on the server inside Bullet.CheckBackstab and is never
    /// replicated, so this fires on the severity split Health already computes — a hit at or over
    /// 40% of max health. Every real backstab clears that bar; a few very large ordinary hits
    /// also will. Making it exact needs a replicated flag, which is a networking change.
    /// </summary>
    public static void HeavyHitSlash(GameObject victim)
    {
        if (victim == null)
            return;

        HitFlash.FlashTarget(victim, BackstabFlashDuration, BackstabFlashStrength);
        FXImpactSprite.Slash(
            victim.transform.position + Vector3.up * 0.9f,
            BackstabSlashSize,
            FXPalette.Red,
            BackstabSlashSeconds
        );
    }

    // =====================================================================================
    // Area Lock
    // =====================================================================================

    /// <summary>
    /// Arms the line: a disc on the centre of every cell the ray crosses. Per-cell discs rather
    /// than one continuous strip, because the game is tile-crisp and the telegraph has to be too
    /// — a player must be able to count the cells they need to leave.
    ///
    /// Fog: this deliberately pierces it. §9.5 rules that telegraphs crossing fogged cells still
    /// read, and Area Lock is the highest-damage ability in the game — hiding its line would make
    /// the counterplay unavailable to exactly the player who needs it. The caster is separately
    /// force-revealed by AreaLock.ExecuteAbility for the same reason.
    /// </summary>
    public static List<GroundTelegraph> AreaLockArm(Vector3 start, Vector3 end)
    {
        List<GroundTelegraph> discs = new();

        foreach (Vector2Int cell in CellsAlong(start, end, AreaLockMaxCells))
        {
            GroundTelegraph disc = GroundTelegraph.Create(
                GameLoop.gridCoordToWorld(cell),
                AreaLockCellDiscDiameter,
                AreaLockCellRingWidth,
                // Threat red for both sides, matching the beam. Area Lock is the one ability whose
                // telegraph is not a team signal: it means "leave this line" to everyone.
                FXPalette.Red,
                AreaLockCellIntensity,
                AreaLockCellPulseSpeed
            );
            if (disc != null)
                discs.Add(disc);
        }
        return discs;
    }

    /// <summary>
    /// The darkening flash: FIELD DAY's impact device, and the inverse of the white flash the dark
    /// board used (§9.2). A filled multiply disc in ink briefly takes the ground out from under the
    /// blast. On a bright board this is the single strongest impact cue available, because darkness
    /// is the scarce resource — there are roughly 2.4 stops of headroom below the deck and about
    /// 0.4 above it, so a darkening has six times the contrast range a brightening does.
    ///
    /// Uses the shared GroundGlow prefab, whose material is Multiply for every ground element
    /// except the muzzle flash and the contested hill, so this costs no extra material and still
    /// instances with every other telegraph on screen.
    /// </summary>
    public static void DarkeningFlash(
        Vector3 position,
        float diameter,
        float peakCoverage,
        float seconds
    )
    {
        GroundTelegraph flash = GroundTelegraph.Create(
            position,
            diameter,
            // Ring width 1 is a filled disc rather than an annulus. An impact frame is a hole in
            // the board, not a ring on it — the ring is the aftermath's job.
            ringWidth: 1f,
            FXPalette.DarkeningFlashTint,
            intensity: peakCoverage,
            pulseSpeed: 0f,
            edgeSoftness: DarkeningFlashEdgeSoftness
        );
        if (flash == null)
            return;

        // Min so a caller with a footprint smaller than a unit's puck still opens at its own size
        // rather than being inflated past its rules footprint.
        float startDiameter = Mathf.Min(DarkeningFlashStartDiameter, diameter);
        flash.SetDiameter(startDiameter);
        FXRunner.Run(OpenAndFade(flash, startDiameter, diameter, seconds));
    }

    private static IEnumerator OpenAndFade(
        GroundTelegraph flash,
        float startDiameter,
        float diameter,
        float seconds
    )
    {
        // Opening and fading overlap but no longer start together. The disc opens over the first
        // half and holds full coverage across all of that, so it is at full extent AND full darkness
        // before anything fades — the impact frame reads as a hole in the board rather than as a dot
        // that dims on its way out.
        FXRunner.Run(
            flash.AnimateDiameter(startDiameter, diameter, seconds * DarkeningFlashGrowthFraction)
        );

        float hold = seconds * DarkeningFlashHoldFraction;
        if (hold > 0f)
            yield return new WaitForSeconds(hold);
        yield return flash.FadeAndDestroy(seconds - hold);
    }

    /// <summary>
    /// The impact frame. <paramref name="lethal"/> is resolved on the server before the damage is
    /// applied and passed down, because a client cannot know whether the shot killed until the
    /// health value replicates — by which point the freeze would be late.
    /// </summary>
    public static void AreaLockImpact(Vector3 hitPoint, GameObject victim, bool lethal)
    {
        HitFlash.FlashTarget(victim, AreaLockImpactFlashDuration, AreaLockImpactFlashStrength);
        DarkeningFlash(
            hitPoint,
            AreaLockDarkeningDiameter,
            AreaLockDarkeningCoverage,
            AreaLockDarkeningSeconds
        );
        ImpactShockwave.Spawn(
            hitPoint,
            FXPalette.Red,
            AreaLockShockwaveRadius,
            AreaLockShockwaveDuration
        );
        FXImpactSprite.Core(hitPoint, AreaLockCoreDiameter, FXPalette.Core, AreaLockCoreSeconds);
        ParticleBurstFX.Sparks(
            hitPoint,
            FXPalette.RedSrgb,
            ParticleBurstFX.AreaLockSparks,
            ParticleBurstFX.AreaLockSparkSpeedMin,
            ParticleBurstFX.AreaLockSparkSpeedMax,
            ParticleBurstFX.AreaLockSparkLifetimeMin,
            ParticleBurstFX.AreaLockSparkLifetimeMax
        );
        Shake(AreaLockShakeDuration, AreaLockShakeIntensity);

        // Lethal only. FXHitstop captures and restores the prior timeScale and refuses outright
        // in dev mode, so an automated run at six-times speed never sees this (risk item 2).
        if (lethal)
            FXHitstop.Request(AreaLockHitstopSeconds, AreaLockHitstopTimeScale);
    }

    /// <summary>Aftermath: the discs decay over 0.45 s while the beam fades.</summary>
    public static void AreaLockRelease(List<GroundTelegraph> discs)
    {
        if (discs == null)
            return;
        foreach (GroundTelegraph disc in discs)
        {
            if (disc != null)
                disc.FadeOut(AreaLockTelegraphFadeSeconds);
        }
        discs.Clear();
    }

    // =====================================================================================
    // Grenade
    // =====================================================================================

    /// <summary>
    /// Anticipation: the landing cell wears the blast radius for the whole one-second arc, with
    /// the pulse rate ramping as the fuse runs out. This doubles as the dodge telegraph, so it is
    /// both the juice and the counterplay — which is exactly why the ring is the *exact* rules
    /// footprint and never a frame larger.
    /// </summary>
    public static GroundTelegraph GrenadeTelegraph(Vector3 landing, float fuseSeconds)
    {
        GroundTelegraph telegraph = GroundTelegraph.Create(
            landing,
            GrenadeFootprintDiameter,
            GrenadeTelegraphRingWidth,
            // amber-deep, not amber: the telegraph is a darkening ring scored into the deck for a
            // full second, and at that duration a saturated ring would out-read the detonation.
            FXPalette.AmberDeep,
            intensity: 1f,
            pulseSpeed: GrenadeTelegraphPulseStart
        );
        if (telegraph != null)
        {
            // Self-timed off the flight duration rather than cleared by the detonation, so the
            // Grenade component carries no FX state and a throw interrupted by anything at all
            // still cleans its own telegraph off the board.
            FXRunner.Run(RampThenFade(telegraph, fuseSeconds));
        }
        return telegraph;
    }

    private static IEnumerator RampThenFade(GroundTelegraph telegraph, float fuseSeconds)
    {
        yield return telegraph.RampPulse(
            GrenadeTelegraphPulseStart,
            GrenadeTelegraphPulseEnd,
            fuseSeconds
        );
        if (telegraph != null)
            yield return telegraph.FadeAndDestroy(GrenadeTelegraphFadeSeconds);
    }

    /// <summary>
    /// Impact frame and aftermath. The shockwave radius is the ability's own AreaRadius in cells
    /// so the ring cannot drift from the damage query that just ran against it.
    /// </summary>
    public static void GrenadeDetonation(Vector3 position, float areaRadiusCells)
    {
        float radius = areaRadiusCells * CellSize;

        // Order matters and is spelled out in §9.4: the ground goes dark first, then the saturated
        // core arrives into the hole it left. Reversed, the core is the brightest thing on a bright
        // board and reads as a pale smudge.
        DarkeningFlash(position, radius * 2f, GrenadeDarkeningCoverage, GrenadeImpactSeconds);
        FXImpactSprite.Core(
            position + Vector3.up * 0.4f,
            GrenadeCoreDiameter,
            // Saturated opaque amber, not white. On FIELD DAY the grenade's colour is its identity
            // and its ink contour is its contrast; a white core would have neither.
            FXPalette.Amber,
            GrenadeImpactSeconds
        );
        ImpactShockwave.Spawn(position, FXPalette.Amber, radius, GrenadeShockwaveDuration);
        ParticleBurstFX.Sparks(
            position + Vector3.up * 0.3f,
            FXPalette.AmberSrgb,
            ParticleBurstFX.GrenadeSparks,
            speedMin: 4f,
            speedMax: 11f,
            lifetimeMin: 0.15f,
            lifetimeMax: 0.35f
        );
        ParticleBurstFX.Debris(
            position + Vector3.up * 0.3f,
            ParticleBurstFX.GrenadeDebris,
            ParticleBurstFX.GrenadeDebrisSize,
            ParticleBurstFX.GrenadeDebrisGravity,
            speed: 5f
        );
        ParticleBurstFX.Dust(
            position,
            ParticleBurstFX.GrenadeDustParticles,
            ParticleBurstFX.GrenadeDustSizeStart,
            ParticleBurstFX.GrenadeDustSizeEnd,
            ParticleBurstFX.GrenadeDustLifetime,
            ParticleBurstFX.GrenadeDustRise,
            spread: radius * 0.4f
        );
        ScorchDecal.Stamp(position);
        Shake(GrenadeShakeDuration, GrenadeShakeIntensity);
    }

    // =====================================================================================
    // Shared helpers
    // =====================================================================================

    /// <summary>
    /// Every grid cell whose centre the segment passes through, walked at half-cell steps and
    /// de-duplicated in order. Cheap and exact enough for a telegraph; the ability's own raycast
    /// remains the authority on what it actually hits.
    /// </summary>
    public static List<Vector2Int> CellsAlong(Vector3 start, Vector3 end, int maxCells)
    {
        List<Vector2Int> cells = new();
        float distance = Vector3.Distance(start, end);
        if (distance <= Mathf.Epsilon)
            return cells;

        int steps = Mathf.Clamp(Mathf.CeilToInt(distance / (CellSize * 0.5f)), 1, 256);
        Vector2Int previous = new(int.MinValue, int.MinValue);
        for (int step = 0; step <= steps; step++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)step / steps);
            Vector2Int cell = GridSystem.ConvertToGridCoords(point);
            if (cell == previous || !GridSystem.IsCellInBounds(cell))
                continue;

            previous = cell;
            cells.Add(cell);
            if (cells.Count >= maxCells)
                break;
        }
        return cells;
    }

    /// <summary>
    /// Shakes this peer's own camera. Run locally rather than through CameraShakeClientRpc: the
    /// call sites are already running on every client, so a broadcast would be duplicate traffic
    /// and would double the shake on the host.
    /// </summary>
    private static void Shake(float duration, float intensity)
    {
        if (CameraEffects.Instance == null)
            return;
        CameraEffects.Instance.StartCoroutine(
            CameraEffects.Instance.CameraShake(duration, intensity)
        );
    }

    private static IEnumerator FadeAfter(GroundTelegraph telegraph, float hold, float fade)
    {
        yield return new WaitForSeconds(hold);
        if (telegraph != null)
            yield return telegraph.FadeAndDestroy(fade);
    }

    private static IEnumerator Sequence(params IEnumerator[] steps)
    {
        foreach (IEnumerator step in steps)
            yield return step;
    }
}
