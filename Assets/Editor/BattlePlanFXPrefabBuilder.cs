using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the FX prefabs and re-applies the §8 projectile specification.
///
/// Two menu items, deliberately separate because they have different risk profiles:
///
///   Battle Plan ▸ FX ▸ Build FX Prefabs
///       Regenerates Assets/Resources/FX/*.prefab from nothing. These are wholly generated and
///       referenced only by path from FXAssets, so recreating them is safe and gives a clean
///       deterministic result every run.
///
///   Battle Plan ▸ FX ▸ Build Projectile Prefabs
///       Edits Assets/Prefabs/Projectiles/*.prefab IN PLACE. These are registered in
///       DefaultNetworkPrefabs.asset by GUID, so they are never deleted and never recreated —
///       doing so silently breaks the network prefab list. Every change here is find-or-create by
///       child name and every value is assigned absolutely, and the six are written bases-first
///       (see BuildProjectilePrefabs), so running it twice is a no-op.
///
/// COLLIDERS ARE NEVER TOUCHED (§8 opening line). Shooting.cs runs a projectile-radius SphereCast
/// for line of sight, so a projectile's SphereCollider radius is a gameplay value. This script
/// changes meshes, scales, materials, trails and components only, and it asserts on exit that no
/// collider moved.
///
/// Distinct from Assets/Editor/ArenaBuilder.cs in class, file and menu path, per the parallel-work
/// convention.
/// </summary>
public static class BattlePlanFXPrefabBuilder
{
    // =====================================================================================
    // PATHS
    // =====================================================================================

    private const string FXPrefabFolder = "Assets/Resources/FX";
    private const string ProjectileFolder = "Assets/Prefabs/Projectiles";
    private const string FXMaterialFolder = "Assets/Materials/FX";
    private const string ProjectileMaterialFolder = "Assets/Materials/Projectiles";

    // =====================================================================================
    // GEOMETRY — ArtDirection §8. Every dimension in world units.
    // =====================================================================================

    // §8.1 Bullet: a 0.11-diameter, 0.42-long slug aligned to travel.
    private static readonly Vector3 BulletScale = new(0.11f, 0.21f, 0.11f);
    private static readonly Vector3 ProjectileRotation = new(90f, 0f, 0f);

    // === The ink contour shell (§8.1, §4.3) ===
    // An inverted hull: the same primitive, grown by a fixed shell thickness, drawn front-culled in
    // FX_Contour so only its far faces survive. The body is opaque and writes depth first, so the
    // shell is rejected everywhere the body covers and survives only as a rim outside the
    // silhouette. That rim is what makes a small saturated object legible on pale concrete.
    //
    // A THICKNESS, NOT A MULTIPLIER. §8.1 gives the bullet's shell as 0.11 -> 0.15 radially and
    // 0.21 -> 0.24 axially, which is a constant offset rather than a constant ratio, and §8.2 then
    // describes the sniper's as "1.4x per §8.1". Taken literally on a lance that is 1.36 units long
    // a 1.4x axial scale adds half a unit of length and the contour reads as a longer projectile
    // rather than an outline on this one. Applying the bullet's measured offsets instead puts the
    // sniper at 1.44x radially — which is what "1.4x" was describing — and 1.04x along its length.
    //
    // MEASURED FROM THE WIDEST ELEMENT, NOT THE BODY. On the two capsules the body is the whole
    // silhouette, but the grenade's cap ring is 0.34 against a 0.30 body and the canister's ribs
    // are 0.48 against 0.45. Growing the body by the shell would land the grenade's contour at
    // exactly 0.34 — coincident with its own band, z-fighting it and enclosing nothing — and the
    // canister's 0.01 outside its ribs, which is sub-pixel at the tactical camera. So each caller
    // passes the widest profile it draws and the shell wraps that.
    //
    // The radial shell is a diameter, so the drawn rim is half of it. 0.042 gives 0.021 per side,
    // which is ArtDirection 4.3's moving-contour floor of 0.78 screen px on the projectile plane.
    // The old 0.04 gave 0.020 and missed it by 3%. The axial 0.03 stays: the law binds on the
    // thinnest rim, and the axial figure already clears the floor.
    private const string ContourChildName = "Contour";
    private const float ContourShellRadial = 0.042f;
    private const float ContourShellAxial = 0.03f;
    private const float BulletTrailTime = 0.1f;
    private const float BulletTrailStartWidth = 0.09f;

    // §8.1's tracer alpha at the muzzle end, shared by the bullet and the sniper lance. The tracer
    // is drawn to darken the deck it crosses, so it is deliberately short of opaque — see
    // TracerFadeGradient for why the -hi hue this rides on lives on the material and not here.
    private const float TracerHeadAlpha = 0.7f;

    // §8.2 Sniper: 0.09 x 1.36, half a cell long. The length is a strobing fix, not a style
    // choice — 15 cells/s is 0.68 units per frame at 60 fps.
    private static readonly Vector3 SniperScale = new(0.09f, 0.68f, 0.09f);
    private const float SniperTrailTime = 0.18f;
    private const float SniperTrailStartWidth = 0.16f;

