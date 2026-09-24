using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the geometry the projectiles are drawn with, the same way
/// <see cref="ArenaBuilder"/> builds the board's props: flat-shaded triangles written to a mesh
/// asset and assigned to a prefab, so the shapes live in source rather than in a modelling file
/// nobody in the repository can open.
///
/// <para>
/// Both projectiles were Unity's default sphere. A sphere is the one silhouette that says nothing
/// about which way a thing is travelling or which way is up, and the board camera sits at 73
/// degrees where silhouette is very nearly all you get. A round now has a nose, and the grenade has
/// a top.
/// </para>
///
/// <para>
/// Two things here are not art decisions and may not be changed for one:
/// </para>
/// <list type="bullet">
/// <item>The round's <c>SphereCollider</c>. <c>Shooting.GetProjectileCollisionRadius</c> reads it
/// off the prefab to decide what a shot can hit, so the hitbox is gameplay and the mesh is only
/// what you see. The mesh is deliberately slimmer than the collider it flies inside.</item>
/// <item>The round is one mesh with one material. <c>Bullet.ApplyTeamPresentation</c> repaints a
/// shot for whoever is watching it through a single <c>GetComponentInChildren&lt;Renderer&gt;</c>,
/// so a second renderer on a round would keep the shooter's colour on one screen and not the
/// other. The grenade belongs to nobody and is free to carry two.</item>
/// </list>
/// </summary>
public static class ProjectileBuilder
{
    private const string MeshFolder = "Assets/Meshes/Projectiles/";
    private const string RoundMeshPath = MeshFolder + "Projectile_Round.asset";
    private const string PelletMeshPath = MeshFolder + "Projectile_Pellet.asset";
    private const string GrenadeMeshPath = MeshFolder + "Projectile_Grenade.asset";

    // BulletBlue is the base of the whole family: BulletRed, SniperSuperRed and SniperSuperBlue are
    // all variants of it and none of them overrides the mesh, so every round in the game is built
    // by writing this one prefab.
    private const string RoundPrefabPath = "Assets/Prefabs/Projectiles/BulletBlue.prefab";
    private const string RoundRedPrefabPath = "Assets/Prefabs/Projectiles/BulletRed.prefab";
    private const string PelletBluePrefabPath = "Assets/Prefabs/Projectiles/PelletBlue.prefab";
    private const string PelletRedPrefabPath = "Assets/Prefabs/Projectiles/PelletRed.prefab";
    private const string RamrodDataPath = "Assets/UnitStats/Ramrod.asset";
    private const string GrenadePrefabPath = "Assets/Prefabs/Projectiles/Grenade.prefab";

    private const string GrenadeMaterialPath = "Assets/Materials/Projectiles/Grenade.mat";
    private const string FittingsMaterialPath =
        "Assets/Materials/Projectiles/GrenadeFittings.mat";

    // Ten around. Enough that a round reads as turned rather than cut, few enough that the facets
    // still catch the key light one at a time, which is the whole reason the board's props are flat
    // shaded in the first place.
    private const int Segments = 10;

    // The round is authored at twice size: its prefab has carried a 0.5 scale since it was a
    // sphere, and unpicking that would move the collider with it.
    private const float RoundPrefabScale = 0.5f;

    [MenuItem("Battle Plan/Art/Build Projectiles", false, 11)]
    public static void BuildProjectiles()
    {
        EnsureFolder(MeshFolder);

        Mesh round = WriteMesh(BuildRound(), RoundMeshPath);
        Mesh pellet = WriteMesh(BuildPellet(), PelletMeshPath);
        Mesh grenade = WriteMesh(BuildGrenade(), GrenadeMeshPath);

        Material fittings = EnsureFittingsMaterial();

        int written = 0;
        written += ApplyRound(round) ? 1 : 0;
        written += ApplyPellets(pellet) ? 2 : 0;
        written += ApplyGrenade(grenade, fittings) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Bounds r = round.bounds;
        Bounds p = pellet.bounds;
        Bounds g = grenade.bounds;
        Debug.Log(
            $"[Projectiles] Built {written} prefab(s). "
                + $"Round {r.size * RoundPrefabScale} world, {round.triangles.Length / 3} tris. "
                + $"Pellet {p.size * RoundPrefabScale} world, {pellet.triangles.Length / 3} tris. "
                + $"Grenade {g.size} world, {grenade.triangles.Length / 3} tris."
        );
    }

    // ================================================================= the round

