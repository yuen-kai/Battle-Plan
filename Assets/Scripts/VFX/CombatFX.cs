using UnityEngine;

/// <summary>
/// The moments that are not abilities (ArtDirection §8.5, §9.4): a round landing on a unit, a
/// round landing on a wall, a unit dying, a unit deploying.
///
/// Every entry point is presentation-only and safe to call on every peer. Nothing here reads or
/// writes simulation state, spawns a NetworkObject, or sends an RPC — the callers are already
/// places the engine runs on all clients (a replicated health change, a projectile despawn), so
/// the effect derives from an authoritative event without adding one.
/// </summary>
public static class CombatFX
{
    // Bullet impact (§8.5).
    public const float BulletShockwaveRadius = 0.4f;
    public const float BulletShockwaveDuration = 0.18f;
    public const float BulletFlashDuration = 0.1f;
    public const float BulletFlashStrength = 1.4f;

    // Heavy hit — anything at or over 40% of max health, matching Health's own severity split.
    public const float HeavyFlashDuration = 0.16f;
    public const float HeavyFlashStrength = 2f;

    // Unit death (§9.4).
    public const float DeathShockwaveRadius = 1.3f;
    public const float DeathShockwaveDuration = 0.35f;
    public const float DeathFlashDuration = 0.1f;
    public const float DeathFlashStrength = 2.5f;
    public const float DeathCoreDiameter = 0.9f;
    public const float DeathCoreSeconds = 0.08f;

    // Unit spawn (§9.4).
    public const float SpawnRingDiameter = 1.4f;
    public const float SpawnRingSeconds = 0.3f;

    /// <summary>
    /// A round connecting with a unit. Called from the replicated health change, so it fires on
    /// every peer at the same moment the damage lands and needs no RPC of its own.
    /// </summary>
    public static void BulletImpact(GameObject victim, bool heavy)
    {
        if (victim == null)
            return;

        Vector3 point = victim.transform.position + Vector3.up * 0.6f;
        int victimTeam = FXPalette.TeamIndexOf(victim);
        // The shot is painted from the shooter's side, and whoever shot this unit is on the
        // other team from it.
        int shooterTeam = GameLoop.GetEnemyTeamIndex(victimTeam);

        ParticleBurstFX.Sparks(
            point,
            FXPalette.TeamSrgb(shooterTeam),
            ParticleBurstFX.BulletImpactSparks
        );
        ImpactShockwave.Spawn(
            point,
            FXPalette.TeamGlow(shooterTeam),
            BulletShockwaveRadius,
            BulletShockwaveDuration,
            withLightPop: false
        );
        HitFlash.FlashTarget(
            victim,
            heavy ? HeavyFlashDuration : BulletFlashDuration,
            heavy ? HeavyFlashStrength : BulletFlashStrength
        );
    }

    /// <summary>
    /// A round stopping against cover. No shockwave — a ring on the floor next to a wall reads as
    /// an area effect, and nothing happened here except a chip of block coming off.
    ///
    /// This is the one impact that lands ON a dark host rather than near one, so §9.2.1's contour
    /// inversion applies: the spark's trailing contour goes to paper instead of ink, and the chips
    /// take --bp-cover-plate rather than --bp-cover. The bible originally specified the chips in
    /// --bp-cover, which is the block's own colour and therefore invisible against it. Looking
    /// different from a floor impact is fine and in fact correct — a wall is visibly a different
    /// object, so the difference reads as material response rather than as a rule.
    /// </summary>
    public static void WallImpact(Vector3 point, int shooterTeamIndex)
    {
        ParticleBurstFX.Sparks(
            point,
            FXPalette.TeamSrgb(shooterTeamIndex),
            ParticleBurstFX.WallImpactSparks,
            coolTo: FXPalette.PaperSrgb
        );
        ParticleBurstFX.Debris(point, ParticleBurstFX.WallImpactDebris);
    }

    /// <summary>
    /// A unit going down (§9.4). Deliberately no corpse and no ground stain: a dead unit's cell
    /// must never read as occupied.
    ///
    /// The 0.22 s squash the bible also asks for is not implemented — Health deactivates the
    /// GameObject on the same frame on clients and one frame later on the host, so there is no
    /// body left to squash. That needs a despawn delay, which is a gameplay timing change and not
    /// this layer's to make. Everything spawned here is detached from the unit so it outlives it.
    /// </summary>
    public static void UnitDeath(GameObject unit)
    {
        if (unit == null)
            return;

        Vector3 position = unit.transform.position;
        int teamIndex = FXPalette.TeamIndexOf(unit);
        Color teamGlow = FXPalette.TeamGlow(teamIndex);

        HitFlash.FlashTarget(unit, DeathFlashDuration, DeathFlashStrength);
        ImpactShockwave.Spawn(
            position,
            teamGlow,
            DeathShockwaveRadius,
            DeathShockwaveDuration
        );
        FXImpactSprite.Core(
            position + Vector3.up * 0.7f,
            DeathCoreDiameter,
            // Team-coloured, not white. A death has to be attributable at a glance, and on a pale
            // board a white pop is both unreadable and anonymous.
            teamGlow,
            DeathCoreSeconds
        );
        ParticleBurstFX.Sparks(
            position + Vector3.up * 0.7f,
            FXPalette.TeamSrgb(teamIndex),
            ParticleBurstFX.DeathSparks,
            speedMin: 2f,
            speedMax: 5f,
            lifetimeMin: 0.18f,
            lifetimeMax: 0.4f
        );
    }

    /// <summary>
    /// A unit arriving on the board (§9.4): a ring opening under its feet in its team colour.
    ///
    /// The 0.35 s opacity fade the bible pairs with this is not implemented — the character
    /// materials are opaque and giving them a transparent variant belongs to whoever owns
    /// Assets/Materials/Characters. The ring alone still reads as an arrival.
    /// </summary>
    public static void UnitSpawn(GameObject unit)
    {
        if (unit == null)
            return;

        int teamIndex = FXPalette.TeamIndexOf(unit);
        GroundTelegraph ring = GroundTelegraph.Create(
            unit.transform.position,
            SpawnRingDiameter,
            ringWidth: 0.3f,
            glow: FXPalette.TeamGlow(teamIndex),
            intensity: 2f
        );
        if (ring == null)
            return;

        ring.SetDiameter(0f);
        // Sequenced on FXRunner, not started as two coroutines on the ring: FadeAndDestroy
        // destroys the object when it finishes, so running it alongside AnimateDiameter races the
        // opening animation and usually wins.
        FXRunner.Run(OpenThenClose(ring));
    }

    private static System.Collections.IEnumerator OpenThenClose(GroundTelegraph ring)
    {
        yield return ring.AnimateDiameter(0f, SpawnRingDiameter, SpawnRingSeconds);
        if (ring != null)
            yield return ring.FadeAndDestroy(0.12f);
    }
}
