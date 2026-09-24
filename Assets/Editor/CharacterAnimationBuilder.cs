using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Writes the clips and the controller for the eight characters <see cref="CharacterBuilder"/>
/// rigs, the same way that builder writes their meshes: keyframes computed in source rather than
/// curves dragged in a window nobody can diff.
///
/// <para>
/// One shared set of motions drives the whole roster, tuned per unit rather than redrawn per unit.
/// <see cref="Tuning"/> is the whole of the per-character difference — how fast a unit moves, how
/// far its limbs travel, how much it rides up and down, and whether both its hands are busy — and
/// it is what makes Blitz read as twitchy and Sentinel as heavy off the same curves. That is the
/// house answer to <c>PRODUCT.md</c>'s note that the poses and animation are meant to carry the
/// personality: the motions are the roster's, the weight is the unit's.
/// </para>
///
/// <para>
/// Three things here are contracts rather than art:
/// </para>
/// <list type="bullet">
/// <item>State names are the logical names <c>AnimationHandler</c> asks for — <c>Idle</c>,
/// <c>Moving</c>, <c>Aiming</c>, <c>Shoot</c>, <c>Dodge</c>, <c>DiveRecovery</c> — plus the unit's
/// own ability. Rename one and the handler silently stops finding it.</item>
/// <item>Curves bind against <see cref="CharacterSkeleton.Path"/>, relative to the Animator that
/// <c>CharacterBuilder</c> puts on the model root. Re-jointing the rig invalidates every clip.</item>
/// <item>Nothing here keys a bone's scale or the model root itself. <c>HitBody</c> shoves body
/// parts and <c>Helper.heightOffset</c> owns the root's height; a clip fighting either would win
/// and the unit would sink into the board.</item>
/// </list>
/// </summary>
public static class CharacterAnimationBuilder
{
    private const string ClipFolder = "Assets/Animation/Characters/";

    /// <summary>
    /// The states <c>AnimationHandler</c> maps its logical names onto. Held as constants because
    /// both this builder and the handler spell them, and a typo in either is a silent no-op.
    /// </summary>
    private const string Idle = "Idle";

    private const string Moving = "Moving";
    private const string Aiming = "Aiming";
    private const string Shoot = "Shoot";
    private const string Dodge = "Dodge";
    private const string DiveRecovery = "DiveRecovery";

    public static string ControllerPath(string unit) => ClipFolder + unit + ".controller";

    private static string ClipPath(string unit, string state) => $"{ClipFolder}{unit}_{state}.anim";

    /// <summary>
    /// How one character carries the shared motions.
    ///
    /// <para>
    /// <c>Tempo</c> scales every cycle's length, so below one is a slower unit. <c>Swing</c> scales
    /// limb travel and <c>Bounce</c> scales vertical travel — a heavy unit swings less and rides
    /// lower, not merely slower, which is the difference between a big character and a tired one.
    /// <c>TwoHanded</c> says both hands are on the weapon: the arms then travel together and
    /// barely, because the rig binds a two-handed weapon to one hand and an arm moving
    /// independently would pull it out of the other.
    /// </para>
    /// </summary>
    private readonly struct Tuning
    {
        public readonly string Unit;
        public readonly float Tempo;
        public readonly float Swing;
        public readonly float Bounce;
        public readonly bool TwoHanded;
        public readonly string Ability;

        /// <summary>
        /// How hard the unit's own weapon hits back, against the roster's one-for-a-rifle. A rotary
        /// gun is the case this exists for: it fires continuously rather than in rounds, so what it
        /// does to the body holding it is a rattle, not a kick.
        /// </summary>
        public readonly float Recoil;

        /// <summary>
        /// Turns a second of <see cref="CharacterBone.Rotor"/> while the unit is firing, or zero for
        /// a weapon with nothing on it that turns. Salvo's six barrels are the only rotor on the
        /// roster.
        /// </summary>
        public readonly float Spin;

        /// <summary>
        /// Which hand the visible weapon is bound to. Right for everyone but Farsight, whose bow —
        /// limbs, string and arrow together — hangs off the bow hand so the assembly can never come
        /// apart at the nock.
        /// </summary>
        public readonly bool LeftHanded;

        public bool HasRotor => Spin > 0f;

        public CharacterBone WeaponHand => LeftHanded ? CharacterBone.HandL : CharacterBone.HandR;

        public Tuning(
            string unit,
            float tempo,
            float swing,
            float bounce,
            bool twoHanded,
            string ability = null,
            float recoil = 1f,
            float spin = 0f,
            bool leftHanded = false
        )
        {
            Unit = unit;
            Tempo = tempo;
            Swing = swing;
            Bounce = bounce;
            TwoHanded = twoHanded;
            Ability = ability;
            Recoil = recoil;
            Spin = spin;
            LeftHanded = leftHanded;
        }
    }

    /// <summary>
    /// Tuned against the bodies in <c>CharacterBuilder.Roster</c> and against what each unit does,
    /// which are not always the same answer: Salvo is nearly as heavy as Sentinel and moves at a
    /// different weight because it is a gunner rather than a wall.
    ///
    /// <para>
    /// The ability column names the clip a unit's own ability plays and is the one state that is not
    /// shared. Outrider has no entry because Outrider has no ability — it is the plain scout, and a
    /// clip for a button it does not have would be dead data.
    /// </para>
    /// </summary>
    private static readonly Tuning[] Roster =
    {
        new("Blitz", 1.32f, 1.18f, 1.24f, true, "ShatterLeap", recoil: 1.15f),

        // The launcher is the heaviest thing anyone fires and the only one drawn to look like it
        // shoves the unit rather than merely jolting it.
        new("Breach", 0.84f, 0.82f, 0.78f, true, "BunkerBuster", recoil: 1.35f),
        new("Farsight", 1.12f, 1.06f, 1.02f, true, "TripleVolley", recoil: 0.55f, leftHanded: true),
        new("Outrider", 1.22f, 1.12f, 1.14f, true),
        new("President", 0.92f, 0.76f, 0.70f, false, "PresidentialRecall"),

        // A rotary gun does not recoil, it vibrates: the shove is spread continuously over the
        // burst instead of arriving in one shot, so the kick comes almost all the way out and the
        // spin is what says the weapon is firing at all.
        new("Salvo", 0.88f, 0.88f, 0.84f, true, "SuppressingFire", recoil: 0.16f, spin: 5.2f),
        new("Sentinel", 0.80f, 0.74f, 0.72f, false, "ShieldStance"),
        new("Voltaic", 1.06f, 1.00f, 0.96f, false, "ArcSurge", recoil: 0.7f),
    };

    [MenuItem("Battle Plan/Art/Build Character Animations", false, 16)]
    public static void BuildAnimations() => BuildAndLog(Roster);

    [MenuItem("Battle Plan/Art/Build Selected Character Animations", true, 17)]
    private static bool ValidateBuildSelected() => Find(Selection.activeObject?.name).Unit != null;

    [MenuItem("Battle Plan/Art/Build Selected Character Animations", false, 17)]
    private static void BuildSelected() => BuildForUnit(Selection.activeObject.name);