    /// <summary>
    /// A tracer round, nose down +Z because <c>Shooting.CreateBullet</c> spawns it with
    /// <c>Quaternion.LookRotation(direction)</c>.
    ///
    /// <para>
    /// Longer than it is wide by better than two to one, which is the only property that makes a
    /// shot read as travelling rather than hanging. It is also *thinner* than the sphere it
    /// replaces — 0.30 across against 0.50 — because the emissive material means a round is read by
    /// its light before its mass, and a fat tracer at this camera is a bead rather than a shot.
    /// </para>
    /// </summary>
    private static Mesh BuildRound()
    {
        var m = new Builder();

        // (z, radius). Flat tail, a short boat-tail flare into the body, then an ogive that gives
        // up its radius slowly at first so the nose is a point rather than a cone.
        var profile = new[]
        {
            new Vector2(-0.72f, 0.18f),
            new Vector2(-0.52f, 0.30f),
            new Vector2(0.16f, 0.30f),
            new Vector2(0.40f, 0.27f),
            new Vector2(0.62f, 0.19f),
            new Vector2(0.76f, 0.10f),
            new Vector2(0.84f, 0.00f),
        };

        m.AddLathe(profile, Vector3.forward, Vector3.right, Vector3.up, Segments, capStart: true);
        return m.Build("Projectile_Round");
    }

    // ================================================================= the pellet

    /// <summary>
    /// One piece of shot, for the Ramrod.
    ///
    /// <para>
    /// It is a ball, and being a ball is the point. The Ramrod empties ten rounds at hundredth-
    /// of-a-second intervals through a 25 degree cone: the direction of that is carried by the
    /// spread, not by any one piece in it, and shot has no nose to point anywhere. What it must not
    /// look like is the rifle round, and length does that on its own — a piece of shot is under
    /// half the round's length while being slightly wider than it, which is the proportion real
    /// buck has against a rifle bullet too.
    /// </para>
    /// <para>
    /// Width is where this was wrong the first time. Sized down to two thirds of the round it was
    /// a scatter of dots you had to look for, and ten dots that go unnoticed are worse than one
    /// round that does not. Wider than this and the cloud closes up into a single mass at the
    /// muzzle, which loses the count. Eight segments around, so the facets read as cast metal
    /// rather than as a ball the renderer failed to tessellate.
    /// </para>
    /// </summary>
    private static Mesh BuildPellet()
    {
        // The prefab carries a 0.5 scale, so a local radius is a world diameter and this number is
        // directly comparable with the round's 0.30 across.
        const float Radius = 0.36f;
        const int Rings = 5;

        var profile = new Vector2[Rings + 1];
        for (int i = 0; i <= Rings; i++)
        {
            float theta = i / (float)Rings * Mathf.PI;
            profile[i] = new Vector2(Radius * Mathf.Cos(theta), Radius * Mathf.Sin(theta));
        }

        var m = new Builder();
        m.AddLathe(profile, Vector3.up, Vector3.right, Vector3.forward, 8);
        return m.Build("Projectile_Pellet");
    }

    // ================================================================= the grenade

    /// <summary>
    /// A grenade, standing on +Y.
    ///
    /// <para>
    /// The body is a barrel rather than a ball: widest below the middle, so it reads as something
    /// with a base even while it is tumbling through the air. What actually identifies it is the
    /// pair of fittings on top — the fuse housing and the lever hooked over it — and those are the
    /// submesh the dark material lands on. A green pill with no fittings is a pill.
    /// </para>
    /// </summary>
    private static Mesh BuildGrenade()
    {
        var m = new Builder();

        // Submesh 0, the body.
        var body = new[]
        {
            new Vector2(-0.390f, 0.115f),
            new Vector2(-0.345f, 0.220f),
            new Vector2(-0.230f, 0.310f),
            new Vector2(-0.045f, 0.345f),
            new Vector2(0.140f, 0.335f),
            new Vector2(0.280f, 0.275f),
            new Vector2(0.345f, 0.185f),
        };
        m.AddLathe(body, Vector3.up, Vector3.right, Vector3.forward, Segments, capStart: true);

        // Submesh 1, the fittings. The housing closes the body's open shoulder, so the barrel above
        // needs no cap of its own.
        m.Submesh = 1;
        var housing = new[]
        {
            new Vector2(0.345f, 0.185f),
            new Vector2(0.375f, 0.150f),
            new Vector2(0.505f, 0.150f),
        };
        m.AddLathe(housing, Vector3.up, Vector3.right, Vector3.forward, Segments, capEnd: true);

        // The lever: an arm down the side and a hook across the housing. The hook overlaps the
        // housing's radius by two centimetres so the two are one object rather than a bar floating
        // beside a can.
        m.AddBox(new Vector3(0.310f, -0.045f, -0.075f), new Vector3(0.375f, 0.370f, 0.075f));
        m.AddBox(new Vector3(0.115f, 0.370f, -0.075f), new Vector3(0.375f, 0.430f, 0.075f));

        return m.Build("Projectile_Grenade");
    }

