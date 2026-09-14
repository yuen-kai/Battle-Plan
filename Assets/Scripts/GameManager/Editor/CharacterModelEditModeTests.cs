using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
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
        foreach (MeshFilter filter in Model(unit).GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            Assert.That(mesh, Is.Not.Null, $"{unit}/{filter.name} has no mesh.");
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                triangles += (int)mesh.GetIndexCount(submesh) / 3;
        }

        Assert.That(triangles, Is.LessThanOrEqualTo(TriangleBudget));
        Assert.That(triangles, Is.GreaterThan(400), "A body this cheap is a box with a head on it.");
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

        MeshFilter filter = surface.GetComponent<MeshFilter>();
        Assert.That(filter, Is.Not.Null, $"{unit}/{name} has no mesh filter.");
        Assert.That(filter.sharedMesh, Is.Not.Null, $"{unit}/{name} has no mesh.");
        return filter.sharedMesh;
    }

    /// <summary>
    /// Every vertex the model paints with <paramref name="colour"/>, in unit-root space. Submeshes
    /// run parallel to the renderer's material list, so the slot a triangle is in is the colour it
    /// comes out.
    /// </summary>
    private static List<Vector3> Painted(Transform body, string colour)
    {
        Matrix4x4 toRoot = body.parent.worldToLocalMatrix;
        List<Vector3> points = new();

        foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            Renderer surface = filter.GetComponent<Renderer>();
            if (mesh == null || surface == null)
                continue;

            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
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

        foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Vector3[] vertices = filter.sharedMesh.vertices;
            for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
            {
                int[] triangles = filter.sharedMesh.GetTriangles(submesh);
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

        foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;
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
