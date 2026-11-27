using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[Flags]
public enum ReplaySampleFlags : byte
{
    None = 0,
    Alive = 1 << 0,

    /// <summary>The viewer this copy was filtered for may be shown this sample.</summary>
    Known = 1 << 1,
    SeenByTeam0 = 1 << 2,
    SeenByTeam1 = 1 << 3,
}

public enum ReplayPlanKind : byte
{
    Held = 0,
    Move = 1,
    Dodge = 2,
    Ability = 3,
    Eliminated = 4,
}

public enum ReplayEventKind : byte
{
    Shot = 0,
    Ability = 1,
    WallDestroyed = 2,
}

/// <summary>One unit's identity in a round, in the order its samples are stored.</summary>
public struct ReplayUnit : INetworkSerializable
{
    public byte teamIndex;
    public byte rosterSlot;
    public sbyte catalogIndex;
    public ushort maxHealth;

    /// <summary>Height the body stands at, which samples leave out because it never changes.</summary>
    public short standHeight;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref teamIndex);
        serializer.SerializeValue(ref rosterSlot);
        serializer.SerializeValue(ref catalogIndex);
        serializer.SerializeValue(ref maxHealth);
        serializer.SerializeValue(ref standHeight);
    }
}

/// <summary>Where one unit was at one instant. Eight bytes, because frames dominate the payload.</summary>
public struct ReplaySample : INetworkSerializable
{
    public const float PositionScale = 100f;

    public short x;
    public short z;
    public byte yaw;
    public ushort health;
    public ReplaySampleFlags flags;

    public bool Alive => (flags & ReplaySampleFlags.Alive) != 0;
    public bool Known => (flags & ReplaySampleFlags.Known) != 0;

    public Vector3 Position => new(x / PositionScale, 0f, z / PositionScale);
    public float Yaw => yaw * (360f / 256f);

    public static ReplaySample Capture(Vector3 position, float yawDegrees, float health, bool alive)
    {
        return new ReplaySample
        {
            x = QuantizePosition(position.x),
            z = QuantizePosition(position.z),
            yaw = (byte)(Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) * (256f / 360f)) & 0xFF),
            health = (ushort)Mathf.Clamp(Mathf.CeilToInt(health), 0, ushort.MaxValue),
            flags = alive ? ReplaySampleFlags.Alive : ReplaySampleFlags.None,
        };
    }

    public static short QuantizePosition(float value) =>
        (short)Mathf.Clamp(Mathf.RoundToInt(value * PositionScale), short.MinValue, short.MaxValue);

    public static ReplaySampleFlags SeenBy(int teamIndex) =>
        teamIndex switch
        {
            0 => ReplaySampleFlags.SeenByTeam0,
            1 => ReplaySampleFlags.SeenByTeam1,
            _ => ReplaySampleFlags.None,
        };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref z);
        serializer.SerializeValue(ref yaw);
        serializer.SerializeValue(ref health);
        byte packed = (byte)flags;
        serializer.SerializeValue(ref packed);
        flags = (ReplaySampleFlags)packed;
    }
}

/// <summary>
/// What one unit committed to a round. An ability carries the shape it threatens, sampled on the
/// server the same way the dodge telegraph is, so the viewer never needs a live ability to draw it.
/// </summary>
public class ReplayPlan : INetworkSerializable
{
    public byte unitIndex;
    public ReplayPlanKind kind;
    public List<Vector2Int> route = new();

    public Vector3 abilityOrigin;
    public Vector3 abilitySquare;
    public float abilityRadiusCells;
    public bool abilityLine;
    public bool abilityCone;
    public bool abilitySmoke;
    public bool abilityShield;
    public Vector3 abilityPicked;
    public float abilityConeHalfSpread;
    public AbilityPathKind abilityPathKind;
    public List<Vector3> abilityPath = new();

    /// <summary>Whether a filtered copy may draw where the ability came from.</summary>
    public bool originKnown = true;

    public bool IsAbility => kind == ReplayPlanKind.Ability;

