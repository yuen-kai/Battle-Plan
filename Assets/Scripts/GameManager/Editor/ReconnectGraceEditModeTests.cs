using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

[TestFixture]
[Category("ReconnectGrace")]
public class ReconnectGraceEditModeTests
{
    private const string HostToken = "0123456789abcdef0123456789abcdef";
    private const string OpponentToken = "fedcba9876543210fedcba9876543210";
    private const string StrangerToken = "aaaaaaaabbbbbbbbccccccccdddddddd";

    private const ulong HostClientId = 0UL;
    private const ulong OpponentClientId = 7UL;
    private const ulong RejoinedClientId = 12UL;

    [SetUp]
    public void SetUp()
    {
        ReconnectGrace.ResetServer();
        MatchOptions.Reset();
        GameLoop.ResetMatchState();
    }

    [TearDown]
    public void TearDown()
    {
        GameLoop.ResetMatchState();
        MatchOptions.Reset();
        ReconnectGrace.ResetServer();
    }

    [Test]
    public void GraceWindowIsBoundedAndSharedByBothHalvesOfTheHandshake()
    {
        Assert.That(
            ReconnectGrace.GraceSeconds,
            Is.InRange(20f, 120f),
            "A hold long enough to cover a blip but short enough that the remaining player is "
                + "never parked in front of a dead board."
        );
        Assert.That(ReconnectSession.TokenLength, Is.EqualTo(32));
    }

    [Test]
    public void ReconnectTokenRoundTripsAndRejectsAnythingElse()
    {
        byte[] payload = ReconnectSession.EncodePayload(OpponentToken);
        Assert.That(payload.Length, Is.EqualTo(ReconnectSession.TokenLength));
        Assert.That(ReconnectSession.DecodeToken(payload), Is.EqualTo(OpponentToken));

        Assert.That(ReconnectSession.IsValidToken(null), Is.False);
        Assert.That(ReconnectSession.IsValidToken(string.Empty), Is.False);
        Assert.That(
            ReconnectSession.IsValidToken(OpponentToken.ToUpperInvariant()),
            Is.False,
            "Tokens are Guid.ToString(\"N\") output; case drift would split one identity in two."
        );
        Assert.That(ReconnectSession.IsValidToken(OpponentToken + "0"), Is.False);
        Assert.That(ReconnectSession.IsValidToken(new string('z', 32)), Is.False);

        Assert.That(ReconnectSession.DecodeToken(null), Is.Null);
        Assert.That(ReconnectSession.DecodeToken(System.Array.Empty<byte>()), Is.Null);
        Assert.That(
            ReconnectSession.DecodeToken(Encoding.ASCII.GetBytes(new string('x', 4096))),
            Is.Null,
            "An approval payload arrives from an unauthenticated peer and must be size-capped."
        );
        Assert.That(ReconnectSession.EncodePayload("not-a-token"), Is.Empty);
    }