    // ================================================================= prefab wiring

    private static bool ApplyRound(Mesh mesh)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RoundPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Projectiles] Missing {RoundPrefabPath}.");
            return false;
        }

        try
        {
            var filter = root.GetComponent<MeshFilter>();
            if (filter == null)
            {
                Debug.LogError("[Projectiles] BulletBlue has no MeshFilter.");
                return false;
            }

            filter.sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(root, RoundPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// The Ramrod's shot, as variants of the two rounds rather than prefabs of their own. The
    /// <c>Bullet</c> script, the rigidbody, the team materials and — the one that matters — the
    /// <c>SphereCollider</c> the hit test is measured off all stay inherited, so this changes what
    /// a blast looks like and nothing whatsoever about what it does.
    /// </summary>
    private static bool ApplyPellets(Mesh mesh)
    {
        bool blue = WritePelletVariant(RoundPrefabPath, PelletBluePrefabPath, "PelletBlue", mesh);
        bool red = WritePelletVariant(RoundRedPrefabPath, PelletRedPrefabPath, "PelletRed", mesh);
        if (!blue || !red)
            return false;

        var data = AssetDatabase.LoadAssetAtPath<UnitData>(RamrodDataPath);
        if (data == null)
        {
            Debug.LogError($"[Projectiles] Missing {RamrodDataPath}.");
            return false;
        }

        data.blueBulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PelletBluePrefabPath);
        data.redBulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PelletRedPrefabPath);
        EditorUtility.SetDirty(data);
        return true;
    }

    private static bool WritePelletVariant(
        string basePath,
        string variantPath,
        string variantName,
        Mesh mesh
    )
    {
        var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
        if (baseAsset == null)
        {
            Debug.LogError($"[Projectiles] Missing {basePath}.");
            return false;
        }

        // Staged in a preview scene: building art must not dirty whichever scene happens to be
        // open when someone runs the menu item.
        Scene staging = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset, staging);
            instance.name = variantName;

            var filter = instance.GetComponent<MeshFilter>();
            if (filter == null)
            {
                Debug.LogError($"[Projectiles] {basePath} has no MeshFilter.");
                return false;
            }

            filter.sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            return true;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(staging);
        }
    }

    private static bool ApplyGrenade(Mesh mesh, Material fittings)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GrenadePrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Projectiles] Missing {GrenadePrefabPath}.");
            return false;
        }

        try
        {
            var filter = root.GetComponent<MeshFilter>();
            var renderer = root.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
            {
                Debug.LogError("[Projectiles] Grenade has no MeshFilter/MeshRenderer.");
                return false;
            }

            filter.sharedMesh = mesh;

            Material shell = AssetDatabase.LoadAssetAtPath<Material>(GrenadeMaterialPath);
            if (shell == null)
                shell = renderer.sharedMaterial;
            renderer.sharedMaterials = new[] { shell, fittings };

            PrefabUtility.SaveAsPrefabAsset(root, GrenadePrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// The dark the housing and lever are painted. Made here rather than by hand so the shape and
    /// the second slot it needs arrive in the same commit.
    /// </summary>
    private static Material EnsureFittingsMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(FittingsMaterialPath);
        if (existing != null)
            return existing;

        Material shell = AssetDatabase.LoadAssetAtPath<Material>(GrenadeMaterialPath);
        Shader shader =
            shell != null ? shell.shader : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[Projectiles] No URP Lit shader for the fittings material.");
            return shell;
        }

        var material = new Material(shader) { name = "GrenadeFittings" };
        // Darker than anything on the deck, which runs 181-184: the fittings have to read as a
        // hole in the body's green from directly above, and a mid grey up there is just a lighter
        // green.
        var ink = new Color(0.105f, 0.115f, 0.125f, 1f);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", ink);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", ink);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.35f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        AssetDatabase.CreateAsset(material, FittingsMaterialPath);
        return material;
    }

    // ================================================================= asset plumbing

    /// <summary>Same contract as <c>ArenaBuilder.WriteMesh</c>: rewrite in place so every prefab
    /// already pointing at the asset keeps pointing at it.</summary>
    private static Mesh WriteMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
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
    /// Accumulates flat-shaded triangles across submeshes. Winding comes from a normal hint, as in
    /// <c>ArenaBuilder.MeshBuilder</c>, so a reversed ring can never ship inside out. The extra
    /// primitive here is the lathe: the board is made of boxes and both of these are turned.
    /// </summary>
    private class Builder
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<List<int>> submeshes = new() { new List<int>() };

        public int Submesh { get; set; }

        private List<int> Triangles
        {
            get
            {
                while (submeshes.Count <= Submesh)
                    submeshes.Add(new List<int>());
                return submeshes[Submesh];
            }
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normalHint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            if (Vector3.Dot(n, normalHint) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }

            int at = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(n);
            normals.Add(n);
            normals.Add(n);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0.5f, 1f));
            Triangles.Add(at);
            Triangles.Add(at + 1);
            Triangles.Add(at + 2);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normalHint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            if (Vector3.Dot(n, normalHint) < 0f)
            {
                (b, d) = (d, b);
                n = -n;
            }

            int at = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            for (int i = 0; i < 4; i++)
                normals.Add(n);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
            Triangles.Add(at);
            Triangles.Add(at + 1);
            Triangles.Add(at + 2);
            Triangles.Add(at);
            Triangles.Add(at + 2);
            Triangles.Add(at + 3);
        }

        /// <summary>
        /// Revolves a profile of (distance along <paramref name="axis"/>, radius) pairs. A ring of
        /// zero radius closes itself with a fan, so a nose needs no special case; the flat ends the
        /// caller does want closed are asked for explicitly.
        /// </summary>
        public void AddLathe(
            IReadOnlyList<Vector2> profile,
            Vector3 axis,
            Vector3 u,
            Vector3 v,
            int segments,
            bool capStart = false,
            bool capEnd = false
        )
        {
            if (profile.Count < 2 || segments < 3)
                return;

            Vector3 Point(Vector2 ring, int segment)
            {
                float angle = segment / (float)segments * Mathf.PI * 2f;
                return axis * ring.x + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * ring.y;
            }

            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 lo = profile[i];
                Vector2 hi = profile[i + 1];

                for (int s = 0; s < segments; s++)
                {
                    int t = (s + 1) % segments;

                    // The outward hint is the ring's own direction, tilted along the axis by how
                    // much the profile is opening or closing across this band.
                    Vector3 mid = (Point(lo, s) + Point(lo, t) + Point(hi, s) + Point(hi, t)) * 0.25f;
                    Vector3 outward = Vector3.ProjectOnPlane(mid, axis);
                    if (outward.sqrMagnitude < 1e-10f)
                        outward = u;
                    Vector3 hint = (outward.normalized + axis * (lo.y - hi.y)).normalized;

                    if (lo.y <= 1e-6f)
                        AddTriangle(Point(lo, s), Point(hi, s), Point(hi, t), hint);
                    else if (hi.y <= 1e-6f)
                        AddTriangle(Point(lo, s), Point(lo, t), Point(hi, s), hint);
                    else
                        AddQuad(Point(lo, s), Point(lo, t), Point(hi, t), Point(hi, s), hint);
                }
            }

            if (capStart && profile[0].y > 1e-6f)
                Cap(profile[0], -axis);
            if (capEnd && profile[profile.Count - 1].y > 1e-6f)
                Cap(profile[profile.Count - 1], axis);

            void Cap(Vector2 ring, Vector3 facing)
            {
                Vector3 centre = axis * ring.x;
                for (int s = 0; s < segments; s++)
                {
                    int t = (s + 1) % segments;
                    AddTriangle(centre, Point(ring, s), Point(ring, t), facing);
                }
            }
        }

        public void AddBox(Vector3 min, Vector3 max)
        {
            var a = new Vector3(min.x, min.y, min.z);
            var b = new Vector3(max.x, min.y, min.z);
            var c = new Vector3(max.x, min.y, max.z);
            var d = new Vector3(min.x, min.y, max.z);
            var e = new Vector3(min.x, max.y, min.z);
            var f = new Vector3(max.x, max.y, min.z);
            var g = new Vector3(max.x, max.y, max.z);
            var h = new Vector3(min.x, max.y, max.z);

            AddQuad(a, b, f, e, Vector3.back);
            AddQuad(d, c, g, h, Vector3.forward);
            AddQuad(b, c, g, f, Vector3.right);
            AddQuad(a, d, h, e, Vector3.left);
            AddQuad(e, f, g, h, Vector3.up);
            AddQuad(a, b, c, d, Vector3.down);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++)
                mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
