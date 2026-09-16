using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Which of its legal ability shots the bot is willing to take.
/// <para>
/// The team sees the union of every unit's vision, so a cell one unit is looking at can sit behind
/// a wall from the unit holding the ability. Planning allows that aim — walls are part of what some
/// of these abilities are for — so nothing here is about legality. It is about the bot not spending
/// the round's one ability on a beam that stops at a wall.
/// </para>
/// </summary>
[TestFixture]
[Category("BotAbilityTargeting")]
public class BotAbilityTargetingEditModeTests
{
    /// <summary>
    /// A caster, an enemy cell it cannot see past a wall, and one it can.
    /// <para>
    /// Board-dependent: <see cref="GameLoop.wallLayout"/> is the live active map, so the geometry
    /// is searched for rather than written down.
    /// </para>
    /// </summary>
    private readonly struct Geometry
    {
        public readonly Vector2Int Caster;
        public readonly Vector2Int Blocked;
        public readonly Vector2Int Clear;

        public Geometry(Vector2Int caster, Vector2Int blocked, Vector2Int clear)
        {
            Caster = caster;
            Blocked = blocked;
            Clear = clear;
        }

        public Vector2Int[] BothTargets => new[] { Blocked, Clear };
    }

    private static IEnumerable<Vector2Int> OpenCells()
    {
        for (int x = 0; x < GridSystem.ColumnCount; x++)
        {
            for (int y = 0; y < GridSystem.RowCount; y++)
            {
                Vector2Int cell = new(x, y);
                if (!GameLoop.wallLayout.Contains(cell))
                    yield return cell;
            }
        }
    }

    private static int GridDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    /// <summary>
    /// Finds a caster with a blocked cell and a clear one, whose distances from it stand in
    /// <paramref name="compareDistances"/> to each other — nearer-blocked to show the filter
    /// working, equidistant to show the tie-break.
    /// </summary>
    private static Geometry FindGeometry(System.Func<int, int, bool> compareDistances)
    {
        List<Vector2Int> open = OpenCells().ToList();

        foreach (Vector2Int caster in open)
        {
            List<Vector2Int> others = open.Where(cell => cell != caster).ToList();
            List<Vector2Int> blockedCells = others
                .Where(cell => !GridSystem.HasGridLineOfSight(caster, cell))
                .ToList();
            List<Vector2Int> clearCells = others
                .Where(cell => GridSystem.HasGridLineOfSight(caster, cell))
                .ToList();

            foreach (Vector2Int blocked in blockedCells)
            {
                foreach (Vector2Int clear in clearCells)
                {
                    if (
                        compareDistances(
                            GridDistance(caster, blocked),
                            GridDistance(caster, clear)
                        )
                    )
                    {
                        return new Geometry(caster, blocked, clear);
                    }
                }
            }
        }

        Assert.Fail(
            "The active map has no caster whose blocked and clear cells stand in the distance "
                + "relation this test needs, so nothing here can distinguish the orders under test."
        );
        return default;
    }

    /// <summary>A nearer blocked cell: the arrangement nearest-first alone picks wrong.</summary>
    private static Geometry FindBlockedNearerGeometry()
    {
        return FindGeometry((blocked, clear) => blocked < clear);
    }

    /// <summary>Both cells the same distance out, where only the line can decide between them.</summary>
    private static Geometry FindEquidistantGeometry()
    {
        return FindGeometry((blocked, clear) => blocked == clear);
    }

    [Test]
    public void ARequiredLineDropsEveryTargetBehindAWall()
    {
        Geometry board = FindBlockedNearerGeometry();

        List<Vector2Int> aims = BotPlayer
            .OrderAimCandidates(board.Caster, board.BothTargets, AbilityLineOfFire.Required)
            .ToList();

        Assert.That(
            aims,
            Is.EqualTo(new[] { board.Clear }),
            "A beam that stops at the wall has nothing to aim at behind one, however close the "
                + "unit standing there is."
        );
    }

    [Test]
    public void APreferredLineBreaksTiesBetweenTargetsTheSameDistanceOut()
    {
        Geometry board = FindEquidistantGeometry();

        List<Vector2Int> aims = BotPlayer
            .OrderAimCandidates(board.Caster, board.BothTargets, AbilityLineOfFire.Preferred)
            .ToList();

        Assert.That(
            aims,
            Is.EqualTo(new[] { board.Clear, board.Blocked }),
            "Given two aims the same distance out, the rocket should be sent at the one it can "
                + "put its blast on the unit with — and still keep the other as a fallback."
        );
    }