    [Test]
    public void DroppedSeatIsHeldForTheGraceWindowThenExpiresExactlyOnce()
    {
        ReconnectGrace grace = BuildPvpMatch();
        Assert.That(grace.IsAwaitingRejoin, Is.False);
        Assert.That(
            grace.GetSeatStatus(GameLoop.OpponentTeamIndex),
            Is.EqualTo(ReconnectSeatStatus.Connected)
        );

        Assert.That(grace.TryBeginGrace(OpponentClientId, 100d, out int heldTeam), Is.True);
        Assert.That(heldTeam, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(grace.IsAwaitingRejoin, Is.True);
        Assert.That(grace.AwaitingTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(
            grace.GetSeatStatus(GameLoop.OpponentTeamIndex),
            Is.EqualTo(ReconnectSeatStatus.AwaitingRejoin)
        );
        Assert.That(
            grace.RemainingSeconds(100d),
            Is.EqualTo((double)ReconnectGrace.GraceSeconds).Within(0.0001d)
        );
        Assert.That(grace.RemainingSeconds(100d + ReconnectGrace.GraceSeconds - 1d), Is.EqualTo(1d).Within(0.0001d));

        Assert.That(
            grace.TryConsumeExpiredSeat(100d + ReconnectGrace.GraceSeconds - 0.01d, out _),
            Is.False,
            "The match must not forfeit while the window is still open."
        );
        Assert.That(
            grace.TryConsumeExpiredSeat(100d + ReconnectGrace.GraceSeconds, out int expiredTeam),
            Is.True
        );
        Assert.That(expiredTeam, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(
            grace.TryConsumeExpiredSeat(9999d, out _),
            Is.False,
            "Expiry is consumed once; the round loop polls it every frame."
        );
        Assert.That(grace.IsAwaitingRejoin, Is.False);
        Assert.That(grace.RemainingSeconds(9999d), Is.EqualTo(0d));
    }

    [Test]
    public void ExpiredSeatForfeitsTheMatchToTheTeamStillPlaying()
    {
        ReconnectGrace grace = BuildPvpMatch();
        grace.TryBeginGrace(OpponentClientId, 0d, out int heldTeam);
        Assert.That(grace.TryConsumeExpiredSeat(ReconnectGrace.GraceSeconds, out int expiredTeam), Is.True);
        Assert.That(expiredTeam, Is.EqualTo(heldTeam));

        MatchResult forfeit = MatchResult.ForWinner(
            GameLoop.GetEnemyTeamIndex(expiredTeam),
            MatchResultReason.DisconnectForfeit
        );

        Assert.That(forfeit.IsValid, Is.True);
        Assert.That(forfeit.Reason, Is.EqualTo(MatchResultReason.DisconnectForfeit));
        Assert.That(forfeit.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(
            forfeit.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo("You win! Opponent disconnected.")
        );
    }

    [Test]
    public void HeldSeatIsReclaimedByTheSameIdentityOnAFreshClientId()
    {
        ReconnectGrace grace = BuildPvpMatch();
        grace.TryBeginGrace(OpponentClientId, 0d, out _);

        Assert.That(
            grace.TryClaimSeat(OpponentToken, RejoinedClientId, 5d, out int reclaimedTeam),
            Is.True,
            "NGO hands out a new client ID on every connection, so the seat has to be keyed on "
                + "the identity rather than the connection."
        );
        Assert.That(reclaimedTeam, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(
            grace.GetSeatStatus(GameLoop.OpponentTeamIndex),
            Is.EqualTo(ReconnectSeatStatus.Connected)
        );
        Assert.That(grace.IsAwaitingRejoin, Is.False);
        Assert.That(grace.TryGetSeatClientId(GameLoop.OpponentTeamIndex, out ulong seatClientId), Is.True);
        Assert.That(seatClientId, Is.EqualTo(RejoinedClientId));
        Assert.That(
            grace.TryConsumeExpiredSeat(9999d, out _),
            Is.False,
            "A reclaimed seat can never expire into a forfeit."
        );

        // A second drop of the same seat, now on the new client ID, must hold again.
        Assert.That(grace.TryBeginGrace(RejoinedClientId, 10d, out int heldAgain), Is.True);
        Assert.That(heldAgain, Is.EqualTo(GameLoop.OpponentTeamIndex));
    }

    [Test]
    public void SeatClaimsRefuseWrongIdentitiesOccupiedSeatsAndClosedWindows()
    {
        ReconnectGrace grace = BuildPvpMatch();

        Assert.That(
            grace.TryClaimSeat(OpponentToken, RejoinedClientId, 0d, out _),
            Is.False,
            "A seat somebody is still sitting in is not claimable."
        );

        grace.TryBeginGrace(OpponentClientId, 0d, out _);

        Assert.That(
            grace.TryClaimSeat(StrangerToken, RejoinedClientId, 1d, out _),
            Is.False,
            "An unrelated identity must not inherit a held crew."
        );
        Assert.That(
            grace.TryClaimSeat(HostToken, RejoinedClientId, 1d, out _),
            Is.False,
            "The other participant's identity must not be able to take this seat either."
        );
        Assert.That(grace.TryClaimSeat(null, RejoinedClientId, 1d, out _), Is.False);
        Assert.That(grace.TryClaimSeat("../../etc/passwd", RejoinedClientId, 1d, out _), Is.False);
        Assert.That(
            grace.GetSeatStatus(GameLoop.OpponentTeamIndex),
            Is.EqualTo(ReconnectSeatStatus.AwaitingRejoin),
            "A refused claim must leave the window exactly as it found it."
        );

        Assert.That(
            grace.TryClaimSeat(OpponentToken, RejoinedClientId, ReconnectGrace.GraceSeconds, out _),
            Is.False,
            "The window closes on its deadline, not after it."
        );
    }

    [Test]
    public void LeavingOnPurposeClosesTheSeatInsteadOfBurningTheWindow()
    {
        ReconnectGrace grace = BuildPvpMatch();

        Assert.That(grace.Forfeit(OpponentClientId), Is.True);
        Assert.That(
            grace.GetSeatStatus(GameLoop.OpponentTeamIndex),
            Is.EqualTo(ReconnectSeatStatus.Forfeited)
        );
        Assert.That(
            grace.TryBeginGrace(OpponentClientId, 0d, out _),
            Is.False,
            "The disconnect that follows a deliberate exit must fall straight through to the "
                + "existing forfeit path."
        );
        Assert.That(grace.IsAwaitingRejoin, Is.False);
        Assert.That(
            grace.TryClaimSeat(OpponentToken, RejoinedClientId, 0d, out _),
            Is.False,
            "A seat given up cannot be walked back into."
        );
    }

    [Test]
    public void BotAndSinglePlayerMatchesHoldNoSeats()
    {
        ReconnectGrace grace = ReconnectGrace.Server;
        grace.RegisterConnection(HostClientId, HostToken);
        grace.BeginMatch(BotMatchSeats());

        Assert.That(
            grace.SeatCount,
            Is.EqualTo(0),
            "The host is the server and the bot has no connection, so a bot match has nothing to "
                + "hold and can never enter the waiting phase."
        );
        Assert.That(grace.IsAwaitingRejoin, Is.False);
        Assert.That(grace.TryBeginGrace(HostClientId, 0d, out _), Is.False);
        Assert.That(
            grace.TryBeginGrace(GameLoop.BotParticipantId, 0d, out _),
            Is.False,
            "The bot sentinel is never an NGO connection and must never reach the hold."
        );
        Assert.That(grace.Describe(0d), Is.EqualTo("no held seats"));
        Assert.That(NetworkHandler.RequiredClientCount(BotOptions()), Is.EqualTo(1));
    }

    [Test]
    public void ConnectionsWithoutAUsableIdentityGetNoReconnectRights()
    {
        ReconnectGrace grace = ReconnectGrace.Server;
        grace.RegisterConnection(HostClientId, HostToken);
        grace.RegisterConnection(OpponentClientId, "definitely not a token");
        grace.BeginMatch(PvpSeats());

        Assert.That(grace.TryGetRegisteredToken(OpponentClientId, out _), Is.False);
        Assert.That(
            grace.SeatCount,
            Is.EqualTo(0),
            "An older or hand-rolled client that sends no identity still plays; it just forfeits "
                + "on a drop the way it always did."
        );
        Assert.That(grace.TryBeginGrace(OpponentClientId, 0d, out _), Is.False);
    }

    [Test]
    public void ReassigningASeatKeepsTheCrewAndRejectsBotOrDoubleOccupancy()
    {
        int[] hostRoster = RosterRules.BuildPreferredRoster(4, 1, 2, 0, 3);
        int[] opponentRoster = RosterRules.BuildPreferredRoster(2, 3, 4, 0, 1);
        GameLoop.ConfigureTeam(GameLoop.HostTeamIndex, HostClientId, hostRoster);
        GameLoop.ConfigureTeam(GameLoop.OpponentTeamIndex, OpponentClientId, opponentRoster);

        GameLoop.ReassignTeamParticipant(GameLoop.OpponentTeamIndex, RejoinedClientId);

        Assert.That(
            GameLoop.TryGetConfiguredParticipantId(GameLoop.OpponentTeamIndex, out ulong opponent),
            Is.True
        );
        Assert.That(opponent, Is.EqualTo(RejoinedClientId));
        Assert.That(
            GameLoop.TryGetConfiguredParticipantId(GameLoop.HostTeamIndex, out ulong host),
            Is.True
        );
        Assert.That(host, Is.EqualTo(HostClientId), "The other seat is untouched by a rejoin.");

        Assert.That(
            () => GameLoop.ReassignTeamParticipant(GameLoop.OpponentTeamIndex, HostClientId),
            Throws.ArgumentException,
            "A returning player may not be dropped into a seat that is already occupied."
        );
        Assert.That(
            () =>
                GameLoop.ReassignTeamParticipant(
                    GameLoop.OpponentTeamIndex,
                    GameLoop.BotParticipantId
                ),
            Throws.ArgumentException
        );
        Assert.That(
            () => GameLoop.ReassignTeamParticipant(GameLoop.TeamCount, RejoinedClientId),
            Throws.TypeOf<System.ArgumentOutOfRangeException>()
        );

        GameLoop.ResetMatchState();
        Assert.That(
            () => GameLoop.ReassignTeamParticipant(GameLoop.HostTeamIndex, RejoinedClientId),
            Throws.InvalidOperationException,
            "There is no seat to return to before the teams are configured."
        );
    }

    [Test]
    public void ReattachedSeatStaysAnAuthorizedObserverUnderTheNewClientId()
    {
        ReconnectGrace grace = BuildPvpMatch();
        grace.TryBeginGrace(OpponentClientId, 0d, out _);
        grace.TryClaimSeat(OpponentToken, RejoinedClientId, 1d, out int teamIndex);
        Assert.That(teamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));

        ulong[] assignedAfterRejoin = { HostClientId, RejoinedClientId };
        Assert.That(
            GameLoop.IsAuthorizedGameplayObserver(RejoinedClientId, HostClientId, assignedAfterRejoin),
            Is.True,
            "Approval repoints the seat before NGO synchronises, so the returning connection is "
                + "already recognised when its observer set is rebuilt."
        );
        Assert.That(
            GameLoop.IsAuthorizedGameplayObserver(OpponentClientId, HostClientId, assignedAfterRejoin),
            Is.False,
            "The connection that dropped keeps no visibility of the board it left."
        );
    }

    [Test]
    public void RejoinNoticeCountsDownAndReportsAClosedWindow()
    {
        Assert.That(
            GameHUDController.FormatRejoinCountdown(ReconnectGrace.GraceSeconds),
            Is.EqualTo($"Rejoin window · {Mathf.CeilToInt(ReconnectGrace.GraceSeconds)}s")
        );
        Assert.That(GameHUDController.FormatRejoinCountdown(9.2f), Is.EqualTo("Rejoin window · 10s"));
        Assert.That(GameHUDController.FormatRejoinCountdown(0.4f), Is.EqualTo("Rejoin window · 1s"));
        Assert.That(GameHUDController.FormatRejoinCountdown(0f), Is.EqualTo("Rejoin window closed"));
        Assert.That(GameHUDController.FormatRejoinCountdown(-3f), Is.EqualTo("Rejoin window closed"));
    }

    [Test]
    public void HoldDescriptionReportsTheSeatAndRemainingTime()
    {
        ReconnectGrace grace = BuildPvpMatch();
        Assert.That(grace.Describe(0d), Does.Contain("Connected"));

        grace.TryBeginGrace(OpponentClientId, 0d, out _);
        string held = grace.Describe(ReconnectGrace.GraceSeconds - 10d);
        Assert.That(held, Does.Contain($"team {GameLoop.OpponentTeamIndex}=awaiting rejoin"));
        Assert.That(held, Does.Contain("10s left"));
    }

    [Test]
    public void SmokeResendRebuildsTheServerFootprintCellForCell()
    {
        Vector2Int center = new(5, 3);
        Assert.That(GridSystem.IsSquareFootprintInBounds(center, Smoke.FootprintRadius), Is.True);
        List<Vector2Int> serverCells = GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius);

        // The payload the rejoin restore ships, and the decode ShowSmokeScreenClientRpc performs.
        Vector3[] payload = serverCells
            .OrderBy(cell => cell.y)
            .ThenBy(cell => cell.x)
            .Select(GameLoop.gridCoordToWorld)
            .ToArray();
        List<Vector2Int> clientCells = payload
            .Select(GridSystem.ConvertToGridCoords)
            .ToList();

        CollectionAssert.AreEquivalent(
            serverCells,
            clientCells,
            "A returning seat has to end up with the server's exact smoke set; a cell lost in the "
                + "world-space round trip is a cell it would wrongly see through."
        );
        CollectionAssert.AreEqual(
            serverCells.OrderBy(cell => cell.y).ThenBy(cell => cell.x).ToList(),
            clientCells,
            "The resend reuses GameLoop.ActiveSmokeCells, whose row-major order the smoke visual "
                + "is built from."
        );
    }

    [Test]
    public void AMissingSmokeModelWidensTheReturningClientsVisionPastTheServers()
    {
        // Board-dependent: GameLoop.wallLayout is a live static holding this map's walls, and a
        // wall behind the cloud hides a cell from both views, which is not the effect under test.
        // The placement is asserted clear rather than assumed, and the shadowed cells are derived.
        Vector2Int viewerCell = new(3, 4);
        Vector2Int cloudCentre = new(7, 4);
        (Vector2Int cell, int range)[] viewers = { (viewerCell, 6) };
        HashSet<Vector2Int> smoke = new(
            GridSystem.GetSquareFootprint(cloudCentre, Smoke.FootprintRadius)
        );

        Assert.That(GameLoop.wallLayout, Has.No.Member(viewerCell), "The viewer needs a cell.");
        Assert.That(
            smoke.Any(GameLoop.wallLayout.Contains),
            Is.False,
            "SanitizeAbilityPlan refuses a non-line ability aimed at a wall, so a cloud that "
                + "overlaps one is a placement the game cannot produce."
        );

        HashSet<Vector2Int> serverView = GridSystem.ComputeVisibleCells(viewers, smoke);
        HashSet<Vector2Int> viewWithoutTheResend = GridSystem.ComputeVisibleCells(
            viewers,
            new HashSet<Vector2Int>()
        );

        Assert.That(
            viewWithoutTheResend.IsProperSupersetOf(serverView),
            Is.True,
            "This is the defect the resend closes: a seat that came back with an empty smoke set "
                + "computes vision the server never granted it."
        );

        List<Vector2Int> leaked = viewWithoutTheResend.Except(serverView).ToList();
        int nearestCloudDistance = smoke.Min(cell => ManhattanDistance(viewerCell, cell));
        foreach (Vector2Int cell in leaked)
        {
            Assert.That(
                GridSystem.DoesCellSegmentCrossCells(viewerCell, cell, smoke),
                Is.True,
                $"{cell} leaked without its sightline crossing the cloud, so something other "
                    + "than smoke is being credited for hiding it."
            );
            Assert.That(
                ManhattanDistance(viewerCell, cell),
                Is.GreaterThan(nearestCloudDistance),
                $"{cell} leaked but stands no deeper than the cloud's near face. Smoke may only "
                    + "ever subtract what is behind it."
            );
        }

        List<Vector2Int> groundBehindTheCloud = leaked
            .Where(cell => !smoke.Contains(cell))
            .ToList();
        Assert.That(
            groundBehindTheCloud,
            Is.Not.Empty,
            "The leak has to include open ground the cloud is covering, not just the far cells "
                + "of the cloud itself; that ground is where an enemy would be standing."
        );
        Assert.That(
            groundBehindTheCloud,
            Does.Contain(new Vector2Int(cloudCentre.x + Smoke.FootprintRadius + 1, viewerCell.y)),
            "The cell one step past the cloud on the viewer's own row is the plainest case of a "
                + "position the server hides and an empty smoke model exposes."
        );

        Assert.That(
            GridSystem.ComputeVisibleCells(viewers, smoke),
            Is.EquivalentTo(serverView),
            "Once resent, the client recomputes exactly the server's set."
        );
    }

    [Test]
    public void RejoinRestoreRpcsCanBeAddressedToTheReturningClientAlone()
    {
        string[] restoreRpcs =
        {
            "ShowSmokeScreenClientRpc",
            "ShowAbilityTelegraphClientRpc",
            "StartDodgePlanningClientRpc",
            "SetUnitCardClientRpc",
            "SetCardDisabledClientRpc",
            "SetCardsInteractableClientRpc",
            "InitializeCameraPositionClientRpc",
            "setOverlayUITextClientRpc",
        };

        foreach (string rpcName in restoreRpcs)
        {
            MethodInfo rpc = typeof(GameLoop).GetMethod(
                rpcName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            Assert.That(rpc, Is.Not.Null, $"{rpcName} is part of the rejoin restore.");

            ParameterInfo last = rpc.GetParameters().Last();
            Assert.That(
                last.ParameterType,
                Is.EqualTo(typeof(ClientRpcParams)),
                $"{rpcName} must be addressable, or restoring one seat would replay round state "
                    + "to the opponent as well."
            );
            Assert.That(
                last.IsOptional,
                Is.True,
                $"{rpcName} is also broadcast during a normal round; those call sites pass no "
                    + "target."
            );
        }

        MethodInfo alerts = typeof(GameLoop).GetMethod(
            "SetDodgeAlerts",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(alerts, Is.Not.Null);
        ParameterInfo filter = alerts.GetParameters().Last();
        Assert.That(
            filter.ParameterType,
            Is.EqualTo(typeof(ulong?)),
            "The dive alert icons have to be restorable for one seat without re-flashing the "
                + "opponent's."
        );
        Assert.That(filter.IsOptional, Is.True);
    }

    [Test]
    public void RejoinedDodgePromptCarriesTheRemainderAndNeverAnExpiredWindow()
    {
        const double openedAt = 500d;
        const double windowLength = 9d;
        const double windowEnd = openedAt + windowLength;

        Assert.That(
            GameLoop.CanRestoreDodgePrompt(openedAt + 1d, windowEnd, true, false),
            Is.True
        );
        Assert.That(
            windowEnd - (openedAt + 1d),
            Is.LessThan(windowLength),
            "The deadline is resent verbatim in server time, so a returning seat can only ever "
                + "get the remainder of the opponent's window, never a fresh one."
        );

        Assert.That(
            GameLoop.CanRestoreDodgePrompt(windowEnd, windowEnd, true, false),
            Is.False,
            "A window that closes on this instant is not a window."
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(windowEnd + 30d, windowEnd, true, false),
            Is.False,
            "PlanMovement submits the moment its deadline is past, so an expired prompt would "
                + "spend the team's one response on an empty dive."
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(
                windowEnd - GameLoop.RejoinDodgeMinimumSeconds,
                windowEnd,
                true,
                false
            ),
            Is.True
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(
                windowEnd - GameLoop.RejoinDodgeMinimumSeconds + 0.01d,
                windowEnd,
                true,
                false
            ),
            Is.False,
            "Too little left to act in is the same as none at all."
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(openedAt, 0d, true, false),
            Is.False,
            "Zero is the closed-window sentinel: between rounds there is no dodge to hand back."
        );
        Assert.That(
            GameLoop.RejoinDodgeMinimumSeconds,
            Is.InRange(0.5f, 5f),
            "Long enough to draw a dive in, short enough not to throw away a usable window."
        );
    }

    [Test]
    public void RejoinedDodgeRestoreRefusesAPromptTheServerWouldNotHonour()
    {
        const double serverTime = 100d;
        const double windowEnd = serverTime + 20d;

        Assert.That(
            GameLoop.CanRestoreDodgePrompt(serverTime, windowEnd, false, false),
            Is.False,
            "No ability threatened this team, so SendDodgePathsToServerRpc would drop whatever "
                + "it sent; showing it a prompt would be a lie."
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(serverTime, windowEnd, true, true),
            Is.False,
            "One response per team. Re-prompting an answered seat would let the player draw a "
                + "dive the server has already stopped listening for."
        );
        Assert.That(
            GameLoop.CanRestoreDodgePrompt(serverTime, windowEnd, true, false),
            Is.True,
            "Alerted, unanswered and in time is the only shape that gets a prompt back."
        );
    }

    [Test]
    public void RejoinedDodgeGuidanceMatchesWhatTheSeatCanActuallyDo()
    {
        // The restore passes (promptRestored, isCaster) straight through, so these are the three
        // lines a returning seat can be given while a window is open.
        Assert.That(
            GameLoop.GetDodgeGuidance(true, false),
            Is.EqualTo(GameLoop.ThreatenedDodgeGuidance)
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(false, true),
            Is.EqualTo(GameLoop.CasterDodgeGuidance),
            "A caster that comes back mid-window is told its ability is being dodged, not that it "
                + "reconnected."
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(false, false),
            Is.EqualTo(GameLoop.NeutralDodgeGuidance)
        );

        Assert.That(
            GameLoop.GetDodgeGuidance(false, true),
            Is.Not.EqualTo(GameLoop.ThreatenedDodgeGuidance),
            "The threatened line is the only one that asks for an input, so a seat whose prompt "
                + "was withheld must never be shown it."
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(false, false),
            Is.Not.EqualTo(GameLoop.ThreatenedDodgeGuidance)
        );

        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(true, false),
            Is.EqualTo(MessagePerspective.Enemy)
        );
        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(false, true),
            Is.EqualTo(MessagePerspective.Friendly)
        );
        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(false, false),
            Is.EqualTo(MessagePerspective.Neutral)
        );
    }

    private static int ManhattanDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    private static ReconnectGrace BuildPvpMatch()
    {
        ReconnectGrace grace = ReconnectGrace.Server;
        grace.RegisterConnection(HostClientId, HostToken);
        grace.RegisterConnection(OpponentClientId, OpponentToken);
        grace.BeginMatch(PvpSeats());

        Assert.That(
            grace.SeatCount,
            Is.EqualTo(1),
            "Only the remote human holds a seat; the host is the server it would come back to."
        );
        return grace;
    }

    private static IEnumerable<(int teamIndex, ulong clientId)> PvpSeats()
    {
        // GameLoop passes remote human seats only, mirroring BeginReconnectGraceForMatch.
        return new[] { (GameLoop.OpponentTeamIndex, OpponentClientId) };
    }

    private static IEnumerable<(int teamIndex, ulong clientId)> BotMatchSeats()
    {
        return Enumerable.Empty<(int, ulong)>();
    }

    private static MatchOptions BotOptions()
    {
        MatchOptions options = MatchOptions.Default;
        options.opponentType = OpponentType.AI;
        return options;
    }
}
