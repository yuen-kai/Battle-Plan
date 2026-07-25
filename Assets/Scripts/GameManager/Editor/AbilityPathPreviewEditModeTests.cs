using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Guards the paths abilities draw for themselves while a player is aiming them. The point of
/// these cases is that a preview is not allowed to be a look-alike: each one is checked against the
/// same curve and the same destination resolution the ability uses when it actually runs, so a
/// throw height or a wall rule that changes on one side and not the other fails here.
/// </summary>
[TestFixture]
[Category("AbilityPathPreview")]
public class AbilityPathPreviewEditModeTests
{
    private const float Tolerance = 0.001f;

    // The apex each lobbed ability flies, matching the constants their execution samples.
    private const float GrenadeApexHeight = 2.5f;
    private const float PogoApexHeight = 5f;
    private const float SmokeApexHeight = 1.4f;

    private readonly List<Vector3> points = new();

    private static UnitData LoadUnit(string unitName)
    {
        string path = $"Assets/UnitStats/{unitName}.asset";
        UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(path);
        Assert.That(data, Is.Not.Null, $"Could not load {path}.");
        Assert.That(data.unitModel, Is.Not.Null, $"{unitName} has no unit prefab.");
        return data;
    }

    private static GameObject SpawnAt(UnitData data, Vector2Int cell)
    {
        GameObject instance = Object.Instantiate(data.unitModel);
        instance.transform.position =
            GameLoop.gridCoordToWorld(cell) + Helper.heightOffset(instance.transform);
        Physics.SyncTransforms();
        return instance;
    }

    private static Vector3 Square(Vector2Int cell) => GameLoop.gridCoordToWorld(cell);

    /// <summary>
    /// Asserts a drawn lob is exactly the parabola <see cref="AbilityTrajectory.SampleLob"/>
    /// produces between its own endpoints, at the apex the ability is specified to fly. Checking
    /// every sample rather than just the peak is what rules out a preview that merely happens to
    /// reach the right height on the way to a different shape.
    /// </summary>
    private static void AssertLobShape(List<Vector3> drawn, float expectedApexHeight)
    {
        Assert.That(drawn.Count, Is.EqualTo(AbilityTrajectory.LobSegments + 1));

        Vector3 start = drawn[0];
        Vector3 end = drawn[drawn.Count - 1];
        for (int step = 0; step < drawn.Count; step++)
        {
            float progress = step / (float)AbilityTrajectory.LobSegments;
            Vector3 expected = AbilityTrajectory.SampleLob(
                start,
                end,
                expectedApexHeight,
                progress
            );
            Assert.That(
                Vector3.Distance(drawn[step], expected),
                Is.LessThan(Tolerance),
                $"Sample {step} left the flown arc: drew {drawn[step]}, flies {expected}."
            );
        }
    }

    /// <summary>Runs a case against a chosen wall layout, restoring the board afterwards.</summary>
    private static void WithWalls(IEnumerable<Vector2Int> walls, System.Action body)
    {
        HashSet<Vector2Int> original = GameLoop.wallLayout;
        GameLoop.wallLayout = new HashSet<Vector2Int>(walls);
        try
        {
            body();
        }
        finally
        {
            GameLoop.wallLayout = original;
        }
    }

    [Test]
    public void SampleLob_LeavesItsEndpointsAloneAndPeaksAtTheApex()
    {
        Vector3 start = new(0f, 1f, 0f);
        Vector3 end = new(10f, 3f, 0f);
        const float apex = 4f;

        Assert.That(
            Vector3.Distance(AbilityTrajectory.SampleLob(start, end, apex, 0f), start),
            Is.LessThan(Tolerance)
        );
        Assert.That(
            Vector3.Distance(AbilityTrajectory.SampleLob(start, end, apex, 1f), end),
            Is.LessThan(Tolerance)
        );

        Vector3 midpoint = AbilityTrajectory.SampleLob(start, end, apex, 0.5f);
        Assert.That(
            Vector3.Distance(midpoint, Vector3.Lerp(start, end, 0.5f) + Vector3.up * apex),
            Is.LessThan(Tolerance)
        );

        // A parabola rises and falls at the same rate, so mirrored samples sit at one height.
        Assert.That(
            AbilityTrajectory.SampleLob(start, end, apex, 0.25f).y
                - Vector3.Lerp(start, end, 0.25f).y,
            Is.EqualTo(
                    AbilityTrajectory.SampleLob(start, end, apex, 0.75f).y
                        - Vector3.Lerp(start, end, 0.75f).y
                )
                .Within(Tolerance)
        );
    }

