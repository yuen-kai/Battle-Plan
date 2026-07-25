using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

// Pure/serialized coverage for the Commander's radial Smoke Screen. Everything here compiles
// against current production and runs without a scene, NetworkManager, or Play mode: the footprint
// and segment math are static (GridSystem), and the Commander contract is read from serialized
// assets. The server-authoritative lifecycle (IsServer + "executing" phase + accepting-registration
// window + client telegraph) lives on a spawned GameLoop instance and is intentionally NOT
// exercised here; its behavioral gating is a runtime acceptance gap for a leased Play session.
[TestFixture]
[Category("SmokeScreen")]
public class SmokeScreenEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";
    private const string CommanderDataPath = "Assets/UnitStats/Commander.asset";
    private const string CommanderPrefabPath = "Assets/Prefabs/Units/Commander.prefab";
    private const string DefaultNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    // === Footprint: exact deterministic interior 3x3 ===

    [Test]
    public void SmokeFootprint_RadiusEncodesTheNineCellContract()
    {
        // The design fixes smoke at a 3x3, nine-cell block; radius 1 is that contract in code.
        Assert.That(Smoke.FootprintRadius, Is.EqualTo(1));
        Assert.That(
            GridSystem.GetSquareFootprint(new Vector2Int(4, 4), Smoke.FootprintRadius).Count,
            Is.EqualTo(9)
        );
    }

    [Test]
    public void SmokeFootprint_InteriorCenterProducesExactRowMajorNineCells()
    {
        Vector2Int center = new(4, 4);
        Vector2Int[] expected =
        {
            new(3, 3),
            new(4, 3),
            new(5, 3),
            new(3, 4),
            new(4, 4),
            new(5, 4),
            new(3, 5),
            new(4, 5),
            new(5, 5),
        };

        List<Vector2Int> footprint = GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius);

        CollectionAssert.AreEqual(
            expected,
            footprint,
            "The footprint must be the exact centered 3x3 in deterministic row-major order."
        );
        Assert.That(footprint.Distinct().Count(), Is.EqualTo(9), "The nine cells are distinct.");
        Assert.That(footprint.Contains(center), Is.True, "The target cell is always covered.");
        Assert.That(footprint.All(GridSystem.IsCellInBounds), Is.True);
        Assert.That(GridSystem.IsSquareFootprintInBounds(center, Smoke.FootprintRadius), Is.True);

        // Deterministic across calls (bots, server, and client overlays share this ordering).
        CollectionAssert.AreEqual(
            footprint,
            GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius)
        );
    }

    // === Footprint: edge invalidity with no clipping ===

    [Test]
    public void SmokeFootprint_ValidTargetBandIsTheBoardInsetByOneCell()
    {
        // A center is legal only when the whole 3x3 stays on the board: an inset-by-one band.
        for (int x = 0; x < GridSystem.ColumnCount; x++)
        {
            for (int y = 0; y < GridSystem.RowCount; y++)
            {
                bool expected =
                    x >= 1
                    && x <= GridSystem.ColumnCount - 2
                    && y >= 1
                    && y <= GridSystem.RowCount - 2;
                Assert.That(
                    GridSystem.IsSquareFootprintInBounds(
                        new Vector2Int(x, y),
                        Smoke.FootprintRadius
                    ),
                    Is.EqualTo(expected),
                    $"Footprint validity mismatch at center ({x},{y})."
                );
            }
        }
    }

    [Test]
    public void SmokeFootprint_EdgeCenterIsRejectedWholesaleAndNeverClipped()
    {
        Vector2Int edgeCenter = new(0, 5);

        Assert.That(
            GridSystem.IsSquareFootprintInBounds(edgeCenter, Smoke.FootprintRadius),
            Is.False,
            "An off-board footprint must be rejected, not partially placed."
        );

        List<Vector2Int> footprint = GridSystem.GetSquareFootprint(
            edgeCenter,
            Smoke.FootprintRadius
        );
        Assert.That(
            footprint.Count,
            Is.EqualTo(9),
            "The 3x3 shape is fixed regardless of position."
        );
        Assert.That(
            footprint.Any(cell => !GridSystem.IsCellInBounds(cell)),
            Is.True,
            "An edge center genuinely overhangs the board."
        );
        Assert.That(
            footprint.Count(GridSystem.IsCellInBounds),
            Is.EqualTo(6),
            "GetSquareFootprint returns the full 3x3; it is never trimmed to the in-bounds subset."
        );

        // Corners and every board edge fail the same way.
        Vector2Int[] invalidCenters =
        {
            new(0, 0),
            new(GridSystem.ColumnCount - 1, 0),
            new(0, GridSystem.RowCount - 1),
            new(GridSystem.ColumnCount - 1, GridSystem.RowCount - 1),
            new(4, 0),
            new(4, GridSystem.RowCount - 1),
            new(0, 4),
            new(GridSystem.ColumnCount - 1, 4),
        };
        foreach (Vector2Int center in invalidCenters)
        {
            Assert.That(
                GridSystem.IsSquareFootprintInBounds(center, Smoke.FootprintRadius),
                Is.False,
                $"Edge/corner center {center} must be invalid."
            );
        }

        // The innermost legal corners still fit exactly.
        Assert.That(
            GridSystem.IsSquareFootprintInBounds(new Vector2Int(1, 1), Smoke.FootprintRadius),
            Is.True
        );
        Assert.That(
            GridSystem.IsSquareFootprintInBounds(
                new Vector2Int(GridSystem.ColumnCount - 2, GridSystem.RowCount - 2),
                Smoke.FootprintRadius
            ),
            Is.True
        );
    }

    // === Segment blocking: symmetry, endpoint-only, boundary/miss ===

    [Test]
    public void SmokeSegment_InteriorCrossingBlocksSymmetrically()
    {
        HashSet<Vector2Int> cloud = new() { new Vector2Int(2, 0) };

        AssertCellSegment(
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            cloud,
            true,
            "A shot through the cloud interior is blocked."
        );

        // The integer overload must agree with the continuous one.
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(new Vector2Int(0, 0), new Vector2Int(4, 0), cloud),
            Is.True
        );
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(new Vector2Int(4, 0), new Vector2Int(0, 0), cloud),
            Is.True,
            "Swapping shooter and target must not change the outcome."
        );
    }

    [Test]
    public void SmokeSegment_EndpointCellsNeverBlockTheirOwnLine()
    {
        // A unit standing at a cloud edge can still shoot out / be targeted: endpoints do not block.
        HashSet<Vector2Int> endpointsOnly = new() { new Vector2Int(0, 0), new Vector2Int(4, 0) };
        AssertCellSegment(
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            endpointsOnly,
            false,
            "Only the endpoint cells are smoke, so the line is clear."
        );

        // Shooting out of a single smoke cell the shooter occupies is not self-blocked.
        HashSet<Vector2Int> shooterCell = new() { new Vector2Int(2, 2) };
        AssertCellSegment(
            new Vector2(2f, 2f),
            new Vector2(5f, 2f),
            shooterCell,
            false,
            "The origin cell is an endpoint and cannot block its own shot."
        );

        // But an additional non-endpoint smoke cell on that same line does block.
        HashSet<Vector2Int> shooterPlusMidline = new()
        {
            new Vector2Int(2, 2),
            new Vector2Int(3, 2),
        };
        AssertCellSegment(
            new Vector2(2f, 2f),
            new Vector2(5f, 2f),
            shooterPlusMidline,
            true,
            "A smoke cell beyond the origin still blocks."
        );
    }

    [Test]
    public void SmokeSegment_GrazingAnEdgeOrCornerIsNotACrossing()
    {
        // A shot riding exactly along the shared edge between rows 0 and 1 grazes neither cell.
        HashSet<Vector2Int> rowZero = new() { new Vector2Int(2, 0) };
        HashSet<Vector2Int> rowOne = new() { new Vector2Int(2, 1) };
        AssertCellSegment(
            new Vector2(0f, 0.5f),
            new Vector2(4f, 0.5f),
            rowZero,
            false,
            "Edge graze misses row 0."
        );
        AssertCellSegment(
            new Vector2(0f, 0.5f),
            new Vector2(4f, 0.5f),
            rowOne,
            false,
            "Edge graze misses row 1."
        );

        // Nudging the same shot into the cell interior is unambiguously a crossing.
        AssertCellSegment(
            new Vector2(0f, 0.2f),
            new Vector2(4f, 0.2f),
            rowZero,
            true,
            "Inside row 0 is a crossing."
        );

        // A diagonal that only clips a cell's corner does not cross that cell.
        HashSet<Vector2Int> cornerLow = new() { new Vector2Int(1, 1) };
        HashSet<Vector2Int> cornerHigh = new() { new Vector2Int(2, 2) };
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(
                new Vector2Int(0, 3),
                new Vector2Int(3, 0),
                cornerLow
            ),
            Is.False,
            "Touching the (1.5,1.5) corner is not a crossing of (1,1)."
        );
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(
                new Vector2Int(3, 0),
                new Vector2Int(0, 3),
                cornerHigh
            ),
            Is.False,
            "Touching the (1.5,1.5) corner is not a crossing of (2,2)."
        );

        // The cells whose interiors the same diagonal truly passes through are blocked.
        HashSet<Vector2Int> onDiagonal = new() { new Vector2Int(2, 1) };
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(
                new Vector2Int(0, 3),
                new Vector2Int(3, 0),
                onDiagonal
            ),
            Is.True
        );
    }

    [Test]
    public void SmokeSegment_EmptyDegenerateAndNullInputsAreClear()
    {
        HashSet<Vector2Int> cloud = new() { new Vector2Int(2, 2) };

        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(new Vector2(0f, 0f), new Vector2(4f, 0f), null),
            Is.False
        );
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new HashSet<Vector2Int>()
            ),
            Is.False
        );
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(new Vector2(2f, 2f), new Vector2(2f, 2f), cloud),
            Is.False,
            "A zero-length segment cannot cross anything."
        );
    }

    // === World-space wrapper mirrors the grid math on the ground plane ===

    [Test]
    public void SmokeWorldSegment_MirrorsCellMathAndIgnoresHeight()
    {
        HashSet<Vector2Int> cloud = new() { new Vector2Int(2, 0) };
        Vector3 worldStart = GameLoop.gridCoordToWorld(new Vector2Int(0, 0));
        Vector3 worldEnd = GameLoop.gridCoordToWorld(new Vector2Int(4, 0));
        // Only the ground-plane (X/Z) projection matters; vertical offset must be ignored.
        worldStart.y = 12.5f;
        worldEnd.y = -7.25f;

        AssertWorldSegment(
            worldStart,
            worldEnd,
            cloud,
            true,
            "A world shot through the cloud is blocked at any height."
        );

        HashSet<Vector2Int> endpointsOnly = new() { new Vector2Int(0, 0), new Vector2Int(4, 0) };
        AssertWorldSegment(
            worldStart,
            worldEnd,
            endpointsOnly,
            false,
            "World endpoints do not block, matching the cell-space rule."
        );
    }

    // === Fog visibility integrates transient smoke blockers (ComputeVisibleCells) ===

    [Test]
    public void SmokeVisibility_IntermediateCloudHidesCellsBehindButNotItsOwnCell()
    {
        // Viewer at a board corner looking straight down row 0. Row 0 carries no walls, so grid LoS
        // is clear across it and only the transient smoke term can remove cells from view.
        Vector2Int viewer = new(0, 0);
        HashSet<Vector2Int> cloud = new() { new Vector2Int(2, 0) };

        HashSet<Vector2Int> withSmoke = Visible(cloud, (viewer, 5));
        HashSet<Vector2Int> withoutSmoke = Visible(null, (viewer, 5));

        // The cloud cell is the segment endpoint of its own sightline, so smoke never hides itself.
        Assert.That(
            withSmoke,
            Does.Contain(new Vector2Int(2, 0)),
            "The smoke cell is still visible: it is an endpoint of its own line."
        );
        // A cell in front of the cloud is untouched.
        Assert.That(
            withSmoke,
            Does.Contain(new Vector2Int(1, 0)),
            "A cell nearer than the cloud stays visible."
        );
        // Every cell strictly behind the cloud on that ray is hidden.
        Assert.That(
            withSmoke.Contains(new Vector2Int(3, 0)),
            Is.False,
            "The cell immediately behind the cloud is hidden."
        );
        Assert.That(
            withSmoke.Contains(new Vector2Int(4, 0)),
            Is.False,
            "A farther cell behind the cloud is hidden."
        );

        // Those behind-cells are hidden only because of the smoke: the legacy pass sees them.
        Assert.That(withoutSmoke, Does.Contain(new Vector2Int(3, 0)));
        Assert.That(withoutSmoke, Does.Contain(new Vector2Int(4, 0)));

        // Smoke can only subtract visibility; it never reveals a cell the legacy pass could not see.
        Assert.That(
            withSmoke.IsSubsetOf(withoutSmoke),
            Is.True,
            "The smoke-filtered visible set is a subset of the unfiltered set."
        );
    }

    [Test]
    public void SmokeVisibility_EndpointCloudNeverBlocksItsOwnSightline()
    {
        Vector2Int viewer = new(0, 0);
        Vector2Int endpointTarget = new(4, 0);

        // Smoke on the viewer's own cell is an endpoint for every ray it casts, so the whole legacy
        // visible set is preserved (a unit can always see out of the cloud it stands in).
        HashSet<Vector2Int> smokeOnViewer = new() { viewer };
        CollectionAssert.AreEquivalent(
            Visible(null, (viewer, 5)),
            Visible(smokeOnViewer, (viewer, 5)),
            "Smoke on the viewer cell blocks nothing: the viewer is always an endpoint."
        );

        // Smoke on the far target is the other endpoint of that ray, so the target itself stays
        // visible even though its cell is full of smoke.
        HashSet<Vector2Int> smokeOnEndpoints = new() { viewer, endpointTarget };
        Assert.That(
            Visible(smokeOnEndpoints, (viewer, 5)),
            Does.Contain(endpointTarget),
            "An endpoint cloud does not hide the endpoint cell."
        );

        // Move that same single cloud one step inward and it becomes an interior blocker that does
        // hide the target, confirming the endpoint carve-out is specific to the endpoints.
        HashSet<Vector2Int> interiorCloud = new() { new Vector2Int(3, 0) };
        Assert.That(
            Visible(interiorCloud, (viewer, 5)).Contains(endpointTarget),
            Is.False,
            "A cloud between the viewer and target blocks the target."
        );
    }

    [Test]
    public void SmokeVisibility_NullAndEmptyBlockersPreserveLegacyOutput()
    {
        // Two viewers placed among the default walls so grid LoS actually shapes the set, making the
        // equality with the legacy oracle non-trivial.
        (Vector2Int cell, int range)[] viewers =
        {
            (new Vector2Int(5, 2), 4),
            (new Vector2Int(9, 7), 4),
        };

        HashSet<Vector2Int> legacy = LegacyVisibleWithoutBlockers(viewers);
        Assert.That(
            legacy.Count,
            Is.GreaterThan(0),
            "The fixture must see cells for the equality to be meaningful."
        );

        CollectionAssert.AreEquivalent(
            legacy,
            GridSystem.ComputeVisibleCells(viewers),
            "Default (no blockers argument) reduces to legacy fog."
        );
        CollectionAssert.AreEquivalent(
            legacy,
            GridSystem.ComputeVisibleCells(viewers, null),
            "Null blockers reduce to legacy fog."
        );
        CollectionAssert.AreEquivalent(
            legacy,
            GridSystem.ComputeVisibleCells(viewers, new HashSet<Vector2Int>()),
            "Empty blockers reduce to legacy fog."
        );
    }

    [Test]
    public void SmokeVisibility_CornerAndEdgeGrazingStaysClear()
    {
        // The (0,3)->(3,0) sightline passes exactly through the (1.5,1.5) grid corner and is wall-clear.
        Vector2Int viewer = new(0, 3);
        Vector2Int target = new(3, 0);
        Assert.That(
            GridSystem.HasGridLineOfSight(viewer, target),
            Is.True,
            "Precondition: the diagonal has clear grid line of sight."
        );

        // A cloud that only touches the shared corner (on either side of the ray) is not a crossing.
        Assert.That(
            Visible(new HashSet<Vector2Int> { new Vector2Int(1, 1) }, (viewer, 6)),
            Does.Contain(target),
            "Grazing the low-side corner cell does not block."
        );
        Assert.That(
            Visible(new HashSet<Vector2Int> { new Vector2Int(2, 2) }, (viewer, 6)),
            Does.Contain(target),
            "Grazing the high-side corner cell does not block."
        );
        // A cloud whose interior the same ray truly enters does block, proving the graze is specific.
        Assert.That(
            Visible(new HashSet<Vector2Int> { new Vector2Int(2, 1) }, (viewer, 6)).Contains(target),
            Is.False,
            "A cloud the diagonal genuinely passes through blocks the target."
        );

        // Edge clearance: a straight row sightline never enters the neighbouring row's interior, so a
        // cloud one row off the line leaves the target visible while a cloud on the line hides it.
        Vector2Int rowViewer = new(0, 0);
        Vector2Int rowTarget = new(4, 0);
        Assert.That(
            Visible(new HashSet<Vector2Int> { new Vector2Int(2, 1) }, (rowViewer, 5)),
            Does.Contain(rowTarget),
            "A cloud in the adjacent row does not graze the straight sightline."
        );
        Assert.That(
            Visible(new HashSet<Vector2Int> { new Vector2Int(2, 0) }, (rowViewer, 5))
                .Contains(rowTarget),
            Is.False,
            "A cloud on the straight sightline hides the target."
        );
    }

    [Test]
    public void SmokeVisibility_MultipleViewersUnionAroundSmoke()
    {
        // Two viewers on the open bottom row look toward a contested central cell from opposite ends.
        Vector2Int left = new(0, 0);
        Vector2Int right = new(GridSystem.ColumnCount - 1, 0);
        Vector2Int contested = new(GridSystem.ColumnCount / 2, 0);
        HashSet<Vector2Int> cloudNearLeft = new() { new Vector2Int(3, 0) };

        HashSet<Vector2Int> leftOnly = Visible(cloudNearLeft, (left, 8));
        HashSet<Vector2Int> rightOnly = Visible(cloudNearLeft, (right, 8));
        HashSet<Vector2Int> both = Visible(cloudNearLeft, (left, 8), (right, 8));

        // The cloud blocks the left viewer but not the clean line from the right, and the union
        // restores the contested cell.
        Assert.That(
            leftOnly.Contains(contested),
            Is.False,
            "The left viewer is blocked by the cloud."
        );
        Assert.That(rightOnly, Does.Contain(contested), "The right viewer sees around the cloud.");
        Assert.That(both, Does.Contain(contested), "The union reveals what either viewer can see.");

        // The multi-viewer result is exactly the set-union of the per-viewer results under the same smoke.
        HashSet<Vector2Int> manualUnion = new(leftOnly);
        manualUnion.UnionWith(rightOnly);
        CollectionAssert.AreEquivalent(
            manualUnion,
            both,
            "Multi-viewer visibility is the union of each viewer's visibility."
        );

        // A cloud that blocks the contested cell from BOTH viewers keeps it hidden even in the union.
        HashSet<Vector2Int> cloudBothSides = new() { new Vector2Int(3, 0), new Vector2Int(11, 0) };
        Assert.That(
            Visible(cloudBothSides, (left, 8), (right, 8)).Contains(contested),
            Is.False,
            "The union cannot reveal a cell every viewer is blocked from."
        );
    }

    // === Finalized server seam surface (locked without a scene) ===

    [Test]
    public void SmokeApiSurface_ExposesServerRegistrationAndQuerySeams()
    {
        // Smoke is a server-authoritative ability; these types anchor that contract.
        Assert.That(typeof(Smoke).IsSubclassOf(typeof(Ability)), Is.True);
        Assert.That(typeof(Ability).IsSubclassOf(typeof(NetworkBehaviour)), Is.True);

        AssertInstanceMethod(
            typeof(Smoke),
            nameof(Smoke.RegisterTargetFootprint),
            typeof(bool),
            typeof(Vector3)
        );
        AssertInstanceMethod(
            typeof(GameLoop),
            nameof(GameLoop.TryRegisterSmokeFootprint),
            typeof(bool),
            typeof(Vector2Int)
        );
        AssertInstanceMethod(
            typeof(GameLoop),
            nameof(GameLoop.IsSmokeCellActive),
            typeof(bool),
            typeof(Vector2Int)
        );
        AssertInstanceMethod(
            typeof(GameLoop),
            nameof(GameLoop.DoesCellSegmentCrossActiveSmoke),
            typeof(bool),
            typeof(Vector2Int),
            typeof(Vector2Int)
        );
        AssertInstanceMethod(
            typeof(GameLoop),
            nameof(GameLoop.DoesWorldSegmentCrossActiveSmoke),
            typeof(bool),
            typeof(Vector3),
            typeof(Vector3)
        );

        PropertyInfo activeSmoke = typeof(GameLoop).GetProperty(
            nameof(GameLoop.ActiveSmokeCells),
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.That(activeSmoke, Is.Not.Null);
        Assert.That(activeSmoke.PropertyType, Is.EqualTo(typeof(IReadOnlyList<Vector2Int>)));
        Assert.That(activeSmoke.CanRead, Is.True);
        Assert.That(
            activeSmoke.CanWrite,
            Is.False,
            "Callers must not mutate the authoritative smoke set."
        );
    }

    // === Serialized Commander asset + prefab contract ===

    [Test]
    public void CommanderAsset_HasSmokeScreenDataValuesAndIsRosterEligible()
    {
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {CatalogPath}.");
        Assert.That(catalog.units, Is.Not.Null);
        Assert.That(catalog.units.Count, Is.GreaterThanOrEqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(RosterRules.ValidateCatalog(catalog.units).IsValid, Is.True);

        UnitData commander = catalog.units[0];
        Assert.That(commander, Is.Not.Null);
        Assert.That(commander.unitName, Is.EqualTo("Commander"));

        UnitData commanderAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(
            CommanderDataPath
        );
        Assert.That(commanderAsset, Is.Not.Null, $"Could not load {CommanderDataPath}.");
        Assert.That(
            ReferenceEquals(commanderAsset, commander),
            Is.True,
            "The catalog's first unit must be the Commander asset."
        );

        // Smoke Screen ability card contract: cell-square targeting, no direction, two-round cooldown.
        Assert.That(commander.abilityName, Is.EqualTo("Smoke Screen"));
        Assert.That(commander.selectAbilitySquare, Is.True);
        Assert.That(commander.selectAbilityDirection, Is.False);
        Assert.That(commander.abilitySquareRange, Is.EqualTo(4));
        Assert.That(commander.abilityFixedDistance, Is.EqualTo(0));
        Assert.That(commander.abilityCooldownRounds, Is.EqualTo(2));
        // abilityRadius drives the visual/target preview; the placed cloud is cell-quantized by
        // Smoke.FootprintRadius, so both values are part of the contract.
        Assert.That(commander.abilityRadius, Is.EqualTo(1.5f).Within(0.0001f));

        Assert.That(
            commander.IsRosterEligible,
            Is.True,
            "With Smoke Screen finalized the Commander is roster-eligible (unavailableForRoster = 0)."
        );
        Assert.That(
            RosterRules.IsUnitEligible(catalog.units, 0),
            Is.True,
            "The production roster gate must also treat the Commander as selectable."
        );
    }

    [Test]
    public void CommanderPrefab_HasExactlyOneSmokeAbilityComponent()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            CommanderPrefabPath
        );
        Assert.That(prefab, Is.Not.Null, $"Could not load {CommanderPrefabPath}.");

        Smoke[] smokeComponents = prefab.GetComponentsInChildren<Smoke>(true);
        Assert.That(
            smokeComponents.Length,
            Is.EqualTo(1),
            "The Commander must carry exactly one Smoke component."
        );

        Ability[] abilityComponents = prefab.GetComponentsInChildren<Ability>(true);
        Assert.That(
            abilityComponents.Length,
            Is.EqualTo(1),
            "Smoke must be the Commander's only ability."
        );
        Assert.That(abilityComponents[0], Is.InstanceOf<Smoke>());
    }

    /// <summary>
    /// The screen is now thrown, so the canister is part of the Commander's contract. An unassigned
    /// prefab silently degrades to an invisible throw, and one that is not registered as a network
    /// prefab spawns for the server alone — a fault that would otherwise only surface in a live
    /// two-client match.
    /// </summary>
    [Test]
    public void CommanderPrefab_ThrowsARegisteredNetworkCanister()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            CommanderPrefabPath
        );
        Assert.That(prefab, Is.Not.Null, $"Could not load {CommanderPrefabPath}.");

        UnityEditor.SerializedProperty canisterProperty = new UnityEditor.SerializedObject(
            prefab.GetComponent<Smoke>()
        ).FindProperty("canisterPrefab");
        Assert.That(canisterProperty, Is.Not.Null, "Smoke must expose a canisterPrefab field.");

        GameObject canister = canisterProperty.objectReferenceValue as GameObject;
        Assert.That(canister, Is.Not.Null, "The Commander must be given a canister to throw.");
        Assert.That(
            canister.GetComponent<NetworkObject>(),
            Is.Not.Null,
            "The canister is spawned through NetworkHelper, so it needs a NetworkObject."
        );

        NetworkPrefabsList networkPrefabs =
            UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                DefaultNetworkPrefabsPath
            );
        Assert.That(networkPrefabs, Is.Not.Null, $"Could not load {DefaultNetworkPrefabsPath}.");
        Assert.That(
            networkPrefabs.PrefabList.Any(entry => entry != null && entry.Prefab == canister),
            Is.True,
            "The canister must be a registered network prefab or it will never reach clients."
        );
    }

    /// <summary>
    /// The flight has to stay brief. It is the window in which the committed screen is telegraphed
    /// but not yet blocking, so a long throw would hand the opponent real shooting time.
    /// </summary>
    [Test]
    public void SmokeThrow_IsShortEnoughToLandInsideTheCombatWindow()
    {
        Assert.That(Smoke.ThrowSeconds, Is.GreaterThan(0f));
        Assert.That(
            Smoke.ThrowSeconds,
            Is.LessThanOrEqualTo(0.5f),
            "A longer throw turns the deploy delay into exploitable clear sight."
        );
    }

    private static void AssertCellSegment(
        Vector2 start,
        Vector2 end,
        ISet<Vector2Int> cells,
        bool expected,
        string because
    )
    {
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(start, end, cells),
            Is.EqualTo(expected),
            because
        );
        Assert.That(
            GridSystem.DoesCellSegmentCrossCells(end, start, cells),
            Is.EqualTo(expected),
            because + " (reversed endpoints)"
        );
    }

    private static void AssertWorldSegment(
        Vector3 start,
        Vector3 end,
        ISet<Vector2Int> cells,
        bool expected,
        string because
    )
    {
        Assert.That(
            GridSystem.DoesWorldSegmentCrossCells(start, end, cells),
            Is.EqualTo(expected),
            because
        );
        Assert.That(
            GridSystem.DoesWorldSegmentCrossCells(end, start, cells),
            Is.EqualTo(expected),
            because + " (reversed endpoints)"
        );
    }

    private static void AssertInstanceMethod(
        System.Type declaringType,
        string methodName,
        System.Type returnType,
        params System.Type[] parameterTypes
    )
    {
        MethodInfo method = declaringType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.That(method, Is.Not.Null, $"{declaringType.Name}.{methodName} must exist.");
        Assert.That(
            method.IsStatic,
            Is.False,
            $"{methodName} is a server-authoritative instance seam."
        );
        Assert.That(method.ReturnType, Is.EqualTo(returnType));
        CollectionAssert.AreEqual(
            parameterTypes,
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
            $"{methodName} parameter types drifted."
        );
    }

    private static HashSet<Vector2Int> Visible(
        ISet<Vector2Int> transientBlockingCells,
        params (Vector2Int cell, int range)[] viewers
    )
    {
        return GridSystem.ComputeVisibleCells(viewers, transientBlockingCells);
    }

    private static HashSet<Vector2Int> LegacyVisibleWithoutBlockers(
        params (Vector2Int cell, int range)[] viewers
    )
    {
        // The pre-smoke definition of fog: a Manhattan diamond intersected with grid line of sight and
        // clamped to the board. Built only from IsCellInBounds + HasGridLineOfSight so it is an
        // independent oracle -- it does not touch DoesCellSegmentCrossCells -- letting the null/empty
        // blocker tests prove ComputeVisibleCells collapses to exactly the legacy visibility.
        HashSet<Vector2Int> visible = new();
        foreach ((Vector2Int viewerCell, int configuredRange) in viewers)
        {
            int range = Mathf.Max(0, configuredRange);
            for (int rowOffset = -range; rowOffset <= range; rowOffset++)
            {
                int horizontalRange = range - Mathf.Abs(rowOffset);
                for (
                    int columnOffset = -horizontalRange;
                    columnOffset <= horizontalRange;
                    columnOffset++
                )
                {
                    Vector2Int target = new(viewerCell.x + columnOffset, viewerCell.y + rowOffset);
                    if (
                        GridSystem.IsCellInBounds(target)
                        && GridSystem.HasGridLineOfSight(viewerCell, target)
                    )
                    {
                        visible.Add(target);
                    }
                }
            }
        }
        return visible;
    }
}
