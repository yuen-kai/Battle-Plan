using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The joint set the generated characters are rigged on, shared by <see cref="CharacterBuilder"/>,
/// which binds geometry to it, and <see cref="CharacterAnimationBuilder"/>, which writes curves
/// against it. Both need the same names in the same hierarchy or a clip drives nothing, so the
/// hierarchy lives here rather than twice.
///
/// <para>
/// Thirteen joints, which is fewer than a skeleton and deliberately so. Every vertex is locked to
/// exactly one of them at weight one — the bodies are interpenetrating rounded solids, so a limb
/// rotating rigidly inside the mass it is pushed into has nothing to tear, and a blended one would
/// only pinch where two solids overlap. What is missing against a real rig is knees: a unit is
/// about forty pixels tall on the board and a shin that breaks from a thigh is not one of the
/// things that reads at that size, so the whole leg swings from the hip with an articulated boot
/// under it.
/// </para>
///
/// <para>
/// The last two are not joints of the body. <see cref="CharacterBone.Weapon"/> sits on the weapon's
/// own axis, resting turned so its local Y runs straight down the barrel, and
/// <see cref="CharacterBone.Rotor"/> hangs off it at the same point resting square. Geometry that
/// has to turn about the bore — Salvo's six barrels, and nothing else on the roster yet — binds to
/// the Rotor, and clips spin it about its local Y.
/// </para>
///
/// <para>
/// The pair exists rather than one aligned pivot because a rotation curve is an absolute local
/// rotation and not an offset from rest. A clip keying zero on a bone that rests aimed down a
/// barrel does not leave it alone: it snaps it square, which tears the barrels off the gun and
/// leaves them hanging in the air beside it. Splitting the aim onto a parent that no clip ever
/// touches is what makes zero mean rest again — see <see cref="IsPosed"/>, which is the rule.
/// </para>
/// </summary>
public enum CharacterBone
{
    Hips,
    Torso,
    Head,
    ShoulderL,
    ForearmL,
    HandL,
    ShoulderR,
    ForearmR,
    HandR,
    ThighL,
    FootL,
    ThighR,
    FootR,
    Weapon,
    Rotor,
}

public static class CharacterSkeleton
{
    /// <summary>The node the rig hangs off, which is also the node the Animator sits on.</summary>
    public const string ModelRootName = "Body";

    public static readonly CharacterBone[] All = (CharacterBone[])System.Enum.GetValues(typeof(CharacterBone));

    private static readonly Dictionary<CharacterBone, CharacterBone> Parents = new()
    {
        { CharacterBone.Torso, CharacterBone.Hips },
        { CharacterBone.Head, CharacterBone.Torso },
        { CharacterBone.ShoulderL, CharacterBone.Torso },
        { CharacterBone.ForearmL, CharacterBone.ShoulderL },
        { CharacterBone.HandL, CharacterBone.ForearmL },
        { CharacterBone.ShoulderR, CharacterBone.Torso },
        { CharacterBone.ForearmR, CharacterBone.ShoulderR },
        { CharacterBone.HandR, CharacterBone.ForearmR },
        { CharacterBone.ThighL, CharacterBone.Hips },
        { CharacterBone.FootL, CharacterBone.ThighL },
        { CharacterBone.ThighR, CharacterBone.Hips },
        { CharacterBone.FootR, CharacterBone.ThighR },
        { CharacterBone.Weapon, CharacterBone.HandR },
        { CharacterBone.Rotor, CharacterBone.Weapon },
    };

    /// <summary>
    /// Whether a clip may drive this bone's rotation, which is true of every bone that rests square
    /// and false of the one that does not. Euler curves are absolute: a clip keying a bone that
    /// rests turned discards that turn rather than adding to it.
    /// </summary>
    public static bool IsPosed(CharacterBone bone) => bone != CharacterBone.Weapon;

    public static bool IsRoot(CharacterBone bone) => bone == CharacterBone.Hips;

    public static CharacterBone Parent(CharacterBone bone) => Parents[bone];

    /// <summary>
    /// The bone's path from the model root, which is what an <c>AnimationClip</c> binds against
    /// and the reason no joint may be renamed without rewriting every clip.
    /// </summary>
    public static string Path(CharacterBone bone)
    {
        List<string> names = new() { bone.ToString() };
        while (!IsRoot(bone))
        {
            bone = Parent(bone);
            names.Add(bone.ToString());
        }
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>The arm chain on one side, shoulder outward.</summary>
    public static CharacterBone[] Arm(bool left) =>
        left
            ? new[] { CharacterBone.ShoulderL, CharacterBone.ForearmL, CharacterBone.HandL }
            : new[] { CharacterBone.ShoulderR, CharacterBone.ForearmR, CharacterBone.HandR };

    /// <summary>The leg chain on one side, hip downward.</summary>
    public static CharacterBone[] Leg(bool left) =>
        left
            ? new[] { CharacterBone.ThighL, CharacterBone.FootL }
            : new[] { CharacterBone.ThighR, CharacterBone.FootR };

    public static IEnumerable<CharacterBone> Mirrored(CharacterBone bone) =>
        All.Where(other => other.ToString() == Flip(bone.ToString()));

    private static string Flip(string name) =>
        name.EndsWith("L") ? name[..^1] + "R" : name.EndsWith("R") ? name[..^1] + "L" : name;
}