    public ReplayPlan Clone()
    {
        ReplayPlan copy = (ReplayPlan)MemberwiseClone();
        copy.route = new List<Vector2Int>(route);
        copy.abilityPath = new List<Vector3>(abilityPath);
        return copy;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref unitIndex);
        byte kindByte = (byte)kind;
        serializer.SerializeValue(ref kindByte);
        kind = (ReplayPlanKind)kindByte;

        ReplayRound.SerializeCells(serializer, ref route);
        if (kind != ReplayPlanKind.Ability)
            return;

        serializer.SerializeValue(ref originKnown);
        ReplayRound.SerializePoint(serializer, ref abilityOrigin);
        ReplayRound.SerializePoint(serializer, ref abilitySquare);
        ReplayRound.SerializeTenths(serializer, ref abilityRadiusCells);
        ReplayRound.SerializeTenths(serializer, ref abilityConeHalfSpread);
        serializer.SerializeValue(ref abilityLine);
        serializer.SerializeValue(ref abilityCone);
        serializer.SerializeValue(ref abilitySmoke);
        serializer.SerializeValue(ref abilityShield);
        ReplayRound.SerializePoint(serializer, ref abilityPicked);
        byte pathKind = (byte)abilityPathKind;
        serializer.SerializeValue(ref pathKind);
        abilityPathKind = (AbilityPathKind)pathKind;

        int count = abilityPath?.Count ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
            abilityPath = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            Vector3 point = serializer.IsWriter ? abilityPath[i] : default;
            ReplayRound.SerializePoint(serializer, ref point);
            if (serializer.IsReader)
                abilityPath.Add(point);
        }
    }
}

public struct ReplayEvent : INetworkSerializable
{
    public ushort timeCentiseconds;
    public ReplayEventKind kind;
    public byte unitIndex;
    public Vector3 from;
    public Vector3 to;
    public ReplaySampleFlags seenBy;

    public float Time => timeCentiseconds / 100f;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref timeCentiseconds);
        byte kindByte = (byte)kind;
        serializer.SerializeValue(ref kindByte);
        kind = (ReplayEventKind)kindByte;
        serializer.SerializeValue(ref unitIndex);
        ReplayRound.SerializePoint(serializer, ref from);
        ReplayRound.SerializePoint(serializer, ref to);
        byte seen = (byte)seenBy;
        serializer.SerializeValue(ref seen);
        seenBy = (ReplaySampleFlags)seen;
    }
}

/// <summary>
/// One round as it physically played out, recorded on the server for the replay viewer.
/// <para>
/// Gameplay is not deterministic — bullet spread is random and movement runs on frame time — so a
/// round cannot be re-simulated from its orders. It is played back from samples instead. Every
/// sample and event remembers which crews could see it when it happened, and
/// <see cref="FilterForTeam"/> cuts a copy down to one crew's view before it leaves the server:
/// mid-match a player gets back exactly what their fog already showed them, and the full round
/// only once the match is over.
/// </para>
/// </summary>
public class ReplayRound
{
    public const float FrameInterval = 0.1f;

    /// <summary>Keeps every network message well under the transport's 6 KB payload limit.</summary>
    public const int SamplesPerChunk = 320;
    public const int EventsPerChunk = 160;
    public const int MaxEvents = 480;

    public int roundNumber;
    public List<ReplayUnit> units = new();
    public List<ReplayPlan> plans = new();
    public List<Vector2Int> smokeCells = new();

    /// <summary>Frame 0 is the board as the round opened; each later frame is <see cref="FrameInterval"/> on.</summary>
    public List<ReplaySample[]> frames = new();
    public List<ReplayEvent> events = new();

    public float Duration => Mathf.Max(0, frames.Count - 1) * FrameInterval;