    /// <summary>Rebuilds one unit's clips and controller by prefab name.</summary>
    public static bool BuildForUnit(string name)
    {
        Tuning tuning = Find(name);
        if (tuning.Unit == null)
        {
            Debug.LogError($"[Characters] No generated character called {name}.");
            return false;
        }

        return BuildAndLog(new[] { tuning }) == 1;
    }

    private static Tuning Find(string name) =>
        string.IsNullOrEmpty(name) ? default : Roster.FirstOrDefault(tuning => tuning.Unit == name);

    private static int BuildAndLog(IReadOnlyList<Tuning> roster)
    {
        EnsureFolder(ClipFolder);

        int clips = 0;
        foreach (Tuning tuning in roster)
            clips += Build(tuning);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            roster.Count == 1
                ? $"[Characters] Animated {roster[0].Unit}, {clips} clips."
                : $"[Characters] Animated {roster.Count} units, {clips} clips total."
        );
        return roster.Count;
    }

    private static int Build(Tuning t)
    {
        Dictionary<CharacterBone, Vector3> rest = RestPose(t.Unit);
        if (rest == null)
            return 0;

        Stance stance = Aim(t);
        Sighted sighted = Sight(t, stance);

        List<(string State, AnimationClip Clip)> states = new()
        {
            (Idle, Write(t, Idle, BuildIdle(t), loop: true, rest)),
            (Moving, Write(t, Moving, BuildMoving(t), loop: true, rest)),
            (Aiming, Write(t, Aiming, BuildAiming(t, stance, sighted), loop: true, rest)),
            (Shoot, Write(t, Shoot, BuildShoot(t, stance, sighted), loop: false, rest)),
            (Dodge, Write(t, Dodge, BuildDodge(t), loop: true, rest)),
            (DiveRecovery, Write(t, DiveRecovery, BuildDiveRecovery(t), loop: false, rest)),
        };

        if (t.Ability != null)
        {
            // ShieldStance is the one ability that is a posture rather than an action: it is held
            // for as long as the stance is up, so it loops where the rest play out and stop.
            bool held = t.Ability == "ShieldStance";
            states.Add((t.Ability, Write(t, t.Ability, BuildAbility(t), held, rest)));
        }

        AnimatorController controller = WriteController(t.Unit, states);
        Assign(t.Unit, controller);
        return states.Count;
    }

    // ================================================================= putting the gun on the shot

    /// <summary>
    /// The constant part of the firing pose, held in one place because two things have to agree on
    /// it exactly: the clips that lay it down, and <see cref="Sight"/>, which solves the rest of the
    /// pose on top of it. Solve against a different stance from the one that ships and the gun comes
    /// out aimed at nothing.
    /// </summary>
    private readonly struct Stance
    {
        public readonly float TorsoPitch;
        public readonly float ShoulderPitch;
        public readonly float ForearmPitch;
        public readonly float HeadPitch;

        /// <summary>
        /// How far the weapon arm swings down, which is the only part of this that is about the shot
        /// rather than about the pose.
        ///
        /// <para>
        /// A round leaves from the unit's own origin, which is the centre of the body at about belt
        /// height, and a gun held at the shoulder sits nearly three tenths of the body's height
        /// above that. On a two-handed unit both shoulders travel by the same amount, because the
        /// weapon is bound to one hand and the other is holding it; on a one-handed unit only the
        /// weapon arm moves, since dropping the free arm with it reads as sleepwalking rather than
        /// as aiming. Either way the wrist takes the bore back to horizontal afterwards.
        /// </para>
        ///
        /// <para>
        /// It does not close the gap, and cannot. Measured against the shipped rigs this brings the
        /// weapon from about 0.66 of the body's height down to about 0.60, against a round that
        /// leaves at 0.36; fifty-two degrees would buy another three hundredths and bend President's
        /// wrist to forty-seven. An arm cannot reach belt height and still be aiming. Closing the
        /// rest would mean lowering the whole body, which puts its feet through its own base plate,
        /// or moving where the round spawns, which is <c>Shooting</c>'s business and not this file's.
        /// So the sideways offset and the aim are solved exactly and the height is only improved.
        /// </para>
        /// </summary>
        public readonly float ShoulderDrop;

        public Stance(float torsoPitch, float shoulderPitch, float forearmPitch, float headPitch, float shoulderDrop)
        {
            TorsoPitch = torsoPitch;
            ShoulderPitch = shoulderPitch;
            ForearmPitch = forearmPitch;
            HeadPitch = headPitch;
            ShoulderDrop = shoulderDrop;
        }
    }

    private static Stance Aim(Tuning t) =>
        new(
            torsoPitch: -2.4f * t.Swing,
            shoulderPitch: -11.0f * t.Swing,
            forearmPitch: -7.0f * t.Swing,
            headPitch: -4.0f * t.Swing,
            shoulderDrop: 32.0f
        );

    /// <summary>
    /// What it takes to put a unit's gun on its own line of fire.
    ///
    /// <para>
    /// <c>Shooting</c> spawns a round at <c>transform.position</c> — the unit's root, dead centre of
    /// the body — and sends it straight down the unit's forward axis. The weapons are not drawn
    /// there: the rig hangs them off a hand, out to one side and canted across the body, so the bore
    /// misses the line the bullet actually travels by up to fourteen hundredths of the body's height
    /// sideways and by twelve to thirty-three degrees of yaw. From the board camera, which is nearly
    /// overhead, that reads as a unit firing out of its chest while its gun points somewhere else.
    /// </para>
    ///
    /// <para>
    /// The fix is two rotations and no new geometry. The chest turns until the bore crosses the
    /// centre line, and the wrist turns until the bore runs straight down it — which also takes out
    /// the weapon's built-in pitch, since a round flies level. Both are solved against the shipped
    /// prefab rather than derived, so a re-posed weapon re-aims itself.
    /// </para>
    /// </summary>
    private readonly struct Sighted
    {
        public readonly float TorsoYaw;
        public readonly Vector3 Hand;

        public Sighted(float torsoYaw, Vector3 hand)
        {
            TorsoYaw = torsoYaw;
            Hand = hand;
        }
    }

    private static Sighted Sight(Tuning t, Stance stance)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Units/{t.Unit}.prefab");
        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        try
        {
            Transform body = rig.transform.Find(CharacterSkeleton.ModelRootName);
            Transform torso = body.Find(CharacterSkeleton.Path(CharacterBone.Torso));
            Transform hand = body.Find(CharacterSkeleton.Path(t.WeaponHand));
            Transform weapon = body.Find(CharacterSkeleton.Path(CharacterBone.Weapon));

            // A stand-in for the weapon, rigidly on the hand that holds it, sitting on the bore and
            // pointing down it. Read off the weapon pivot, which is on the bore for every unit, but
            // parented to the hand the geometry actually follows — those differ on Farsight.
            Transform bore = new GameObject("Bore").transform;
            bore.SetParent(hand, false);
            bore.position = weapon.position;
            bore.rotation = Quaternion.LookRotation(weapon.up, Vector3.up);

            Pose(body, CharacterBone.Torso, new Vector3(stance.TorsoPitch, 0f, 0f));
            Pose(body, CharacterBone.Head, new Vector3(stance.HeadPitch, 0f, 0f));
            foreach (bool left in new[] { true, false })
            {
                CharacterBone[] arm = CharacterSkeleton.Arm(left);
                Pose(body, arm[0], new Vector3(stance.ShoulderPitch + Drop(t, left), 0f, 0f));
                Pose(body, arm[1], new Vector3(stance.ForearmPitch, 0f, 0f));
            }

            // Turning the chest sweeps the bore across the centre line once and only once over any
            // sane range, so it is bisected rather than stepped toward: no gain to tune, and it
            // lands on the crossing to a ten-thousandth of the body's height.
            float Offset(float yaw)
            {
                torso.localRotation = Quaternion.Euler(stance.TorsoPitch, yaw, 0f);
                hand.localRotation = Quaternion.identity;

                // Exact rather than iterated: one rotation takes the bore from where it points to
                // straight down the shot. Solving this per axis instead fights itself through
                // gimbal and walks the wrist into its own limits.
                Quaternion onto = Quaternion.FromToRotation(
                    body.InverseTransformDirection(bore.forward).normalized,
                    Vector3.forward
                );
                hand.rotation = body.rotation * onto * Quaternion.Inverse(body.rotation) * hand.rotation;
                return body.InverseTransformPoint(bore.position).x;
            }

            float low = -SweepLimit;
            float high = SweepLimit;
            for (int step = 0; step < 60; step++)
            {
                float mid = (low + high) * 0.5f;
                if (Offset(low) * Offset(mid) <= 0f)
                    high = mid;
                else
                    low = mid;
            }

            float solved = (low + high) * 0.5f;
            Offset(solved);
            return new Sighted(solved, hand.localRotation.eulerAngles);
        }
        finally
        {
            Object.DestroyImmediate(rig);
        }
    }

    /// <summary>How far the chest may be turned to find the centre line, either way.</summary>
    private const float SweepLimit = 58f;

    /// <summary>
    /// How far one shoulder drops. Both of them on a two-handed unit, so the weapon stays in both
    /// hands; the weapon arm alone otherwise.
    /// </summary>
    private static float Drop(Tuning t, bool left) =>
        t.TwoHanded || left == t.LeftHanded ? Aim(t).ShoulderDrop : 0f;

    private static void Pose(Transform body, CharacterBone bone, Vector3 euler) =>
        body.Find(CharacterSkeleton.Path(bone)).localRotation = Quaternion.Euler(euler);

    // ================================================================= the shared motions

    /// <summary>
    /// Standing. The whole clip is one breath: the hips rise, the chest opens a degree behind them
    /// and the head drifts off-centre and back, all on different phases so nothing in the body
    /// arrives anywhere at the same time as anything else. That phase offset is the entire
    /// difference between a character breathing and a model scaling.
    /// </summary>
    private static Take BuildIdle(Tuning t)
    {
        Take take = new(3.2f / t.Tempo);

        take.Ride(CharacterBone.Hips, 0.009f * t.Bounce, cycles: 1f);
        take.Cycle(CharacterBone.Torso, Axis.X, -1.4f * t.Swing, phase: 0.18f);
        take.Cycle(CharacterBone.Head, Axis.Y, 3.2f * t.Swing, phase: 0.42f);
        take.Cycle(CharacterBone.Head, Axis.X, -1.1f * t.Swing, phase: 0.10f);

        // Arms hang and sway a little behind the chest they hang off. Both of them together on a
        // two-handed unit, because a weapon is bound to one hand and the other is holding it.
        float arms = (t.TwoHanded ? 1.1f : 2.4f) * t.Swing;
        take.Cycle(CharacterBone.ShoulderL, Axis.X, -arms, phase: 0.26f);
        take.Cycle(CharacterBone.ShoulderR, Axis.X, -arms, phase: t.TwoHanded ? 0.26f : 0.34f);

        return take;
    }

    /// <summary>
    /// Walking, as two strides so the clip loops on the same foot it started on. Legs swing from
    /// the hip with no knee to break at, which is what the boots are for: a foot that levels off at
    /// the front of the swing and rolls under at the back reads as a step even when the shin above
    /// it is one rigid piece.
    /// </summary>
    private static Take BuildMoving(Tuning t)
    {
        Take take = new(0.92f / t.Tempo);

        foreach (bool left in new[] { true, false })
        {
            float phase = left ? 0f : 0.5f;
            CharacterBone[] leg = CharacterSkeleton.Leg(left);

            take.Cycle(leg[0], Axis.X, -24f * t.Swing, phase);
            take.Cycle(leg[1], Axis.X, 13f * t.Swing, phase + 0.22f);
        }

        // Two rises to the stride, hips highest as each leg passes underneath.
        take.Ride(CharacterBone.Hips, 0.016f * t.Bounce, cycles: 2f, phase: 0.25f);
        take.Sway(CharacterBone.Hips, Axis.X, 0.010f * t.Swing, cycles: 1f);

        take.Lean(CharacterBone.Torso, Axis.X, -6.5f * t.Swing);
        take.Cycle(CharacterBone.Torso, Axis.Y, 4.0f * t.Swing, phase: 0.5f);
        take.Cycle(CharacterBone.Head, Axis.Y, -2.6f * t.Swing, phase: 0.5f);

        // Arms counter the legs on a free-handed unit and merely jostle on a two-handed one.
        if (t.TwoHanded)
        {
            take.Lean(CharacterBone.ShoulderL, Axis.X, -9f * t.Swing);
            take.Lean(CharacterBone.ShoulderR, Axis.X, -9f * t.Swing);
            take.Cycle(CharacterBone.ShoulderL, Axis.X, 4.5f * t.Swing, phase: 0.5f);
            take.Cycle(CharacterBone.ShoulderR, Axis.X, 4.5f * t.Swing, phase: 0.5f);
        }
        else
        {
            take.Cycle(CharacterBone.ShoulderL, Axis.X, 26f * t.Swing, phase: 0.5f);
            take.Cycle(CharacterBone.ShoulderR, Axis.X, 26f * t.Swing, phase: 0f);
            take.Cycle(CharacterBone.ForearmL, Axis.X, -9f * t.Swing, phase: 0.62f);
            take.Cycle(CharacterBone.ForearmR, Axis.X, -9f * t.Swing, phase: 0.12f);
        }

        return take;
    }

    /// <summary>
    /// Holding a target. Almost nothing moves, which is the point — this plays while a unit is
    /// lined up and about to fire, so what it has to say is "settled" against Idle's "waiting".
    /// The weapon comes up, the chest turns a few degrees into the sight line, the chin drops, and
    /// the only travel left is the slow sway of holding something heavy at arm's length.
    /// </summary>
    private static Take BuildAiming(Tuning t, Stance stance, Sighted sighted)
    {
        Take take = new(2.6f / t.Tempo);

        Present(take, t, stance, sighted);

        // The sway. Kept off the chest and the wrist, which the aim owns: the bore is exact at the
        // middle of the breath and drifts under two degrees either side of it, which reads as a unit
        // holding a weapon rather than as one clamped to a tripod.
        take.Ride(CharacterBone.Hips, 0.004f * t.Bounce, cycles: 1f);
        take.Cycle(CharacterBone.Head, Axis.Y, 1.2f * t.Swing, phase: 0.55f);

        // Nothing turns the rotor here. Aiming is where a unit waits between shots — it is what
        // Shoot hands back to once its round is away — so a rotor idling through it is a rotor that
        // never stops once the unit has fired once, which is not what a gun does.
        return take;
    }

    /// <summary>
    /// The pose a unit fires from: the stance, and on top of it the chest turn and wrist turn that
    /// put the bore on the line the round actually travels. Laid down by both firing clips, so a
    /// shot does not knock the gun off the shot on its way out.
    /// </summary>
    private static void Present(Take take, Tuning t, Stance stance, Sighted sighted)
    {
        take.Lean(CharacterBone.Torso, Axis.X, stance.TorsoPitch);
        take.Lean(CharacterBone.Head, Axis.X, stance.HeadPitch);

        foreach (bool left in new[] { true, false })
        {
            CharacterBone[] arm = CharacterSkeleton.Arm(left);
            take.Lean(arm[0], Axis.X, stance.ShoulderPitch + Drop(t, left));
            take.Lean(arm[1], Axis.X, stance.ForearmPitch);
        }

        take.Lean(CharacterBone.Torso, Axis.Y, sighted.TorsoYaw);

        // The head keeps most of the chest's turn out of itself, so a unit bladed to its target is
        // still looking at it. Most and not all: a head held perfectly square over a turned chest
        // reads as a doll's.
        take.Lean(CharacterBone.Head, Axis.Y, -sighted.TorsoYaw * 0.85f);

        take.Lean(t.WeaponHand, Axis.X, sighted.Hand.x);
        take.Lean(t.WeaponHand, Axis.Y, sighted.Hand.y);
        take.Lean(t.WeaponHand, Axis.Z, sighted.Hand.z);
    }

    /// <summary>
    /// The given spin rounded to whole turns, which is the only total a rotor may finish on.
    ///
    /// <para>
    /// Nothing else in the roster holds the rotor at an angle, so whatever a firing clip leaves it
    /// at, the next clip unwinds back to square — and a rotor left a third of a turn out counter-
    /// spins through that third on the way back. Whole turns make the same angle the rotor started
    /// at, so coming to rest costs nothing and consecutive shots run on from one another.
    /// </para>
    /// </summary>
    private static float Turns(float perSecond, float seconds) => Mathf.Max(1f, Mathf.Round(perSecond * seconds)) * 360f;

    /// <summary>
    /// Firing. A hard kick on the second frame and a long settle after it, because the shot is sold
    /// by how fast the body leaves its pose and how slowly it comes back — the recoil is four
    /// hundredths of a second and the recovery is most of a third of one. Overshooting past rest on
    /// the way back is what keeps it from reading as a rewind.
    /// </summary>
    private static Take BuildShoot(Tuning t, Stance stance, Sighted sighted)
    {
        Take take = new(0.46f);
        float kick = Mathf.Lerp(1.15f, 0.80f, Mathf.InverseLerp(0.74f, 1.24f, t.Bounce)) * t.Recoil;

        // Fired from the aiming pose rather than from rest. Authored from rest, the first frame of
        // every shot would snap the gun back off the line it was lined up on and the last would snap
        // it back, which is a flinch on the one frame the shot is meant to be read.
        Present(take, t, stance, sighted);

        take.Punch(CharacterBone.ShoulderR, Axis.X, 17f * kick, hit: 0.05f, over: -4f * t.Recoil, settle: 0.34f);
        take.Punch(CharacterBone.ForearmR, Axis.X, 12f * kick, hit: 0.05f, over: -3f * t.Recoil, settle: 0.30f);
        take.Punch(CharacterBone.Torso, Axis.X, 6.5f * kick, hit: 0.06f, over: -2f * t.Recoil, settle: 0.38f);
        take.Punch(CharacterBone.Head, Axis.X, 4.0f * kick, hit: 0.07f, over: -1.5f * t.Recoil, settle: 0.34f);

        if (t.TwoHanded)
        {
            take.Punch(CharacterBone.ShoulderL, Axis.X, 14f * kick, hit: 0.05f, over: -3f * t.Recoil, settle: 0.34f);
            take.Punch(CharacterBone.ForearmL, Axis.X, 9f * kick, hit: 0.05f, over: -2f * t.Recoil, settle: 0.30f);
        }

        if (!t.HasRotor)
            return take;

        // What a rotary gun does instead. The barrels come up to speed and the body buzzes: a dozen
        // beats of a degree and a half across the burst, which is a different thing from a recoil
        // and has to be, because the shot it is standing in for never stops long enough to kick.
        take.Spin(CharacterBone.Rotor, Axis.Y, (0f, 0f), (take.Length, Turns(t.Spin, take.Length)));

        for (int beat = 0; beat <= 12; beat++)
        {
            float at = take.Length * beat / 12f;
            float away = beat % 2 == 0 ? 1.6f : -1.1f;
            take.Key(CharacterBone.ShoulderR, Axis.X, false, (at, away));
            take.Key(CharacterBone.Torso, Axis.X, false, (at, away * 0.45f));
            take.Key(CharacterBone.Head, Axis.X, false, (at, away * 0.3f));
        }

        return take;
    }

    /// <summary>
    /// In the dodge window: low, loose and never still, so a unit that can be shot at looks like it
    /// knows. The weave is a full cycle of the hips across the cell with the chest turning against
    /// it, which from the board camera is the one motion that reads as evasion rather than as
    /// fidgeting.
    ///
    /// <para>
    /// There is no crouch in it, and that is the rig rather than a choice: the legs swing from the
    /// hip with no knee to fold, so dropping the hips would lift both boots off the board. The duck
    /// is carried by the spine and the shoulders instead.
    /// </para>
    /// </summary>
    private static Take BuildDodge(Tuning t)
    {
        Take take = new(1.15f / t.Tempo);

        take.Sway(CharacterBone.Hips, Axis.X, 0.034f * t.Swing, cycles: 1f);
        take.Lean(CharacterBone.Torso, Axis.X, -11f * t.Swing);
        take.Cycle(CharacterBone.Torso, Axis.Y, -13f * t.Swing, phase: 0.25f);
        take.Cycle(CharacterBone.Torso, Axis.Z, 5f * t.Swing, phase: 0.5f);

        take.Lean(CharacterBone.Head, Axis.X, -6f * t.Swing);
        take.Cycle(CharacterBone.Head, Axis.Y, 9f * t.Swing, phase: 0.35f);

        // Arms in and up, the way anything expecting to be hit holds them.
        take.Lean(CharacterBone.ShoulderL, Axis.X, -19f * t.Swing);
        take.Lean(CharacterBone.ShoulderR, Axis.X, -19f * t.Swing);
        take.Cycle(CharacterBone.ShoulderL, Axis.X, -7f * t.Swing, phase: 0.5f);
        take.Cycle(CharacterBone.ShoulderR, Axis.X, -7f * t.Swing, phase: 0f);

        take.Cycle(CharacterBone.ThighL, Axis.X, -8f * t.Swing, phase: 0.5f);
        take.Cycle(CharacterBone.ThighR, Axis.X, -8f * t.Swing, phase: 0f);

        return take;
    }

    /// <summary>
    /// Getting up off the board after a dive, which is the one clip on the roster that starts from
    /// a pose the unit is never otherwise in: pitched flat forward with both arms out. It resolves
    /// most of the way up in the first third and spends the rest arriving, with a small sag past
    /// standing so the unit lands on its feet rather than snapping to attention.
    /// </summary>
    private static Take BuildDiveRecovery(Tuning t)
    {
        Take take = new(0.78f / Mathf.Lerp(1f, t.Tempo, 0.5f));

        take.Rise(CharacterBone.Torso, Axis.X, -52f, over: 7f);
        take.Rise(CharacterBone.Head, Axis.X, 22f, over: -5f);
        take.Rise(CharacterBone.ShoulderL, Axis.X, -46f, over: 6f);
        take.Rise(CharacterBone.ShoulderR, Axis.X, -46f, over: 6f);
        take.Rise(CharacterBone.ForearmL, Axis.X, -20f, over: 4f);
        take.Rise(CharacterBone.ForearmR, Axis.X, -20f, over: 4f);
        take.Rise(CharacterBone.ThighL, Axis.X, 15f, over: -4f);
        take.Rise(CharacterBone.ThighR, Axis.X, 21f, over: -4f);
        take.Rise(CharacterBone.Hips, Axis.Y, -0.07f * t.Bounce, over: 0.012f, position: true);

        return take;
    }

    // ================================================================= the abilities

    /// <summary>
    /// The one clip per unit that is nobody else's. Each is drawn from what the ability actually
    /// does rather than from a generic flourish, because these play at the moment a player has just
    /// spent a cooldown and is watching for it to land.
    /// </summary>
    private static Take BuildAbility(Tuning t) =>
        t.Ability switch
        {
            // Blitz coils and goes. The dip is longer than the launch by a factor of three, which
            // is the whole read: anticipation is what makes a jump look powered rather than lifted.
            "ShatterLeap" => Leap(t),

            // Breach plants and fires something that fires back. The body rocks further than any
            // other shot on the roster and takes the longest to come back off it.
            "BunkerBuster" => Brace(t),

            // Farsight looses three. The draw arm holds the bow rigid, so the three beats are cut
            // into the chest and the shoulders instead of into the arms.
            "TripleVolley" => Volley(t),

            // The President calls someone back: an open sweep of the free arm across the body, the
            // one gesture on this roster that is addressed to an ally rather than at an enemy.
            "PresidentialRecall" => Beckon(t),

            // Salvo holds the trigger down. A fast rattle over a heavy forward brace, with the
            // barrels coming up to speed behind it — the rattle is the ability and the brace is why
            // the unit does not fall over doing it.
            "SuppressingFire" => Rattle(t),

            // Sentinel plants the slab and stays there. Held, not played.
            "ShieldStance" => Plant(t),

            _ => Surge(t),
        };

    /// <summary>
    /// Blitz coils and goes. The dip is a third of the clip and the launch is a sixth of it, which
    /// is the whole read: what makes a jump look powered rather than lifted is how long the
    /// character spends deciding to do it.
    /// </summary>
    private static Take Leap(Tuning t)
    {
        Take take = new(0.92f);

        take.Key(CharacterBone.Hips, Axis.Y, position: true, (0f, 0f), (0.30f, -0.030f * t.Bounce), (0.44f, 0.115f * t.Bounce), (0.66f, 0.020f), (0.92f, 0f));
        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.30f, 16f), (0.44f, -20f), (0.62f, 5f), (0.92f, 0f));
        take.Key(CharacterBone.ThighL, Axis.X, position: false, (0f, 0f), (0.30f, -18f), (0.46f, 30f), (0.70f, -6f), (0.92f, 0f));
        take.Key(CharacterBone.ThighR, Axis.X, position: false, (0f, 0f), (0.30f, -18f), (0.46f, 26f), (0.70f, -6f), (0.92f, 0f));
        take.Key(CharacterBone.ShoulderL, Axis.X, position: false, (0f, 0f), (0.30f, 20f), (0.46f, -44f), (0.70f, 8f), (0.92f, 0f));
        take.Key(CharacterBone.ShoulderR, Axis.X, position: false, (0f, 0f), (0.30f, 20f), (0.46f, -40f), (0.70f, 8f), (0.92f, 0f));
        take.Key(CharacterBone.Head, Axis.X, position: false, (0f, 0f), (0.30f, 10f), (0.46f, -14f), (0.92f, 0f));

        return take;
    }

    private static Take Brace(Tuning t)
    {
        Take take = new(1.10f);

        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.26f, -9f), (0.36f, 19f), (0.58f, -4f), (1.10f, 0f));
        take.Key(CharacterBone.ShoulderR, Axis.X, position: false, (0f, 0f), (0.26f, -14f), (0.36f, 26f), (0.60f, -5f), (1.10f, 0f));
        take.Key(CharacterBone.ShoulderL, Axis.X, position: false, (0f, 0f), (0.26f, -14f), (0.36f, 22f), (0.60f, -4f), (1.10f, 0f));
        take.Key(CharacterBone.ForearmR, Axis.X, position: false, (0f, 0f), (0.36f, 17f), (0.62f, -4f), (1.10f, 0f));
        take.Key(CharacterBone.Head, Axis.X, position: false, (0f, 0f), (0.26f, -5f), (0.36f, 12f), (1.10f, 0f));
        take.Key(CharacterBone.Hips, Axis.Y, position: true, (0f, 0f), (0.26f, -0.014f), (0.40f, 0.008f), (1.10f, 0f));
        take.Key(CharacterBone.ThighR, Axis.X, position: false, (0f, 0f), (0.26f, 12f), (1.10f, 0f));

        return take;
    }

    private static Take Volley(Tuning t)
    {
        Take take = new(1.05f);

        List<(float, float)> chest = new() { (0f, 0f) };
        List<(float, float)> shoulder = new() { (0f, 0f) };
        List<(float, float)> head = new() { (0f, 0f) };

        for (int shot = 0; shot < 3; shot++)
        {
            float at = 0.18f + shot * 0.28f;
            chest.Add((at, 8f));
            chest.Add((at + 0.14f, -1.5f));
            shoulder.Add((at, 12f));
            shoulder.Add((at + 0.14f, -2f));
            head.Add((at, 4f));
            head.Add((at + 0.14f, -1f));
        }

        chest.Add((1.05f, 0f));
        shoulder.Add((1.05f, 0f));
        head.Add((1.05f, 0f));

        take.Key(CharacterBone.Torso, Axis.Y, position: false, chest.ToArray());
        take.Key(CharacterBone.ShoulderR, Axis.X, position: false, shoulder.ToArray());
        take.Key(CharacterBone.Head, Axis.X, position: false, head.ToArray());

        return take;
    }

    private static Take Beckon(Tuning t)
    {
        Take take = new(1.00f);

        take.Key(CharacterBone.ShoulderL, Axis.X, position: false, (0f, 0f), (0.24f, -18f), (0.48f, -62f), (0.76f, -30f), (1.00f, 0f));
        take.Key(CharacterBone.ShoulderL, Axis.Y, position: false, (0f, 0f), (0.48f, -26f), (0.76f, 14f), (1.00f, 0f));
        take.Key(CharacterBone.ForearmL, Axis.X, position: false, (0f, 0f), (0.48f, -34f), (0.76f, -12f), (1.00f, 0f));
        take.Key(CharacterBone.Torso, Axis.Y, position: false, (0f, 0f), (0.48f, -12f), (0.80f, 4f), (1.00f, 0f));
        take.Key(CharacterBone.Head, Axis.Y, position: false, (0f, 0f), (0.44f, -16f), (0.84f, 3f), (1.00f, 0f));
        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.48f, -5f), (1.00f, 0f));

        return take;
    }

    private static Take Rattle(Tuning t)
    {
        Take take = new(1.20f);

        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.18f, -12f), (1.00f, -10f), (1.20f, 0f));
        take.Key(CharacterBone.ThighL, Axis.X, position: false, (0f, 0f), (0.18f, 9f), (1.00f, 9f), (1.20f, 0f));
        take.Key(CharacterBone.ThighR, Axis.X, position: false, (0f, 0f), (0.18f, -11f), (1.00f, -11f), (1.20f, 0f));

        // The rattle itself: eight beats between the brace and the release, on the weapon shoulder
        // and on the hips, which is enough to read as automatic fire and few enough to still count.
        List<(float, float)> shoulder = new() { (0f, 0f), (0.18f, -8f) };
        List<(float, float)> hips = new() { (0f, 0f), (0.18f, 0f) };
        for (int beat = 0; beat < 8; beat++)
        {
            float at = 0.22f + beat * 0.095f;
            shoulder.Add((at, -8f + (beat % 2 == 0 ? 7f : 0f)));
            hips.Add((at, beat % 2 == 0 ? -0.006f : 0.002f));
        }

        shoulder.Add((1.20f, 0f));
        hips.Add((1.20f, 0f));
        take.Key(CharacterBone.ShoulderR, Axis.X, position: false, shoulder.ToArray());
        take.Key(CharacterBone.Hips, Axis.Y, position: true, hips.ToArray());

        // The barrels come up to speed behind the brace, run flat out through the burst and coast
        // down to a stop as the unit stands back up. Averaged over the two ramps, which is why
        // their ends are worth half the rate, and then scaled so the whole thing lands on a whole
        // turn — the ramp keeps its shape and the rotor still finishes square.
        float up = t.Spin * 0.18f * 0.5f * 360f;
        float through = up + t.Spin * (1.00f - 0.18f) * 360f;
        float down = through + t.Spin * (1.20f - 1.00f) * 0.5f * 360f;
        float onto = Turns(down / 360f, 1f) / down;
        take.Spin(CharacterBone.Rotor, Axis.Y, (0f, 0f), (0.18f, up * onto), (1.00f, through * onto), (1.20f, down * onto));

        return take;
    }

    private static Take Plant(Tuning t)
    {
        Take take = new(2.00f);

        // Arrives in the first fifth and then holds, breathing. A stance that keeps animating after
        // it is set reads as a unit that has not finished deciding.
        take.Key(CharacterBone.Torso, Axis.Y, position: false, (0f, 0f), (0.34f, -22f), (2.00f, -22f));
        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.34f, -7f), (1.10f, -5.5f), (2.00f, -7f));
        take.Key(CharacterBone.ShoulderL, Axis.X, position: false, (0f, 0f), (0.34f, -26f), (2.00f, -26f));
        take.Key(CharacterBone.ShoulderL, Axis.Y, position: false, (0f, 0f), (0.34f, 20f), (2.00f, 20f));
        take.Key(CharacterBone.ForearmL, Axis.X, position: false, (0f, 0f), (0.34f, -18f), (2.00f, -18f));
        take.Key(CharacterBone.Head, Axis.Y, position: false, (0f, 0f), (0.34f, 14f), (1.10f, 11f), (2.00f, 14f));
        take.Key(CharacterBone.ThighL, Axis.X, position: false, (0f, 0f), (0.34f, -13f), (2.00f, -13f));
        take.Key(CharacterBone.ThighR, Axis.X, position: false, (0f, 0f), (0.34f, 11f), (2.00f, 11f));
        take.Key(CharacterBone.Hips, Axis.Y, position: true, (0f, 0f), (0.34f, -0.016f), (1.10f, -0.013f), (2.00f, -0.016f));

        return take;
    }

    private static Take Surge(Tuning t)
    {
        Take take = new(0.96f);

        // Both arms out and the chest opened past vertical, which is the only pose on the roster
        // that puts a unit's weight behind it rather than under it.
        take.Key(CharacterBone.Torso, Axis.X, position: false, (0f, 0f), (0.22f, 12f), (0.40f, -17f), (0.70f, 4f), (0.96f, 0f));
        take.Key(CharacterBone.ShoulderL, Axis.X, position: false, (0f, 0f), (0.22f, 14f), (0.40f, -54f), (0.72f, -20f), (0.96f, 0f));
        take.Key(CharacterBone.ShoulderR, Axis.X, position: false, (0f, 0f), (0.22f, 14f), (0.40f, -50f), (0.72f, -18f), (0.96f, 0f));
        take.Key(CharacterBone.ForearmL, Axis.X, position: false, (0f, 0f), (0.40f, -24f), (0.72f, -8f), (0.96f, 0f));
        take.Key(CharacterBone.ForearmR, Axis.X, position: false, (0f, 0f), (0.40f, -22f), (0.72f, -8f), (0.96f, 0f));
        take.Key(CharacterBone.Head, Axis.X, position: false, (0f, 0f), (0.22f, 8f), (0.40f, -13f), (0.96f, 0f));
        take.Key(CharacterBone.Hips, Axis.Y, position: true, (0f, 0f), (0.22f, -0.018f), (0.42f, 0.016f), (0.96f, 0f));

        return take;
    }

    // ================================================================= authoring

    private enum Axis
    {
        X,
        Y,
        Z,
    }

    /// <summary>
    /// A clip under construction. Curves are collected per bone and per property so the same bone
    /// can be written by several motions — a walk leans the chest and twists it, and those are two
    /// statements about one transform — and are only turned into keyframes at <see cref="Bake"/>.
    ///
    /// <para>
    /// Everything is in degrees and in model units, on a body authored one unit tall. The model root
    /// carries the unit's real height as scale, so a number here means the same thing on Breach as
    /// on Farsight.
    /// </para>
    /// </summary>
    private sealed class Take
    {
        private readonly Dictionary<(CharacterBone Bone, Axis Axis, bool Position), AnimationCurve> curves = new();
        private readonly float length;

        public Take(float length) => this.length = length;

        public float Length => length;

        private static string Property(Axis axis, bool position) =>
            (position ? "m_LocalPosition." : "localEulerAnglesRaw.") + char.ToLowerInvariant(axis.ToString()[0]);

        private AnimationCurve Curve(CharacterBone bone, Axis axis, bool position)
        {
            (CharacterBone, Axis, bool) key = (bone, axis, position);
            if (!curves.TryGetValue(key, out AnimationCurve curve))
            {
                curve = new AnimationCurve();
                curves[key] = curve;
            }
            return curve;
        }

        /// <summary>
        /// Lays one motion onto a bone's curve, on top of whatever is already there.
        ///
        /// <para>
        /// The whole motion arrives at once, and that is the point: the baseline under every one of
        /// its keys is read off the curve <em>before</em> any of them are written. A key landing on
        /// an existing one adds to it and a key landing between them inherits what the curve already
        /// reads there, so a recoil keyed at five hundredths of a second composes correctly with a
        /// stance keyed across the whole clip even though the two share no key times at all.
        /// </para>
        ///
        /// <para>
        /// Reading the baseline per key instead of per motion is the trap. A motion that states one
        /// constant at three times would then inherit its own first key into its second and its
        /// second into its third, and a twenty-degree stance would ship as sixty.
        /// </para>
        /// </summary>
        private void Layer(CharacterBone bone, Axis axis, bool position, IReadOnlyList<(float Time, float Value)> motion)
        {
            AnimationCurve curve = Curve(bone, axis, position);

            float[] under = new float[motion.Count];
            for (int i = 0; i < motion.Count; i++)
                under[i] = curve.length > 0 ? curve.Evaluate(motion[i].Time) : 0f;

            for (int i = 0; i < motion.Count; i++)
            {
                (float time, float value) = motion[i];

                int at = -1;
                for (int k = 0; k < curve.length; k++)
                {
                    if (Mathf.Approximately(curve[k].time, time))
                    {
                        at = k;
                        break;
                    }
                }

                if (at >= 0)
                {
                    Keyframe existing = curve[at];
                    existing.value += value;
                    curve.MoveKey(at, existing);
                }
                else
                {
                    curve.AddKey(new Keyframe(time, under[i] + value));
                }
            }
        }

        /// <summary>Keys a property outright at times given in seconds.</summary>
        public void Key(CharacterBone bone, Axis axis, bool position, params (float Time, float Value)[] keys) =>
            Layer(bone, axis, position, keys);

        /// <summary>
        /// A constant offset held for the whole clip, keyed at both ends so it survives being
        /// summed with a cycle on the same property.
        /// </summary>
        public void Lean(CharacterBone bone, Axis axis, float degrees) =>
            Layer(bone, axis, false, new[] { (0f, degrees), (length * 0.5f, degrees), (length, degrees) });

        /// <summary>
        /// One or more full sine cycles of rotation, laid on quarter-phase keys so the curve loops
        /// on itself: the value and the slope at the end of the clip both match the start, which is
        /// what stops a looping clip from ticking once per cycle.
        /// </summary>
        public void Cycle(CharacterBone bone, Axis axis, float degrees, float phase = 0f, float cycles = 1f) =>
            Wave(bone, axis, false, degrees, phase, cycles);

        /// <summary>As <see cref="Cycle"/>, on local position rather than rotation.</summary>
        public void Sway(CharacterBone bone, Axis axis, float distance, float cycles = 1f, float phase = 0f) =>
            Wave(bone, axis, true, distance, phase, cycles);

        /// <summary>
        /// Vertical travel, which is its own method because it is never centred: a body rides up
        /// from where it stands rather than sinking below it, so the wave is offset to sit wholly
        /// above zero.
        /// </summary>
        public void Ride(CharacterBone bone, float distance, float cycles, float phase = 0f) =>
            Wave(bone, Axis.Y, true, distance * 0.5f, phase, cycles, lift: distance * 0.5f);

        /// <summary>
        /// <paramref name="lift"/> is folded into the wave rather than added after it, so the offset
        /// reaches the keys between the wave's own rather than only the two it happens to share.
        /// </summary>
        private void Wave(
            CharacterBone bone,
            Axis axis,
            bool position,
            float amplitude,
            float phase,
            float cycles,
            float lift = 0f
        )
        {
            int steps = Mathf.Max(4, Mathf.RoundToInt(cycles * 4f));
            List<(float, float)> motion = new(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float at = i / (float)steps;
                motion.Add((at * length, Mathf.Sin((at * cycles + phase) * Mathf.PI * 2f) * amplitude + lift));
            }
            Layer(bone, axis, position, motion);
        }

        /// <summary>
        /// Turns a bone through a running total of degrees, keyed at the milestones given and then
        /// subdivided so no segment of the curve spans more than half a turn.
        ///
        /// <para>
        /// The subdivision is the point. A curve told to go from nought to four turns in one step
        /// has no way to say which way round it went, and a rotor that picks the short way looks
        /// like it is running backwards at a fraction of the speed — the wagon-wheel effect, keyed
        /// in by hand. Half a turn between keys leaves the direction unambiguous.
        /// </para>
        /// </summary>
        public void Spin(CharacterBone bone, Axis axis, params (float Time, float Degrees)[] milestones)
        {
            List<(float, float)> motion = new() { (milestones[0].Time, milestones[0].Degrees) };

            for (int i = 1; i < milestones.Length; i++)
            {
                (float wasAt, float was) = milestones[i - 1];
                (float nowAt, float now) = milestones[i];

                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(now - was) / 180f));
                for (int step = 1; step <= steps; step++)
                {
                    float at = step / (float)steps;
                    motion.Add((Mathf.Lerp(wasAt, nowAt, at), Mathf.Lerp(was, now, at)));
                }
            }

            Layer(bone, axis, false, motion);
        }

        /// <summary>
        /// Out fast, back slow, past rest and in. <paramref name="hit"/> is when the pose peaks,
        /// <paramref name="over"/> how far past rest it swings coming back, and
        /// <paramref name="settle"/> when it gets there.
        /// </summary>
        public void Punch(CharacterBone bone, Axis axis, float degrees, float hit, float over, float settle) =>
            Layer(bone, axis, false, new[] { (0f, 0f), (hit, degrees), (settle, over), (length, 0f) });

        /// <summary>
        /// Starts held at <paramref name="degrees"/> and resolves to rest, overshooting by
        /// <paramref name="over"/> on the way. The inverse of <see cref="Punch"/>, for the clips
        /// that begin somewhere the unit is not allowed to stay.
        /// </summary>
        public void Rise(CharacterBone bone, Axis axis, float from, float over, bool position = false) =>
            Layer(
                bone,
                axis,
                position,
                new[] { (0f, from), (length * 0.34f, from * 0.28f), (length * 0.74f, over), (length, 0f) }
            );

        /// <summary>
        /// Turns the collected motions into curves against <paramref name="rest"/>, the rig's own
        /// pose as the prefab ships it.
        ///
        /// <para>
        /// Position is the reason that argument exists. Rotation curves are written as authored,
        /// because every bone rests unrotated — but a position curve is an absolute local position,
        /// not a nudge, so a hip bob keyed as travel around zero would not bob the hips: it would
        /// move them to within a centimetre of the model's origin and drop the whole body through
        /// the board by the height of its own pelvis. Offsetting by the rest pose is what makes a
        /// number here mean what it reads as.
        /// </para>
        /// </summary>
        public AnimationClip Bake(AnimationClip clip, string name, bool loop, IReadOnlyDictionary<CharacterBone, Vector3> rest)
        {
            clip.name = name;
            clip.ClearCurves();
            clip.frameRate = 30f;

            Complete(rest);

            foreach (KeyValuePair<(CharacterBone Bone, Axis Axis, bool Position), AnimationCurve> track in curves)
            {
                AnimationCurve curve = track.Value;
                if (track.Key.Position)
                    Offset(curve, Rest(rest, track.Key.Bone, track.Key.Axis));

                Smooth(curve);
                clip.SetCurve(
                    CharacterSkeleton.Path(track.Key.Bone),
                    typeof(Transform),
                    Property(track.Key.Axis, track.Key.Position),
                    curve
                );
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>
        /// Fills in the position axes a motion did not ask for.
        ///
        /// <para>
        /// Unity animates a local position as a whole <c>Vector3</c>, not as three independent
        /// channels: a clip carrying only <c>m_LocalPosition.x</c> drives the other two to zero
        /// rather than leaving them where the rig put them. A hip that sways sideways and says
        /// nothing about its height therefore lands on the model's origin, which drops the unit
        /// through the board by the length of its own legs. So any bone with one position axis keyed
        /// gets all three, the unasked-for ones held flat at rest.
        /// </para>
        /// </summary>
        private void Complete(IReadOnlyDictionary<CharacterBone, Vector3> rest)
        {
            List<CharacterBone> moved = new();
            foreach (KeyValuePair<(CharacterBone Bone, Axis Axis, bool Position), AnimationCurve> track in curves)
            {
                if (track.Key.Position && !moved.Contains(track.Key.Bone))
                    moved.Add(track.Key.Bone);
            }

            foreach (CharacterBone bone in moved)
            {
                foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                {
                    if (curves.ContainsKey((bone, axis, true)))
                        continue;

                    Layer(bone, axis, true, new[] { (0f, 0f), (length, 0f) });
                }
            }
        }

        private static float Rest(IReadOnlyDictionary<CharacterBone, Vector3> rest, CharacterBone bone, Axis axis)
        {
            if (!rest.TryGetValue(bone, out Vector3 at))
                return 0f;

            return axis switch
            {
                Axis.X => at.x,
                Axis.Y => at.y,
                _ => at.z,
            };
        }

        private static void Offset(AnimationCurve curve, float by)
        {
            if (Mathf.Approximately(by, 0f))
                return;

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe key = curve[i];
                key.value += by;
                curve.MoveKey(i, key);
            }
        }

        /// <summary>
        /// Rounds every corner off the curve. Keys arrive flat by default, which turns a sine laid
        /// on five keys into four eases and a visible stop at each one.
        /// </summary>
        private static void Smooth(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
        }
    }

    // ================================================================= asset plumbing

    /// <summary>
    /// Rewrites a clip in place when one is already there, on the same contract as
    /// <c>CharacterBuilder.WriteMesh</c>: the controller and any prefab pointing at this clip keep
    /// pointing at it across a rebuild.
    /// </summary>
    private static AnimationClip Write(
        Tuning t,
        string state,
        Take take,
        bool loop,
        IReadOnlyDictionary<CharacterBone, Vector3> rest
    )
    {
        string path = ClipPath(t.Unit, state);
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
            return take.Bake(existing, state, loop, rest);

        AnimationClip created = take.Bake(new AnimationClip(), state, loop, rest);
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    /// <summary>
    /// Each joint's resting local position, read off the prefab the clips are going to drive rather
    /// than recomputed from <c>CharacterBuilder</c>'s rig. Reading the shipped data is what keeps a
    /// re-proportioned character's clips correct without anyone having to remember to rebuild them
    /// in the right order — and it means a rig that was never built fails here, loudly, instead of
    /// writing fifty-five clips that drop every unit through the board.
    /// </summary>
    private static Dictionary<CharacterBone, Vector3> RestPose(string unit)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Units/{unit}.prefab");
        Transform model = prefab != null ? prefab.transform.Find(CharacterSkeleton.ModelRootName) : null;
        if (model == null)
        {
            Debug.LogError($"[Characters] {unit} has no {CharacterSkeleton.ModelRootName}; build the character before animating it.");
            return null;
        }

        Dictionary<CharacterBone, Vector3> rest = new();
        foreach (CharacterBone bone in CharacterSkeleton.All)
        {
            Transform joint = model.Find(CharacterSkeleton.Path(bone));
            if (joint == null)
            {
                Debug.LogError($"[Characters] {unit} has no joint at {CharacterSkeleton.Path(bone)}; rebuild the character.");
                return null;
            }
            rest[bone] = joint.localPosition;
        }

        return rest;
    }

    /// <summary>
    /// One state per clip and no transitions between any of them, which is deliberate:
    /// <c>AnimationHandler</c> cross-fades by name, so the graph it drives wants to be a flat set
    /// of poses rather than a machine with its own opinion about what may follow what. A unit's
    /// state is decided by <c>Movement</c>, <c>Shooting</c> and <c>GameLoop</c>, and duplicating
    /// that here is how the two come to disagree.
    /// </summary>
    private static AnimatorController WriteController(string unit, IReadOnlyList<(string State, AnimationClip Clip)> states)
    {
        string path = ControllerPath(unit);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states.ToArray())
            machine.RemoveState(child.state);

        for (int i = 0; i < states.Count; i++)
        {
            AnimatorState state = machine.AddState(states[i].State, new Vector3(260f, 60f + i * 70f, 0f));
            state.motion = states[i].Clip;
            state.writeDefaultValues = true;
            if (i == 0)
                machine.defaultState = state;
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// Hangs the controller on the prefab's own Animator, so the two builders can be run in either
    /// order: <c>CharacterBuilder</c> picks up a controller that is already there, and this picks up
    /// an Animator that is already there. Run the mesh build first on a fresh checkout and the
    /// controller does not exist yet — without this, the unit would carry an empty Animator until
    /// somebody thought to rebuild it.
    /// </summary>
    private static void Assign(string unit, AnimatorController controller)
    {
        string path = $"Assets/Prefabs/Units/{unit}.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogError($"[Characters] Missing {path}.");
            return;
        }

        try
        {
            Transform model = root.transform.Find(CharacterSkeleton.ModelRootName);
            if (model == null)
            {
                Debug.LogError($"[Characters] {unit} has no {CharacterSkeleton.ModelRootName} to animate; build the character first.");
                return;
            }

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
                animator = model.gameObject.AddComponent<Animator>();

            if (animator.runtimeAnimatorController == controller)
                return;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureFolder(string folder)
    {
        string trimmed = folder.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(trimmed))
            return;

        string parent = Path.GetDirectoryName(trimmed)?.Replace('\\', '/');
        string leaf = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
