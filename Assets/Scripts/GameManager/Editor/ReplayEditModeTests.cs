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
            if (f >= 20 && f <= 35)
            {
                own.status = ReplayStatus.ShieldUp | ReplayStatus.Stunned;
                own.shieldDirection = ReplaySample.PackDirection(Vector2Int.left);
                own.cooldown = 2;
            }
            ReplaySample enemy = ReplaySample.Capture(
                Vector3.Lerp(Cell(5, 2), Cell(7, 2), progress),
                270f,
                80f - f,
                true
            );
            enemy.flags |= ReplaySample.SeenBy(Opponent);
            enemy.status = ReplayStatus.Locking;
            enemy.lockTarget = 1;
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
            orderIsAbility = true,
            order = new List<Vector2Int> { new(1, 1), new(4, 4) },
        });
        round.plans.Add(new ReplayPlan
        {
            unitIndex = 1,
            kind = ReplayPlanKind.Move,
            route = new List<Vector2Int> { new(5, 2), new(6, 2), new(7, 2) },
            order = new List<Vector2Int> { new(5, 2), new(6, 2), new(7, 2) },
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
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 420,
            kind = ReplayEventKind.Fx,
            cue = ReplayFxCue.GrenadeExplosion,
            unitIndex = 0,
            from = Cell(4, 4),
            to = Cell(4, 4),
            value = 1.5f,
            seenBy = ReplaySample.SeenBy(Viewer),
        });

        round.animationNames.Add("Aiming");
        round.animationNames.Add("Shoot");
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 610,
            kind = ReplayEventKind.Animation,
            unitIndex = 1,
            value = 1,
            detail = ReplayDetail.OneShotAnimation,
            seenBy = ReplaySample.SeenBy(Opponent),
        });
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 650,
            kind = ReplayEventKind.Damage,
            unitIndex = 0,
            from = Cell(1, 1),
            to = Cell(2, 1),
            value = 42f,
            detail = ReplayDetail.CritHit,
            seenBy = ReplaySample.SeenBy(Viewer) | ReplaySample.SeenBy(Opponent),
        });
        round.events.Add(new ReplayEvent
        {
            timeCentiseconds = 655,
            kind = ReplayEventKind.Fx,
            cue = ReplayFxCue.CameraShake,
            unitIndex = ReplayDetail.NoUnit,
            value = 0.1f,
            seenBy = ReplaySample.SeenBy(Viewer) | ReplaySample.SeenBy(Opponent),
        });
        round.hasHill = true;
        round.hill = new HillControlState(HillControlStatus.Controlled, Opponent, 2);

        round.smokeChanges.Add(new ReplaySmokeChange
        {
            timeCentiseconds = 350,
            cells = new List<Vector2Int> { new(6, 6), new(2, 7) },
        });

        ReplayTrack grenade = new() { prefabHash = 0xBEEF, endCentiseconds = 420 };
        grenade.points.Add(new ReplayTrackPoint { timeCentiseconds = 320, position = Cell(1, 1), scale = 1f });
        grenade.points.Add(new ReplayTrackPoint { timeCentiseconds = 370, position = Cell(2, 2) + Vector3.up * 2f, euler = new Vector3(0f, 90f, 0f), scale = 1f });
        grenade.points.Add(new ReplayTrackPoint { timeCentiseconds = 420, position = Cell(4, 4), scale = 1f });
        round.tracks.Add(grenade);
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
        Assert.That(archive.TryGet(round.roundNumber, out _), Is.EqualTo(round.tracks.Count == 0));
        foreach (ReplayTrack track in round.tracks)
            archive.Receive(RoundTrip(new ReplayTrackMessage { roundNumber = round.roundNumber, track = track }));

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
        Assert.That(read.events.Count, Is.EqualTo(7));

        ReplaySample shielded = read.frames[25][0];
        Assert.That(shielded.Has(ReplayStatus.ShieldUp), Is.True);
        Assert.That(shielded.Has(ReplayStatus.Stunned), Is.True);
        Assert.That(shielded.ShieldFacing, Is.EqualTo(Vector2Int.left));
        Assert.That(shielded.cooldown, Is.EqualTo(2));
        Assert.That(read.frames[40][0].Has(ReplayStatus.ShieldUp), Is.False);

        Assert.That(read.animationNames, Is.EqualTo(new List<string> { "Aiming", "Shoot" }));
        ReplayEvent hit = read.events.Find(e => e.kind == ReplayEventKind.Damage);
        Assert.That(hit.value, Is.EqualTo(42f));
        Assert.That(hit.detail & ReplayDetail.CritHit, Is.Not.Zero);
        Assert.That(read.events.Find(e => e.cue == ReplayFxCue.CameraShake).HasUnit, Is.False);

        Assert.That(read.hasHill, Is.True);
        Assert.That(read.hill.ControllingTeamIndex, Is.EqualTo(Opponent));
        Assert.That(read.hill.Streak, Is.EqualTo(2));

        ReplayEvent explosion = read.events.Find(e => e.kind == ReplayEventKind.Fx);
        Assert.That(explosion.cue, Is.EqualTo(ReplayFxCue.GrenadeExplosion));
        Assert.That(explosion.value, Is.EqualTo(1.5f));

        Assert.That(read.smokeChanges.Count, Is.EqualTo(1));
        Assert.That(read.smokeChanges[0].Time, Is.EqualTo(3.5f).Within(1e-4f));
        Assert.That(read.smokeChanges[0].cells.Count, Is.EqualTo(2));

        Assert.That(read.tracks.Count, Is.EqualTo(1));
        Assert.That(read.tracks[0].prefabHash, Is.EqualTo(0xBEEFu));
        Assert.That(read.tracks[0].points.Count, Is.EqualTo(3));
        Assert.That(read.frames[10][1].Position.y, Is.EqualTo(Cell(5, 2).y).Within(0.01f));

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
        Assert.That(hidden.status, Is.EqualTo(ReplayStatus.None));
        Assert.That(hidden.lockTarget, Is.Zero);
        Assert.That(view.frames[SeenUntilFrame][1].Has(ReplayStatus.Locking), Is.True);
        Assert.That(
            view.events.Exists(e => e.kind == ReplayEventKind.Animation),
            Is.False,
            "an animation the viewer never saw is not handed to it"
        );
        Assert.That(view.events.Exists(e => e.cue == ReplayFxCue.CameraShake), Is.True);
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
    public void FilterForTeam_KeepsThrownObjectsAndSmokeThatEveryScreenSawLive()
    {
        ReplayRound view = BuildRound().FilterForTeam(Opponent);

        Assert.That(view.tracks.Count, Is.EqualTo(1));
        Assert.That(view.smokeChanges.Count, Is.EqualTo(1));
    }

    [Test]
    public void Track_FollowsItsPathAndVanishesOutsideItsLife()
    {
        ReplayTrack track = BuildRound().tracks[0];

        Assert.That(track.TrySample(3.1f, out _, out _, out _), Is.False);
        Assert.That(track.TrySample(4.3f, out _, out _, out _), Is.False);

        Assert.That(track.TrySample(3.45f, out Vector3 midway, out _, out _), Is.True);
        Vector3 expected = Vector3.Lerp(Cell(1, 1), Cell(2, 2) + Vector3.up * 2f, 0.5f);
        Assert.That(Vector3.Distance(midway, expected), Is.LessThan(0.01f));

        Assert.That(track.TrySample(4.2f, out Vector3 landed, out _, out _), Is.True);
        Assert.That(Vector3.Distance(landed, Cell(4, 4)), Is.LessThan(0.01f));
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
        Assert.That(total, Is.LessThan(48 * 1024));
    }

    private static ReplayArchive ArchiveOfRounds(int count)
    {
        ReplayArchive archive = new();
        for (int number = 1; number <= count; number++)
        {
            ReplayRound round = BuildRound();
            round.roundNumber = number;
            archive.Receive(RoundTrip(round.BuildHeader()));
            for (int frame = 0; frame < round.frames.Count; frame += round.FramesPerChunk)
                archive.Receive(RoundTrip(round.BuildFrameChunk(frame)));
            for (int i = 0; i < round.events.Count; i += ReplayRound.EventsPerChunk)
                archive.Receive(RoundTrip(round.BuildEventChunk(i)));
            foreach (ReplayTrack track in round.tracks)
                archive.Receive(new ReplayTrackMessage { roundNumber = number, track = track });
        }
        return archive;
    }

    private static ReplayRound BranchRound(int number)
    {
        ReplayRound round = BuildRound();
        round.roundNumber = number;
        round.events.Clear();
        return round;
    }

    [Test]
    public void Branch_ReplacesTheCanonLineFromWhereItStarts()
    {
        ReplayArchive archive = ArchiveOfRounds(5);
        archive.StartBranchAt(3);
        archive.AddBranchRound(BranchRound(3));
        archive.AddBranchRound(BranchRound(4));

        Assert.That(archive.RoundNumbers, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(archive.IsBranchRound(2), Is.False);
        Assert.That(archive.IsBranchRound(3), Is.True);
        Assert.That(archive.TryGet(3, out ReplayRound three), Is.True);
        Assert.That(three.events, Is.Empty, "round 3 is now the branch's, not canon's");
        Assert.That(archive.TryGet(5, out _), Is.False, "canon after the branch point is out of the line");
        Assert.That(archive.LatestRoundNumber, Is.EqualTo(4));
    }

    [Test]
    public void Branch_OverriddenPartWayDropsOnlyItsOwnLaterRounds()
    {
        ReplayArchive archive = ArchiveOfRounds(5);
        archive.StartBranchAt(2);
        archive.AddBranchRound(BranchRound(2));
        archive.AddBranchRound(BranchRound(3));
        archive.AddBranchRound(BranchRound(4));

        archive.StartBranchAt(3);

        Assert.That(archive.RoundNumbers, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(archive.IsBranchRound(2), Is.True);
    }

    [Test]
    public void Branch_StartedBeforeTheOldOneReplacesItAndClearingRestoresCanon()
    {
        ReplayArchive archive = ArchiveOfRounds(5);
        archive.StartBranchAt(4);
        archive.AddBranchRound(BranchRound(4));

        archive.StartBranchAt(2);
        Assert.That(archive.RoundNumbers, Is.EqualTo(new[] { 1 }));

        archive.ClearBranch();
        Assert.That(archive.RoundNumbers, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(archive.TryGet(4, out ReplayRound four), Is.True);
        Assert.That(four.events, Is.Not.Empty);
    }

    [Test]
    public void Orders_SurviveShippingButStayOffAnEnemysCopy()
    {
        ReplayRound read = Ship(BuildRound().FilterForTeam(-1));
        Assert.That(read.plans[0].orderIsAbility, Is.True);
        Assert.That(read.plans[0].order, Is.EqualTo(new[] { new Vector2Int(1, 1), new Vector2Int(4, 4) }));
        Assert.That(read.plans[1].order.Count, Is.EqualTo(3));

        ReplayRound seen = BuildRound().FilterForTeam(Viewer);
        ReplayPlan enemy = seen.plans.Find(plan => plan.unitIndex == 1);
        Assert.That(enemy, Is.Not.Null);
        Assert.That(enemy.order, Is.Empty);
        Assert.That(seen.plans.Find(plan => plan.unitIndex == 0).order.Count, Is.EqualTo(2));
    }

    [Test]
    public void MirrorTeams_TradesSeatsWithoutMovingAnything()
    {
        ReplayRound round = BuildRound();
        round.hasHill = true;
        round.hill = new HillControlState(HillControlStatus.Controlled, Viewer, 2);
        ReplayRound mirrored = round.FilterForTeam(-1).MirrorTeams();

        Assert.That(mirrored.units[0].teamIndex, Is.EqualTo(Opponent));
        Assert.That(mirrored.units[1].teamIndex, Is.EqualTo(Viewer));
        Assert.That(mirrored.units[1].rosterSlot, Is.EqualTo(1));
        Assert.That(mirrored.hill.ControllingTeamIndex, Is.EqualTo(Opponent));
        Assert.That(mirrored.hill.Streak, Is.EqualTo(2));
        Assert.That(mirrored.frames[60][1].Known, Is.True);
        Assert.That(mirrored.frames[60][1].Position, Is.EqualTo(round.frames[60][1].Position));
        Assert.That(mirrored.events.Count, Is.EqualTo(round.events.Count));
        Assert.That(round.units[0].teamIndex, Is.EqualTo(Viewer));
    }

    [Test]
    public void ReplayCopy_SeatsTheSecondPlayerAsHostWithTheirOwnCrewAndView()
    {
        try
        {
            ReplayCopySession.Begin(
                new[] { BuildRound().FilterForTeam(-1) },
                new MatchOptions { opponentType = OpponentType.Player, fogOfWar = true },
                MatchResult.ForWinner(Opponent, MatchResultReason.Elimination),
                Opponent,
                HillControlState.Empty,
                3,
                1.5f
            );

            Assert.That(ReplayCopySession.SeatMirrored, Is.True);
            Assert.That(ReplayCopySession.Options.IsBotMatch, Is.True);
            Assert.That(ReplayCopySession.Options.fogOfWar, Is.False);
            Assert.That(ReplayCopySession.Result.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
            Assert.That(ReplayCopySession.CameraSeat(GameLoop.HostTeamIndex), Is.EqualTo(Opponent));

            int[] own = ReplayCopySession.BuildRoster(GameLoop.HostTeamIndex);
            Assert.That(own.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
            Assert.That(own[1], Is.EqualTo(4));
            Assert.That(ReplayCopySession.BuildRoster(GameLoop.OpponentTeamIndex)[0], Is.EqualTo(2));
            Assert.That(ReplayCopySession.Rounds[0].units[1].teamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        }
        finally
        {
            ReplayCopySession.End();
        }
        Assert.That(ReplayCopySession.IsActive, Is.False);
        Assert.That(ReplayCopySession.CameraSeat(GameLoop.HostTeamIndex), Is.EqualTo(GameLoop.HostTeamIndex));
    }

    [Test]
    public void Archive_HoldsAStoredRoundAndReportsOneStillArriving()
    {
        ReplayArchive archive = new();
        archive.Store(BuildRound().FilterForTeam(-1));
        Assert.That(archive.TryGet(3, out _), Is.True);
        Assert.That(archive.IsAssembling, Is.False);

        archive.Receive(RoundTrip(BuildRound().FilterForTeam(-1).BuildHeader()));
        Assert.That(archive.IsAssembling, Is.True);
        Assert.That(archive.TryGet(3, out _), Is.True);
    }
}
