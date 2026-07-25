using UnityEngine;

/// <summary>
/// One-shot Shuriken bursts: sparks, debris chips and dust. Every count, speed, size and lifetime
/// below is quoted from ArtDirection §8.5 and §9.3 and lives in a named constant so a change is a
/// diff rather than an inspector archaeology exercise.
///
/// There is no VFX Graph in this project and there must not be (§14). These are plain particle
/// systems on Resources prefabs, tuned per call and destroyed when they finish.
///
/// The HDR magnitude lives on the material (_TintColor × _Intensity) because particle vertex
/// colours are only eight bits per channel; the gradient passed here therefore carries the *hue*
/// ramp — white-hot core to team colour to transparent — and nothing brighter than white.
/// </summary>
public static class ParticleBurstFX
{
    // Bullet impact on a unit (§8.5).
    public const int BulletImpactSparks = 5;
    public const float BulletSparkSpeedMin = 3f;
    public const float BulletSparkSpeedMax = 6f;
    public const float BulletSparkLifetimeMin = 0.12f;
    public const float BulletSparkLifetimeMax = 0.22f;
    public const float BulletSparkSize = 0.05f;
    public const float BulletSparkGravity = 2f;

    // Bullet impact on a wall (§8.5).
    public const int WallImpactSparks = 4;
    public const int WallImpactDebris = 3;
    public const float WallDebrisSize = 0.06f;
    public const float WallDebrisGravity = 3f;
    public const float WallDebrisLifetime = 0.5f;

    // Area Lock impact frame (§9.3).
    public const int AreaLockSparks = 18;
    public const float AreaLockSparkSpeedMin = 6f;
    public const float AreaLockSparkSpeedMax = 14f;
    public const float AreaLockSparkLifetimeMin = 0.15f;
    public const float AreaLockSparkLifetimeMax = 0.35f;

    // Grenade detonation (§9.3).
    public const int GrenadeSparks = 24;
    public const int GrenadeDebris = 10;
    public const float GrenadeDebrisSize = 0.08f;
    public const float GrenadeDebrisGravity = 4f;
    public const int GrenadeDustParticles = 12;
    public const float GrenadeDustSizeStart = 1.6f;
    public const float GrenadeDustSizeEnd = 2.6f;
    public const float GrenadeDustLifetime = 1.2f;
    public const float GrenadeDustRise = 0.4f;

    // Pogo launch and landing dust (§9.3).
    public const int PogoLaunchDust = 8;
    public const int PogoLandingDust = 12;
    public const float PogoDustSizeMin = 0.3f;
    public const float PogoDustSizeMax = 0.6f;
    public const float PogoDustLifetime = 0.45f;

    // Shield deflection (§9.3).
    public const int ShieldBlockSparks = 6;

    // Unit death (§9.4).
    public const int DeathSparks = 10;

    /// <summary>
    /// A radial spark burst. <paramref name="teamSrgb"/> opens the ramp and the burst always ends
    /// transparent. <paramref name="coolTo"/> is the value it decays through on the way, and it is
    /// the contour-inversion knob from §9.2.1: ink for a spark thrown against the pale deck, paper
    /// for one thrown against a cover face. Null takes ink, which is the common case.
    /// </summary>
    public static void Sparks(
        Vector3 position,
        Color teamSrgb,
        int count,
        float speedMin = BulletSparkSpeedMin,
        float speedMax = BulletSparkSpeedMax,
        float lifetimeMin = BulletSparkLifetimeMin,
        float lifetimeMax = BulletSparkLifetimeMax,
        float size = BulletSparkSize,
        float gravity = BulletSparkGravity,
        Color? coolTo = null
    )
    {
        ParticleSystem system = Prepare(FXAssets.SparkBurst, position, lifetimeMax + 0.4f);
        if (system == null)
            return;

        ParticleSystem.MainModule main = system.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = size;
        main.gravityModifier = gravity;

        ApplyHeatRamp(system, teamSrgb, coolTo ?? FXPalette.InkSrgb);
        system.Emit(count);
    }

    /// <summary>Solid dark chips thrown off a wall or a detonation. Non-emissive by contract.</summary>
    public static void Debris(
        Vector3 position,
        int count,
        float size = WallDebrisSize,
        float gravity = WallDebrisGravity,
        float lifetime = WallDebrisLifetime,
        float speed = 3f
    )
    {
        ParticleSystem system = Prepare(FXAssets.DebrisBurst, position, lifetime + 0.4f);
        if (system == null)
            return;

        ParticleSystem.MainModule main = system.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSize = size;
        main.gravityModifier = gravity;
        system.Emit(count);
    }

    /// <summary>Slow rising dust. The aftermath beat for anything that hits the deck.</summary>
    public static void Dust(
        Vector3 position,
        int count,
        float sizeStart = PogoDustSizeMin,
        float sizeEnd = PogoDustSizeMax,
        float lifetime = PogoDustLifetime,
        float rise = 0.25f,
        float spread = 0.6f
    )
    {
        ParticleSystem system = Prepare(FXAssets.DustBurst, position, lifetime + 0.6f);
        if (system == null)
            return;

        ParticleSystem.MainModule main = system.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeStart, sizeEnd);

        ParticleSystem.ShapeModule shape = system.shape;
        shape.radius = Mathf.Max(0.01f, spread);
        system.Emit(count);
    }

    private static ParticleSystem Prepare(string resourcePath, Vector3 position, float teardown)
    {
        GameObject spawned = FXAssets.SpawnTimed(
            resourcePath,
            position,
            Quaternion.identity,
            teardown
        );
        return spawned == null ? null : spawned.GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// White-hot core → team colour → transparent, the ramp §8.5 specifies for every spark in the
    /// game. Keys are sRGB and stay inside 0–1; the material supplies the HDR headroom.
    /// </summary>
    private static void ApplyHeatRamp(ParticleSystem system, Color teamSrgb, Color coolTo)
    {
        // §8.5: team -hi → contour → transparent. Note the direction — sparks DARKEN as they die
        // against the deck. The dark board's ramp started white-hot and cooled to team colour,
        // which is what a hot fragment does against black. On a pale deck the same fragment is
        // only visible while it is darker than the concrete, so it cools to ink instead — or to
        // paper, when the host it is thrown against is itself dark.
        Gradient ramp = new();
        ramp.SetKeys(
            new[]
            {
                new GradientColorKey(teamSrgb, 0f),
                new GradientColorKey(teamSrgb, 0.4f),
                new GradientColorKey(coolTo, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            }
        );

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(ramp);
    }
}
