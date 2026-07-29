using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What a unit spent a round doing, as recorded for the post-match reveal.
/// </summary>
public enum BattleReportOrder : byte
{
    /// <summary>Alive with no submitted route — it held its cell.</summary>
    Held = 0,
    Move = 1,
    Ability = 2,

    /// <summary>Dove during the dodge window, which replaced whatever it had planned.</summary>
    Dodge = 3,

    /// <summary>Already eliminated when the round began.</summary>
    Eliminated = 4,
}

/// <summary>One unit's committed order and outcome for one round.</summary>
public struct BattleReportEntry
{
    public int teamIndex;
    public int rosterSlot;
    public BattleReportOrder order;

    /// <summary>Cell occupied when the round began.</summary>
    public Vector2Int startCell;

    /// <summary>Cell occupied when the round resolved.</summary>
    public Vector2Int endCell;

    /// <summary>Committed route, start-inclusive. Empty for abilities and held orders.</summary>
    public List<Vector2Int> path;

    public bool hasAbilityTarget;
    public Vector2Int abilityTarget;

    public bool aliveAtRoundEnd;
    public bool diedThisRound;

    /// <summary>Rounded up, because the reveal shows a number rather than a bar.</summary>
    public int healthAtRoundEnd;
}

/// <summary>Roster identity, sent once instead of on every round entry.</summary>
public struct BattleReportCrewMember
{
    public int teamIndex;
    public int rosterSlot;

    /// <summary>Index into <c>UnitDatabase.units</c>, so the client resolves name and portrait.</summary>
    public int catalogIndex;
    public int maxHealth;
}

public class BattleReportRound
{
    public int roundNumber;
    public List<BattleReportEntry> entries = new();

    /// <summary>King of the Hill only. <see cref="GameLoop.NoHillController"/> when uncontrolled.</summary>
    public int hillControllerTeamIndex = GameLoop.NoHillController;
    public int hillStreak;
    public bool hillContested;
}

/// <summary>
/// The match seen from outside both fogs: every committed order from both crews, round by round.
///
/// This exists because the game's whole conceit is that orders are hidden and simultaneous, and
/// until the match is over a player only ever sees what physically happened, never what the
/// opponent actually intended. It is therefore built server-side and withheld until
/// <c>GameLoop.FinishGame</c> — replicating it any earlier would hand a client the enemy's plans
/// mid-match, which is exactly the information fog of war exists to deny.
/// </summary>
public class BattleReport : INetworkSerializable
{
    /// <summary>
    /// Bounds the end-of-match payload. Ten units at roughly 27 bytes per entry is about 270 bytes
    /// a round, so the cap keeps a pathological match well inside a single reliable message.
    /// Rounds past the cap are dropped from the reveal, not from the match.
    /// </summary>
    public const int MaxRecordedRounds = 40;

    public GameMode gameMode;
    public List<BattleReportCrewMember> crew = new();
    public List<BattleReportRound> rounds = new();

    public bool HasContent => rounds.Count > 0;

    public bool TryGetCrewMember(int teamIndex, int rosterSlot, out BattleReportCrewMember member)
    {
        foreach (BattleReportCrewMember candidate in crew)
        {
            if (candidate.teamIndex == teamIndex && candidate.rosterSlot == rosterSlot)
            {
                member = candidate;
                return true;
            }
        }

        member = default;
        return false;
    }