    /// <summary>
    /// The preference is a tie-break and nothing more. Reaching past a nearer target for a clear
    /// line at a further one trades a shot that lands for one that may not: this planner measures
    /// range by how far a player may aim, which for Breach is the whole board, while its rocket
    /// flies eight cells and detonates wherever it runs out.
    /// </summary>
    [Test]
    public void APreferredLineNeverReachesPastANearerTarget()
    {
        Geometry board = FindBlockedNearerGeometry();

        List<Vector2Int> aims = BotPlayer
            .OrderAimCandidates(board.Caster, board.BothTargets, AbilityLineOfFire.Preferred)
            .ToList();

        Assert.That(aims, Is.EqualTo(new[] { board.Blocked, board.Clear }));
    }

    /// <summary>
    /// A lob and a leap clear walls, so nothing about them should have changed: nearest first,
    /// which is the order this planner picked targets in before any of this existed.
    /// </summary>
    [Test]
    public void AnIrrelevantLineStillAimsAtWhateverIsNearest()
    {
        Geometry board = FindBlockedNearerGeometry();

        List<Vector2Int> aims = BotPlayer
            .OrderAimCandidates(board.Caster, board.BothTargets, AbilityLineOfFire.Irrelevant)
            .ToList();

        Assert.That(aims, Is.EqualTo(new[] { board.Blocked, board.Clear }));
    }

    [Test]
    public void OrderingIsDeterministicAcrossTheOrderCellsArriveIn()
    {
        Geometry board = FindEquidistantGeometry();
        Vector2Int[] forwards = board.BothTargets;
        Vector2Int[] backwards = forwards.Reverse().ToArray();

        foreach (AbilityLineOfFire lineOfFire in System.Enum.GetValues(typeof(AbilityLineOfFire)))
        {
            Assert.That(
                BotPlayer.OrderAimCandidates(board.Caster, backwards, lineOfFire).ToList(),
                Is.EqualTo(
                    BotPlayer.OrderAimCandidates(board.Caster, forwards, lineOfFire).ToList()
                ),
                $"{lineOfFire} must not depend on the order the visible cell set enumerates in."
            );
        }
    }

    [Test]
    public void OnlyARequiredLineRefusesAShot()
    {
        Geometry board = FindBlockedNearerGeometry();

        Assert.That(
            BotPlayer.HasUsableLineOfFire(
                board.Caster,
                board.Blocked,
                AbilityLineOfFire.Required
            ),
            Is.False
        );
        Assert.That(
            BotPlayer.HasUsableLineOfFire(
                board.Caster,
                board.Blocked,
                AbilityLineOfFire.Preferred
            ),
            Is.True,
            "Preferred only ever demotes; refusing here would silence Bunker Buster on the shots "
                + "it was built for."
        );
        Assert.That(
            BotPlayer.HasUsableLineOfFire(
                board.Caster,
                board.Blocked,
                AbilityLineOfFire.Irrelevant
            ),
            Is.True
        );
        Assert.That(
            BotPlayer.HasUsableLineOfFire(board.Caster, board.Clear, AbilityLineOfFire.Required),
            Is.True
        );
    }

    // === What each ability declares ===

    private static AbilityLineOfFire DeclaredBy<T>()
        where T : Ability
    {
        GameObject host = new($"{typeof(T).Name}LineOfFireProbe");
        try
        {
            return host.AddComponent<T>().LineOfFire;
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    /// <summary>
    /// The declarations the bot reads. Each one is a claim about that ability's own resolution —
    /// a raycast, a ground run, a lob — so a change to how one of them lands has to come back
    /// through here.
    /// </summary>
    [Test]
    public void TheLineAbilitiesDeclareTheLinesTheyResolveAlong()
    {
        Assert.That(
            DeclaredBy<AreaLock>(),
            Is.EqualTo(AbilityLineOfFire.Required),
            "The sniper's beam is a raycast that stops at the first wall."
        );
        Assert.That(
            DeclaredBy<ArcSurge>(),
            Is.EqualTo(AbilityLineOfFire.Required),
            "Every arc is raycast out of the caster's hands."
        );
        Assert.That(
            DeclaredBy<BunkerBuster>(),
            Is.EqualTo(AbilityLineOfFire.Preferred),
            "The rocket detonates on the wall and destroys it, so a blocked shot still buys "
                + "something."
        );
    }

    [Test]
    public void EverythingThrownOrLeaptIgnoresWalls()
    {
        Assert.That(DeclaredBy<Grenade>(), Is.EqualTo(AbilityLineOfFire.Irrelevant));
        Assert.That(DeclaredBy<ShatterLeap>(), Is.EqualTo(AbilityLineOfFire.Irrelevant));
        Assert.That(DeclaredBy<Pogo>(), Is.EqualTo(AbilityLineOfFire.Irrelevant));
        Assert.That(DeclaredBy<Smoke>(), Is.EqualTo(AbilityLineOfFire.Irrelevant));
    }
}
