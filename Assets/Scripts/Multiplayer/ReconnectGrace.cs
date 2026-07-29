using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ReconnectSeatStatus : byte
{
    Unknown,
    Connected,
    AwaitingRejoin,
    Forfeited,
}

/// <summary>
/// Server-side record of which player identity owns which logical team, and how long a dropped
/// seat is held open before the match resolves as a forfeit.
/// <para>
/// NGO hands out a fresh client ID on every connection, so a seat is keyed on the reconnect token
/// the client presents in its approval payload; the client ID is only a cache of whichever
/// connection currently holds the seat. Bots never occupy a seat: they have no connection to lose.
/// </para>
/// </summary>
public sealed class ReconnectGrace
{
    /// <summary>
    /// How long a dropped seat is held open. Long enough to cover a router blip plus a fresh Relay
    /// allocation and NGO resynchronisation, short enough that the player still at the board is
    /// never waiting on an opponent who is not coming back.
    /// </summary>
    public const float GraceSeconds = 45f;

    private sealed class Seat
    {
        public int TeamIndex;
        public string Token;
        public ulong ClientId;
        public ReconnectSeatStatus Status;
        public double GraceDeadline;
    }

    private readonly Dictionary<ulong, string> tokensByClientId = new();
    private readonly Dictionary<int, Seat> seatsByTeam = new();

    private static ReconnectGrace server = new();

    /// <summary>The registry for the match this process is hosting.</summary>
    public static ReconnectGrace Server => server;

    /// <summary>
    /// Wall clock. A dropped connection is a real-world event, so the hold is counted in real
    /// seconds and never stretches or shrinks with <see cref="Time.timeScale"/>. This agrees in
    /// rate with NGO's <c>ServerTime</c>, which the dodge window uses: that advances on
    /// <c>RealTimeProvider.UnscaledDeltaTime</c> and is equally unaffected by dev fast-forward. So
    /// both windows are the length they claim at any speed setting, and the two clocks differ only
    /// in epoch — never mix them in the same subtraction.
    /// </summary>
    public static double Now => Time.realtimeSinceStartupAsDouble;

    /// <summary>Drops all seats and identities; used between matches and by tests.</summary>
    public static void ResetServer()
    {
        server = new ReconnectGrace();
    }

    /// <summary>
    /// Remembers the identity a connection presented at approval so a later drop knows whose seat
    /// to hold. An unusable token simply leaves the connection without reconnect rights.
    /// </summary>
    public void RegisterConnection(ulong clientId, string token)
    {
        if (!ReconnectSession.IsValidToken(token))
        {
            tokensByClientId.Remove(clientId);
            return;
        }

        tokensByClientId[clientId] = token;
    }

    public bool TryGetRegisteredToken(ulong clientId, out string token)
    {
        return tokensByClientId.TryGetValue(clientId, out token);
    }

    /// <summary>
    /// Binds each holdable seat to the identity currently occupying it. Callers pass remote human
    /// seats only: the host is the server, so its seat cannot outlive the session it would rejoin.
    /// </summary>
    public void BeginMatch(IEnumerable<(int teamIndex, ulong clientId)> humanSeats)
    {
        seatsByTeam.Clear();
        if (humanSeats == null)
            return;

        foreach ((int teamIndex, ulong clientId) in humanSeats)
        {
            if (teamIndex < 0 || !tokensByClientId.TryGetValue(clientId, out string token))
                continue;

            seatsByTeam[teamIndex] = new Seat
            {
                TeamIndex = teamIndex,
                Token = token,
                ClientId = clientId,
                Status = ReconnectSeatStatus.Connected,
                GraceDeadline = 0d,
            };
        }
    }

    /// <summary>Forgets every seat; identities stay so a fresh match can rebind them.</summary>
    public void EndMatch()
    {
        seatsByTeam.Clear();
    }

    public int SeatCount => seatsByTeam.Count;

    /// <summary>
    /// Starts the hold for the seat a lost connection was occupying. Returns false for the bot,
    /// for connections that never owned a seat, and for a seat whose occupant already left on
    /// purpose — all of which fall through to the caller's immediate forfeit path.
    /// </summary>
    public bool TryBeginGrace(ulong clientId, double now, out int teamIndex)
    {
        teamIndex = -1;
        Seat seat = FindSeat(ReconnectSeatStatus.Connected, entry => entry.ClientId == clientId);
        if (seat == null)
            return false;

        tokensByClientId.Remove(clientId);
        seat.Status = ReconnectSeatStatus.AwaitingRejoin;
        seat.GraceDeadline = now + GraceSeconds;
        teamIndex = seat.TeamIndex;
        return true;
    }