    // === Team colour, §8.1 and §8.2 ===
    // The slots of Bullet.teamMaterials: index 0 is the shot as own fire, index 1 as incoming.
    // Both players read their own crew as blue, so the pair is viewer-relative and identical on
    // every prefab; which side actually fired is fixed by which prefab spawned. See
    // TeamFireMaterials.
    private const int OwnFireSlot = 0;
    private const int IncomingFireSlot = 1;

    // §8.3 Grenade: 8-sided cylinder 0.30 x 0.42, 0.34 x 0.06 cap ring, 0.10 fuse dot.
    private static readonly Vector3 GrenadeBodyScale = new(0.3f, 0.21f, 0.3f);
    private static readonly Vector3 GrenadeBandScale = new(0.34f, 0.04f, 0.34f);
    private const float GrenadeFuseDiameter = 0.1f;
    private const float GrenadeFuseHeight = 0.24f;
    private const float GrenadeTrailTime = 0.35f;
    private const float GrenadeTrailStartWidth = 0.05f;

    // §8.4 Smoke canister: 0.45 x 0.80 with two 0.48 x 0.05 cap ribs. No trail — it is not a
    // weapon and must not read as one.
    private static readonly Vector3 CanisterBodyScale = new(0.45f, 0.4f, 0.45f);
    private static readonly Vector3 CanisterRibScale = new(0.48f, 0.025f, 0.48f);
    private const float CanisterRibOffset = 0.34f;
    private const float CanisterCapDotDiameter = 0.1f;
    private const float CanisterCapDotHeight = 0.42f;

    // §8.5 Muzzle flash: 0.55 quad, life 0.06 s, scaling to 0.80.
    private const float MuzzleQuadSize = 0.55f;

    // =====================================================================================
    // PARTICLES — ArtDirection §8.5 and §9.3. Counts and speeds are set per call by
    // ParticleBurstFX; these are the structural defaults baked into the prefab.
    // =====================================================================================

    private const int SparkMaxParticles = 64;
    private const float SparkStretchSpeedScale = 0.06f;
    private const int DebrisMaxParticles = 32;
    private const int DustMaxParticles = 32;
    private const float DustRotationRange = 180f;

    // =====================================================================================
    // MENU ITEMS
    // =====================================================================================