    // Cells are a byte pair rather than a Vector2Int because the board is 15x10 and the report is
    // the one payload in the game whose size scales with match length.
    private static void SerializeCell<T>(BufferSerializer<T> serializer, ref Vector2Int cell)
        where T : IReaderWriter
    {
        byte x = (byte)Mathf.Clamp(cell.x, 0, byte.MaxValue);
        byte y = (byte)Mathf.Clamp(cell.y, 0, byte.MaxValue);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        if (serializer.IsReader)
            cell = new Vector2Int(x, y);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        byte mode = (byte)gameMode;
        serializer.SerializeValue(ref mode);

        int crewCount = crew?.Count ?? 0;
        serializer.SerializeValue(ref crewCount);

        if (serializer.IsReader)
        {
            gameMode = (GameMode)mode;
            crew = new List<BattleReportCrewMember>(crewCount);
        }

        for (int i = 0; i < crewCount; i++)
        {
            BattleReportCrewMember member = serializer.IsWriter ? crew[i] : default;

            byte teamIndex = (byte)member.teamIndex;
            byte rosterSlot = (byte)member.rosterSlot;
            byte catalogIndex = (byte)member.catalogIndex;
            ushort maxHealth = (ushort)Mathf.Clamp(member.maxHealth, 0, ushort.MaxValue);

            serializer.SerializeValue(ref teamIndex);
            serializer.SerializeValue(ref rosterSlot);
            serializer.SerializeValue(ref catalogIndex);
            serializer.SerializeValue(ref maxHealth);

            if (serializer.IsReader)
            {
                crew.Add(
                    new BattleReportCrewMember
                    {
                        teamIndex = teamIndex,
                        rosterSlot = rosterSlot,
                        catalogIndex = catalogIndex,
                        maxHealth = maxHealth,
                    }
                );
            }
        }

        int roundCount = rounds?.Count ?? 0;
        serializer.SerializeValue(ref roundCount);

        if (serializer.IsReader)
            rounds = new List<BattleReportRound>(roundCount);

        for (int i = 0; i < roundCount; i++)
        {
            BattleReportRound round = serializer.IsWriter ? rounds[i] : new BattleReportRound();

            ushort roundNumber = (ushort)Mathf.Clamp(round.roundNumber, 0, ushort.MaxValue);
            sbyte hillController = (sbyte)round.hillControllerTeamIndex;
            byte hillStreak = (byte)Mathf.Clamp(round.hillStreak, 0, byte.MaxValue);
            bool hillContested = round.hillContested;

            serializer.SerializeValue(ref roundNumber);
            serializer.SerializeValue(ref hillController);
            serializer.SerializeValue(ref hillStreak);
            serializer.SerializeValue(ref hillContested);

            int entryCount = round.entries?.Count ?? 0;
            serializer.SerializeValue(ref entryCount);

            if (serializer.IsReader)
            {
                round.roundNumber = roundNumber;
                round.hillControllerTeamIndex = hillController;
                round.hillStreak = hillStreak;
                round.hillContested = hillContested;
                round.entries = new List<BattleReportEntry>(entryCount);
            }

            for (int j = 0; j < entryCount; j++)
            {
                BattleReportEntry entry = serializer.IsWriter ? round.entries[j] : default;

                byte teamIndex = (byte)entry.teamIndex;
                byte rosterSlot = (byte)entry.rosterSlot;
                byte order = (byte)entry.order;
                bool hasAbilityTarget = entry.hasAbilityTarget;
                bool aliveAtRoundEnd = entry.aliveAtRoundEnd;
                bool diedThisRound = entry.diedThisRound;
                ushort health = (ushort)Mathf.Clamp(entry.healthAtRoundEnd, 0, ushort.MaxValue);
                Vector2Int startCell = entry.startCell;
                Vector2Int endCell = entry.endCell;
                Vector2Int abilityTarget = entry.abilityTarget;

                serializer.SerializeValue(ref teamIndex);
                serializer.SerializeValue(ref rosterSlot);
                serializer.SerializeValue(ref order);
                serializer.SerializeValue(ref hasAbilityTarget);
                serializer.SerializeValue(ref aliveAtRoundEnd);
                serializer.SerializeValue(ref diedThisRound);
                serializer.SerializeValue(ref health);
                SerializeCell(serializer, ref startCell);
                SerializeCell(serializer, ref endCell);
                SerializeCell(serializer, ref abilityTarget);

                int pathCount = entry.path?.Count ?? 0;
                serializer.SerializeValue(ref pathCount);

                List<Vector2Int> path = serializer.IsWriter
                    ? entry.path
                    : new List<Vector2Int>(pathCount);

                for (int k = 0; k < pathCount; k++)
                {
                    Vector2Int cell = serializer.IsWriter ? path[k] : default;
                    SerializeCell(serializer, ref cell);
                    if (serializer.IsReader)
                        path.Add(cell);
                }

                if (serializer.IsReader)
                {
                    round.entries.Add(
                        new BattleReportEntry
                        {
                            teamIndex = teamIndex,
                            rosterSlot = rosterSlot,
                            order = (BattleReportOrder)order,
                            hasAbilityTarget = hasAbilityTarget,
                            aliveAtRoundEnd = aliveAtRoundEnd,
                            diedThisRound = diedThisRound,
                            healthAtRoundEnd = health,
                            startCell = startCell,
                            endCell = endCell,
                            abilityTarget = abilityTarget,
                            path = path,
                        }
                    );
                }
            }

            if (serializer.IsReader)
                rounds.Add(round);
        }
    }
}
