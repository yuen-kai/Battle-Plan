using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// The eight units <c>CharacterBuilder</c> writes, checked as shipped data rather than as builder
/// output — the prefab is what the game loads, and a builder that runs clean but writes the wrong
/// thing is the failure worth catching.
///
/// <para>
/// Most of this is contract: the runtime finds these models by node name, by tag, and by the shader
/// property it pushes a tint through. The judgement call is
/// <see cref="TheRosterHasEightDifferentSilhouettes"/>, which is the whole reason these characters
/// were rebuilt — the stand-ins they replace were eight bodies of one width and one height.
/// </para>
/// </summary>
[TestFixture]
public class CharacterModelEditModeTests
{
    private static readonly string[] Units =
    {
        "Blitz",
        "Breach",
        "Farsight",
        "Outrider",
        "President",
        "Salvo",
        "Sentinel",
        "Voltaic",
    };

    /// <summary>
    /// Around what the hand-made Ramrod costs, and well under Soldier's twenty thousand. Rounded
    /// bodies are not free — a smooth limb is rings of triangles where a faceted one was six
    /// quads — so this is headroom for a launcher rather than for a mistake.
    /// </summary>
    private const int TriangleBudget = 4600;

    /// <summary>
    /// <c>HitBody.Collect</c> walks the unit root's direct children and shoves each body part it
    /// finds, so the model has to hang off exactly one node or a hit tears the character apart.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheModelHangsOffOneNodeUnderTheUnitRoot(string unit)
    {
        Transform body = Model(unit);

        Assert.That(body.Find("Mesh"), Is.Not.Null, "The character surface is missing.");
        Assert.That(body.Find("TeamKit"), Is.Not.Null, "The team surface is missing.");
        Assert.That(body.Find("Anchors"), Is.Not.Null, "The anchor group is missing.");
    }

    /// <summary><c>ArcSurge</c> resolves Voltaic's lightning origin through these two paths by name.</summary>
    [TestCaseSource(nameof(Units))]
    public void BothHandAnchorsResolveByPath(string unit)
    {
        Transform root = Root(unit).transform;

        Assert.That(root.Find("Body/Anchors/LeftHand"), Is.Not.Null);
        Assert.That(root.Find("Body/Anchors/RightHand"), Is.Not.Null);
    }

    /// <summary>
    /// <c>Unit.SetTeamIndicators</c> swaps slot 0 on every node carrying the tag, and looks at that
    /// node's own renderer only. Two tagged nodes inside one model would recolour twice; none would
    /// leave the unit with nothing on it saying whose it is.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void ExactlyOneTaggedSurfaceSaysWhoseUnitThisIs(string unit)
    {
        Transform body = Model(unit);

        List<Transform> tagged = new();
        foreach (Transform child in body.GetComponentsInChildren<Transform>(true))
        {
            if (child.CompareTag("TeamIndicatorProp"))
                tagged.Add(child);
        }

        Assert.That(tagged.Count, Is.EqualTo(1), "A model carries one team surface.");
        Assert.That(tagged[0].GetComponent<Renderer>(), Is.Not.Null);
    }

