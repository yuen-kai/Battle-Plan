using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the eight characters that shipped as primitive stand-ins — Blitz, Breach, Farsight,
/// Outrider, President, Salvo, Sentinel and Voltaic — the same way <see cref="ArenaBuilder"/>
/// builds the board and <see cref="ProjectileBuilder"/> builds the rounds: triangles written to a
/// mesh asset and assigned to the unit prefab, so the shapes live in source rather than in a
/// modelling file nobody in the repository can open.
///
/// <para>
/// These are drawn to sit next to Soldier, Commander, Sniper, Ramrod and PogoRider, which were
/// modelled by hand and set the house style. Measured off Soldier, that style is:
/// </para>
/// <list type="bullet">
/// <item>Smooth shading everywhere. Not one of the five has a hard-edged body panel on it; the
/// only flat surfaces in the set are their weapons.</item>
/// <item>A body assembled from separate rounded primitives that simply interpenetrate — head
/// sphere into neck cylinder into torso — rather than from one welded, panelled shell.</item>
/// <item>A head that is a quarter of the whole figure: the crown sits at the top of the silhouette
/// and the jaw is barely above the shoulders, with a cartoon face on the front of it.</item>
/// <item>Three or four flat colours on a character and no more — suit, skin, black boots, and
/// whatever the weapon is made of.</item>
/// <item>Team colour on the headgear, which with the base plate is the pair of surfaces a camera
/// at 73 degrees sees squarely.</item>
/// </list>
///
/// <para>
/// Four things here are contracts rather than art, and may not be changed for a look:
/// </para>
/// <list type="bullet">
/// <item>The model root is a child of the unit root named <c>Body</c>. <c>HitBody.Collect</c> walks
/// the unit root's direct children and treats each one it does not recognise as board UI as a body
/// part, so the whole model has to hang off exactly one node for a hit to shove it as one piece.</item>
/// <item><c>Body/Anchors/LeftHand</c> and <c>Body/Anchors/RightHand</c> exist. <c>ArcSurge</c>
/// resolves Voltaic's lightning origin through those two paths by name.</item>
/// <item>Surfaces are URP/Lit with flat <c>_BaseColor</c> and no albedo map. <c>HitFlash</c>,
/// <c>StunPulse</c> and <c>DiveRecoveryPulse</c> all tint a body by pushing <c>_BaseColor</c>
/// through a property block, which a textured material would multiply rather than replace.</item>
/// <item>One node carries the <c>TeamIndicatorProp</c> tag and its own renderer.
/// <c>Unit.SetTeamIndicators</c> swaps slot 0 on that renderer to say whose unit this is, and it
/// only looks at the tagged node itself.</item>
/// </list>
/// </summary>
public static class CharacterBuilder
{
    private const string MeshFolder = "Assets/Meshes/Characters/";
    private const string MaterialFolder = "Assets/Materials/Characters/";
    private const string PrefabFolder = "Assets/Prefabs/Units/";
    private const string PortraitFolder = "Assets/Images/Portraits/Portrait_";

    private const string ModelRootName = "Body";
    private const string SurfaceName = "Mesh";
    private const string TeamPropName = "TeamKit";
    private const string AnchorsName = "Anchors";
    private const string TeamIndicatorTag = "TeamIndicatorProp";

    /// <summary>
    /// What the unit prefab roots carry. Deliberately non-uniform — the hand-authored FBX units are
    /// drawn to compensate for it — so the model root takes the inverse back out, per
    /// <see cref="FitUnderRoot"/>.
    /// </summary>
    private static readonly Vector3 UnitRootScale = new(1.3f, 1f, 1.3f);

    /// <summary>
    /// Crown to sole, cut under Soldier so the eight sit on the board as figures rather than as
    /// the tallest things on it. The ladder in <see cref="Roster"/> spreads them +/-4% around this.
    /// </summary>
    private const float BaseHeight = 2.95f;

    [MenuItem("Battle Plan/Art/Build Characters", false, 12)]
    public static void BuildCharacters()
    {
        EnsureFolder(MeshFolder);

        int written = 0;
        int triangles = 0;
        foreach (CharacterSpec spec in Roster)
        {
            Rig rig = new(spec);

            MeshBuilder body = new();
            BuildBody(body, rig);
            spec.Gear(body, rig);
            Mesh bodyMesh = WriteMesh(body.Build(spec.Name + "_Body"), MeshFolder + spec.Name + "_Body.asset");

            MeshBuilder team = new();
            BuildTeamProp(team, rig);
            Mesh teamMesh = WriteMesh(team.Build(spec.Name + "_Team"), MeshFolder + spec.Name + "_Team.asset");

            if (!Apply(spec, rig, bodyMesh, teamMesh, body.SubmeshPaints))
                continue;

            written++;
            triangles += CountTriangles(bodyMesh) + CountTriangles(teamMesh);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Characters] Built {written} of {Roster.Length} units, {triangles} triangles total.");
    }

    /// <summary>
    /// Prefab name and the PNG stem under <see cref="PortraitFolder"/>. Ramrod's file is still
    /// called Shotgunner because that is the sprite <c>UnitData</c> already points at.
    /// </summary>
    private static readonly (string Prefab, string Portrait)[] HandMadePortraits =
    {
        ("Soldier", "Soldier"),
        ("Commander", "Commander"),
        ("Sniper", "Sniper"),
        ("Ramrod", "Shotgunner"),
        ("PogoRider", "PogoRider"),
    };

    /// <summary>
    /// Re-shoots every unit portrait from the model itself, generated and hand-made alike, so a
    /// tile never shows a photograph of a stand-in the prefab no longer is. Framed as an upper-body
    /// close-up so the face and what the unit is holding fill the tile.
    /// </summary>
    [MenuItem("Battle Plan/Art/Build Character Portraits", false, 13)]
    public static void BuildPortraits()
    {
        HeadshotFraming framing = HeadshotFraming.Default;
        int written = 0;
        int total = Roster.Length + HandMadePortraits.Length;

        foreach (CharacterSpec spec in Roster)
        {
            if (ShootPortrait(spec.PrefabPath, PortraitFolder + spec.Name + ".png", framing))
                written++;
        }

        foreach ((string prefab, string portrait) in HandMadePortraits)
        {
            if (ShootPortrait(PrefabFolder + prefab + ".prefab", PortraitFolder + portrait + ".png", framing))
                written++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Characters] Re-shot {written} of {total} portraits.");
    }

    private static bool ShootPortrait(string prefabPath, string portraitPath, HeadshotFraming framing)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return false;