    public int IndexOf(int teamIndex, int rosterSlot)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].teamIndex == teamIndex && units[i].rosterSlot == rosterSlot)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Blends the two frames either side of <paramref name="time"/>. A sample the viewer may not
    /// see is never blended toward, so a unit stepping out of fog appears rather than sliding in
    /// from where it was last hidden.
    /// </summary>
    public bool TrySample(int unitIndex, float time, out Vector3 position, out float yaw, out ReplaySample nearest)
    {
        position = default;
        yaw = 0f;
        nearest = default;
        if (frames.Count == 0 || unitIndex < 0 || unitIndex >= units.Count)
            return false;

        float scaled = Mathf.Clamp(time, 0f, Duration) / FrameInterval;
        int lower = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, frames.Count - 1);
        int upper = Mathf.Min(lower + 1, frames.Count - 1);
        float blend = scaled - lower;

        ReplaySample a = frames[lower][unitIndex];
        ReplaySample b = frames[upper][unitIndex];
        nearest = blend < 0.5f ? a : b;
        if (!nearest.Known)
            return false;

        if (!a.Known || !b.Known || a.Alive != b.Alive)
        {
            position = nearest.Position;
            yaw = nearest.Yaw;
            return true;
        }

        position = Vector3.Lerp(a.Position, b.Position, blend);
        yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, blend);
        return true;
    }

    /// <summary>
    /// The copy one crew is allowed to keep. <paramref name="viewerTeamIndex"/> below zero keeps
    /// everything, which is what the post-match reveal sends.
    /// </summary>
    public ReplayRound FilterForTeam(int viewerTeamIndex)
    {
        bool revealAll = viewerTeamIndex < 0;
        ReplaySampleFlags seenBit = ReplaySample.SeenBy(viewerTeamIndex);
        ReplayRound copy = new()
        {
            roundNumber = roundNumber,
            units = new List<ReplayUnit>(units),
            smokeCells = new List<Vector2Int>(smokeCells),
        };

        List<HashSet<Vector2Int>> seenCells = new(units.Count);
        for (int i = 0; i < units.Count; i++)
            seenCells.Add(new HashSet<Vector2Int>());

        foreach (ReplaySample[] frame in frames)
        {
            ReplaySample[] filtered = new ReplaySample[frame.Length];
            for (int i = 0; i < frame.Length; i++)
            {
                ReplaySample sample = frame[i];
                bool known =
                    revealAll
                    || units[i].teamIndex == viewerTeamIndex
                    || (sample.flags & seenBit) != 0;
                sample.flags &= ReplaySampleFlags.Alive;
                if (known)
                {
                    sample.flags |= ReplaySampleFlags.Known;
                    seenCells[i].Add(GridSystem.ConvertToGridCoords(sample.Position));
                }
                else
                {
                    sample.x = 0;
                    sample.z = 0;
                    sample.yaw = 0;
                }
                filtered[i] = sample;
            }
            copy.frames.Add(filtered);
        }

        foreach (ReplayPlan plan in plans)
        {
            bool own = revealAll || units[plan.unitIndex].teamIndex == viewerTeamIndex;
            if (own)
            {
                copy.plans.Add(plan.Clone());
                continue;
            }

            ReplayPlan seen = plan.Clone();
            if (plan.IsAbility)
            {
                seen.originKnown = frames.Count > 0 && (frames[0][plan.unitIndex].flags & seenBit) != 0;
                if (!seen.originKnown)
                    seen.abilityPath.Clear();
            }
            else
            {
                seen.route.RemoveAll(cell => !seenCells[plan.unitIndex].Contains(cell));
                if (seen.route.Count < 2)
                    continue;
            }
            copy.plans.Add(seen);
        }

        foreach (ReplayEvent replayEvent in events)
        {
            if (
                revealAll
                || replayEvent.kind == ReplayEventKind.WallDestroyed
                || units[replayEvent.unitIndex].teamIndex == viewerTeamIndex
                || (replayEvent.seenBy & seenBit) != 0
            )
            {
                copy.events.Add(replayEvent);
            }
        }

        return copy;
    }

    public ReplayRoundHeader BuildHeader() =>
        new()
        {
            roundNumber = roundNumber,
            frameCount = frames.Count,
            eventCount = events.Count,
            units = units,
            plans = plans,
            smokeCells = smokeCells,
        };

    public ReplayFrameChunk BuildFrameChunk(int firstFrame)
    {
        int unitCount = units.Count;
        int framesPerChunk = Mathf.Max(1, SamplesPerChunk / Mathf.Max(1, unitCount));
        int count = Mathf.Min(framesPerChunk, frames.Count - firstFrame);
        ReplaySample[] samples = new ReplaySample[count * unitCount];
        for (int f = 0; f < count; f++)
            Array.Copy(frames[firstFrame + f], 0, samples, f * unitCount, unitCount);

        return new ReplayFrameChunk
        {
            roundNumber = roundNumber,
            firstFrame = firstFrame,
            unitCount = unitCount,
            samples = samples,
        };
    }

    public ReplayEventChunk BuildEventChunk(int firstEvent)
    {
        int count = Mathf.Min(EventsPerChunk, events.Count - firstEvent);
        return new ReplayEventChunk
        {
            roundNumber = roundNumber,
            firstEvent = firstEvent,
            events = events.GetRange(firstEvent, count).ToArray(),
        };
    }

    public int FramesPerChunk => Mathf.Max(1, SamplesPerChunk / Mathf.Max(1, units.Count));

    public static void SerializePoint<T>(BufferSerializer<T> serializer, ref Vector3 point)
        where T : IReaderWriter
    {
        short x = ReplaySample.QuantizePosition(point.x);
        short y = ReplaySample.QuantizePosition(point.y);
        short z = ReplaySample.QuantizePosition(point.z);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref z);
        if (serializer.IsReader)
        {
            point = new Vector3(
                x / ReplaySample.PositionScale,
                y / ReplaySample.PositionScale,
                z / ReplaySample.PositionScale
            );
        }
    }

    public static void SerializeTenths<T>(BufferSerializer<T> serializer, ref float value)
        where T : IReaderWriter
    {
        ushort tenths = (ushort)Mathf.Clamp(Mathf.RoundToInt(value * 10f), 0, ushort.MaxValue);
        serializer.SerializeValue(ref tenths);
        if (serializer.IsReader)
            value = tenths / 10f;
    }

    public static void SerializeCells<T>(BufferSerializer<T> serializer, ref List<Vector2Int> cells)
        where T : IReaderWriter
    {
        int count = cells?.Count ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
            cells = new List<Vector2Int>(count);
        for (int i = 0; i < count; i++)
        {
            byte x = serializer.IsWriter ? (byte)Mathf.Clamp(cells[i].x, 0, byte.MaxValue) : default;
            byte y = serializer.IsWriter ? (byte)Mathf.Clamp(cells[i].y, 0, byte.MaxValue) : default;
            serializer.SerializeValue(ref x);
            serializer.SerializeValue(ref y);
            if (serializer.IsReader)
                cells.Add(new Vector2Int(x, y));
        }
    }
}