    [MenuItem("Battle Plan/FX/Build FX Prefabs")]
    public static void BuildFXPrefabs()
    {
        EnsureFolder(FXPrefabFolder);

        BuildSparkBurst();
        BuildDebrisBurst();
        BuildDustBurst();
        BuildMuzzleFlash();
        BuildCoreSphere();
        BuildGroundGlowQuad();
        BuildScorchQuad();
        BuildSlashQuad();
        BuildSmokePuffQuad();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BattlePlanFXPrefabBuilder] Built 9 FX prefabs into {FXPrefabFolder}.");
    }

    [MenuItem("Battle Plan/FX/Build Projectile Prefabs")]
    public static void BuildProjectilePrefabs()
    {
        ProjectileJob[] jobs =
        {
            BulletJob("BulletBlue", OwnFireSlot),
            BulletJob("BulletRed", IncomingFireSlot),
            SniperJob("SniperSuperBlue", OwnFireSlot),
            SniperJob("SniperSuperRed", IncomingFireSlot),
            GrenadeJob(),
            SmokeCanisterJob(),
        };

        foreach (ProjectileJob job in OrderBasesBeforeVariants(jobs))
            EditProjectile(job);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[BattlePlanFXPrefabBuilder] Applied §8 geometry to {jobs.Length} projectile prefabs in place."
        );
    }

    /// <summary>
    /// One prefab's worth of work. Kept as data rather than as a straight sequence of calls so the
    /// six can be reordered before any of them is written — see OrderBasesBeforeVariants.
    /// </summary>
    private readonly struct ProjectileJob
    {
        public readonly string PrefabName;
        public readonly ProjectileFX.ProjectileKind Kind;
        public readonly System.Action<GameObject> Mutate;

        public ProjectileJob(
            string prefabName,
            ProjectileFX.ProjectileKind kind,
            System.Action<GameObject> mutate
        )
        {
            PrefabName = prefabName;
            Kind = kind;
            Mutate = mutate;
        }
    }

    /// <summary>
    /// Sorts so a prefab is always written before anything that inherits from it. Four of the six
    /// are variants: BulletRed and SniperSuperRed are variants of BulletBlue, and SniperSuperBlue
    /// is a variant of SniperSuperRed.
    ///
    /// This is not tidiness, it is the difference between one build being enough and two being
    /// needed. PrefabUtility.SaveAsPrefabAsset records a variant's overrides as the diff against
    /// its base <i>as the base stands at that moment</i>. Writing SniperSuperBlue while
    /// SniperSuperRed still held the old shared tracer produced no override at all — the two
    /// agreed — and the very next call then moved the base to FX_BeamRed and took the blue lance
    /// with it. The override only appeared on the second run, so a single build shipped a blue
    /// sniper round trailing red. Sorting by depth fixes the cause; running the builder twice
    /// fixes one symptom and leaves the ordering free to bite again the next time a variant is
    /// added.
    ///
    /// OrderBy is a stable sort, so prefabs at equal depth keep their authored order.
    /// </summary>
    private static IEnumerable<ProjectileJob> OrderBasesBeforeVariants(
        IEnumerable<ProjectileJob> jobs
    )
    {
        return jobs.OrderBy(job => VariantDepth(ProjectilePath(job.PrefabName)));
    }

    /// <summary>
    /// How many prefab bases sit above this asset: 0 for a plain prefab, 1 for a variant, 2 for a
    /// variant of a variant. Used only for ordering, so a missing asset or a self-referential
    /// chain degrades to "treat it as a base" rather than throwing.
    /// </summary>
    private static int VariantDepth(string path)
    {
        const int maxChain = 8;
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        int depth = 0;
        while (
            asset != null
            && depth < maxChain
            && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant
        )
        {
            GameObject basePrefab = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            if (basePrefab == null || basePrefab == asset)
                break;
            asset = basePrefab;
            depth++;
        }
        return depth;
    }

    // =====================================================================================
    // FX PREFABS
    // =====================================================================================

    private static void BuildSparkBurst()
    {
        GameObject root = new("Burst_Sparks");
        ParticleSystem system = root.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        ConfigureBurstMain(main, SparkMaxParticles);
        main.startSize = 0.05f;
        main.gravityModifier = 2f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        ParticleSystemRenderer particleRenderer = root.GetComponent<ParticleSystemRenderer>();
        ConfigureBurstRenderer(particleRenderer, LoadFXMaterial("FX_Spark"));
        // Stretched billboard is what turns a dot into a streak travelling away from the impact.
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = SparkStretchSpeedScale;
        particleRenderer.lengthScale = 2f;

        SaveFXPrefab(root);
    }

    private static void BuildDebrisBurst()
    {
        GameObject root = new("Burst_Debris");
        ParticleSystem system = root.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        ConfigureBurstMain(main, DebrisMaxParticles);
        main.startSize = 0.06f;
        main.gravityModifier = 3f;
        // Chips tumble. Sparks do not.
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.08f;

        ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        ConfigureBurstRenderer(
            root.GetComponent<ParticleSystemRenderer>(),
            LoadFXMaterial("FX_Debris")
        );
        SaveFXPrefab(root);
    }

    private static void BuildDustBurst()
    {
        GameObject root = new("Burst_Dust");
        ParticleSystem system = root.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        ConfigureBurstMain(main, DustMaxParticles);
        main.startSize = 0.4f;
        // Dust rises and settles; it must not fall like debris.
        main.gravityModifier = -0.05f;
        main.startRotation = new ParticleSystem.MinMaxCurve(
            -DustRotationRange * Mathf.Deg2Rad,
            DustRotationRange * Mathf.Deg2Rad
        );

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.6f;
        shape.radiusThickness = 1f;

        ParticleSystem.SizeOverLifetimeModule growth = system.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f)
        );

        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(FadeOutGradient());

        ConfigureBurstRenderer(
            root.GetComponent<ParticleSystemRenderer>(),
            LoadFXMaterial("FX_Dust")
        );
        SaveFXPrefab(root);
    }

    private static void BuildMuzzleFlash()
    {
        GameObject root = CreatePrimitiveRoot("Quad_MuzzleFlash", PrimitiveType.Quad);
        root.transform.localScale = Vector3.one * MuzzleQuadSize;
        AssignMaterial(root, LoadFXMaterial("FX_MuzzleFlash"));
        root.AddComponent<MuzzleFlashFX>();
        SaveFXPrefab(root);
    }

    private static void BuildCoreSphere()
    {
        GameObject root = CreatePrimitiveRoot("Sphere_Core", PrimitiveType.Sphere);
        AssignMaterial(root, LoadFXMaterial("FX_CoreWhite"));
        root.AddComponent<FXImpactSprite>();
        SaveFXPrefab(root);
    }

    private static void BuildGroundGlowQuad()
    {
        // Rotation is applied at spawn by GroundTelegraph, not baked here, because the same prefab
        // is also parented flat against a shield face by ShieldFX.
        GameObject root = CreatePrimitiveRoot("Quad_GroundGlow", PrimitiveType.Quad);
        AssignMaterial(root, LoadFXMaterial("FX_GroundGlow"));
        root.AddComponent<GroundTelegraph>();
        SaveFXPrefab(root);
    }

    private static void BuildScorchQuad()
    {
        GameObject root = CreatePrimitiveRoot("Quad_Scorch", PrimitiveType.Quad);
        root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        AssignMaterial(root, LoadFXMaterial("FX_Scorch"));
        root.AddComponent<ScorchDecal>();
        SaveFXPrefab(root);
    }

    private static void BuildSlashQuad()
    {
        GameObject root = CreatePrimitiveRoot("Quad_Slash", PrimitiveType.Quad);
        AssignMaterial(root, LoadFXMaterial("FX_Slash"));
        root.AddComponent<FXImpactSprite>();
        SaveFXPrefab(root);
    }

    private static void BuildSmokePuffQuad()
    {
        GameObject root = CreatePrimitiveRoot("Quad_SmokePuff", PrimitiveType.Quad);
        AssignMaterial(root, LoadFXMaterial("FX_SmokePuff"));
        SaveFXPrefab(root);
    }

    // =====================================================================================
    // PROJECTILE PREFABS — in place, never recreated
    // =====================================================================================

    private static ProjectileJob BulletJob(string prefabName, int fireSlot)
    {
        return new ProjectileJob(
            prefabName,
            ProjectileFX.ProjectileKind.Bullet,
            root =>
            {
                // §8.1: the bullet's body carries the team colour, so it takes the same slot as
                // its tracer and is drawn out of the same array the runtime will repaint it from.
                Material[] teamBody = TeamBodyMaterials();
                ApplySlugGeometry(root, BulletScale, teamBody[fireSlot]);
                ApplyTeamFire(root, fireSlot, BulletTrailTime, BulletTrailStartWidth, teamBody);
            }
        );
    }

    private static ProjectileJob SniperJob(string prefabName, int fireSlot)
    {
        return new ProjectileJob(
            prefabName,
            ProjectileFX.ProjectileKind.Sniper,
            root =>
            {
                // §8.2: the lance is --bp-ink on both sides of the board and takes its team colour
                // from the trail alone, so it is authored with no body pair at all. See
                // Bullet.bodyTeamMaterials for why that is deliberate rather than an omission.
                ApplySlugGeometry(root, SniperScale, LoadProjectileMaterial("SniperSuperBullet"));
                ApplyTeamFire(
                    root,
                    fireSlot,
                    SniperTrailTime,
                    SniperTrailStartWidth,
                    teamBody: null
                );
            }
        );
    }

    /// <summary>
    /// The ordered tracer pair every bullet and sniper prefab answers to, indexed by
    /// <see cref="OwnFireSlot"/> and <see cref="IncomingFireSlot"/>.
    ///
    /// This exists because the two ends of a shot's team colour used to be authored in different
    /// places. Bullet.ApplyTeamPresentation repaints the first Renderer under the prefab with one
    /// of these two at spawn; the builder authors that same renderer's tracer. The list was
    /// maintained by hand and still named the pre-FIELD DAY body materials, so every shot repainted
    /// itself out of the redesign on the frame it appeared — a tracer authored FX_BeamBlue came
    /// back as BulletBlue. Both ends are now read from here, and EditProjectile refuses to save a
    /// prefab where they disagree.
    ///
    /// The tracer is also the right channel to carry identity rather than an accident of which
    /// renderer is found first: §8.2 authors the sniper round itself in ink and says in as many
    /// words that its team colour comes from the trail.
    ///
    /// THESE TWO ARE FLAT --bp-blue-hi / --bp-red-hi WITH NO HOT CORE. They share a shader and a
    /// name with the Area Lock and target-lock beams, and palette §6 licenses an HDR core for
    /// those — but those build their materials at runtime in BeamVFX from FXPalette.TeamBeamCore
    /// and never load these assets. A tracer is not one of §6's seven emitters, and it fails
    /// §9.2.1's point-flash test on extent, so it gets no HDR at all. Authored the other way the
    /// streak measured *lighter* than the deck it crossed, which is the one thing §8.1 says a
    /// tracer must never be.
    /// </summary>
    private static Material[] TeamFireMaterials()
    {
        return new[] { LoadFXMaterial("FX_BeamBlue"), LoadFXMaterial("FX_BeamRed") };
    }

    /// <summary>
    /// The same two slots for the slug itself, §8.1. Separate from <see cref="TeamFireMaterials"/>
    /// because it lands on a different renderer — the body sits on Bullet.BodyChildName while the
    /// tracer sits on the root — and pinning the tracer alone was only half the fix: a client still
    /// saw its own fire as a red slug behind a blue tracer, because the body kept the absolute
    /// colour its prefab was authored in.
    /// </summary>
    private static Material[] TeamBodyMaterials()
    {
        return new[] { LoadProjectileMaterial("BulletBlue"), LoadProjectileMaterial("BulletRed") };
    }

    /// <summary>
    /// Authors the tracer and, where the projectile has one, the body — then fills in the two lists
    /// Bullet.ApplyTeamPresentation reads from the very arrays just drawn, so the material a prefab
    /// is drawn in and the material it is repainted with at spawn cannot drift apart.
    ///
    /// A null <paramref name="teamBody"/> writes an empty list rather than leaving the field alone:
    /// "this body is not team-coloured" is an authored state and has to survive a prefab that once
    /// carried a pair.
    /// </summary>
    private static void ApplyTeamFire(
        GameObject root,
        int fireSlot,
        float trailTime,
        float trailStartWidth,
        Material[] teamBody
    )
    {
        Material[] teamFire = TeamFireMaterials();
        ApplyTrail(root, teamFire[fireSlot], trailTime, trailStartWidth);

        if (!root.TryGetComponent(out Bullet bullet))
            return;

        bullet.teamMaterials = new List<Material>(teamFire);
        bullet.bodyTeamMaterials =
            teamBody != null ? new List<Material>(teamBody) : new List<Material>();
    }

    private static ProjectileJob GrenadeJob()
    {
        return new ProjectileJob(
            "Grenade",
            ProjectileFX.ProjectileKind.Grenade,
            root =>
            {
                SetMesh(
                    root,
                    PrimitiveType.Cylinder,
                    GrenadeBodyScale,
                    Vector3.zero,
                    LoadProjectileMaterial("Grenade")
                );

                FindOrCreateChild(
                    root,
                    "Band",
                    PrimitiveType.Cylinder,
                    GrenadeBandScale,
                    Vector3.zero,
                    LoadProjectileMaterial("GrenadeBand")
                );
                FindOrCreateChild(
                    root,
                    ProjectileFX.FuseDotChildName,
                    PrimitiveType.Sphere,
                    Vector3.one * GrenadeFuseDiameter,
                    new Vector3(0f, GrenadeFuseHeight, 0f),
                    LoadProjectileMaterial("GrenadeFuse")
                );

                // Radially off the band, which is the widest thing here; axially off the body,
                // which is the tallest. The fuse dot deliberately sits proud of the shell — it is
                // the blinking signal and one of the seven things permitted to bloom, so wrapping
                // it in ink would be working against §8.3.
                AddContour(
                    root,
                    PrimitiveType.Cylinder,
                    new Vector3(GrenadeBandScale.x, GrenadeBodyScale.y, GrenadeBandScale.z)
                );

                ApplyTrail(
                    root,
                    LoadFXMaterial("FX_BeamAmber"),
                    GrenadeTrailTime,
                    GrenadeTrailStartWidth
                );
            }
        );
    }

    private static ProjectileJob SmokeCanisterJob()
    {
        return new ProjectileJob(
            "SmokeCanister",
            ProjectileFX.ProjectileKind.SmokeCanister,
            root =>
            {
                SetMesh(
                    root,
                    PrimitiveType.Cylinder,
                    CanisterBodyScale,
                    Vector3.zero,
                    LoadProjectileMaterial("SmokeCanister")
                );

                FindOrCreateChild(
                    root,
                    "RibTop",
                    PrimitiveType.Cylinder,
                    CanisterRibScale,
                    new Vector3(0f, CanisterRibOffset, 0f),
                    LoadProjectileMaterial("SmokeCanisterCap")
                );
                FindOrCreateChild(
                    root,
                    "RibBottom",
                    PrimitiveType.Cylinder,
                    CanisterRibScale,
                    new Vector3(0f, -CanisterRibOffset, 0f),
                    LoadProjectileMaterial("SmokeCanisterCap")
                );
                FindOrCreateChild(
                    root,
                    "CapDot",
                    PrimitiveType.Sphere,
                    Vector3.one * CanisterCapDotDiameter,
                    new Vector3(0f, CanisterCapDotHeight, 0f),
                    LoadProjectileMaterial("SmokeCanisterCap")
                );

                // Radially off the ribs, axially off the body, on the same reasoning as the
                // grenade. The cap dot sits proud of the shell for the same reason the fuse does.
                AddContour(
                    root,
                    PrimitiveType.Cylinder,
                    new Vector3(CanisterRibScale.x, CanisterBodyScale.y, CanisterRibScale.z)
                );

                // §8.4: no trail in flight, and if a previous run added one, take it away.
                RemoveTrail(root);
            }
        );
    }

    private static string ProjectilePath(string prefabName)
    {
        return $"{ProjectileFolder}/{prefabName}.prefab";
    }

    /// <summary>
    /// Opens a projectile prefab, applies the job's mutation, and saves it back to the same asset —
    /// so the GUID DefaultNetworkPrefabs.asset holds is preserved. Two invariants are checked
    /// before the save and either one aborts it: colliders must be untouched, and the renderer
    /// Bullet repaints at spawn must be authored in one of the materials it will be repainted with.
    /// </summary>
    private static void EditProjectile(ProjectileJob job)
    {
        string path = ProjectilePath(job.PrefabName);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[BattlePlanFXPrefabBuilder] {path} not found — skipped.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            string before = DescribeColliders(root);
            job.Mutate(root);

            ProjectileFX projectileFX = root.GetComponent<ProjectileFX>();
            if (projectileFX == null)
                projectileFX = root.AddComponent<ProjectileFX>();
            projectileFX.kind = job.Kind;

            string after = DescribeColliders(root);
            if (before != after)
            {
                Debug.LogError(
                    $"[BattlePlanFXPrefabBuilder] {job.PrefabName}: collider changed, refusing to save.\n"
                        + $"before: {before}\nafter:  {after}"
                );
                return;
            }

            string mismatch = DescribeTeamPresentationMismatch(root);
            if (mismatch != null)
            {
                Debug.LogError(
                    $"[BattlePlanFXPrefabBuilder] {job.PrefabName}: {mismatch} Refusing to save."
                );
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Checks the invariant behind Bullet.ApplyTeamPresentation, which is the one thing about
    /// these prefabs that no amount of correct authoring can establish on its own: at spawn it
    /// takes two renderers and assigns each one slot [0] or slot [1] of its own pair. That only
    /// produces the authored look if the renderer it lands on is already drawn in one of those
    /// two, so a prefab can otherwise look right in the project window and revert on the frame it
    /// appears — which is what shipped, first on the tracer and then on the body. Returns null when
    /// it holds, or a description of the break.
    ///
    /// Both lookups deliberately mirror Bullet's rather than naming the children they expect to
    /// find: what matters is which renderers that call will actually reach, not which ones we meant
    /// it to.
    /// </summary>
    private static string DescribeTeamPresentationMismatch(GameObject root)
    {
        if (!root.TryGetComponent(out Bullet bullet))
            return null;

        if (bullet.teamMaterials == null || bullet.teamMaterials.Count < 2)
            return "teamMaterials must hold own fire and incoming fire.";

        string tracer = DescribeRepaintMismatch(
            root.GetComponentInChildren<Renderer>(true),
            bullet.teamMaterials,
            nameof(Bullet.teamMaterials),
            "tracer"
        );
        if (tracer != null)
            return tracer;

        // An empty body pair is the authored "not team-coloured" state, not a gap: §8.2's sniper
        // lance is ink on both sides of the board and takes its identity from the trail.
        if (bullet.bodyTeamMaterials == null || bullet.bodyTeamMaterials.Count == 0)
            return null;

        if (bullet.bodyTeamMaterials.Count < 2)
            return "bodyTeamMaterials must hold own fire and incoming fire, or be empty.";

        return DescribeRepaintMismatch(
            Bullet.FindBodyRenderer(root.transform),
            bullet.bodyTeamMaterials,
            nameof(Bullet.bodyTeamMaterials),
            "body"
        );
    }

    /// <summary>
    /// One renderer against the pair it will be repainted from. Shared by the tracer and the body
    /// so a second drawn part cannot be added later with only half the check.
    /// </summary>
    private static string DescribeRepaintMismatch(
        Renderer repainted,
        List<Material> pair,
        string pairName,
        string part
    )
    {
        if (repainted == null)
            return $"there is no {part} Renderer for Bullet.ApplyTeamPresentation to repaint.";

        if (pair.Contains(repainted.sharedMaterial))
            return null;

        return $"'{repainted.name}' is the {part} Bullet.ApplyTeamPresentation repaints, and it is "
            + $"authored in {Describe(repainted.sharedMaterial)}, which is neither {pairName}[0] "
            + $"{Describe(pair[0])} nor {pairName}[1] {Describe(pair[1])} — every shot would "
            + "revert at spawn.";
    }

    private static string Describe(Material material)
    {
        return material == null ? "no material" : $"'{material.name}'";
    }

    /// <summary>
    /// A stable text description of every collider under the prefab. Compared before and after the
    /// edit because §8 opens by forbidding collider changes: Shooting.cs raycasts line of sight
    /// with the projectile's own radius, so a "cosmetic" scale change to the wrong object silently
    /// alters what the weapon can shoot past.
    /// </summary>
    private static string DescribeColliders(GameObject root)
    {
        System.Text.StringBuilder description = new();
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            description.Append(collider.transform.name).Append('|');
            if (collider is SphereCollider sphere)
            {
                description
                    .Append("sphere r=")
                    .Append(sphere.radius.ToString("F5"))
                    .Append(" c=")
                    .Append(sphere.center.ToString("F5"));
            }
            else if (collider is CapsuleCollider capsule)
            {
                description
                    .Append("capsule r=")
                    .Append(capsule.radius.ToString("F5"))
                    .Append(" h=")
                    .Append(capsule.height.ToString("F5"));
            }
            else if (collider is BoxCollider box)
            {
                description.Append("box s=").Append(box.size.ToString("F5"));
            }
            // Lossy scale matters as much as the collider's own numbers, because scaling the
            // renderer's transform scales the collider with it.
            description
                .Append(" scale=")
                .Append(collider.transform.lossyScale.ToString("F5"))
                .Append('\n');
        }
        return description.ToString();
    }

    private static void ApplySlugGeometry(GameObject root, Vector3 scale, Material bodyMaterial)
    {
        SetMesh(root, PrimitiveType.Capsule, scale, ProjectileRotation, bodyMaterial);

        // A capsule's body is its whole silhouette, so the shell wraps the body directly.
        AddContour(root, PrimitiveType.Capsule, scale);
    }

    /// <summary>
    /// Adds the ink contour shell around <paramref name="silhouetteScale"/> — which is the widest
    /// profile the projectile draws, not necessarily its body. See the constants block.
    ///
    /// Parented under Mesh, so it inherits the body's travel alignment and needs no rotation of
    /// its own, and so FindOrCreateChild divides the body's scale back out and the numbers passed
    /// in are world dimensions rather than ratios against whatever the body happens to be. It
    /// carries no collider: FindOrCreateChild builds a bare GameObject rather than using
    /// CreatePrimitive, which would attach one and trip the collider guard on the next run.
    /// </summary>
    private static void AddContour(
        GameObject root,
        PrimitiveType primitive,
        Vector3 silhouetteScale
    )
    {
        FindOrCreateChild(
            root,
            ContourChildName,
            primitive,
            new Vector3(
                silhouetteScale.x + ContourShellRadial,
                silhouetteScale.y + ContourShellAxial,
                silhouetteScale.z + ContourShellRadial
            ),
            Vector3.zero,
            LoadFXMaterial("FX_Contour")
        );
    }

    /// <summary>
    /// Replaces the root's mesh without touching its collider. The mesh, its scale and its material
    /// all land on the Mesh child, so the root transform — which the collider rides — never moves.
    ///
    /// The material is a parameter rather than a separate AssignMaterial call because the separate
    /// call was the bug. This method deletes the root's MeshRenderer so the body cannot double-draw
    /// alongside the child, which left AssignMaterial(root, …) resolving to the TrailRenderer, and
    /// the body with none at all. A body with no material draws nothing and — the part that costs
    /// more — writes no depth, and the contour in <see cref="AddContour"/> is an inverted hull that
    /// only reads because the body occludes its front faces (§8.1). Taking the material here means
    /// there is no window in which the renderer and its material are chosen separately.
    /// </summary>
    private static void SetMesh(
        GameObject root,
        PrimitiveType primitive,
        Vector3 scale,
        Vector3 eulerRotation,
        Material material
    )
    {
        Transform meshChild = FindOrCreateMeshChild(root);
        MeshFilter filter = meshChild.GetComponent<MeshFilter>();
        filter.sharedMesh = PrimitiveMesh(primitive);
        meshChild.localScale = scale;
        meshChild.localRotation = Quaternion.Euler(eulerRotation);
        meshChild.localPosition = Vector3.zero;
        AssignMaterial(meshChild.gameObject, material);

        // A mesh left on the root would double-draw alongside the child.
        MeshFilter rootFilter = root.GetComponent<MeshFilter>();
        if (rootFilter != null)
            Object.DestroyImmediate(rootFilter, true);
        MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
        if (rootRenderer != null)
            Object.DestroyImmediate(rootRenderer, true);
    }

    // Taken from the runtime rather than spelled again here: Bullet.ApplyTeamPresentation resolves
    // the body by this name, so the two must be the same string or the body swap silently no-ops.
    private const string MeshChildName = Bullet.BodyChildName;

    private static Transform FindOrCreateMeshChild(GameObject root)
    {
        Transform existing = root.transform.Find(MeshChildName);
        if (existing != null)
            return EnsureRenderable(existing.gameObject).transform;

        GameObject meshChild = new(MeshChildName);
        meshChild.transform.SetParent(root.transform, worldPositionStays: false);
        return EnsureRenderable(meshChild).transform;
    }

    /// <summary>
    /// Guarantees the MeshFilter/MeshRenderer pair a primitive needs, and the shadow settings §8
    /// wants on every one of them — a projectile is four pixels across and its shadow costs more
    /// than it is worth. Idempotent, so re-running the builder writes the same values it read.
    /// </summary>
    private static GameObject EnsureRenderable(GameObject target)
    {
        if (target.GetComponent<MeshFilter>() == null)
            target.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = target.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return target;
    }

    private static void FindOrCreateChild(
        GameObject root,
        string childName,
        PrimitiveType primitive,
        Vector3 scale,
        Vector3 localPosition,
        Material material
    )
    {
        Transform mesh = FindOrCreateMeshChild(root);
        Transform child = mesh.Find(childName);
        if (child == null)
        {
            GameObject created = new(childName);
            created.transform.SetParent(mesh, worldPositionStays: false);
            child = created.transform;
        }
        EnsureRenderable(child.gameObject);

        child.GetComponent<MeshFilter>().sharedMesh = PrimitiveMesh(primitive);
        // Divided out of the parent's scale so the numbers in the constants block are the world
        // dimensions §8 actually specifies, not values relative to whatever the body happens to be.
        child.localScale = new Vector3(
            SafeDivide(scale.x, mesh.localScale.x),
            SafeDivide(scale.y, mesh.localScale.y),
            SafeDivide(scale.z, mesh.localScale.z)
        );
        child.localPosition = new Vector3(
            SafeDivide(localPosition.x, mesh.localScale.x),
            SafeDivide(localPosition.y, mesh.localScale.y),
            SafeDivide(localPosition.z, mesh.localScale.z)
        );
        child.localRotation = Quaternion.identity;

        AssignMaterial(child.gameObject, material);
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 1e-5f ? value : value / divisor;
    }

    private static void ApplyTrail(
        GameObject root,
        Material trailMaterial,
        float time,
        float startWidth
    )
    {
        TrailRenderer trail = root.GetComponent<TrailRenderer>();
        if (trail == null)
            trail = root.AddComponent<TrailRenderer>();

        trail.time = time;
        trail.startWidth = startWidth;
        trail.endWidth = 0f;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.autodestruct = false;
        trail.numCapVertices = 2;
        if (trailMaterial != null)
            trail.sharedMaterial = trailMaterial;
        // Team colour to transparent. The hue is on the material; this is the fade only.
        trail.colorGradient = TracerFadeGradient();
    }

    private static void RemoveTrail(GameObject root)
    {
        TrailRenderer trail = root.GetComponent<TrailRenderer>();
        if (trail != null)
            Object.DestroyImmediate(trail, true);
    }

    // =====================================================================================
    // SHARED HELPERS
    // =====================================================================================

    private static void ConfigureBurstMain(ParticleSystem.MainModule main, int maxParticles)
    {
        // Emission is driven entirely by ParticleBurstFX calling Emit(count), so the system must
        // not play on its own or loop.
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = 0.2f;
        main.startSpeed = 4f;
        main.startColor = Color.white;
    }

    private static void ConfigureBurstRenderer(
        ParticleSystemRenderer particleRenderer,
        Material material
    )
    {
        particleRenderer.sharedMaterial = material;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        particleRenderer.alignment = ParticleSystemRenderSpace.View;
        particleRenderer.sortMode = ParticleSystemSortMode.Distance;
    }

    /// <summary>
    /// §8.1's tracer envelope: 0.7 at the muzzle end, transparent at the tail.
    ///
    /// THE COLOUR KEYS ARE WHITE ON PURPOSE, and this is the part that keeps getting rewritten.
    /// §8.1 words the tracer as "gradient --bp-blue-hi / --bp-red-hi -> transparent at 0.7 alpha",
    /// which reads like an instruction to paste a team colour into key0. It is not, for two
    /// reasons that both point the same way.
    ///
    /// One: there is only one gradient. BulletRed, SniperSuperBlue and SniperSuperRed are all
    /// prefab variants descending from BulletBlue and none of them overrides colorGradient, so a
    /// team colour written here would be blue on every projectile in the game. The channel that
    /// *is* per-team is the material — Bullet.ApplyTeamPresentation swaps FX_BeamBlue/FX_BeamRed at
    /// spawn, viewer-relative, and that swap cannot reach a gradient.
    ///
    /// Two: the material already carries --bp-blue-hi / --bp-red-hi (see TeamFireMaterials). The
    /// shader multiplies material tint by vertex colour, so a second -hi here would square it and
    /// land the tracer near ink — darker than §8.1 asks for, not merely different.
    ///
    /// So the material owns the hue and the gradient owns the envelope, and the rendered streak is
    /// the -hi step at 0.7 that §8.1 specifies. Changing this to a coloured key re-breaks the team
    /// swap that ApplyTeamPresentation exists to fix.
    /// </summary>
    private static Gradient TracerFadeGradient()
    {
        return WhiteFadeGradient(TracerHeadAlpha);
    }

    /// <summary>Opaque to transparent, no tint. The generic particle fade.</summary>
    private static Gradient FadeOutGradient()
    {
        return WhiteFadeGradient(1f);
    }

    private static Gradient WhiteFadeGradient(float headAlpha)
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(headAlpha, 0f), new GradientAlphaKey(0f, 1f) }
        );
        return gradient;
    }

    private static GameObject CreatePrimitiveRoot(string name, PrimitiveType primitive)
    {
        GameObject root = GameObject.CreatePrimitive(primitive);
        root.name = name;
        // FX geometry never collides with anything.
        Collider collider = root.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider, true);

        MeshRenderer meshRenderer = root.GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return root;
    }

    private static Mesh PrimitiveMesh(PrimitiveType primitive)
    {
        GameObject temporary = GameObject.CreatePrimitive(primitive);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary);
        return mesh;
    }

    private static void AssignMaterial(GameObject target, Material material)
    {
        if (material == null)
            return;
        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer != null)
            targetRenderer.sharedMaterial = material;
    }

    private static Material LoadFXMaterial(string materialName)
    {
        return LoadMaterial($"{FXMaterialFolder}/{materialName}.mat");
    }

    private static Material LoadProjectileMaterial(string materialName)
    {
        return LoadMaterial($"{ProjectileMaterialFolder}/{materialName}.mat");
    }

    private static Material LoadMaterial(string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
            Debug.LogWarning($"[BattlePlanFXPrefabBuilder] Missing material {path}.");
        return material;
    }

    private static void SaveFXPrefab(GameObject root)
    {
        string path = $"{FXPrefabFolder}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