        GameObject model = Object.Instantiate(prefab);
        model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            StripForPortrait(model);
            ModelHeadshotRenderer.RenderAndSaveHeadshot(
                model,
                portraitPath,
                ModelHeadshotRenderer.DefaultWidth,
                ModelHeadshotRenderer.DefaultHeight,
                framing
            );
            return true;
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
    }

    /// <summary>
    /// The board UI, the base plate, the vision cone and a stowed shield slab are all part of the
    /// prefab and none of them are part of the character. Destroyed rather than deactivated so a
    /// shield deployed on the prefab — wider than the unit is tall — cannot decide the framing
    /// either; the renderer ignores what is switched off, but not what is switched on.
    /// </summary>
    private static void StripForPortrait(GameObject model)
    {
        Transform body = model.transform.Find(ModelRootName);
        if (body != null)
        {
            foreach (Transform child in model.transform.Cast<Transform>().ToArray())
            {
                if (child != body)
                    Object.DestroyImmediate(child.gameObject);
            }
            return;
        }

        foreach (Canvas canvas in model.GetComponentsInChildren<Canvas>(true))
            Object.DestroyImmediate(canvas.gameObject);
        foreach (VisionConeVisual cone in model.GetComponentsInChildren<VisionConeVisual>(true))
            Object.DestroyImmediate(cone.gameObject);

        foreach (string name in new[] { "BasePuck", "BasePuckRim", "VisionCone", "Shield" })
        {
            Transform found = model.transform.Find(name);
            if (found != null)
                Object.DestroyImmediate(found.gameObject);
        }
    }

    // ================================================================= palette

    /// <summary>
    /// Paint slots. A character names a material per slot and the geometry only ever asks for a
    /// slot, so a unit can be re-coloured without touching a single coordinate.
    /// </summary>
    private enum Paint
    {
        Suit,
        Kit,
        Metal,
        Skin,
        Ink,
        Accent,
        Trim,
        Eye,
    }

    /// <summary>
    /// Colours the roster needs that the hand-made Char_* set does not already have. Values are
    /// display sRGB, as in <c>ArenaBuilder</c>'s palette: URP converts at bind time and nothing
    /// here pre-converts. Existing materials are left alone — this only fills gaps.
    /// </summary>
    private static readonly (string Name, string Hex, float Metallic, float Smoothness, string EmissionHex, float EmissionGain)[]
        NewPaints =
        {
            ("Char_PlateSteel", "#4E5A63", 0.30f, 0.28f, null, 0f),
            ("Char_SentinelBlue", "#2F5F86", 0.00f, 0.14f, null, 0f),
            ("Char_BreachRust", "#8A4B2B", 0.00f, 0.12f, null, 0f),
            ("Char_FarsightMoss", "#3D5540", 0.00f, 0.10f, null, 0f),
            ("Char_OutriderSand", "#9A8A62", 0.00f, 0.12f, null, 0f),
            ("Char_VoltaicViolet", "#4C3F7A", 0.00f, 0.16f, null, 0f),
            ("Char_BlitzCrimson", "#8E2F36", 0.00f, 0.14f, null, 0f),
            ("Char_TieRed", "#A2242A", 0.00f, 0.18f, null, 0f),
            // Reads as black next to Char_Black rather than as the same surface as it, so a shoe
            // still parts from a trouser leg where the two meet.
            ("Char_BlazerBlack", "#1A1C20", 0.00f, 0.17f, null, 0f),
            // The one emissive surface on the roster, and the reason the gain is this low: bloom
            // thresholds at 1.8, so a coil is meant to sit just under it and bloom on the frames
            // ArcSurge drives rather than glowing through the whole match.
            ("Char_ArcCyan", "#59D7E0", 0.00f, 0.55f, "#59D7E0", 1.45f),
        };

    // ================================================================= roster

    private sealed class CharacterSpec
    {
        public string Name;
        public float Height;
        public float Bulk;
        public Pose Pose;
        public Legs Legs;
        public Sleeves Sleeves;

        /// <summary>
        /// When set, the legs are Kit rather than Suit — shirt and trousers, the way Ramrod and
        /// PogoRider split a body into two colours. A jumpsuit leaves this off.
        /// </summary>
        public bool Trousers;

        /// <summary>
        /// The shape of the team-coloured piece. Every hand-made unit puts team colour on one
        /// object it wears — Soldier's helmet, Commander's cap, Ramrod's chest — so what separates
        /// the eight here is which hat, not whether they have one.
        /// </summary>
        public Crest Crest;

        public Vector3 WeaponNudge;
        public Vector3 WeaponTilt;
        public Vector3 RootScale;
        public string[] Paints;
        public System.Action<MeshBuilder, Rig> Gear;

        public string PrefabPath => PrefabFolder + Name + ".prefab";
    }

    private enum Pose
    {
        Rifle,
        Sidearm,
        Bow,
    }

    private enum Legs
    {
        Human,
        Sprung,
    }

    /// <summary>
    /// Whether the forearm is suit or skin. Soldier is short-sleeved and Commander is not, and the
    /// difference is worth more than it sounds: a pale forearm doubles the amount of skin on a
    /// silhouette, which is most of what separates a lightly-equipped unit from an armoured one.
    /// </summary>
    private enum Sleeves
    {
        Short,
        Long,
    }

    private enum Crest
    {
        Dome,
        Flat,
        Cap,
        Hood,
        Beanie,
        Tie,
    }

    private static readonly CharacterSpec[] Roster =
    {
        // Bulwark. The heaviest body on the roster and the only unit carrying a slab in front of
        // it, so the shape says "cannot be walked through" before the shield stance is ever used.
        new()
        {
            Name = "Sentinel",
            Height = BaseHeight * 1.008f,
            Bulk = 1.26f,
            Pose = Pose.Sidearm,
            Legs = Legs.Human,
            Sleeves = Sleeves.Long,
            Crest = Crest.Dome,
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_SentinelBlue",
                "Char_PlateSteel",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_TealAccent",
                "Char_PlateSteel",
                "Char_White"
            ),
            Gear = GearSentinel,
        },
        // Demolisher. Tallest and heaviest, and the launcher runs back past the head, which puts
        // mass above the shoulder line where the camera reads it best.
        new()
        {
            Name = "Breach",
            Height = BaseHeight * 1.036f,
            Bulk = 1.30f,
            Pose = Pose.Rifle,
            Legs = Legs.Human,
            Sleeves = Sleeves.Short,
            Trousers = true,
            Crest = Crest.Flat,
            // Canted well across the body rather than aimed down the unit's own axis: a tube this
            // long pointed at the camera is a circle, and a circle is the one shape on this roster
            // that says nothing about what the unit does.
            WeaponNudge = new Vector3(0.006f, 0.062f, -0.014f),
            WeaponTilt = new Vector3(-8f, 17f, 0f),
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_BreachRust",
                "Char_KhakiGear",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_AmberPads",
                "Char_PlateSteel",
                "Char_White"
            ),
            Gear = GearBreach,
        },
        // Archer. Lean, hooded and cloaked: the only outline on the roster that is wider at the
        // ankle than at the shoulder, and the drawn bow reads as a horizontal bar from above.
        new()
        {
            Name = "Farsight",
            Height = BaseHeight * 1.020f,
            Bulk = 0.94f,
            Pose = Pose.Bow,
            Legs = Legs.Human,
            Sleeves = Sleeves.Long,
            Trousers = true,
            Crest = Crest.Hood,
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_FarsightMoss",
                "Char_CloakGray",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_AmberPads",
                "Char_HairBrown",
                "Char_White"
            ),
            Gear = GearFarsight,
        },
        // Scout. Short, light and the only unit with a soft cap instead of a shell — bare arms and
        // a peaked brim against seven silhouettes in armour, which is its own kind of legible.
        new()
        {
            Name = "Outrider",
            Height = BaseHeight * 0.986f,
            Bulk = 0.96f,
            Pose = Pose.Rifle,
            Legs = Legs.Human,
            Sleeves = Sleeves.Short,
            Trousers = true,
            Crest = Crest.Cap,
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_OutriderSand",
                "Char_OliveFatigues",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_TealAccent",
                "Char_KhakiGear",
                "Char_White"
            ),
            Gear = GearOutrider,
        },
        // Support gunner. The gatling is the longest object on the board and the widest thing any
        // unit is holding, so the silhouette reads as an emplacement rather than as a person.
        new()
        {
            Name = "Salvo",
            Height = BaseHeight * 1.024f,
            Bulk = 1.22f,
            Pose = Pose.Rifle,
            Legs = Legs.Human,
            Sleeves = Sleeves.Short,
            Trousers = true,
            Crest = Crest.Flat,
            WeaponNudge = new Vector3(0f, -0.010f, 0f),
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_OliveFatigues",
                "Char_KhakiGear",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_AmberPads",
                "Char_PlateSteel",
                "Char_White"
            ),
            Gear = GearSalvo,
        },
        // The one emissive unit. Two coil rings stand off the back above the shoulders, which is
        // the only part of a character the camera sees whole.
        new()
        {
            Name = "Voltaic",
            Height = BaseHeight,
            Bulk = 1.00f,
            Pose = Pose.Sidearm,
            Legs = Legs.Human,
            Sleeves = Sleeves.Long,
            Crest = Crest.Beanie,
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_VoltaicViolet",
                "Char_TealAccent",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_ArcCyan",
                "Char_PlateSteel",
                "Char_White"
            ),
            Gear = GearVoltaic,
        },
        // Skirmisher. The sprung shins are the whole identity: from above they are the only legs on
        // the board that are not two posts, and they explain the leap before it happens.
        new()
        {
            Name = "Blitz",
            Height = BaseHeight * 0.948f,
            Bulk = 0.98f,
            Pose = Pose.Rifle,
            Legs = Legs.Sprung,
            Sleeves = Sleeves.Short,
            Trousers = true,
            Crest = Crest.Beanie,
            WeaponNudge = new Vector3(0f, -0.004f, -0.012f),
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_BlitzCrimson",
                "Char_JumpsuitOrange",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_TealAccent",
                "Char_PlateSteel",
                "Char_White"
            ),
            Gear = GearBlitz,
        },
        // Not a soldier, and has to look like it from the first frame: no armour anywhere, a coat
        // skirt below the belt no other unit has, and a head of silver hair where the rest wear
        // shells. He is also the only unit built out of a value break rather than a hue — a white
        // shirt wedge and white cuffs inside a black suit, which is two steps wider than any
        // camouflaged body on the board and is what makes a head-and-shoulders tile of him read.
        // The team colour goes on the tie, the one thing he is wearing a party would have coloured.
        new()
        {
            Name = "President",
            Height = BaseHeight * 0.990f,
            Bulk = 0.92f,
            Pose = Pose.Sidearm,
            Legs = Legs.Human,
            Sleeves = Sleeves.Long,
            Crest = Crest.Tie,
            RootScale = UnitRootScale,
            Paints = Named(
                "Char_BlazerBlack",
                "Char_White",
                "Char_Gunmetal",
                "Char_Skin",
                "Char_Black",
                "Char_TieRed",
                "Char_CloakGray",
                "Char_White"
            ),
            Gear = GearPresident,
        },
    };

    private static string[] Named(params string[] paints) => paints;

    // ================================================================= the rig

    /// <summary>
    /// Joint positions for one character, in a space where the soles are at y = 0, the crown is at
    /// y = 1 and the unit faces +Z. Every number below was measured off the hand-made Soldier and
    /// divided by its height, so the roster is proportioned the way the house style is rather than
    /// the way a skeleton is — the head is a quarter of the figure and the shoulders are at 0.68,
    /// which on a real body would be the armpit.
    /// </summary>
    private sealed class Rig
    {
        internal readonly float Bulk;
        internal readonly Legs Legs;
        internal readonly Sleeves Sleeves;
        internal readonly Crest Crest;
        internal readonly bool Trousers;

        internal const float AnkleY = 0.158f;
        internal const float KneeY = 0.278f;
        internal const float HipY = 0.412f;
        internal const float WaistY = 0.470f;
        internal const float ChestY = 0.572f;
        internal const float ShoulderY = 0.676f;

        internal const float HeadY = 0.868f;
        internal const float HeadHalfX = 0.108f;
        internal const float HeadHalfY = 0.136f;
        internal const float HeadHalfZ = 0.112f;

        internal const float EyeY = 0.848f;
        internal const float MouthY = 0.800f;

        /// <summary>
        /// Crown of the skull, which is where a hat actually sits. Soldier's helmet is a smaller
        /// sphere parked on this point, not a shell drawn over the face.
        /// </summary>
        internal const float CrownY = 0.968f;

        internal readonly float WaistRadius,
            ChestRadius,
            ShoulderRadius;

        /// <summary>How much wider than deep the torso is. Soldier measures a shade over two.</summary>
        internal const float TorsoWidth = 2.02f;

        internal readonly float UpperArmR,
            ForearmR,
            HandR,
            ThighR,
            ShinR;

        internal readonly Vector3 ShoulderL,
            ShoulderR,
            ElbowL,
            ElbowR,
            WristL,
            WristR;
        internal readonly Vector3 HipL,
            HipR,
            KneeL,
            KneeR,
            AnkleL,
            AnkleR;

        internal readonly Vector3 WeaponOrigin;
        internal readonly Quaternion WeaponRot;

        internal Vector3 WeaponAxis => WeaponRot * Vector3.forward;
        internal Vector3 WeaponUp => WeaponRot * Vector3.up;
        internal Vector3 Head => new(0f, HeadY, 0f);
        internal Vector3 Chest => new(0f, ChestY, 0f);
        internal Vector3 Pelvis => new(0f, HipY, 0f);

        /// <summary>Half the torso's width at the shoulder, which is where arms and packs hang off.</summary>
        internal float ShoulderHalfX => ShoulderRadius * TorsoWidth;

        internal float ChestHalfZ => ChestRadius;

        internal Rig(CharacterSpec spec)
        {
            Bulk = spec.Bulk;
            Legs = spec.Legs;
            Sleeves = spec.Sleeves;
            Crest = spec.Crest;
            Trousers = spec.Trousers;

            WaistRadius = 0.064f * Bulk;
            ChestRadius = 0.070f * Bulk;
            ShoulderRadius = 0.074f * Bulk;

            // Arms barely taper. Soldier's measures the same at the wrist as at the deltoid, which
            // is what keeps a limb reading as a limb once it is eight pixels long; a tapered one
            // reads as a stick, and a thin one disappears into the body it is drawn against.
            UpperArmR = 0.063f * Bulk;
            ForearmR = 0.058f * Bulk;
            HandR = 0.062f * Bulk;
            ThighR = 0.058f * Bulk;
            ShinR = 0.050f * Bulk;

            // The legs splay: narrowest at the waist and widest at the sole. That A is the outline
            // every one of the hand-made five has, and it is the reason none of them need a stance.
            float hipX = 0.052f * Bulk;
            HipL = new Vector3(-hipX, HipY, 0.002f);
            HipR = new Vector3(hipX, HipY, 0.002f);
            KneeL = new Vector3(-0.090f * Bulk, KneeY, 0.008f);
            KneeR = new Vector3(0.090f * Bulk, KneeY, 0.008f);
            AnkleL = new Vector3(-0.118f * Bulk, AnkleY, -0.002f);
            AnkleR = new Vector3(0.118f * Bulk, AnkleY, -0.002f);

            float armX = ShoulderHalfX * 0.78f;
            ShoulderL = new Vector3(-armX, ShoulderY - 0.008f, 0f);
            ShoulderR = new Vector3(armX, ShoulderY - 0.008f, 0f);

            switch (spec.Pose)
            {
                // Both hands on the weapon, elbows out. Ramrod and Sniper hold theirs this way —
                // a toy gun across the chest, not an aimed rifle tucked against the ribs.
                case Pose.Rifle:
                    WeaponOrigin = new Vector3(0.040f, ShoulderY - 0.040f, 0.188f) + spec.WeaponNudge;
                    WeaponRot = Quaternion.Euler(2f + spec.WeaponTilt.x, 16f + spec.WeaponTilt.y, spec.WeaponTilt.z);
                    WristR = WeaponOrigin - WeaponRot * Vector3.forward * 0.010f + new Vector3(0.010f, -0.028f, 0f);
                    WristL = WeaponOrigin + WeaponRot * Vector3.forward * 0.100f + new Vector3(-0.010f, -0.036f, 0f);
                    ElbowR = new Vector3(ShoulderR.x + 0.110f, ShoulderY - 0.050f, 0.020f);
                    ElbowL = new Vector3(ShoulderL.x - 0.090f, ShoulderY - 0.056f, 0.050f);
                    break;

                // Weapon arm out in front, off hand out to the side so the figure still reads as a
                // T from above. The off hand being free is what lets Sentinel hang a shield.
                case Pose.Sidearm:
                    WeaponOrigin = new Vector3(0.140f, ShoulderY - 0.020f, 0.210f) + spec.WeaponNudge;
                    WeaponRot = Quaternion.Euler(2f + spec.WeaponTilt.x, 12f + spec.WeaponTilt.y, spec.WeaponTilt.z);
                    WristR = WeaponOrigin - WeaponRot * Vector3.forward * 0.018f + new Vector3(0f, -0.024f, 0f);
                    WristL = new Vector3(ShoulderL.x - 0.150f, ShoulderY - 0.020f, 0.016f);
                    ElbowR = new Vector3(ShoulderR.x + 0.090f, ShoulderY - 0.016f, 0.080f);
                    ElbowL = new Vector3(ShoulderL.x - 0.074f, ShoulderY - 0.012f, 0.008f);
                    break;

                // Bow arm extended, draw hand back at the jaw, draw elbow flared behind the
                // shoulder. Held rather than at rest, because a bow at rest is a stick.
                default:
                    WeaponOrigin = new Vector3(-0.100f, ShoulderY - 0.010f, 0.240f) + spec.WeaponNudge;
                    WeaponRot = Quaternion.Euler(spec.WeaponTilt.x, spec.WeaponTilt.y, 7f + spec.WeaponTilt.z);
                    WristL = WeaponOrigin;
                    WristR = new Vector3(0.090f, ShoulderY - 0.010f, 0.016f);
                    ElbowL = new Vector3(ShoulderL.x - 0.050f, ShoulderY - 0.030f, 0.110f);
                    ElbowR = new Vector3(ShoulderR.x + 0.100f, ShoulderY - 0.008f, -0.040f);
                    break;
            }
        }

        /// <summary>A point on the weapon axis, <paramref name="distance"/> ahead of the grip.</summary>
        internal Vector3 OnWeapon(float distance) => WeaponOrigin + WeaponAxis * distance;

        /// <summary>Rotation whose local +Y runs down the weapon, for prisms laid along the barrel.</summary>
        internal Quaternion WeaponSection =>
            Quaternion.LookRotation(WeaponAxis, WeaponUp) * Quaternion.Euler(90f, 0f, 0f);
    }

    // ================================================================= the shared body

    private static void BuildBody(MeshBuilder m, Rig r)
    {
        BuildTorso(m, r);
        BuildLegs(m, r);
        BuildArms(m, r);
        BuildHead(m, r);
        BuildFace(m, r);
    }

    /// <summary>
    /// Crotch to shoulder as one round-ended tube, squashed twice as wide as it is deep. The whole
    /// torso is a single primitive because that is what the hand-made bodies read as from the
    /// board: a soft mass with limbs pushed into it, not a chest bolted to a waist.
    /// </summary>
    private static void BuildTorso(MeshBuilder m, Rig r)
    {
        m.Paint = (int)Paint.Suit;
        m.AddTube(
            new[]
            {
                new Vector3(0f, Rig.HipY + 0.006f, 0f),
                new Vector3(0f, Rig.WaistY, 0f),
                new Vector3(0f, Rig.ChestY, 0.004f),
                new Vector3(0f, Rig.ShoulderY, 0f),
            },
            new[] { r.WaistRadius * 1.04f, r.WaistRadius, r.ChestRadius, r.ShoulderRadius },
            squash: new Vector3(Rig.TorsoWidth, 1f, 1f)
        );
    }

    private static void BuildLegs(MeshBuilder m, Rig r)
    {
        BuildLeg(m, r, r.HipL, r.KneeL, r.AnkleL);
        BuildLeg(m, r, r.HipR, r.KneeR, r.AnkleR);
    }

    private static void BuildLeg(MeshBuilder m, Rig r, Vector3 hip, Vector3 knee, Vector3 ankle)
    {
        m.Paint = (int)(r.Trousers ? Paint.Kit : Paint.Suit);
        if (r.Legs == Legs.Sprung)
        {
            m.AddTube(new[] { hip, knee }, new[] { r.ThighR, r.ShinR });

            m.Paint = (int)Paint.Metal;
            m.AddHelix(knee + new Vector3(0f, -0.006f, 0.004f), ankle + new Vector3(0f, 0.026f, 0f), r.ShinR * 1.20f, 0.015f, 3, 6);
        }
        else
        {
            m.AddTube(new[] { hip, knee, ankle }, new[] { r.ThighR, r.ShinR * 1.04f, r.ShinR });
        }

        // The boot is a squashed ball running forward of the ankle rather than a box centred on it,
        // which is the cheapest facing cue on the board: from 73 degrees a unit's feet are always
        // visible and always point where it is looking.
        m.Paint = (int)Paint.Ink;
        m.AddEllipsoid(
            new Vector3(ankle.x, 0.080f, ankle.z + 0.022f),
            new Vector3(r.ShinR * 1.22f, 0.080f, 0.088f),
            Quaternion.identity,
            12,
            7
        );
    }

    private static void BuildArms(MeshBuilder m, Rig r)
    {
        BuildArm(m, r, r.ShoulderL, r.ElbowL, r.WristL);
        BuildArm(m, r, r.ShoulderR, r.ElbowR, r.WristR);
    }

    private static void BuildArm(MeshBuilder m, Rig r, Vector3 shoulder, Vector3 elbow, Vector3 wrist)
    {
        m.Paint = (int)Paint.Suit;
        m.AddTube(new[] { shoulder, elbow }, new[] { r.UpperArmR, r.ForearmR * 1.08f });

        // Skin from the elbow out on a short-sleeved unit, and a skin hand on every unit either
        // way. A dark mitt at the end of a dark sleeve makes the arm stop at the elbow from any
        // distance; a pale one is a second light value on the silhouette and the only thing that
        // says where a unit is pointing its weapon.
        m.Paint = (int)(r.Sleeves == Sleeves.Short ? Paint.Skin : Paint.Suit);
        m.AddTube(new[] { elbow, wrist }, new[] { r.ForearmR * 1.02f, r.ForearmR * 0.92f });

        m.Paint = (int)Paint.Skin;
        m.AddEllipsoid(wrist, Vector3.one * r.HandR, Quaternion.identity, 10, 6);
    }

    /// <summary>
    /// A short skin cylinder with a big skin egg on top of it, both of them separate solids pushed
    /// into the shoulders. Deliberately oversized against a real skeleton: at the board camera's
    /// distance a unit is about forty pixels tall, and of those the crown is the only surface
    /// facing the lens squarely, so an anatomical head disappears into the body entirely.
    /// </summary>
    private static void BuildHead(MeshBuilder m, Rig r)
    {
        m.Paint = (int)Paint.Skin;
        m.AddTube(
            new[] { new Vector3(0f, Rig.ShoulderY - 0.020f, -0.004f), new Vector3(0f, 0.772f, -0.002f) },
            new[] { 0.046f, 0.043f },
            roundStart: false,
            roundEnd: false
        );
        m.AddEllipsoid(
            new Vector3(0f, Rig.HeadY, 0.002f),
            new Vector3(Rig.HeadHalfX, Rig.HeadHalfY, Rig.HeadHalfZ),
            Quaternion.identity,
            16,
            10
        );
    }

    /// <summary>
    /// Two eyeballs standing proud of the face with a pupil in front of each, and an open mouth.
    /// Every hand-made unit has exactly this and it is most of why they read as characters rather
    /// than as chess pieces: it is also the only detail on a body that survives to board distance,
    /// because it is three hard value steps — white, black, skin — inside one silhouette.
    /// </summary>
    private static void BuildFace(MeshBuilder m, Rig r)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 eye = new(side * 0.052f, Rig.EyeY, 0.092f);

            m.Paint = (int)Paint.Eye;
            m.AddEllipsoid(eye, new Vector3(0.034f, 0.038f, 0.034f), Quaternion.identity, 10, 7);

            m.Paint = (int)Paint.Ink;
            m.AddEllipsoid(
                eye + new Vector3(side * 0.004f, -0.002f, 0.022f),
                new Vector3(0.016f, 0.017f, 0.016f),
                Quaternion.identity,
                8,
                5
            );
        }

        m.Paint = (int)Paint.Ink;
        m.AddTorus(new Vector3(0f, Rig.MouthY, 0.100f), Quaternion.Euler(90f, 0f, 0f), 0.016f, 0.008f, 12, 6);
    }

    /// <summary>
    /// The team surface, and on seven of the eight it is a hat. That is where the hand-made units
    /// put theirs — Soldier's helmet, Commander's peaked cap, Ramrod's beret — and it is the right
    /// answer for the same reason they found: with the base plate, the crown is one of only two
    /// surfaces a camera at 73 degrees sees square-on. It is its own mesh and its own renderer
    /// because <c>Unit.SetTeamIndicators</c> swaps slot 0 on the tagged node.
    /// </summary>
    private static void BuildTeamProp(MeshBuilder m, Rig r)
    {
        // A hat here is a smaller solid sitting on the crown, the way Soldier's helmet is a second
        // sphere parked on the head rather than a shell drawn over it. Anything that drops below
        // the brow covers the face, and the face is the whole reason these read as the same toys
        // as the original five.
        switch (r.Crest)
        {
            case Crest.Dome:
                m.AddEllipsoid(
                    new Vector3(0f, Rig.CrownY, -0.002f),
                    new Vector3(Rig.HeadHalfX * 1.08f, 0.058f, Rig.HeadHalfZ * 1.04f),
                    Quaternion.identity,
                    16,
                    10
                );
                break;

            case Crest.Flat:
                m.AddTube(
                    new[] { new Vector3(0f, 0.952f, -0.002f), new Vector3(0f, 1.018f, -0.004f) },
                    new[] { Rig.HeadHalfX * 1.10f, Rig.HeadHalfX * 1.04f },
                    roundStart: false,
                    roundEnd: true,
                    segments: 16,
                    squash: new Vector3(1f, 1f, Rig.HeadHalfZ / Rig.HeadHalfX)
                );
                break;

            case Crest.Cap:
                m.AddEllipsoid(
                    new Vector3(0f, Rig.CrownY, -0.006f),
                    new Vector3(Rig.HeadHalfX * 1.04f, 0.054f, Rig.HeadHalfZ * 1.00f),
                    Quaternion.identity,
                    16,
                    10
                );
                m.AddEllipsoid(
                    new Vector3(0f, 0.938f, Rig.HeadHalfZ * 1.16f),
                    new Vector3(Rig.HeadHalfX * 0.88f, 0.012f, 0.052f),
                    Quaternion.Euler(-8f, 0f, 0f),
                    12,
                    6
                );
                break;

            case Crest.Hood:
                m.AddEllipsoid(
                    new Vector3(0f, Rig.CrownY - 0.008f, -0.042f),
                    new Vector3(Rig.HeadHalfX * 1.10f, 0.062f, Rig.HeadHalfZ * 0.92f),
                    Quaternion.Euler(-14f, 0f, 0f),
                    16,
                    10
                );
                break;

            case Crest.Beanie:
                m.AddEllipsoid(
                    new Vector3(0f, Rig.CrownY + 0.006f, -0.002f),
                    new Vector3(Rig.HeadHalfX * 1.02f, 0.050f, Rig.HeadHalfZ * 0.98f),
                    Quaternion.identity,
                    16,
                    10
                );
                break;

            // Knot and blade, and no collar: the collar is shirt and belongs to the body, so the
            // one team surface on this unit is the single stripe running down the middle of it.
            // Cut wider than a tie is for the same reason the hats are oversized — this is the
            // whole of the unit's team read, and a correctly-scaled tie is two pixels of it. The
            // knot is pitched back rather than left flush with the chest for the same reason the
            // others wear their colour on the crown: at 73 degrees a surface facing the sky is
            // worth several facing the horizon, and the crown here is taken up by hair.
            default:
                m.AddTaperedPrism(
                    new Vector3(0f, Rig.ShoulderY - 0.026f, r.ChestHalfZ * 1.16f),
                    Quaternion.Euler(-24f, 0f, 0f),
                    new Vector2(0.015f, 0.014f),
                    new Vector2(0.025f, 0.016f),
                    0.026f,
                    0.006f
                );
                m.AddTaperedPrism(
                    new Vector3(0f, Rig.ChestY - 0.020f, r.ChestHalfZ * 1.18f),
                    Quaternion.identity,
                    new Vector2(0.020f, 0.012f),
                    new Vector2(0.013f, 0.012f),
                    0.074f,
                    0.007f
                );
                break;
        }
    }

    // ================================================================= per-character gear

    private static void GearSentinel(MeshBuilder m, Rig r)
    {
        // The riot slab, and nothing else on the off arm. It is the single largest flat surface any
        // unit carries, and one unbroken plate is worth more here than a rimmed, strapped, badged
        // one: the shield is this character's whole read, and every seam cut into it is a pixel of
        // noise at the distance where the read has to happen.
        Vector3 face = r.WristL + new Vector3(-0.020f, 0.020f, 0.090f);
        m.Paint = (int)Paint.Kit;
        m.AddEllipsoid(face, new Vector3(0.118f, 0.168f, 0.028f), Quaternion.Euler(-8f, -12f, 0f), 12, 8);

        Pistol(m, r);
    }

    private static void GearBreach(MeshBuilder m, Rig r)
    {
        m.Paint = (int)Paint.Ink;
        m.AddEllipsoid(new Vector3(0f, Rig.MouthY + 0.004f, 0.108f), new Vector3(0.048f, 0.010f, 0.016f), Quaternion.identity, 10, 6);

        m.Paint = (int)Paint.Metal;
        Barrel(m, r, -0.200f, 0.260f, 0.048f);
        m.AddLathe(
            new[] { new Vector2(0.260f, 0.048f), new Vector2(0.310f, 0.072f), new Vector2(0.328f, 0.068f) },
            r.WeaponAxis,
            r.WeaponRot * Vector3.right,
            r.WeaponUp,
            r.WeaponOrigin,
            12,
            capEnd: true
        );
        m.Paint = (int)Paint.Ink;
        m.AddPrism(r.OnWeapon(-0.040f) - r.WeaponUp * 0.050f, r.WeaponSection, new Vector3(0.022f, 0.036f, 0.028f), 0.010f);
    }

    private static void GearFarsight(MeshBuilder m, Rig r)
    {
        // Cloak down the back, as one tapering tube rather than a panel: a flat slab behind a round
        // body is the one thing on this roster that would give itself away as a cut-out.
        m.Paint = (int)Paint.Kit;
        m.AddTube(
            new[]
            {
                new Vector3(0f, Rig.ShoulderY - 0.010f, -r.ChestHalfZ * 0.80f),
                new Vector3(0f, Rig.WaistY, -r.ChestHalfZ * 1.40f),
                new Vector3(0f, Rig.KneeY + 0.020f, -r.ChestHalfZ * 1.90f),
            },
            new[] { 0.028f, 0.026f, 0.024f },
            segments: 12,
            squash: new Vector3(r.ShoulderHalfX / 0.028f * 0.90f, 1f, 1f)
        );

        m.Paint = (int)Paint.Trim;
        m.AddTube(
            new[]
            {
                new Vector3(-0.070f, Rig.ChestY - 0.080f, -r.ChestHalfZ * 1.50f),
                new Vector3(-0.100f, Rig.ChestY + 0.090f, -r.ChestHalfZ * 1.78f),
            },
            new[] { 0.032f, 0.032f },
            segments: 10
        );

        // The bow: an arc through the bow hand, plus a drawn string back to the other.
        Quaternion bowRot = r.WeaponRot;
        Vector3[] limb =
        {
            r.WeaponOrigin + bowRot * new Vector3(0f, -0.258f, -0.032f),
            r.WeaponOrigin + bowRot * new Vector3(0f, -0.148f, 0.010f),
            r.WeaponOrigin + bowRot * new Vector3(0f, 0f, 0.024f),
            r.WeaponOrigin + bowRot * new Vector3(0f, 0.148f, 0.010f),
            r.WeaponOrigin + bowRot * new Vector3(0f, 0.258f, -0.032f),
        };
        m.AddTube(limb, new[] { 0.009f, 0.012f, 0.014f, 0.012f, 0.009f }, segments: 8);

        m.Paint = (int)Paint.Ink;
        Vector3 nock = r.WristR + new Vector3(0f, 0.014f, 0.012f);
        m.AddTube(new[] { limb[0], nock }, new[] { 0.004f, 0.004f }, segments: 5);
        m.AddTube(new[] { limb[4], nock }, new[] { 0.004f, 0.004f }, segments: 5);

        m.Paint = (int)Paint.Accent;
        m.AddTube(new[] { nock, nock + (limb[2] - nock) * 1.34f }, new[] { 0.005f, 0.005f }, segments: 6);
    }

    private static void GearOutrider(MeshBuilder m, Rig r)
    {
        Pack(m, r, Paint.Trim, 0.076f, 0.082f, 0.044f);
        Carbine(m, r, 0.320f, 0.034f);
    }

    /// <summary>Six is what a silhouette can still be counted at; eight close the gaps a cluster is
    /// read by.</summary>
    private const int SalvoBarrels = 6;

    private const float SalvoCluster = 0.044f;

    private static void GearSalvo(MeshBuilder m, Rig r)
    {
        // One read at board distance: a bright drum ringed in black at both ends, with six separate
        // dark rods out of the front of it. Trim carries the light and Metal and Ink both read as
        // black — they are four hundredths of luminance apart — so Trim against either is the only
        // contrast on this weapon that survives being twenty pixels wide.
        //
        // The rig cages the middle of the weapon: both hands on it with the elbows out means
        // anything slung between the grips ends up inside a forearm. Hence the magazine ahead of the
        // support hand and the handle on top, which is the face a camera at 73 degrees sees.
        Quaternion stand = Quaternion.LookRotation(r.WeaponAxis, r.WeaponUp);
        Vector3 right = r.WeaponRot * Vector3.right;

        Vector3 Off(float forward, float rise, float side) =>
            r.OnWeapon(forward) + r.WeaponUp * rise + right * side;

        // Barrels pass through a solid disc rather than into drilled holes: at this size a clamp
        // plate is six pixels of ring between two rods, and the holes were never visible.
        void Plate(float from, float to, float radius) =>
            m.AddLathe(
                new[] { new Vector2(from, radius), new Vector2(to, radius) },
                r.WeaponAxis,
                right,
                r.WeaponUp,
                r.WeaponOrigin,
                16,
                capStart: true,
                capEnd: true
            );

        // Drive can where a rifle would keep its stock.
        m.Paint = (int)Paint.Ink;
        Block(m, r, -0.222f, -0.084f, 0.026f, 0.030f);

        m.Paint = (int)Paint.Metal;
        Block(m, r, -0.094f, 0.150f, 0.042f, 0.048f);

        // Raked apart so the pair reads as held. Both sit under wrists the rig has already placed.
        m.Paint = (int)Paint.Ink;
        m.AddPrism(Off(-0.004f, -0.058f, 0f), stand * Quaternion.Euler(-14f, 0f, 0f), new Vector3(0.018f, 0.050f, 0.024f), 0.009f);
        m.AddPrism(Off(0.104f, -0.060f, 0f), stand * Quaternion.Euler(10f, 0f, 0f), new Vector3(0.017f, 0.050f, 0.022f), 0.008f);

        // Carry handle, standing clear of the receiver so the board camera gets daylight under it.
        m.AddPrism(Off(0.006f, 0.070f, 0f), stand, new Vector3(0.012f, 0.024f, 0.013f), 0.005f);
        m.AddPrism(Off(0.122f, 0.070f, 0f), stand, new Vector3(0.012f, 0.024f, 0.013f), 0.005f);
        m.Paint = (int)Paint.Metal;
        m.AddPrism(Off(0.064f, 0.100f, 0f), r.WeaponSection, new Vector3(0.014f, 0.074f, 0.011f), 0.005f);

        m.Paint = (int)Paint.Accent;
        m.AddPrism(Off(0.126f, 0.104f, 0f), r.WeaponSection, new Vector3(0.010f, 0.014f, 0.009f), 0.004f);

        m.Paint = (int)Paint.Ink;
        Plate(0.144f, 0.164f, 0.082f);

        m.Paint = (int)Paint.Trim;
        m.AddLathe(
            new[]
            {
                new Vector2(0.150f, 0.052f),
                new Vector2(0.168f, 0.076f),
                new Vector2(0.284f, 0.076f),
                new Vector2(0.300f, 0.058f),
            },
            r.WeaponAxis,
            right,
            r.WeaponUp,
            r.WeaponOrigin,
            16,
            capStart: true,
            capEnd: true
        );

        // Feed cover along the top of the rotor, so the drum is not the one blank surface on the
        // weapon and the eye has a line carrying the receiver through to the barrels.
        m.Paint = (int)Paint.Metal;
        m.AddPrism(Off(0.225f, 0.078f, 0f), r.WeaponSection, new Vector3(0.014f, 0.058f, 0.010f), 0.005f);

        m.Paint = (int)Paint.Ink;
        Plate(0.286f, 0.306f, 0.082f);

        // Spindle first: the gaps between barrels have to look into a shaft, not through the gun.
        m.AddTube(
            new[] { r.OnWeapon(0.290f), r.OnWeapon(0.540f) },
            new[] { 0.014f, 0.014f },
            roundStart: false,
            roundEnd: false,
            segments: 10
        );

        // Half a step around, so one barrel sits square on top where the camera looks straight down.
        m.Paint = (int)Paint.Metal;
        for (int i = 0; i < SalvoBarrels; i++)
        {
            float around = (i + 0.5f) / SalvoBarrels * Mathf.PI * 2f;
            Vector3 offset = right * (Mathf.Cos(around) * SalvoCluster) + r.WeaponUp * (Mathf.Sin(around) * SalvoCluster);
            m.AddTube(
                new[] { r.OnWeapon(0.298f) + offset, r.OnWeapon(0.536f) + offset },
                new[] { 0.0135f, 0.0135f },
                roundStart: false,
                roundEnd: false,
                segments: 9
            );
        }

        // Set back far enough that the six bores clear it: a bright band with dark rods out of it is
        // what makes the count legible from above.
        m.Paint = (int)Paint.Trim;
        Plate(0.478f, 0.506f, 0.062f);

        m.Paint = (int)Paint.Metal;
        m.AddLathe(
            new[] { new Vector2(0.540f, 0.016f), new Vector2(0.560f, 0.006f) },
            r.WeaponAxis,
            right,
            r.WeaponUp,
            r.WeaponOrigin,
            10,
            capEnd: true
        );

        // Seated up into the underside of the rotor, carrying the mass the old side drum did. Kept
        // dark so the drum stays the only light mass on the weapon, with the straps reading against
        // it instead of the can reading against the drum.
        m.Paint = (int)Paint.Metal;
        m.AddPrism(Off(0.200f, -0.126f, -0.008f), r.WeaponSection, new Vector3(0.032f, 0.058f, 0.052f), 0.012f);
        m.Paint = (int)Paint.Trim;
        foreach (float along in new[] { 0.156f, 0.244f })
            m.AddPrism(Off(along, -0.126f, -0.008f), r.WeaponSection, new Vector3(0.035f, 0.008f, 0.055f), 0.004f);

        // Belt out of the can's back, hanging loose. Run up to the rotor instead, it wrapped the
        // can's corner and read as a plastic carry handle; a tail that ends in open air is the only
        // routing at this size that can only be one thing. Squashed thin across so it reads as a
        // strip of cartridges rather than as a hose.
        m.Paint = (int)Paint.Accent;
        m.AddTube(
            new[]
            {
                Off(0.152f, -0.148f, 0.012f),
                Off(0.104f, -0.192f, 0.016f),
                Off(0.050f, -0.222f, 0.020f),
            },
            new[] { 0.014f, 0.014f, 0.013f },
            roundStart: false,
            roundEnd: false,
            segments: 8,
            squash: new Vector3(0.62f, 1f, 1f)
        );
    }

    private static void GearVoltaic(MeshBuilder m, Rig r)
    {
        Pack(m, r, Paint.Trim, 0.090f, 0.096f, 0.050f);

        // Two coil rings standing off the back above the shoulder line. A torus is the one
        // primitive nothing else on this board uses and it survives being three pixels across.
        m.Paint = (int)Paint.Accent;
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddTorus(
                new Vector3(side * 0.094f, Rig.ChestY + 0.046f, -r.ChestHalfZ * 2.10f),
                Quaternion.Euler(0f, 0f, 90f),
                0.042f,
                0.013f,
                12,
                7
            );
        }

        // Emitter on the free forearm — the hand ArcSurge reads its origin from. One cuff and one
        // lit band under it, where there used to be a stack of three: the stack was three glowing
        // lines a pixel apart, which bloom smears into a single smudge anyway.
        Vector3 emitter = r.WristL + new Vector3(0f, 0.020f, 0.008f);
        m.Paint = (int)Paint.Accent;
        m.AddTorus(emitter, Quaternion.identity, r.ForearmR * 1.10f, 0.012f, 12, 6);

        // Charge pistol: a blunt body with a lit core down the top, not a barrel.
        m.Paint = (int)Paint.Metal;
        Block(m, r, -0.032f, 0.086f, 0.028f, 0.032f);
        m.Paint = (int)Paint.Ink;
        m.AddPrism(r.WristR + new Vector3(0f, -0.038f, -0.006f), r.WeaponSection, new Vector3(0.018f, 0.038f, 0.026f), 0.010f);
        m.Paint = (int)Paint.Accent;
        m.AddPrism(r.OnWeapon(0.032f) + r.WeaponUp * 0.030f, r.WeaponSection, new Vector3(0.010f, 0.050f, 0.010f), 0.004f);
        m.AddLathe(
            new[] { new Vector2(0.086f, 0.024f), new Vector2(0.106f, 0.030f), new Vector2(0.114f, 0.026f) },
            r.WeaponAxis,
            r.WeaponRot * Vector3.right,
            r.WeaponUp,
            r.WeaponOrigin,
            10,
            capEnd: true
        );
    }

    private static void GearBlitz(MeshBuilder m, Rig r)
    {
        // Goggles pushed up onto the crown, riding over the beanie rather than over the eyes: a
        // character whose face is covered loses the one detail that says which way it is looking.
        m.Paint = (int)Paint.Ink;
        m.AddTube(
            new[] { new Vector3(-0.098f, 0.902f, 0.016f), new Vector3(0f, 0.912f, 0.106f), new Vector3(0.098f, 0.902f, 0.016f) },
            new[] { 0.013f, 0.015f, 0.013f },
            segments: 7
        );
        m.Paint = (int)Paint.Accent;
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddEllipsoid(
                new Vector3(side * 0.042f, 0.914f, 0.102f),
                new Vector3(0.026f, 0.021f, 0.016f),
                Quaternion.Euler(-28f, 0f, 0f),
                10,
                6
            );
        }

        // Short shotgun: wide receiver, stubby barrel, one bright band where the pump sits.
        m.Paint = (int)Paint.Metal;
        Block(m, r, -0.078f, 0.064f, 0.032f, 0.036f);
        Barrel(m, r, 0.064f, 0.198f, 0.022f);
        m.Paint = (int)Paint.Trim;
        Barrel(m, r, 0.090f, 0.168f, 0.028f);
        m.Paint = (int)Paint.Ink;
        m.AddPrism(r.WristR + new Vector3(0f, -0.038f, -0.006f), r.WeaponSection, new Vector3(0.018f, 0.040f, 0.030f), 0.010f);
        m.AddPrism(r.OnWeapon(-0.110f), r.WeaponSection, new Vector3(0.022f, 0.044f, 0.030f), 0.018f);
    }

    private static void GearPresident(MeshBuilder m, Rig r)
    {
        // Hair rather than the smooth cap a single dome reads as: a crown that hugs the skull, a
        // sweep standing off the brow, and a pad at each temple, so the silhouette breaks where a
        // hat's brim would be a continuous line.
        m.Paint = (int)Paint.Trim;
        m.AddEllipsoid(
            new Vector3(0f, Rig.CrownY - 0.014f, -0.012f),
            new Vector3(Rig.HeadHalfX * 1.04f, 0.054f, Rig.HeadHalfZ * 1.02f),
            Quaternion.Euler(-8f, 0f, 0f),
            16,
            10
        );
        m.AddEllipsoid(
            new Vector3(0f, 0.930f, Rig.HeadHalfZ * 0.66f),
            new Vector3(Rig.HeadHalfX * 0.80f, 0.030f, 0.038f),
            Quaternion.Euler(-26f, 0f, 0f),
            12,
            8
        );
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddEllipsoid(
                new Vector3(side * Rig.HeadHalfX * 0.89f, 0.894f, -0.034f),
                new Vector3(0.016f, 0.046f, Rig.HeadHalfZ * 0.74f),
                Quaternion.identity,
                10,
                7
            );
        }

        m.Paint = (int)Paint.Ink;
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddEllipsoid(
                new Vector3(side * 0.042f, Rig.EyeY + 0.038f, 0.086f),
                new Vector3(0.022f, 0.006f, 0.010f),
                Quaternion.identity,
                8,
                5
            );
        }

        // The shirt, and the reason this unit reads at all: one panel standing a centimetre off
        // the chest, cut as the V a jacket makes when it is worn open — a hand's width across at
        // the collar, a tie's width at the belt. It has to stand that far proud to be white: sat
        // flush on the torso its surface is tangent to it, and a white tangent to a black under a
        // light from overhead renders the same grey as the black does.
        m.Paint = (int)Paint.Kit;
        m.AddTube(
            new[] { new Vector3(0f, 0.470f, 0.056f), new Vector3(0f, 0.575f, 0.052f), new Vector3(0f, 0.672f, 0.050f) },
            new[] { 0.030f, 0.062f, 0.090f },
            roundStart: false,
            segments: 16,
            squash: new Vector3(1f, 1f, 0.34f)
        );
        m.AddTube(
            new[] { new Vector3(0f, 0.652f, 0.004f), new Vector3(0f, 0.704f, 0.002f) },
            new[] { 0.066f, 0.054f },
            roundStart: false,
            roundEnd: false,
            segments: 14,
            squash: new Vector3(1.10f, 1f, 1f)
        );
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddPrism(
                new Vector3(side * 0.038f, 0.636f, 0.072f),
                Quaternion.Euler(-12f, 0f, side * -24f),
                new Vector3(0.015f, 0.028f, 0.011f),
                0.005f
            );
        }

        // Cuffs. A long sleeve ends in a skin ball the same size as the sleeve, so without these
        // the arm is one black stick with a blob on it — and both hands are what the eye follows
        // on a unit holding a sidearm out in front of itself.
        foreach ((Vector3 elbow, Vector3 wrist) in new[] { (r.ElbowL, r.WristL), (r.ElbowR, r.WristR) })
        {
            Vector3 along = (wrist - elbow).normalized;
            m.AddTube(
                new[] { wrist - along * 0.062f, wrist - along * 0.034f },
                new[] { r.ForearmR * 1.06f, r.ForearmR * 1.10f },
                roundStart: false,
                roundEnd: false,
                segments: 10
            );
        }

        m.Paint = (int)Paint.Suit;
        m.AddTube(
            new[] { new Vector3(0f, Rig.HipY + 0.024f, 0f), new Vector3(0f, Rig.HipY - 0.070f, 0f) },
            new[] { r.WaistRadius * 1.04f, r.WaistRadius * 1.24f },
            roundStart: false,
            roundEnd: false,
            segments: 14,
            squash: new Vector3(Rig.TorsoWidth, 1f, 1f)
        );

        Pistol(m, r);
    }

    // ================================================================= shared gear pieces

    /// <summary>A rounded pack sitting against the back, sized in half-extents.</summary>
    private static void Pack(MeshBuilder m, Rig r, Paint paint, float halfX, float halfY, float halfZ)
    {
        m.Paint = (int)paint;
        m.AddEllipsoid(
            new Vector3(0f, Rig.ChestY - 0.010f, -r.ChestHalfZ - halfZ * 0.72f),
            new Vector3(halfX, halfY, halfZ),
            Quaternion.Euler(-4f, 0f, 0f),
            12,
            8
        );
    }

    /// <summary>
    /// The sidearm shared by Sentinel and President: slide, grip, and nothing else. Drawn heavier
    /// than a pistol is, because the hand holding it is a ball forty millimetres across and a
    /// correctly-sized sidearm behind one is a smudge.
    /// </summary>
    private static void Pistol(MeshBuilder m, Rig r)
    {
        m.Paint = (int)Paint.Metal;
        Block(m, r, -0.030f, 0.130f, 0.026f, 0.038f);
        m.Paint = (int)Paint.Ink;
        m.AddPrism(r.OnWeapon(-0.010f) - r.WeaponUp * 0.042f, r.WeaponSection, new Vector3(0.018f, 0.034f, 0.024f), 0.009f);
    }

    /// <summary>A service carbine, sized by its overall length.</summary>
    private static void Carbine(MeshBuilder m, Rig r, float length, float depth)
    {
        m.Paint = (int)Paint.Metal;
        Block(m, r, -0.080f, length * 0.42f, 0.030f, depth);
        Barrel(m, r, length * 0.42f, length, 0.022f);
        m.Paint = (int)Paint.Ink;
        m.AddPrism(r.OnWeapon(-0.020f) - r.WeaponUp * 0.042f, r.WeaponSection, new Vector3(0.018f, 0.034f, 0.024f), 0.009f);
        m.AddPrism(r.OnWeapon(-0.100f), r.WeaponSection, new Vector3(0.022f, 0.040f, 0.028f), 0.012f);
    }

    /// <summary>A round barrel laid along the weapon axis between two distances from the grip.</summary>
    private static void Barrel(MeshBuilder m, Rig r, float from, float to, float radius)
    {
        m.AddTube(
            new[] { r.OnWeapon(from), r.OnWeapon(to) },
            new[] { radius, radius },
            roundStart: false,
            roundEnd: false,
            segments: 10
        );
    }

    /// <summary>A box receiver laid along the weapon axis between two distances from the grip.</summary>
    private static void Block(MeshBuilder m, Rig r, float from, float to, float halfWidth, float halfHeight)
    {
        m.AddPrism(
            r.OnWeapon((from + to) * 0.5f),
            r.WeaponSection,
            new Vector3(halfWidth, (to - from) * 0.5f, halfHeight),
            Mathf.Min(halfWidth, halfHeight) * 0.5f
        );
    }

    // ================================================================= prefab wiring

    private static bool Apply(CharacterSpec spec, Rig rig, Mesh body, Mesh team, IReadOnlyList<int> paints)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Characters] Missing {spec.PrefabPath}.");
            return false;
        }

        try
        {
            Transform previous = root.transform.Find(ModelRootName);
            if (previous != null)
                Object.DestroyImmediate(previous.gameObject);

            GameObject model = new(ModelRootName);
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = FitUnderRoot(spec.RootScale, spec.Height);
            model.transform.localPosition = new Vector3(0f, GroundLine(root), 0f);

            Material[] materials = new Material[paints.Count];
            for (int i = 0; i < paints.Count; i++)
                materials[i] = LoadPaint(spec.Paints[paints[i]]);

            AddSurface(model.transform, SurfaceName, body, materials);

            GameObject teamProp = AddSurface(model.transform, TeamPropName, team, new[] { LoadPaint("Char_TeamBase") });
            teamProp.tag = TeamIndicatorTag;

            Transform anchors = new GameObject(AnchorsName).transform;
            anchors.SetParent(model.transform, false);
            Anchor(anchors, "Torso", rig.Chest);
            Anchor(anchors, "Head", rig.Head);
            Anchor(anchors, "Pelvis", rig.Pelvis);
            Anchor(anchors, "LeftForearm", Vector3.Lerp(rig.ElbowL, rig.WristL, 0.6f));
            Anchor(anchors, "RightForearm", Vector3.Lerp(rig.ElbowR, rig.WristR, 0.6f));
            Anchor(anchors, "LeftHand", rig.WristL);
            Anchor(anchors, "RightHand", rig.WristR);

            PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject AddSurface(Transform parent, string name, Mesh mesh, Material[] materials)
    {
        GameObject surface = new(name);
        surface.transform.SetParent(parent, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return surface;
    }

    private static void Anchor(Transform parent, string name, Vector3 position)
    {
        Transform anchor = new GameObject(name).transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = position;
    }

    /// <summary>
    /// Where a model authored with its feet at zero has to sit so the unit stands on the board.
    ///
    /// <para>
    /// A unit is not spawned at the height it is drawn at. <c>NetworkHelper</c> puts it on the grid
    /// and then <c>Helper.heightOffset</c> lifts the root by half its collider's world height, so
    /// the board plane ends up at root-local <c>-height/2</c> rather than at the root. A model
    /// parented at zero therefore floats by exactly that much — which is invisible in the prefab
    /// view, where there is no board to float above.
    /// </para>
    ///
    /// <para>
    /// The answer is not that plane but the top of the unit's own base puck, which stands a few
    /// centimetres proud of it: these characters are meant to stand on their plate, as the rest of
    /// the roster does. Reading it off the prefab rather than hard-coding it means a unit with a
    /// re-tuned collider or a thicker plate still lands on its feet.
    /// </para>
    /// </summary>
    private static float GroundLine(GameObject root)
    {
        Transform puck = root.transform.Find("BasePuck");
        if (puck != null)
            return puck.localPosition.y + puck.localScale.y;

        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        return capsule != null ? capsule.center.y - capsule.height * 0.5f : 0f;
    }

    /// <summary>
    /// The scale that makes a model authored one unit tall stand <paramref name="height"/> tall
    /// under a root whose own scale is non-uniform, cancelling that non-uniformity out. Without
    /// this the roots' 1.3 x/z squashes a procedurally built body that has no compensation baked in.
    /// </summary>
    private static Vector3 FitUnderRoot(Vector3 rootScale, float height)
    {
        float uniform = height / rootScale.y;
        return new Vector3(uniform * rootScale.y / rootScale.x, uniform, uniform * rootScale.y / rootScale.z);
    }

    // ================================================================= asset plumbing

    private static Material LoadPaint(string name)
    {
        string path = MaterialFolder + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        foreach ((string paintName, string hex, float metallic, float smoothness, string emissionHex, float gain) in NewPaints)
        {
            if (paintName != name)
                continue;
            return CreatePaint(path, paintName, hex, metallic, smoothness, emissionHex, gain);
        }

        Debug.LogError($"[Characters] No material at {path} and no recipe for it.");
        return null;
    }

    private static Material CreatePaint(
        string path,
        string name,
        string hex,
        float metallic,
        float smoothness,
        string emissionHex,
        float gain
    )
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[Characters] No URP Lit shader.");
            return null;
        }

        Material material = new(shader) { name = name };
        material.SetColor("_BaseColor", Hex(hex));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Hex(hex));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);

        if (emissionHex != null)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", Hex(emissionHex) * gain);
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Color Hex(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color colour))
            Debug.LogError($"[Characters] Bad hex {hex}");
        return colour;
    }

    /// <summary>Same contract as <c>ArenaBuilder.WriteMesh</c>: rewrite in place so every prefab already pointing at the asset keeps pointing at it.</summary>
    private static Mesh WriteMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        existing.Clear();
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.uv = mesh.uv;
        existing.subMeshCount = mesh.subMeshCount;
        for (int i = 0; i < mesh.subMeshCount; i++)
            existing.SetTriangles(mesh.GetTriangles(i), i);
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static int CountTriangles(Mesh mesh)
    {
        int total = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            total += mesh.GetTriangles(i).Length / 3;
        return total;
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

    // ================================================================= geometry

    /// <summary>
    /// Accumulates triangles across paint slots, as <c>ProjectileBuilder.Builder</c> does, with
    /// winding taken from an outward hint so a section can never ship inside out.
    ///
    /// <para>
    /// The one idea worth knowing here is that shading is decided by nothing but vertex sharing.
    /// Every face adds its own area-weighted normal onto its three vertices and <see cref="Build"/>
    /// normalises the sum, so a primitive that stitches a ring to its neighbour comes out smooth
    /// and one that spends four fresh vertices on every quad comes out flat. That is the whole
    /// difference between a body here and a weapon, and it is why there is no smoothing pass, no
    /// angle threshold and no second code path.
    /// </para>
    /// </summary>
    private class MeshBuilder
    {
        /// <summary>Rings of latitude in a rounded end cap. Three is where a limb stops looking cut off.</summary>
        private const int CapRings = 3;

        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> uvs = new();
        private readonly Dictionary<int, List<int>> paints = new();
        private readonly List<int> order = new();

        public int Paint { get; set; }

        /// <summary>Paint slots that actually got geometry, in the order their submeshes are written.</summary>
        public IReadOnlyList<int> SubmeshPaints => order;

        private List<int> Triangles
        {
            get
            {
                if (paints.TryGetValue(Paint, out List<int> existing))
                    return existing;

                List<int> created = new();
                paints[Paint] = created;
                order.Add(Paint);
                return created;
            }
        }

        private int Vertex(Vector3 position)
        {
            vertices.Add(position);
            normals.Add(Vector3.zero);
            uvs.Add(Vector2.zero);
            return vertices.Count - 1;
        }

        private void Tri(int a, int b, int c, Vector3 outward)
        {
            if (a == b || b == c || c == a)
                return;

            Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (n.sqrMagnitude < 1e-16f)
                return;

            if (Vector3.Dot(n, outward) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }

            normals[a] += n;
            normals[b] += n;
            normals[c] += n;

            List<int> triangles = Triangles;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private void Patch(int a, int b, int c, int d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) =>
            Tri(Vertex(a), Vertex(b), Vertex(c), outward);

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = Vertex(a);
            int j = Vertex(b);
            int k = Vertex(c);
            int l = Vertex(d);
            Tri(i, j, k, outward);
            Tri(i, k, l, outward);
        }

        private int[] Ring(Vector3 centre, Vector3 u, Vector3 v, float radius, Vector3 squash, int segments)
        {
            int[] ring = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                float around = i / (float)segments * Mathf.PI * 2f;
                Vector3 offset = u * (Mathf.Cos(around) * radius) + v * (Mathf.Sin(around) * radius);
                ring[i] = Vertex(centre + Vector3.Scale(offset, squash));
            }
            return ring;
        }

        /// <summary>
        /// Sews consecutive rings into a tube. Each ring comes with the point its surface bulges
        /// away from — the path point for a body section, the sphere's centre for a cap — so
        /// winding never has to be reasoned about at the call site.
        /// </summary>
        private void Stitch(IReadOnlyList<int[]> rings, IReadOnlyList<Vector3> origins)
        {
            for (int i = 0; i < rings.Count - 1; i++)
            {
                int[] lo = rings[i];
                int[] hi = rings[i + 1];
                Vector3 origin = (origins[i] + origins[i + 1]) * 0.5f;

                for (int j = 0; j < lo.Length; j++)
                {
                    int k = (j + 1) % lo.Length;
                    Vector3 mid = (vertices[lo[j]] + vertices[lo[k]] + vertices[hi[j]] + vertices[hi[k]]) * 0.25f;
                    Patch(lo[j], lo[k], hi[k], hi[j], mid - origin);
                }
            }
        }

        /// <summary>
        /// A tube of circular sections swept through <paramref name="points"/>, optionally closed
        /// with a hemisphere at each end. Sections are framed by parallel transport rather than by
        /// a fixed up vector, so a limb twists as little as it can between one joint and the next.
        ///
        /// <para>
        /// <paramref name="squash"/> scales every section away from its own centre, which is how a
        /// torso gets to be twice as wide as it is deep without needing an elliptical section: the
        /// normals are accumulated from the squashed triangles, so they come out correct for the
        /// shape that actually shipped rather than for the round one it was swept as.
        /// </para>
        /// </summary>
        public void AddTube(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<float> radii,
            bool roundStart = true,
            bool roundEnd = true,
            int segments = 12,
            Vector3 squash = default
        )
        {
            if (points.Count < 2)
                return;
            if (squash == default)
                squash = Vector3.one;

            int count = points.Count;
            Vector3[] tangents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 into = i > 0 ? points[i] - points[i - 1] : points[1] - points[0];
                Vector3 outOf = i < count - 1 ? points[i + 1] - points[i] : points[count - 1] - points[count - 2];
                Vector3 tangent = into.normalized + outOf.normalized;
                tangents[i] = (tangent.sqrMagnitude < 1e-10f ? outOf : tangent).normalized;
            }

            Vector3[] u = new Vector3[count];
            Vector3[] v = new Vector3[count];
            Vector3 seed = Vector3.ProjectOnPlane(Vector3.forward, tangents[0]);
            if (seed.sqrMagnitude < 1e-8f)
                seed = Vector3.ProjectOnPlane(Vector3.right, tangents[0]);
            u[0] = seed.normalized;
            v[0] = Vector3.Cross(tangents[0], u[0]).normalized;
            for (int i = 1; i < count; i++)
            {
                u[i] = (Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * u[i - 1]).normalized;
                v[i] = Vector3.Cross(tangents[i], u[i]).normalized;
            }

            List<int[]> rings = new();
            List<Vector3> origins = new();

            if (roundStart)
                Cap(rings, origins, points[0], -tangents[0], u[0], v[0], radii[0], squash, segments, toward: true);

            for (int i = 0; i < count; i++)
            {
                rings.Add(Ring(points[i], u[i], v[i], radii[i], squash, segments));
                origins.Add(points[i]);
            }

            if (roundEnd)
            {
                Cap(
                    rings,
                    origins,
                    points[count - 1],
                    tangents[count - 1],
                    u[count - 1],
                    v[count - 1],
                    radii[count - 1],
                    squash,
                    segments,
                    toward: false
                );
            }

            Stitch(rings, origins);

            if (!roundStart)
                FlatCap(points[0], -tangents[0], u[0], v[0], radii[0], squash, segments);
            if (!roundEnd)
                FlatCap(points[count - 1], tangents[count - 1], u[count - 1], v[count - 1], radii[count - 1], squash, segments);
        }

        private void Cap(
            List<int[]> rings,
            List<Vector3> origins,
            Vector3 centre,
            Vector3 outward,
            Vector3 u,
            Vector3 v,
            float radius,
            Vector3 squash,
            int segments,
            bool toward
        )
        {
            List<int[]> made = new(CapRings);
            for (int i = 0; i < CapRings; i++)
            {
                float up = (CapRings - i) / (float)CapRings * (Mathf.PI * 0.5f);
                Vector3 ringCentre = centre + Vector3.Scale(outward * (radius * Mathf.Sin(up)), squash);

                if (i == 0)
                {
                    int pole = Vertex(ringCentre);
                    int[] tip = new int[segments];
                    for (int s = 0; s < segments; s++)
                        tip[s] = pole;
                    made.Add(tip);
                    continue;
                }

                made.Add(Ring(ringCentre, u, v, radius * Mathf.Cos(up), squash, segments));
            }

            if (!toward)
                made.Reverse();

            rings.AddRange(made);
            for (int i = 0; i < CapRings; i++)
                origins.Add(centre);
        }

        /// <summary>
        /// A flat disc closing an open end. Its ring is a second copy of the tube's own, which is
        /// the point: fresh vertices mean the disc keeps its own normal instead of rounding the
        /// barrel over into it.
        /// </summary>
        private void FlatCap(Vector3 centre, Vector3 outward, Vector3 u, Vector3 v, float radius, Vector3 squash, int segments)
        {
            int hub = Vertex(centre);
            int[] ring = Ring(centre, u, v, radius, squash, segments);
            for (int i = 0; i < segments; i++)
                Tri(hub, ring[i], ring[(i + 1) % segments], outward);
        }

        public void AddEllipsoid(Vector3 centre, Vector3 radii, Quaternion rotation, int segments = 16, int rings = 10)
        {
            List<int[]> grid = new(rings + 1);
            List<Vector3> origins = new(rings + 1);

            for (int r = 0; r <= rings; r++)
            {
                float down = r / (float)rings * Mathf.PI;
                float height = Mathf.Cos(down);
                float band = Mathf.Sin(down);
                origins.Add(centre);

                if (r == 0 || r == rings)
                {
                    int pole = Vertex(centre + rotation * new Vector3(0f, height * radii.y, 0f));
                    int[] tip = new int[segments];
                    for (int s = 0; s < segments; s++)
                        tip[s] = pole;
                    grid.Add(tip);
                    continue;
                }

                int[] ring = new int[segments];
                for (int s = 0; s < segments; s++)
                {
                    float around = s / (float)segments * Mathf.PI * 2f;
                    Vector3 direction = new(band * Mathf.Sin(around), height, band * Mathf.Cos(around));
                    ring[s] = Vertex(centre + rotation * Vector3.Scale(direction, radii));
                }
                grid.Add(ring);
            }

            Stitch(grid, origins);
        }

        /// <summary>A ring of <paramref name="minor"/> thickness about the local +Y of <paramref name="rotation"/>.</summary>
        public void AddTorus(
            Vector3 centre,
            Quaternion rotation,
            float major,
            float minor,
            int segments = 16,
            int sides = 8
        )
        {
            int[][] rings = new int[segments + 1][];
            Vector3[] hubs = new Vector3[segments + 1];

            for (int s = 0; s < segments; s++)
            {
                float around = s / (float)segments * Mathf.PI * 2f;
                Vector3 direction = new(Mathf.Sin(around), 0f, Mathf.Cos(around));
                hubs[s] = centre + rotation * (direction * major);

                int[] ring = new int[sides];
                for (int t = 0; t < sides; t++)
                {
                    float round = t / (float)sides * Mathf.PI * 2f;
                    Vector3 offset = direction * (Mathf.Cos(round) * minor) + Vector3.up * (Mathf.Sin(round) * minor);
                    ring[t] = Vertex(hubs[s] + rotation * offset);
                }
                rings[s] = ring;
            }

            rings[segments] = rings[0];
            hubs[segments] = hubs[0];
            Stitch(rings, hubs);
        }

        /// <summary>A coil spring between two points, for the one unit whose legs are not posts.</summary>
        public void AddHelix(Vector3 from, Vector3 to, float coilRadius, float wireRadius, int turns, int stepsPerTurn)
        {
            int steps = Mathf.Max(turns * stepsPerTurn, 3);
            List<Vector3> path = new(steps + 1);
            List<float> radii = new(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float along = i / (float)steps;
                float angle = along * turns * Mathf.PI * 2f;
                Vector3 centre = Vector3.Lerp(from, to, along);
                path.Add(centre + new Vector3(Mathf.Cos(angle) * coilRadius, 0f, Mathf.Sin(angle) * coilRadius));
                radii.Add(wireRadius);
            }

            AddTube(path, radii, segments: 7);
        }

        // ---------------------------------------------------------- flat-shaded pieces

        /// <summary>
        /// One cross-section: a rectangle with its corners cut, lying in the local XZ plane of
        /// <paramref name="rotation"/>. Only weapons are built from these — the hand-made units are
        /// rounded everywhere a body is and hard-edged everywhere a machine is.
        /// </summary>
        private static Vector3[] Section(Vector3 centre, Quaternion rotation, float halfX, float halfZ, float bevel)
        {
            float bx = Mathf.Clamp(bevel, 0f, halfX * 0.85f);
            float bz = Mathf.Clamp(bevel, 0f, halfZ * 0.85f);
            Vector2[] flat =
            {
                new(halfX, halfZ - bz),
                new(halfX - bx, halfZ),
                new(-halfX + bx, halfZ),
                new(-halfX, halfZ - bz),
                new(-halfX, -halfZ + bz),
                new(-halfX + bx, -halfZ),
                new(halfX - bx, -halfZ),
                new(halfX, -halfZ + bz),
            };

            Vector3[] ring = new Vector3[8];
            for (int i = 0; i < 8; i++)
                ring[i] = centre + rotation * new Vector3(flat[i].x, 0f, flat[i].y);
            return ring;
        }

        private static Vector3 Centroid(Vector3[] ring)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < ring.Length; i++)
                sum += ring[i];
            return sum / ring.Length;
        }

        /// <summary>A closed box section, extruded along the local +Y of <paramref name="rotation"/>.</summary>
        public void AddPrism(Vector3 centre, Quaternion rotation, Vector3 halfExtents, float bevel) =>
            AddTaperedPrism(
                centre,
                rotation,
                new Vector2(halfExtents.x, halfExtents.z),
                new Vector2(halfExtents.x, halfExtents.z),
                halfExtents.y,
                bevel
            );

        /// <summary>As <see cref="AddPrism"/>, but the top section may differ from the bottom one.</summary>
        public void AddTaperedPrism(
            Vector3 centre,
            Quaternion rotation,
            Vector2 lowerHalf,
            Vector2 upperHalf,
            float halfHeight,
            float bevel
        )
        {
            Vector3 up = rotation * Vector3.up;
            Vector3[] lower = Section(centre - up * halfHeight, rotation, lowerHalf.x, lowerHalf.y, bevel);
            Vector3[] upper = Section(centre + up * halfHeight, rotation, upperHalf.x, upperHalf.y, bevel);

            Vector3 axis = Centroid(upper) - Centroid(lower);
            for (int i = 0; i < 8; i++)
            {
                int j = (i + 1) % 8;
                Vector3 mid = (lower[i] + lower[j] + upper[j] + upper[i]) * 0.25f;
                Vector3 outward = Vector3.ProjectOnPlane(mid - (Centroid(lower) + Centroid(upper)) * 0.5f, axis.normalized);
                if (outward.sqrMagnitude < 1e-12f)
                    outward = mid - Centroid(lower);
                AddQuad(lower[i], lower[j], upper[j], upper[i], outward);
            }

            Face(lower, -axis);
            Face(upper, axis);
        }

        private void Face(Vector3[] ring, Vector3 outward)
        {
            Vector3 centre = Centroid(ring);
            for (int i = 0; i < ring.Length; i++)
                AddTriangle(centre, ring[i], ring[(i + 1) % ring.Length], outward);
        }

        /// <summary>
        /// Revolves a profile of (distance along <paramref name="axis"/>, radius) pairs around an
        /// arbitrary origin. Same contract as <c>ProjectileBuilder.Builder.AddLathe</c>, which has
        /// no origin because a projectile is always turned about its own centre and a muzzle on a
        /// character never is.
        /// </summary>
        public void AddLathe(
            IReadOnlyList<Vector2> profile,
            Vector3 axis,
            Vector3 u,
            Vector3 v,
            Vector3 origin,
            int segments,
            bool capStart = false,
            bool capEnd = false
        )
        {
            if (profile.Count < 2 || segments < 3)
                return;

            int[][] rings = new int[profile.Count][];
            Vector3[] hubs = new Vector3[profile.Count];
            for (int i = 0; i < profile.Count; i++)
            {
                hubs[i] = origin + axis * profile[i].x;
                int[] ring = new int[segments];
                for (int s = 0; s < segments; s++)
                {
                    float around = s / (float)segments * Mathf.PI * 2f;
                    ring[s] = Vertex(hubs[i] + (u * Mathf.Cos(around) + v * Mathf.Sin(around)) * profile[i].y);
                }
                rings[i] = ring;
            }

            Stitch(rings, hubs);

            if (capStart && profile[0].y > 1e-6f)
                LatheCap(0, -axis);
            if (capEnd && profile[profile.Count - 1].y > 1e-6f)
                LatheCap(profile.Count - 1, axis);

            void LatheCap(int at, Vector3 outward)
            {
                int hub = Vertex(hubs[at]);
                for (int s = 0; s < segments; s++)
                {
                    float around = s / (float)segments * Mathf.PI * 2f;
                    float next = (s + 1) % segments / (float)segments * Mathf.PI * 2f;
                    Vector3 a = hubs[at] + (u * Mathf.Cos(around) + v * Mathf.Sin(around)) * profile[at].y;
                    Vector3 b = hubs[at] + (u * Mathf.Cos(next) + v * Mathf.Sin(next)) * profile[at].y;
                    Tri(hub, Vertex(a), Vertex(b), outward);
                }
            }
        }

        public Mesh Build(string name)
        {
            for (int i = 0; i < normals.Count; i++)
            {
                normals[i] = normals[i].sqrMagnitude > 1e-16f ? normals[i].normalized : Vector3.up;
            }

            Mesh mesh = new() { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++)
                mesh.SetTriangles(paints[order[i]], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