    /// <summary>
    /// Hands a held seat to a connection that proved it owns that seat's identity. A token that
    /// matches nothing, matches a seat somebody is still sitting in, or arrives after the window
    /// closed is refused, so a stale or hostile client cannot take another player's crew.
    /// </summary>
    public bool TryClaimSeat(string token, ulong claimantClientId, double now, out int teamIndex)
    {
        teamIndex = -1;
        if (!ReconnectSession.IsValidToken(token))
            return false;

        Seat seat = FindSeat(
            ReconnectSeatStatus.AwaitingRejoin,
            entry => string.Equals(entry.Token, token, StringComparison.Ordinal)
        );
        if (seat == null || now >= seat.GraceDeadline)
            return false;

        seat.ClientId = claimantClientId;
        seat.Status = ReconnectSeatStatus.Connected;
        seat.GraceDeadline = 0d;
        tokensByClientId[claimantClientId] = token;
        teamIndex = seat.TeamIndex;
        return true;
    }

    /// <summary>Closes a seat immediately: its occupant left deliberately rather than dropping.</summary>
    public bool Forfeit(ulong clientId)
    {
        Seat seat = seatsByTeam.Values.FirstOrDefault(entry =>
            entry.Status != ReconnectSeatStatus.Forfeited && entry.ClientId == clientId
        );
        tokensByClientId.Remove(clientId);
        if (seat == null)
            return false;

        seat.Status = ReconnectSeatStatus.Forfeited;
        seat.GraceDeadline = 0d;
        return true;
    }

    /// <summary>
    /// Consumes the first seat whose window has closed. Consuming it marks the seat forfeited so
    /// the match ends exactly once no matter how often the caller polls.
    /// </summary>
    public bool TryConsumeExpiredSeat(double now, out int teamIndex)
    {
        teamIndex = -1;
        Seat seat = FindSeat(
            ReconnectSeatStatus.AwaitingRejoin,
            entry => now >= entry.GraceDeadline
        );
        if (seat == null)
            return false;

        seat.Status = ReconnectSeatStatus.Forfeited;
        seat.GraceDeadline = 0d;
        teamIndex = seat.TeamIndex;
        return true;
    }

    /// <summary>Pulls a held seat's deadline to <paramref name="now"/>; dev hook for expiry tests.</summary>
    public bool ExpireNow(double now)
    {
        Seat seat = FindSeat(ReconnectSeatStatus.AwaitingRejoin, _ => true);
        if (seat == null)
            return false;

        seat.GraceDeadline = now;
        return true;
    }

    public bool IsAwaitingRejoin =>
        seatsByTeam.Values.Any(entry => entry.Status == ReconnectSeatStatus.AwaitingRejoin);

    public int AwaitingTeamIndex =>
        FindSeat(ReconnectSeatStatus.AwaitingRejoin, _ => true)?.TeamIndex ?? -1;

    public double RemainingSeconds(double now)
    {
        Seat seat = FindSeat(ReconnectSeatStatus.AwaitingRejoin, _ => true);
        return seat == null ? 0d : Math.Max(0d, seat.GraceDeadline - now);
    }

    public ReconnectSeatStatus GetSeatStatus(int teamIndex)
    {
        return seatsByTeam.TryGetValue(teamIndex, out Seat seat)
            ? seat.Status
            : ReconnectSeatStatus.Unknown;
    }

    public bool TryGetSeatClientId(int teamIndex, out ulong clientId)
    {
        if (
            seatsByTeam.TryGetValue(teamIndex, out Seat seat)
            && seat.Status == ReconnectSeatStatus.Connected
        )
        {
            clientId = seat.ClientId;
            return true;
        }

        clientId = default;
        return false;
    }

    /// <summary>One-line snapshot for DevInput.Dump().</summary>
    public string Describe(double now)
    {
        if (seatsByTeam.Count == 0)
            return "no held seats";

        return string.Join(
            ", ",
            seatsByTeam
                .OrderBy(entry => entry.Key)
                .Select(entry =>
                    entry.Value.Status == ReconnectSeatStatus.AwaitingRejoin
                        ? $"team {entry.Key}=awaiting rejoin ({Math.Max(0d, entry.Value.GraceDeadline - now):0.#}s left)"
                        : $"team {entry.Key}={entry.Value.Status} (client {entry.Value.ClientId})"
                )
        );
    }

    private Seat FindSeat(ReconnectSeatStatus status, Func<Seat, bool> predicate)
    {
        return seatsByTeam
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .FirstOrDefault(seat => seat.Status == status && predicate(seat));
    }
}