    [Test]
    public void BuildLob_DrawsNothingForATargetOnTheCastersOwnCell()
    {
        Vector3 caster = new(4f, 1f, 7f);

        Assert.That(AbilityTrajectory.BuildLob(caster, caster, 3f, points), Is.False);
        Assert.That(points, Is.Empty);
    }

    [Test]
    public void SoldierGrenade_PreviewsTheArcItThrows()
    {
        UnitData soldier = LoadUnit("Soldier");
        GameObject caster = SpawnAt(soldier, new Vector2Int(3, 3));
        try
        {
            Vector3 target = Square(new Vector2Int(7, 3));
            Grenade grenade = caster.GetComponent<Grenade>();
            Assert.That(grenade, Is.Not.Null, "The Soldier prefab must retain its Grenade.");

            Assert.That(
                grenade.BuildPlannedPath(target, soldier, points),
                Is.EqualTo(AbilityPathKind.Lob)
            );

            // The grenade is spawned at the thrower and comes to rest at the thrower's own height
            // over the target square, so the drawn arc has to start and finish in those places.
            Assert.That(
                Vector3.Distance(points[0], caster.transform.position),
                Is.LessThan(Tolerance)
            );
            Assert.That(
                Vector3.Distance(
                    points[points.Count - 1],
                    target + Helper.heightOffset(caster.transform)
                ),
                Is.LessThan(Tolerance)
            );
            AssertLobShape(points, GrenadeApexHeight);
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }

    [Test]
    public void PogoRider_PreviewsTheJumpItFliesAndClearsMoreThanAThrownGrenade()
    {
        UnitData rider = LoadUnit("PogoRider");
        GameObject caster = SpawnAt(rider, new Vector2Int(6, 4));
        try
        {
            Vector3 target = Square(new Vector2Int(6, 8));
            Pogo pogo = caster.GetComponent<Pogo>();
            Assert.That(pogo, Is.Not.Null, "The Pogo Rider prefab must retain its Pogo.");

            Assert.That(
                pogo.BuildPlannedPath(target, rider, points),
                Is.EqualTo(AbilityPathKind.Lob)
            );
            Assert.That(
                Vector3.Distance(points[0], caster.transform.position),
                Is.LessThan(Tolerance)
            );
            Assert.That(
                Vector3.Distance(
                    points[points.Count - 1],
                    target + Helper.heightOffset(caster.transform)
                ),
                Is.LessThan(Tolerance)
            );
            AssertLobShape(points, PogoApexHeight);

            // The rider vaults over cover a grenade only lobs across; the previews should show it.
            Assert.That(PogoApexHeight, Is.GreaterThan(GrenadeApexHeight));
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }

    [Test]
    public void ShotgunnerRush_PreviewsTheWholeRunWhenTheLaneIsClear()
    {
        UnitData shotgunner = LoadUnit("Shotgunner");
        Assert.That(shotgunner.abilityFixedDistance, Is.GreaterThan(0));

        WithWalls(
            System.Array.Empty<Vector2Int>(),
            () =>
            {
                GameObject caster = SpawnAt(shotgunner, new Vector2Int(3, 3));
                try
                {
                    Shield shield = caster.GetComponent<Shield>();
                    Assert.That(shield, Is.Not.Null, "The Shotgunner prefab must retain its Shield.");

                    // The player picks a neighbouring cell to name a direction, not a destination.
                    Assert.That(
                        shield.BuildPlannedPath(Square(new Vector2Int(4, 3)), shotgunner, points),
                        Is.EqualTo(AbilityPathKind.Ground)
                    );
                    Assert.That(points.Count, Is.EqualTo(2));
                    Assert.That(
                        Vector3.Distance(points[0], Square(new Vector2Int(3, 3))),
                        Is.LessThan(Tolerance)
                    );
                    Assert.That(
                        Vector3.Distance(
                            points[1],
                            Square(new Vector2Int(3 + shotgunner.abilityFixedDistance, 3))
                        ),
                        Is.LessThan(Tolerance)
                    );
                }
                finally
                {
                    Object.DestroyImmediate(caster);
                }
            }
        );
    }

    [Test]
    public void ShotgunnerRush_PreviewStopsAtTheWallThatWillStopTheRush()
    {
        UnitData shotgunner = LoadUnit("Shotgunner");

        WithWalls(
            new[] { new Vector2Int(5, 3) },
            () =>
            {
                GameObject caster = SpawnAt(shotgunner, new Vector2Int(3, 3));
                try
                {
                    Shield shield = caster.GetComponent<Shield>();
                    Assert.That(
                        shield.BuildPlannedPath(Square(new Vector2Int(4, 3)), shotgunner, points),
                        Is.EqualTo(AbilityPathKind.Ground)
                    );

                    // Cut short one cell before the wall rather than run its full three cells.
                    Assert.That(
                        Vector3.Distance(points[1], Square(new Vector2Int(4, 3))),
                        Is.LessThan(Tolerance)
                    );
                    Assert.That(
                        Vector3.Distance(points[0], points[1]),
                        Is.LessThan(shotgunner.abilityFixedDistance * GameLoop.cellSize)
                    );
                }
                finally
                {
                    Object.DestroyImmediate(caster);
                }
            }
        );
    }

    [Test]
    public void ShotgunnerRush_DrawsNothingUntilADirectionIsPicked()
    {
        UnitData shotgunner = LoadUnit("Shotgunner");
        GameObject caster = SpawnAt(shotgunner, new Vector2Int(3, 3));
        try
        {
            Shield shield = caster.GetComponent<Shield>();

            Assert.That(
                shield.BuildPlannedPath(Square(new Vector2Int(9, 3)), shotgunner, points),
                Is.EqualTo(AbilityPathKind.None)
            );
            Assert.That(points, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }

    [Test]
    public void CommanderSmoke_PreviewsTheCanisterThrow()
    {
        UnitData commander = LoadUnit("Commander");
        GameObject caster = SpawnAt(commander, new Vector2Int(4, 5));
        try
        {
            Vector3 target = Square(new Vector2Int(7, 5));
            Smoke smoke = caster.GetComponent<Smoke>();
            Assert.That(smoke, Is.Not.Null, "The Commander prefab must retain its Smoke.");

            Assert.That(
                smoke.BuildPlannedPath(target, commander, points),
                Is.EqualTo(AbilityPathKind.Lob),
                "Smoke is thrown as a canister, so it previews the arc it flies."
            );
            Assert.That(
                Vector3.Distance(points[0], caster.transform.position),
                Is.LessThan(Tolerance)
            );
            Assert.That(
                Vector3.Distance(
                    points[points.Count - 1],
                    target + Helper.heightOffset(caster.transform)
                ),
                Is.LessThan(Tolerance)
            );
            AssertLobShape(points, SmokeApexHeight);

            // A canister is pitched onto nearby ground; it should not sail like a grenade.
            Assert.That(SmokeApexHeight, Is.LessThan(GrenadeApexHeight));
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }

    [TestCase("Sniper")]
    public void AbilitiesThatDoNotTravel_ReportNoPath(string unitName)
    {
        UnitData data = LoadUnit(unitName);
        GameObject caster = SpawnAt(data, new Vector2Int(5, 5));
        try
        {
            Ability ability = caster.GetComponent<Ability>();
            Assert.That(ability, Is.Not.Null, $"{unitName} must have an ability component.");

            Assert.That(
                ability.BuildPlannedPath(Square(new Vector2Int(7, 5)), data, points),
                Is.EqualTo(AbilityPathKind.None),
                $"{unitName}'s ability reaches its target without travelling, so it draws no path."
            );
            Assert.That(points, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }

    [Test]
    public void EndChevron_PointsTheWayThePathArrivesAndIsSymmetricAboutIt()
    {
        Vector3[] chevron = new Vector3[3];
        Vector3 tip = new(4f, PlanPathStyle.AbilityPathHeight, 9f);

        Assert.That(PlanPathStyle.TryBuildEndChevron(tip, Vector3.right * 3f, chevron), Is.True);
        Assert.That(chevron[1], Is.EqualTo(tip));

        // Both barbs trail the tip by the same distance and sit either side of the approach.
        Vector3 back = tip - Vector3.right * PlanPathStyle.DestinationLength;
        Assert.That(
            Vector3.Distance(chevron[0], back),
            Is.EqualTo(PlanPathStyle.DestinationHalfWidth).Within(Tolerance)
        );
        Assert.That(
            Vector3.Distance(chevron[2], back),
            Is.EqualTo(PlanPathStyle.DestinationHalfWidth).Within(Tolerance)
        );
        Assert.That(
            Vector3.Distance((chevron[0] + chevron[2]) * 0.5f, back),
            Is.LessThan(Tolerance)
        );
    }

    [Test]
    public void EndChevron_IsDeclinedWhenThePathHasNoDirectionToPointAlong()
    {
        Vector3[] chevron = new Vector3[3];

        Assert.That(
            PlanPathStyle.TryBuildEndChevron(Vector3.zero, Vector3.up * 5f, chevron),
            Is.False,
            "A purely vertical approach names no direction across the board."
        );
        Assert.That(PlanPathStyle.TryBuildEndChevron(Vector3.zero, Vector3.zero, chevron), Is.False);
    }
}
