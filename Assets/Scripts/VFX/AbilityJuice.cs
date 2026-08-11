using UnityEngine;

/// <summary>
/// Shared vocabulary for the ability juice layer.
/// <para>
/// The eight effects that make an ability land — anticipation, core flash, shockwave, debris, the
/// victim's reaction, the camera's reaction, the aftermath, and the damage read — are built and
/// tuned independently, so they agree on these types rather than on each other's internals.
/// </para>
/// </summary>
public static class AbilityJuice
{
    /// <summary>
    /// White-hot centre reserved for energy cores. Bright enough to clear the scene's bloom
    /// threshold of 1.8 so it blooms rather than merely being white.
    /// </summary>
    public static readonly Color HotCore = new(6f, 5.6f, 5.2f, 1f);

    /// <summary>Alarm yellow: the board's "this is happening to you now" colour.</summary>
    public static readonly Color Alarm = new(1f, 0.77f, 0f, 1f);

    /// <summary>Scales <paramref name="color"/> into HDR so additive layers bloom.</summary>
    public static Color Hot(Color color, float intensity)
    {
        return new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
    }
}

/// <summary>Shape an ability's telegraph takes on the ground during its wind-up.</summary>
public enum WindupShape
{
    /// <summary>A single square: something is about to land exactly here.</summary>
    Point,

    /// <summary>A filled area: everything inside is about to be hit.</summary>
    Disc,

    /// <summary>A lane from caster to target: anything crossing is about to be hit.</summary>
    Line,

    /// <summary>No ground mark; the caster itself is what winds up.</summary>
    Self,
}

/// <summary>What an impact leaves behind once the flash is gone.</summary>
public enum AftermathKind
{
    Scorch,
    Dust,
    Embers,
    Smoke,
}

/// <summary>How loudly a damage number should read.</summary>
public enum DamageTone
{
    Normal,
    Heavy,
    Critical,
}
