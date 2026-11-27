using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[TestFixture]
[Category("GameplayNetwork")]
public class ReplayEditModeTests
{
    private const int Viewer = 0;
    private const int Opponent = 1;
    private const int SeenUntilFrame = 40;
    private const int FrameCount = 81;

    private static Vector3 Cell(int x, int y) => GameLoop.gridCoordToWorld(new Vector2Int(x, y));

    /// <summary>
    /// One unit a side. The opponent walks three cells but is only in the viewer's sight for the
    /// first half of the round, then fires from the fog.
    /// </summary>
    private static ReplayRound BuildRound()
    {
        ReplayRound round = new() { roundNumber = 3 };
        round.units.Add(new ReplayUnit { teamIndex = Viewer, rosterSlot = 0, catalogIndex = 2, maxHealth = 120 });
        round.units.Add(new ReplayUnit { teamIndex = Opponent, rosterSlot = 1, catalogIndex = 4, maxHealth = 80 });
        round.smokeCells.Add(new Vector2Int(6, 6));

        for (int f = 0; f < FrameCount; f++)
        {
            float progress = f / (float)(FrameCount - 1);
            ReplaySample own = ReplaySample.Capture(Cell(1, 1), 90f, 120f, true);
            own.flags |= ReplaySample.SeenBy(Viewer);
            ReplaySample enemy = ReplaySample.Capture(
                Vector3.Lerp(Cell(5, 2), Cell(7, 2), progress),
                270f,
                80f - f,
                true
            );
            enemy.flags |= ReplaySample.SeenBy(Opponent);
            if (f <= SeenUntilFrame)
                enemy.flags |= ReplaySample.SeenBy(Viewer);
            round.frames.Add(new[] { own, enemy });
        }

        round.plans.Add(new ReplayPlan
        {
            unitIndex = 0,
            kind = ReplayPlanKind.Ability,
            route = new List<Vector2Int> { new(1, 1), new(4, 4) },
            abilityOrigin = Cell(1, 1),
            abilitySquare = Cell(4, 4),
            abilityRadiusCells = 1.5f,
            abilityPathKind = AbilityPathKind.Lob,
            abilityPath = new List<Vector3> { Cell(1, 1), Cell(2, 2) + Vector3.up * 2f, Cell(4, 4) },
        });
        round.plans.Add(new ReplayPlan
        {
            unitIndex = 1,
            kind = ReplayPlanKind.Move,
            route = new List<Vector2Int> { new(5, 2), new(6, 2), new(7, 2) },
        });

        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 600,
            kind = ReplayEventKind.Shot,
            unitIndex = 1,
            from = Cell(7, 2),
            to = Cell(1, 1),
            seenBy = ReplaySample.SeenBy(Opponent),
        });
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 250,
            kind = ReplayEventKind.Shot,
            unitIndex = 0,
            from = Cell(1, 1),
            to = Cell(5, 2),
            seenBy = ReplaySample.SeenBy(Viewer),
        });
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 700,
            kind = ReplayEventKind.WallDestroyed,
            from = Cell(3, 3),
            to = Cell(3, 3),
        });
        return round;
    }

    private static T RoundTrip<T>(T value)
        where T : INetworkSerializable, new()
    {
        using FastBufferWriter writer = new(1024, Allocator.Temp, 1 << 16);
        writer.WriteNetworkSerializable(value);
        using FastBufferReader reader = new(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out T read);
        return read;
    }

    private static int SerializedSize<T>(T value)
        where T : INetworkSerializable
    {
        using FastBufferWriter writer = new(1024, Allocator.Temp, 1 << 16);
        writer.WriteNetworkSerializable(value);
        return writer.Length;
    }

    private static ReplayRound Ship(ReplayRound round)
    {
        ReplayArchive archive = new();
        archive.Receive(RoundTrip(round.BuildHeader()));
        for (int frame = 0; frame < round.frames.Count; frame += round.FramesPerChunk)
            archive.Receive(RoundTrip(round.BuildFrameChunk(frame)));
        for (int i = 0; i < round.events.Count; i += ReplayRound.EventsPerChunk)
            archive.Receive(RoundTrip(round.BuildEventChunk(i)));

        Assert.That(archive.TryGet(round.roundNumber, out ReplayRound shipped), Is.True);
        return shipped;
    }

    [Test]
    public void ShippedRound_ReassemblesFromItsChunksIntact()
    {
        ReplayRound written = BuildRound().FilterForTeam(-1);
        ReplayRound read = Ship(written);

        Assert.That(read.roundNumber, Is.EqualTo(3));
        Assert.That(read.units.Count, Is.EqualTo(2));
        Assert.That(read.units[1].catalogIndex, Is.EqualTo(4));
        Assert.That(read.units[1].maxHealth, Is.EqualTo(80));
        Assert.That(read.smokeCells, Is.EqualTo(new List<Vector2Int> { new(6, 6) }));
        Assert.That(read.frames.Count, Is.EqualTo(FrameCount));
        Assert.That(read.Duration, Is.EqualTo((FrameCount - 1) * ReplayRound.FrameInterval).Within(1e-4f));
        Assert.That(read.events.Count, Is.EqualTo(3));

        ReplayPlan ability = read.plans[0];
        Assert.That(ability.kind, Is.EqualTo(ReplayPlanKind.Ability));
        Assert.That(ability.abilityRadiusCells, Is.EqualTo(1.5f).Within(1e-4f));
        Assert.That(ability.abilityPathKind, Is.EqualTo(AbilityPathKind.Lob));
        Assert.That(ability.abilityPath.Count, Is.EqualTo(3));
        Assert.That(ability.abilityPath[1].y, Is.EqualTo(2f).Within(0.01f));
        Assert.That(read.plans[1].route.Count, Is.EqualTo(3));
    }

    [Test]
    public void Samples_KeepPositionWellInsideACell()
    {
        Vector3 position = new(12.3456f, 0f, 21.0987f);
        ReplaySample sample = ReplaySample.Capture(position, 123f, 57.2f, true);

        Assert.That(Vector3.Distance(sample.Position, position), Is.LessThan(0.05f * GameLoop.cellSize));
        Assert.That(Mathf.DeltaAngle(sample.Yaw, 123f), Is.LessThan(1.5f));
        Assert.That(sample.health, Is.EqualTo(58));
        Assert.That(sample.Alive, Is.True);
    }

    [Test]
    public void FilterForTeam_HidesTheEnemyOnceItLeavesSight()
    {
        ReplayRound view = BuildRound().FilterForTeam(Viewer);

        Assert.That(view.frames[SeenUntilFrame][1].Known, Is.True);
        ReplaySample hidden = view.frames[SeenUntilFrame + 1][1];
        Assert.That(hidden.Known, Is.False);
        Assert.That(hidden.x, Is.Zero);
        Assert.That(hidden.z, Is.Zero);
        Assert.That(view.frames[FrameCount - 1][0].Known, Is.True);

        Assert.That(view.TrySample(1, 1f, out _, out _, out _), Is.True);
        Assert.That(view.TrySample(1, 7f, out _, out _, out _), Is.False);
    }

    [Test]
    public void FilterForTeam_TrimsAnEnemyRouteToTheCellsItWasSeenIn()
    {
        ReplayRound view = BuildRound().FilterForTeam(Viewer);

        ReplayPlan route = view.plans.Find(plan => plan.unitIndex == 1);
        Assert.That(route, Is.Not.Null);
        Assert.That(route.route.Contains(new Vector2Int(7, 2)), Is.False);
        Assert.That(route.route.Contains(new Vector2Int(5, 2)), Is.True);

        ReplayPlan own = view.plans.Find(plan => plan.unitIndex == 0);
        Assert.That(own.abilityPath.Count, Is.EqualTo(3));
    }

    [Test]
    public void FilterForTeam_DropsShotsFiredFromFogButKeepsWallsAndOwnFire()
    {
        ReplayRound view = BuildRound().FilterForTeam(Viewer);

        Assert.That(view.events.Exists(e => e.kind == ReplayEventKind.Shot && e.unitIndex == 1), Is.False);
        Assert.That(view.events.Exists(e => e.kind == ReplayEventKind.Shot && e.unitIndex == 0), Is.True);
        Assert.That(view.events.Exists(e => e.kind == ReplayEventKind.WallDestroyed), Is.True);
    }

    [Test]
    public void FilterForTeam_PostMatchRevealKeepsEverything()
    {
        ReplayRound source = BuildRound();
        ReplayRound reveal = source.FilterForTeam(-1);

        Assert.That(reveal.frames[FrameCount - 1][1].Known, Is.True);
        Assert.That(reveal.plans.Find(plan => plan.unitIndex == 1).route.Count, Is.EqualTo(3));
        Assert.That(reveal.events.Count, Is.EqualTo(source.events.Count));
    }

    [Test]
    public void FilterForTeam_HidesWhereAnUnseenEnemyAbilityCameFrom()
    {
        ReplayRound source = BuildRound();
        ReplayPlan plan = source.plans[0];
        for (int f = 0; f < source.frames.Count; f++)
            source.frames[f][0].flags &= ~ReplaySample.SeenBy(Opponent);

        ReplayPlan seenByOpponent = source.FilterForTeam(Opponent).plans.Find(p => p.unitIndex == 0);
        Assert.That(seenByOpponent.originKnown, Is.False);
        Assert.That(seenByOpponent.abilityPath, Is.Empty);
        Assert.That(seenByOpponent.abilitySquare, Is.EqualTo(plan.abilitySquare));
    }

    [Test]
    public void FullRound_EveryMessageFitsTheTransportPayload()
    {
        ReplayRound round = new() { roundNumber = 1 };
        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 5; slot++)
            {
                round.units.Add(new ReplayUnit { teamIndex = (byte)team, rosterSlot = (byte)slot });
                round.plans.Add(new ReplayPlan
                {
                    unitIndex = (byte)round.plans.Count,
                    kind = ReplayPlanKind.Move,
                    route = new List<Vector2Int> { new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0) },
                });
            }
        }
        for (int f = 0; f < 81; f++)
        {
            ReplaySample[] frame = new ReplaySample[round.units.Count];
            for (int i = 0; i < frame.Length; i++)
                frame[i] = ReplaySample.Capture(Cell(i, 1), 0f, 100f, true);
            round.frames.Add(frame);
        }
        for (int i = 0; i < ReplayRound.MaxEvents; i++)
            round.events.Add(new ReplayEvent { kind = ReplayEventKind.Shot, from = Cell(1, 1), to = Cell(9, 9) });

        const int TransportPayload = 6144;
        int total = SerializedSize(round.BuildHeader());
        Assert.That(SerializedSize(round.BuildHeader()), Is.LessThan(TransportPayload));
        for (int frame = 0; frame < round.frames.Count; frame += round.FramesPerChunk)
        {
            int size = SerializedSize(round.BuildFrameChunk(frame));
            Assert.That(size, Is.LessThan(TransportPayload));
            total += size;
        }
        for (int i = 0; i < round.events.Count; i += ReplayRound.EventsPerChunk)
        {
            int size = SerializedSize(round.BuildEventChunk(i));
            Assert.That(size, Is.LessThan(TransportPayload));
            total += size;
        }
        Assert.That(total, Is.LessThan(24 * 1024));
    }
}