public struct ReplayRoundHeader : INetworkSerializable
{
    public int roundNumber;
    public int frameCount;
    public int eventCount;
    public List<ReplayUnit> units;
    public List<ReplayPlan> plans;
    public List<Vector2Int> smokeCells;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref roundNumber);
        serializer.SerializeValue(ref frameCount);
        serializer.SerializeValue(ref eventCount);
        ReplayRound.SerializeCells(serializer, ref smokeCells);

        int unitCount = units?.Count ?? 0;
        serializer.SerializeValue(ref unitCount);
        if (serializer.IsReader)
            units = new List<ReplayUnit>(unitCount);
        for (int i = 0; i < unitCount; i++)
        {
            ReplayUnit unit = serializer.IsWriter ? units[i] : default;
            unit.NetworkSerialize(serializer);
            if (serializer.IsReader)
                units.Add(unit);
        }

        int planCount = plans?.Count ?? 0;
        serializer.SerializeValue(ref planCount);
        if (serializer.IsReader)
            plans = new List<ReplayPlan>(planCount);
        for (int i = 0; i < planCount; i++)
        {
            ReplayPlan plan = serializer.IsWriter ? plans[i] : new ReplayPlan();
            plan.NetworkSerialize(serializer);
            if (serializer.IsReader)
                plans.Add(plan);
        }
    }
}