    /// <summary>
    /// <c>HitFlash</c>, <c>StunPulse</c> and <c>DiveRecoveryPulse</c> all tint a body by pushing
    /// <c>_BaseColor</c> through a property block. An albedo map would survive that push and
    /// multiply against it, so a textured character never flashes.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EverySurfaceIsFlatColourAndTintable(string unit)
    {
        foreach (Renderer surface in Model(unit).GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in surface.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null, $"{unit}/{surface.name} has an empty material slot.");
                Assert.That(material.HasProperty("_BaseColor"), Is.True, $"{material.name} has no _BaseColor to tint.");
                if (material.HasProperty("_BaseMap"))
                {
                    Assert.That(
                        material.GetTexture("_BaseMap"),
                        Is.Null,
                        $"{material.name} carries an albedo map; a tint would multiply against it."
                    );
                }
            }
        }
    }

    /// <summary>A character's hitbox is the capsule on the root. Geometry that can be hit on its own is a bug.</summary>
    [TestCaseSource(nameof(Units))]
    public void TheModelCarriesNoColliders(string unit)
    {
        Assert.That(Model(unit).GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [TestCaseSource(nameof(Units))]
    public void TheModelIsSizedToTheBoard(string unit)
    {
        Bounds bounds = Measure(Model(unit));

        Assert.That(bounds.size.y, Is.InRange(2.6f, 3.5f), "Characters are sized against a 2.7 cell and 2.0 cover.");
        Assert.That(bounds.size.x, Is.LessThan(GameLoop.cellSize), "A unit has to stay inside its own cell.");
        Assert.That(bounds.size.z, Is.LessThan(GameLoop.cellSize * 1.35f), "Carried gear may overhang, but not by a cell.");
    }

    /// <summary>
    /// The one that actually catches a floating character, and the reason it is not simply
    /// "feet at the model root": a unit is never drawn where it is spawned. <c>NetworkHelper</c>
    /// puts the root on the grid and <c>Helper.heightOffset</c> then lifts it by half the capsule's
    /// world height, so a model parented at zero hovers by exactly that much — invisible in the
    /// prefab view, where there is no board to hover above. What has to be true is that after the
    /// lift the feet meet the top of the unit's own base plate, which is where every hand-authored
    /// unit stands too.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheModelStandsOnItsBasePlateOnceSpawnHeightIsApplied(string unit)
    {
        GameObject root = Root(unit);
        Transform plate = root.transform.Find("BasePuck");
        Assert.That(plate, Is.Not.Null, $"{unit} has no base plate to stand on.");

        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        Assert.That(capsule, Is.Not.Null, $"{unit} has no capsule for Helper.heightOffset to measure.");

        float lift = capsule.height * root.transform.localScale.y * 0.5f;
        float plateTop = plate.localPosition.y + plate.localScale.y;
        float feet = Measure(Model(unit)).min.y;

        Assert.That(feet, Is.EqualTo(plateTop).Within(0.02f), "A unit stands on its plate, not above or through it.");
        Assert.That(lift + feet, Is.GreaterThan(0f), "Standing on the plate leaves the feet clear of the deck.");
        Assert.That(lift + feet, Is.LessThan(0.4f), "A unit this far off the deck reads as hovering.");
    }

    /// <summary>
    /// Limbs are solid, not flat. Everything on a character except its weapon is swept by
    /// <c>MeshBuilder.AddTube</c>, whose sections have to come out square to the run; get the
    /// frame wrong and each sweep collapses into a ribbon extruded along its own length instead of
    /// a tube around it. That failure is close to invisible from the front — the body still has the
    /// right outline and the gear still has volume — and obvious the moment a unit turns. The thigh
    /// band is the probe because between knee and hip there is nothing but leg.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void LimbsAreSolidRatherThanFlat(string unit)
    {
        Transform body = Model(unit);
        Bounds bounds = Measure(body);
        Bounds slice = SliceAt(body, bounds.min.y + bounds.size.y * 0.35f);

        Assert.That(slice.size.x, Is.GreaterThan(0f), $"{unit} has no geometry at thigh height.");
        Assert.That(
            slice.size.z,
            Is.GreaterThan(slice.size.x * 0.25f),
            $"{unit}'s legs are flat front-to-back; the sweep sections are not square to the run."
        );
    }

    /// <summary>
    /// The body is smooth-shaded and the weapon is not, which is the single clearest thing the five
    /// hand-authored units have in common: not one of them has a faceted limb or a rounded-off gun.
    /// Shading here is decided by nothing but vertex sharing — <c>MeshBuilder</c> accumulates a
    /// face's normal onto its three vertices and normalises the sum — so a body that stopped being
    /// smooth would be a body whose sections stopped being stitched, and this is what notices.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheBodyIsSmoothShaded(string unit)
    {
        Mesh mesh = Surface(unit, "Mesh");
        Vector3[] normals = mesh.normals;

        Assert.That(normals.Length, Is.EqualTo(mesh.vertexCount), "Every vertex carries a normal.");
        foreach (Vector3 normal in normals)
            Assert.That(normal.sqrMagnitude, Is.EqualTo(1f).Within(0.01f), $"{unit} ships an unnormalised normal.");

        // A flat-shaded mesh spends three vertices on every triangle and shares none of them, so
        // the ratio pins down which of the two a mesh is without having to inspect an angle.
        int triangles = 0;
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            triangles += (int)mesh.GetIndexCount(submesh) / 3;

        Assert.That(
            mesh.vertexCount,
            Is.LessThan(triangles * 2),
            $"{unit}'s body is built the way a weapon is: nothing shares a vertex, so nothing is smooth."
        );
    }

    /// <summary>
    /// Surfaces face out. <c>MeshBuilder</c> takes winding from an outward hint rather than from
    /// the order a call site happens to list its corners in, and the cheapest way to show that held
    /// for a whole character is that the mesh encloses a positive volume — an inside-out solid
    /// encloses the same volume with the sign flipped.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EverySolidEnclosesVolumeRatherThanBeingInsideOut(string unit)
    {
        foreach (string surface in new[] { "Mesh", "TeamKit" })
        {
            Mesh mesh = Surface(unit, surface);
            Vector3[] vertices = mesh.vertices;
            float volume = 0f;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]];
                    Vector3 b = vertices[triangles[i + 1]];
                    Vector3 c = vertices[triangles[i + 2]];
                    volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                }
            }

            Assert.That(volume, Is.GreaterThan(0f), $"{unit}/{surface} is wound inside out.");
        }
    }

    /// <summary>
    /// There is a face on the front of the head. Two eyeballs, two pupils and an open mouth is what
    /// every hand-made unit has and it is most of why they read as characters rather than as chess
    /// pieces — three hard value steps inside one silhouette, which is the only kind of detail that
    /// survives to board distance. Probed as geometry rather than as node names because the face is
    /// part of the body mesh, the way Soldier's is part of his.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheHeadHasAFaceOnTheFrontOfIt(string unit)
    {
        Transform body = Model(unit);

        // The crown, and the one landmark on the figure that no weapon, pack or shield can move:
        // the head is the highest skin on any of them. Anchoring to the whole model's box instead
        // would measure Farsight's bow and Sentinel's shield as if they were part of his face.
        List<Vector3> skin = Painted(body, "Char_Skin");
        Assert.That(skin, Is.Not.Empty, $"{unit} has no skin on it.");

        float crown = float.MinValue;
        foreach (Vector3 point in skin)
            crown = Mathf.Max(crown, point.y);

        float chin = crown - Measure(body).size.y * 0.2f;
        List<Vector3> eyes = Above(Painted(body, "Char_White"), chin);
        List<Vector3> ink = Above(Painted(body, "Char_Black"), chin);

        Assert.That(eyes, Is.Not.Empty, $"{unit} has no eye whites on its head.");
        Assert.That(ink, Is.Not.Empty, $"{unit} has nothing inked on its head.");

        Bounds white = Box(eyes);
        Assert.That(white.min.x, Is.LessThan(0f), $"{unit} is missing its left eye.");
        Assert.That(white.max.x, Is.GreaterThan(0f), $"{unit} is missing its right eye.");

        // Pupils sit proud of the eyeballs, which is what keeps the face three separate values at
        // board distance instead of one grey smudge.
        Assert.That(
            Box(ink).max.z,
            Is.GreaterThan(white.max.z),
            $"{unit} has no pupils standing off the front of its eyes."
        );
    }

    [TestCaseSource(nameof(Units))]
    public void TheModelStaysInsideItsTriangleBudget(string unit)
    {
        int triangles = 0;
        foreach ((Transform _, Mesh mesh, Renderer _) in Surfaces(Model(unit)))
        {
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                triangles += (int)mesh.GetIndexCount(submesh) / 3;
        }

        Assert.That(triangles, Is.LessThanOrEqualTo(TriangleBudget));
        Assert.That(triangles, Is.GreaterThan(400), "A body this cheap is a box with a head on it.");
    }

    /// <summary>
    /// The rig, joint by joint against <see cref="CharacterSkeleton"/>. Clips bind by path, so a
    /// renamed or reparented joint does not fail — it silently stops driving the character, which
    /// is the failure mode worth a test.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheRigHangsUnderTheModelRootWhereTheClipsLookForIt(string unit)
    {
        Transform body = Model(unit);

        foreach (CharacterBone bone in CharacterSkeleton.All)
        {
            string path = CharacterSkeleton.Path(bone);
            Assert.That(body.Find(path), Is.Not.Null, $"{unit} has no joint at {path}.");
        }
    }

    /// <summary>
    /// Every vertex locked to exactly one bone at full weight. A blended vertex would pinch the two
    /// interpenetrating solids it straddles, and one naming a bone the mesh has no bind pose for
    /// collapses to the model's origin — a spike out from between the unit's feet.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EverySurfaceIsRigidlyBoundToOneBoneEach(string unit)
    {
        Transform body = Model(unit);
        int bones = CharacterSkeleton.All.Length;

        foreach (string name in new[] { "Mesh", "TeamKit" })
        {
            SkinnedMeshRenderer surface = body.Find(name).GetComponent<SkinnedMeshRenderer>();
            Assert.That(surface, Is.Not.Null, $"{unit}/{name} is not skinned.");
            Assert.That(surface.bones.Length, Is.EqualTo(bones), $"{unit}/{name} is bound to the wrong rig.");
            Assert.That(surface.rootBone, Is.Not.Null, $"{unit}/{name} has no root bone.");

            Mesh mesh = surface.sharedMesh;
            Assert.That(mesh.bindposes.Length, Is.EqualTo(bones), $"{unit}/{name} is missing bind poses.");

            BoneWeight[] weights = mesh.boneWeights;
            Assert.That(weights.Length, Is.EqualTo(mesh.vertexCount), $"{unit}/{name} has unweighted vertices.");

            foreach (BoneWeight weight in weights)
            {
                Assert.That(weight.weight0, Is.EqualTo(1f).Within(0.0001f), $"{unit}/{name} has a blended vertex.");
                Assert.That(weight.weight1, Is.EqualTo(0f).Within(0.0001f), $"{unit}/{name} has a vertex on two bones.");
                Assert.That(weight.boneIndex0, Is.InRange(0, bones - 1), $"{unit}/{name} names a bone off the rig.");
            }
        }
    }

    /// <summary>
    /// The states <c>AnimationHandler</c> asks for by name. It cross-fades onto whatever the
    /// controller has and no-ops on what it has not, so a missing state is not an error in the
    /// console — it is a unit that never animates and nobody notices for a month.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void TheControllerCarriesTheStatesTheGameAsksFor(string unit)
    {
        AnimatorStateMachine machine = Controller(unit).layers[0].stateMachine;
        List<string> states = machine.states.Select(child => child.state.name).ToList();

        foreach (string state in new[] { "Idle", "Moving", "Aiming", "Shoot", "Dodge", "DiveRecovery" })
            Assert.That(states, Contains.Item(state), $"{unit} has no {state} state.");

        Assert.That(machine.defaultState.name, Is.EqualTo("Idle"), "A unit stands idle until it is told otherwise.");
    }

    /// <summary>
    /// Every curve in every clip resolves against the prefab it is written to drive, which is the
    /// test here that earns its keep: a clip with the wrong binding path or the wrong property name
    /// imports clean, plays clean, reports its full length, and moves nothing whatsoever.
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EveryCurveDrivesAJointThatExists(string unit)
    {
        Transform body = Model(unit);
        int curves = 0;

        foreach (ChildAnimatorState child in Controller(unit).layers[0].stateMachine.states)
        {
            AnimationClip clip = child.state.motion as AnimationClip;
            Assert.That(clip, Is.Not.Null, $"{unit}/{child.state.name} has no clip on it.");
            Assert.That(clip.length, Is.GreaterThan(0.1f), $"{unit}/{child.state.name} is an empty clip.");

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.type, Is.EqualTo(typeof(Transform)), $"{clip.name} drives a {binding.type}.");
                Assert.That(body.Find(binding.path), Is.Not.Null, $"{clip.name} drives {binding.path}, which {unit} has not got.");
                curves++;
            }
        }

        Assert.That(curves, Is.GreaterThan(40), $"{unit} is barely animated at {curves} curves.");
    }

    /// <summary>
    /// A unit's gun has to be on the line its own shot travels.
    ///
    /// <para>
    /// <c>Shooting</c> spawns a round at <c>transform.position</c> — the unit's root, the centre of
    /// the body — and sends it straight down the unit's forward axis. The rig draws the weapons
    /// somewhere else entirely: hung off a hand, out to one side and canted across the body. Left
    /// alone that is a unit firing out of its chest while its gun points somewhere else, which from
    /// the board camera, nearly overhead, is exactly the part that shows.
    /// </para>
    ///
    /// <para>
    /// So the aiming pose turns the chest until the bore crosses the centre line and the wrist until
    /// it runs straight down it. Both are solved rather than dialled in, which is why this asserts
    /// hundredths of a degree rather than something forgiving — anything looser would pass on a
    /// solve that had quietly stopped converging.
    /// </para>
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EveryUnitAimsAlongTheLineItsOwnShotTravels(string unit)
    {
        GameObject spawned = Object.Instantiate(Root(unit));
        try
        {
            Transform body = spawned.transform.Find("Body");
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"Assets/Animation/Characters/{unit}_Aiming.anim"
            );
            Assert.That(clip, Is.Not.Null, $"{unit} has no Aiming clip.");

            // Which hand holds the weapon is whichever one the clip turns: the aim correction is
            // applied to the hand the geometry follows and to no other. Read rather than restated,
            // so the bow being in the other hand cannot go stale here.
            Transform hand = WeaponHand(body, clip);
            Transform weapon = body.Find(CharacterSkeleton.Path(CharacterBone.Weapon));

            // A stand-in for the weapon, rigidly on that hand, sitting on the bore and aimed down it.
            Transform bore = new GameObject("Bore").transform;
            bore.SetParent(hand, false);
            bore.position = weapon.position;
            bore.rotation = Quaternion.LookRotation(weapon.up, Vector3.up);

            for (int step = 0; step <= 12; step++)
            {
                clip.SampleAnimation(body.gameObject, clip.length * step / 12f);

                Vector3 on = body.InverseTransformPoint(bore.position);
                Vector3 down = body.InverseTransformDirection(bore.forward).normalized;

                Assert.That(on.x, Is.EqualTo(0f).Within(0.002f), $"{unit}'s bore is off its own centre line.");
                Assert.That(
                    Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg,
                    Is.EqualTo(0f).Within(0.05f),
                    $"{unit}'s gun points across the line its shot travels."
                );
                Assert.That(
                    Mathf.Asin(Mathf.Clamp(down.y, -1f, 1f)) * Mathf.Rad2Deg,
                    Is.EqualTo(0f).Within(0.05f),
                    $"{unit}'s gun is not level, but its shot is."
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(spawned);
        }
    }

    private static Transform WeaponHand(Transform body, AnimationClip clip)
    {
        foreach (CharacterBone hand in new[] { CharacterBone.HandR, CharacterBone.HandL })
        {
            string path = CharacterSkeleton.Path(hand);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path == path && binding.propertyName.StartsWith("localEuler"))
                    return body.Find(path);
            }
        }

        Assert.Fail($"{clip.name} turns neither hand, so nothing is aiming the weapon.");
        return null;
    }

    /// <summary>
    /// No clip may rotate a bone that rests turned, which is the rule the rig is built around and
    /// the one worth a test of its own.
    ///
    /// <para>
    /// An Euler curve carries an absolute local rotation rather than an offset from rest, so a clip
    /// keying zero on a bone that rests aimed somewhere does not leave it alone — it snaps it
    /// square. On the weapon pivot, which rests aimed down the barrel, that tore Salvo's six
    /// barrels off the gun and left them hanging in the air beside it, reading as a second weapon.
    /// The rig answers it by splitting the aim onto a parent no clip touches; this is what keeps it
    /// answered.
    /// </para>
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void NoClipRotatesABoneThatRestsTurned(string unit)
    {
        Transform body = Model(unit);

        foreach (CharacterBone bone in CharacterSkeleton.All)
        {
            Transform joint = body.Find(CharacterSkeleton.Path(bone));
            bool square = Quaternion.Angle(joint.localRotation, Quaternion.identity) < 0.01f;

            Assert.That(
                square,
                Is.EqualTo(CharacterSkeleton.IsPosed(bone)),
                $"{unit}/{bone} rests {(square ? "square" : "turned")}, which contradicts CharacterSkeleton.IsPosed."
            );
        }

        foreach (ChildAnimatorState child in Controller(unit).layers[0].stateMachine.states)
        {
            AnimationClip clip = child.state.motion as AnimationClip;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("localEuler"))
                    continue;

                Transform driven = body.Find(binding.path);
                Assert.That(
                    Quaternion.Angle(driven.localRotation, Quaternion.identity),
                    Is.LessThan(0.01f),
                    $"{clip.name} rotates {binding.path}, which rests turned; the curve would discard its aim."
                );
            }
        }
    }

    /// <summary>
    /// Salvo's six barrels turn while it is firing and only while it is firing.
    ///
    /// <para>
    /// Aiming is the state that matters here and the reason it is asserted rather than assumed:
    /// <c>AnimationHandler.TriggerAnimation</c> hands back to whatever was being held once a round
    /// is away, which through a magazine is Aiming. A rotor turning in that clip is a rotor that
    /// never stops again once the unit has fired its first shot.
    /// </para>
    /// </summary>
    [Test]
    public void SalvosRotorTurnsOnlyWhileItIsFiring()
    {
        Assert.That(RotorTravel("Salvo", "Shoot"), Is.GreaterThan(360f), "The barrels barely move while firing.");
        Assert.That(RotorTravel("Salvo", "SuppressingFire"), Is.GreaterThan(720f), "A held burst should spin the rotor up.");

        foreach (string state in new[] { "Idle", "Moving", "Aiming", "Dodge", "DiveRecovery" })
            Assert.That(RotorTravel("Salvo", state), Is.EqualTo(0f), $"The rotor turns in {state}, so it never stops.");

        // Nothing holds the rotor at an angle, so a firing clip that stops part-way through a turn
        // gets counter-spun back to square by whatever plays next.
        foreach (string state in new[] { "Shoot", "SuppressingFire" })
        {
            Assert.That(
                RotorTravel("Salvo", state) % 360f,
                Is.EqualTo(0f).Within(0.5f),
                $"{state} leaves the rotor part-way through a turn; coming to rest will spin it backwards."
            );
        }

        foreach (string unit in Units)
        {
            if (unit != "Salvo")
                Assert.That(RotorTravel(unit, "Shoot"), Is.EqualTo(0f), $"{unit} has no rotor but turns one.");
        }
    }

    /// <summary>
    /// What the weapon does to the body that fires it. A rotary gun's shove arrives continuously
    /// rather than in rounds, so Salvo has to buzz where the rest of the roster kicks — and by a
    /// wide enough margin that nobody retunes it back by accident.
    /// </summary>
    [Test]
    public void ARotaryGunBuzzesWhereTheRosterKicks()
    {
        float salvo = Recoil("Salvo");

        // Six degrees as it ships, against eleven for the bow and thirty-one for the launcher.
        Assert.That(salvo, Is.LessThan(8f), $"Salvo kicks {salvo:0.0} degrees; a rotary gun does not kick.");

        foreach (string unit in Units)
        {
            if (unit == "Salvo")
                continue;

            Assert.That(
                Recoil(unit),
                Is.GreaterThan(salvo * 1.5f),
                $"{unit} recoils no harder than the rotary gun does."
            );
        }
    }

    /// <summary>
    /// Total degrees the rotor is turned through by one clip, read off its curves.
    ///
    /// <para>
    /// Taken as the widest of the three axes rather than the first one found. Setting any one
    /// Euler component makes Unity write the whole triple, so a rotor spun about Y alone still
    /// ships flat X and Z curves — and X is the one that comes back first.
    /// </para>
    /// </summary>
    private static float RotorTravel(string unit, string state)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            $"Assets/Animation/Characters/{unit}_{state}.anim"
        );
        Assert.That(clip, Is.Not.Null, $"{unit} has no {state} clip.");

        string rotor = CharacterSkeleton.Path(CharacterBone.Rotor);
        float turned = 0f;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path != rotor || !binding.propertyName.StartsWith("localEuler"))
                continue;

            (float low, float _, float high, float __) = Extremes(AnimationUtility.GetEditorCurve(clip, binding));
            turned = Mathf.Max(turned, high - low);
        }
        return turned;
    }

    /// <summary>How far the body — never the rotor — is thrown by the unit's own shot.</summary>
    private static float Recoil(string unit)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/Animation/Characters/{unit}_Shoot.anim");
        Assert.That(clip, Is.Not.Null, $"{unit} has no Shoot clip.");

        string rotor = CharacterSkeleton.Path(CharacterBone.Rotor);
        float worst = 0f;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (!binding.propertyName.StartsWith("localEuler") || binding.path == rotor)
                continue;

            (float low, float _, float high, float __) = Extremes(AnimationUtility.GetEditorCurve(clip, binding));
            worst = Mathf.Max(worst, high - low);
        }
        return worst;
    }

    /// <summary>
    /// How far a curve travels either side of where it starts, and when.
    ///
    /// <para>
    /// Measured against its own first key rather than against zero, which matters now that the
    /// firing clips hold a pose: a wrist rolled two degrees back is keyed as a constant 358, and
    /// read against zero that is a body flinging itself through most of a full turn.
    /// </para>
    /// </summary>
    private static (float Low, float LowAt, float High, float HighAt) Extremes(AnimationCurve curve)
    {
        if (curve.length == 0)
            return (0f, 0f, 0f, 0f);

        float low = curve[0].value;
        float high = curve[0].value;
        float lowAt = curve[0].time;
        float highAt = curve[0].time;

        for (int i = 0; i < curve.length; i++)
        {
            Keyframe key = curve[i];
            if (key.value < low)
            {
                low = key.value;
                lowAt = key.time;
            }
            if (key.value > high)
            {
                high = key.value;
                highAt = key.time;
            }
        }
        return (low, lowAt, high, highAt);
    }

    private static AnimatorController Controller(string unit)
    {
        Animator animator = Model(unit).GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null, $"{unit} has no Animator on its model root.");

        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        Assert.That(controller, Is.Not.Null, $"{unit} has no animator controller assigned.");
        return controller;
    }

    /// <summary>
    /// The board camera sits at 73 degrees, where a unit is very nearly silhouette alone. Eight
    /// characters that measure the same across are eight characters nobody can tell apart, which is
    /// exactly what the primitive stand-ins were.
    /// </summary>
    [Test]
    public void TheRosterHasEightDifferentSilhouettes()
    {
        List<(string Unit, Vector3 Size)> footprints = new();
        foreach (string unit in Units)
            footprints.Add((unit, Measure(Model(unit)).size));

        for (int i = 0; i < footprints.Count; i++)
        {
            for (int j = i + 1; j < footprints.Count; j++)
            {
                Vector3 a = footprints[i].Size;
                Vector3 b = footprints[j].Size;
                float apart = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
                Assert.That(
                    apart,
                    Is.GreaterThan(0.08f),
                    $"{footprints[i].Unit} and {footprints[j].Unit} measure the same from every side."
                );
            }
        }
    }

    private static GameObject Root(string unit)
    {
        string path = $"Assets/Prefabs/Units/{unit}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, $"Could not load {path}.");
        return prefab;
    }

    private static Transform Model(string unit)
    {
        Transform body = Root(unit).transform.Find("Body");
        Assert.That(body, Is.Not.Null, $"{unit} has no model root named Body.");
        return body;
    }

    private static Mesh Surface(string unit, string name)
    {
        Transform surface = Model(unit).Find(name);
        Assert.That(surface, Is.Not.Null, $"{unit} has no {name}.");

        Mesh mesh = MeshOf(surface.GetComponent<Renderer>());
        Assert.That(mesh, Is.Not.Null, $"{unit}/{name} has no mesh.");
        return mesh;
    }

    /// <summary>
    /// Every drawn surface in the model, as the node that carries it and the mesh it draws. Both
    /// rig generations land here: the generated characters are skinned now, so walking
    /// <c>MeshFilter</c> would find nothing at all, and what these tests are about is the shape
    /// that ships rather than which renderer ships it.
    ///
    /// <para>
    /// The node's own matrix is still the right one to read vertices through. Every bone rests
    /// unrotated at its joint, so a bind-posed skinned vertex is exactly where
    /// <c>CharacterBuilder</c> drew it in the model root's space.
    /// </para>
    /// </summary>
    private static List<(Transform Node, Mesh Mesh, Renderer Renderer)> Surfaces(Transform body)
    {
        List<(Transform, Mesh, Renderer)> found = new();
        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = MeshOf(renderer);
            if (mesh != null)
                found.Add((renderer.transform, mesh, renderer));
        }
        return found;
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer switch
        {
            null => null,
            SkinnedMeshRenderer skinned => skinned.sharedMesh,
            _ => renderer.GetComponent<MeshFilter>()?.sharedMesh,
        };

    /// <summary>
    /// Every vertex the model paints with <paramref name="colour"/>, in unit-root space. Submeshes
    /// run parallel to the renderer's material list, so the slot a triangle is in is the colour it
    /// comes out.
    /// </summary>
    private static List<Vector3> Painted(Transform body, string colour)
    {
        Matrix4x4 toRoot = body.parent.worldToLocalMatrix;
        List<Vector3> points = new();

        foreach ((Transform node, Mesh mesh, Renderer surface) in Surfaces(body))
        {
            Matrix4x4 matrix = toRoot * node.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = surface.sharedMaterials;

            for (int submesh = 0; submesh < mesh.subMeshCount && submesh < materials.Length; submesh++)
            {
                if (materials[submesh] == null || materials[submesh].name != colour)
                    continue;

                foreach (int index in mesh.GetTriangles(submesh))
                    points.Add(matrix.MultiplyPoint3x4(vertices[index]));
            }
        }

        return points;
    }

    private static List<Vector3> Above(List<Vector3> points, float height)
    {
        List<Vector3> kept = new();
        foreach (Vector3 point in points)
            if (point.y > height)
                kept.Add(point);
        return kept;
    }

    private static Bounds Box(List<Vector3> points)
    {
        Bounds box = new(points[0], Vector3.zero);
        foreach (Vector3 point in points)
            box.Encapsulate(point);
        return box;
    }

    /// <summary>
    /// The footprint of everything the model puts through the horizontal plane at
    /// <paramref name="height"/>, found by cutting triangles rather than by collecting vertices
    /// near it. A lofted body only has vertices where its sections are, so a band between two
    /// sections — which mid-thigh is, by construction — contains no vertices at all however solid
    /// the leg through it happens to be.
    /// </summary>
    private static Bounds SliceAt(Transform body, float height)
    {
        Matrix4x4 toRoot = body.parent.worldToLocalMatrix;
        bool cut = false;
        Bounds footprint = default;

        foreach ((Transform node, Mesh mesh, Renderer _) in Surfaces(body))
        {
            Matrix4x4 matrix = toRoot * node.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);

                    Crossing(a, b);
                    Crossing(b, c);
                    Crossing(c, a);
                }
            }
        }

        Assert.That(cut, Is.True, "Nothing crosses that height.");
        return footprint;

        void Crossing(Vector3 from, Vector3 to)
        {
            if ((from.y < height) == (to.y < height))
                return;

            Vector3 point = Vector3.Lerp(from, to, (height - from.y) / (to.y - from.y));
            if (!cut)
            {
                footprint = new Bounds(point, Vector3.zero);
                cut = true;
                return;
            }
            footprint.Encapsulate(point);
        }
    }

    /// <summary>
    /// The model's box in unit-root space, composed from mesh bounds rather than read off
    /// <c>Renderer.bounds</c>: a prefab asset is never in a scene, so its renderers have never been
    /// asked where they are.
    /// </summary>
    private static Bounds Measure(Transform body)
    {
        Matrix4x4 toRoot = body.parent.worldToLocalMatrix;
        bool measured = false;
        Bounds bounds = default;

        foreach ((Transform node, Mesh mesh, Renderer _) in Surfaces(body))
        {
            Matrix4x4 matrix = toRoot * node.localToWorldMatrix;
            Bounds local = mesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(
                    new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z
                    )
                );

                if (!measured)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    measured = true;
                    continue;
                }
                bounds.Encapsulate(point);
            }
        }

        Assert.That(measured, Is.True, "The model has no geometry at all.");
        return bounds;
    }
}