public struct ReplayFrameChunk : INetworkSerializable
{
    public int roundNumber;
    public int firstFrame;
    public int unitCount;
    public ReplaySample[] samples;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref roundNumber);
        serializer.SerializeValue(ref firstFrame);
        serializer.SerializeValue(ref unitCount);
        int count = samples?.Length ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
            samples = new ReplaySample[count];
        for (int i = 0; i < count; i++)
            samples[i].NetworkSerialize(serializer);
    }
}

public struct ReplayEventChunk : INetworkSerializable
{
    public int roundNumber;
    public int firstEvent;
    public ReplayEvent[] events;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref roundNumber);
        serializer.SerializeValue(ref firstEvent);
        int count = events?.Length ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
            events = new ReplayEvent[count];
        for (int i = 0; i < count; i++)
            events[i].NetworkSerialize(serializer);
    }
}

/// <summary>Reassembles rounds from the header and chunks they arrive in.</summary>
public class ReplayArchive
{
    private readonly SortedDictionary<int, ReplayRound> rounds = new();
    private readonly Dictionary<int, (int frames, int events)> expected = new();

    public event Action Changed;

    public IReadOnlyCollection<int> RoundNumbers => rounds.Keys;

    public void Clear()
    {
        rounds.Clear();
        expected.Clear();
        Changed?.Invoke();
    }

    public void TruncateFrom(int roundNumber)
    {
        List<int> stale = new();
        foreach (int number in rounds.Keys)
        {
            if (number >= roundNumber)
                stale.Add(number);
        }
        foreach (int number in stale)
        {
            rounds.Remove(number);
            expected.Remove(number);
        }
        if (stale.Count > 0)
            Changed?.Invoke();
    }

    public void Receive(ReplayRoundHeader header)
    {
        rounds.Remove(header.roundNumber);
        expected[header.roundNumber] = (header.frameCount, header.eventCount);
        pending[header.roundNumber] = new ReplayRound
        {
            roundNumber = header.roundNumber,
            units = header.units ?? new List<ReplayUnit>(),
            plans = header.plans ?? new List<ReplayPlan>(),
            smokeCells = header.smokeCells ?? new List<Vector2Int>(),
            frames = new List<ReplaySample[]>(header.frameCount),
            events = new List<ReplayEvent>(header.eventCount),
        };
        TryComplete(header.roundNumber);
    }

    public void Receive(ReplayFrameChunk chunk)
    {
        if (!pending.TryGetValue(chunk.roundNumber, out ReplayRound round) || chunk.unitCount <= 0)
            return;

        int frameCount = chunk.samples.Length / chunk.unitCount;
        for (int f = 0; f < frameCount; f++)
        {
            ReplaySample[] frame = new ReplaySample[chunk.unitCount];
            Array.Copy(chunk.samples, f * chunk.unitCount, frame, 0, chunk.unitCount);
            round.frames.Add(frame);
        }
        TryComplete(chunk.roundNumber);
    }

    public void Receive(ReplayEventChunk chunk)
    {
        if (!pending.TryGetValue(chunk.roundNumber, out ReplayRound round))
            return;

        round.events.AddRange(chunk.events);
        TryComplete(chunk.roundNumber);
    }

    private readonly Dictionary<int, ReplayRound> pending = new();

    private void TryComplete(int roundNumber)
    {
        if (
            !pending.TryGetValue(roundNumber, out ReplayRound round)
            || !expected.TryGetValue(roundNumber, out var counts)
            || round.frames.Count < counts.frames
            || round.events.Count < counts.events
        )
        {
            return;
        }

        pending.Remove(roundNumber);
        rounds[roundNumber] = round;
        Changed?.Invoke();
    }

    public bool TryGet(int roundNumber, out ReplayRound round) =>
        rounds.TryGetValue(roundNumber, out round);

    public int Count => rounds.Count;

    public int LatestRoundNumber
    {
        get
        {
            int latest = -1;
            foreach (int number in rounds.Keys)
                latest = number;
            return latest;
        }
    }
}
