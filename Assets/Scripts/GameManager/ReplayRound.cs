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

/// <summary>The status a unit's body was showing, read off the same state its live visuals draw.</summary>
[Flags]
public enum ReplayStatus : byte
{
    None = 0,
    Stunned = 1 << 0,
    DiveRecovering = 1 << 1,
    SpeedBoost = 1 << 2,
    Guarded = 1 << 3,
    ShieldUp = 1 << 4,
    Locking = 1 << 5,
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
    Fx = 3,
    Death = 4,
    Damage = 5,
    Animation = 6,
}

/// <summary>
/// The one-shot effects an ability fires on every screen. Each names the static player its live
/// RPC also calls, so a replayed effect is the same effect rather than a lookalike.
/// </summary>
public enum ReplayFxCue : byte
{
    None = 0,
    GrenadeExplosion = 1,
    RocketExplosion = 2,
    LaserBeam = 3,
    LaserImpact = 4,
    ArcCharge = 5,
    ArcZap = 6,
    PogoLanding = 7,
    LeapLaunch = 8,
    LeapImpact = 9,
    BarrageMuzzle = 10,
    BulletBlast = 11,
    ShieldBlock = 12,
    CameraShake = 13,
    ArcStrike = 14,
    ArcCancel = 15,
    LaserRush = 16,
}

public static class ReplayDetail
{
    public const byte VolleyShot = 1 << 0;
    public const byte PiercingShot = 1 << 1;
    public const byte CritHit = 1 << 0;
    public const byte LethalHit = 1 << 1;
    public const byte OneShotAnimation = 1 << 0;

    /// <summary>The event belongs to no unit, which is how an effect with no caster is kept.</summary>
    public const byte NoUnit = byte.MaxValue;
}

/// <summary>One unit's identity in a round, in the order its samples are stored.</summary>
public struct ReplayUnit : INetworkSerializable
{
    public byte teamIndex;
    public byte rosterSlot;
    public sbyte catalogIndex;
    public ushort maxHealth;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref teamIndex);
        serializer.SerializeValue(ref rosterSlot);
        serializer.SerializeValue(ref catalogIndex);
        serializer.SerializeValue(ref maxHealth);
    }
}

/// <summary>Where one unit was and what it was showing at one instant. Fourteen bytes, because frames dominate the payload.</summary>
public struct ReplaySample : INetworkSerializable
{
    public const float PositionScale = 100f;

    public short x;
    public short y;
    public short z;
    public byte yaw;
    public ushort health;
    public ReplaySampleFlags flags;
    public ReplayStatus status;
    public byte cooldown;

    /// <summary>0 for none, then up, right, down, left.</summary>
    public byte shieldDirection;

    /// <summary>The locked target's unit index plus one, or 0 for no lock.</summary>
    public byte lockTarget;

    public bool Alive => (flags & ReplaySampleFlags.Alive) != 0;
    public bool Known => (flags & ReplaySampleFlags.Known) != 0;

    public Vector3 Position => new(x / PositionScale, y / PositionScale, z / PositionScale);
    public float Yaw => yaw * (360f / 256f);

    public bool Has(ReplayStatus flag) => (status & flag) != 0;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    public static byte PackDirection(Vector2Int direction)
    {
        int index = Array.IndexOf(Directions, direction);
        return (byte)(index < 0 ? 0 : index + 1);
    }

    public Vector2Int ShieldFacing =>
        shieldDirection >= 1 && shieldDirection <= Directions.Length
            ? Directions[shieldDirection - 1]
            : Vector2Int.zero;

    public static ReplaySample Capture(Vector3 position, float yawDegrees, float health, bool alive)
    {
        return new ReplaySample
        {
            x = QuantizePosition(position.x),
            y = QuantizePosition(position.y),
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
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref z);
        serializer.SerializeValue(ref yaw);
        serializer.SerializeValue(ref health);
        byte packed = (byte)flags;
        serializer.SerializeValue(ref packed);
        flags = (ReplaySampleFlags)packed;
        byte statusBits = (byte)status;
        serializer.SerializeValue(ref statusBits);
        status = (ReplayStatus)statusBits;
        serializer.SerializeValue(ref cooldown);
        serializer.SerializeValue(ref shieldDirection);
        serializer.SerializeValue(ref lockTarget);
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
    public ReplayFxCue cue;

    /// <summary>Shot speed, effect radius, beam duration, damage or animation index, by kind.</summary>
    public float value;

    /// <summary>Kind-specific flags; see <see cref="ReplayDetail"/>.</summary>
    public byte detail;

    public bool HasUnit => unitIndex != ReplayDetail.NoUnit;

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
        byte cueByte = (byte)cue;
        serializer.SerializeValue(ref cueByte);
        cue = (ReplayFxCue)cueByte;
        serializer.SerializeValue(ref value);
        serializer.SerializeValue(ref detail);
    }
}

/// <summary>The smoke on the board from <see cref="Time"/> on, as a whole set rather than a delta.</summary>
public class ReplaySmokeChange
{
    public ushort timeCentiseconds;
    public List<Vector2Int> cells = new();

    public float Time => timeCentiseconds / 100f;
}

public struct ReplayTrackPoint
{
    public ushort timeCentiseconds;
    public Vector3 position;
    public Vector3 euler;
    public float scale;

    public float Time => timeCentiseconds / 100f;
}

/// <summary>
/// Something the server spawned on the network mid-round — a grenade in flight, a rocket, a smoke
/// canister, an explosion — followed from spawn to despawn. Identified by its network prefab, which
/// every peer already holds, so the viewer stands up a copy of the very object that flew.
/// </summary>
public class ReplayTrack : INetworkSerializable
{
    public uint prefabHash;
    public ushort endCentiseconds;
    public List<ReplayTrackPoint> points = new();

    public float StartTime => points.Count > 0 ? points[0].Time : 0f;
    public float EndTime => endCentiseconds / 100f;

    public bool TrySample(float time, out Vector3 position, out Quaternion rotation, out float scale)
    {
        position = default;
        rotation = Quaternion.identity;
        scale = 1f;
        if (points.Count == 0 || time < StartTime || time > EndTime)
            return false;

        int upper = points.FindIndex(point => point.Time > time);
        if (upper < 0)
            upper = points.Count - 1;
        int lower = Mathf.Max(0, upper - 1);
        ReplayTrackPoint a = points[lower];
        ReplayTrackPoint b = points[upper];
        float span = b.Time - a.Time;
        float blend = span > 1e-4f ? Mathf.Clamp01((time - a.Time) / span) : 1f;
        if (time >= b.Time)
            blend = 1f;

        position = Vector3.Lerp(a.position, b.position, blend);
        rotation = Quaternion.Slerp(Quaternion.Euler(a.euler), Quaternion.Euler(b.euler), blend);
        scale = Mathf.Lerp(a.scale, b.scale, blend);
        return true;
    }

    public ReplayTrack Clone() =>
        new()
        {
            prefabHash = prefabHash,
            endCentiseconds = endCentiseconds,
            points = new List<ReplayTrackPoint>(points),
        };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref prefabHash);
        serializer.SerializeValue(ref endCentiseconds);
        int count = points?.Count ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
            points = new List<ReplayTrackPoint>(count);
        for (int i = 0; i < count; i++)
        {
            ReplayTrackPoint point = serializer.IsWriter ? points[i] : default;
            serializer.SerializeValue(ref point.timeCentiseconds);
            ReplayRound.SerializePoint(serializer, ref point.position);
            byte pitch = (byte)(Mathf.RoundToInt(Mathf.Repeat(point.euler.x, 360f) * (256f / 360f)) & 0xFF);
            byte yaw = (byte)(Mathf.RoundToInt(Mathf.Repeat(point.euler.y, 360f) * (256f / 360f)) & 0xFF);
            byte roll = (byte)(Mathf.RoundToInt(Mathf.Repeat(point.euler.z, 360f) * (256f / 360f)) & 0xFF);
            serializer.SerializeValue(ref pitch);
            serializer.SerializeValue(ref yaw);
            serializer.SerializeValue(ref roll);
            ReplayRound.SerializeTenths(serializer, ref point.scale);
            if (serializer.IsReader)
            {
                point.euler = new Vector3(pitch, yaw, roll) * (360f / 256f);
                points.Add(point);
            }
        }
    }
}

public struct ReplayTrackMessage : INetworkSerializable
{
    public int roundNumber;
    public ReplayTrack track;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref roundNumber);
        track ??= new ReplayTrack();
        track.NetworkSerialize(serializer);
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
    public const int SamplesPerChunk = 300;
    public const int EventsPerChunk = 160;
    public const int MaxEvents = 960;

    public int roundNumber;
    public List<ReplayUnit> units = new();
    public List<ReplayPlan> plans = new();
    public List<Vector2Int> smokeCells = new();
    public List<ReplaySmokeChange> smokeChanges = new();
    public List<ReplayTrack> tracks = new();
    public List<string> animationNames = new();
    public bool hasHill;
    public HillControlState hill;

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
            smokeChanges = new List<ReplaySmokeChange>(smokeChanges),
            animationNames = new List<string>(animationNames),
            hasHill = hasHill,
            hill = hill,
        };
        foreach (ReplayTrack track in tracks)
            copy.tracks.Add(track.Clone());

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
                    sample.y = 0;
                    sample.z = 0;
                    sample.yaw = 0;
                    sample.status = ReplayStatus.None;
                    sample.shieldDirection = 0;
                    sample.lockTarget = 0;
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
                || (replayEvent.HasUnit && units[replayEvent.unitIndex].teamIndex == viewerTeamIndex)
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
            smokeChanges = smokeChanges,
            trackCount = tracks.Count,
            animationNames = animationNames,
            hasHill = hasHill,
            hill = hill,
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
    public List<ReplaySmokeChange> smokeChanges;
    public int trackCount;
    public List<string> animationNames;
    public bool hasHill;
    public HillControlState hill;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref roundNumber);
        serializer.SerializeValue(ref frameCount);
        serializer.SerializeValue(ref eventCount);
        serializer.SerializeValue(ref trackCount);
        ReplayRound.SerializeCells(serializer, ref smokeCells);

        serializer.SerializeValue(ref hasHill);
        if (hasHill)
            hill.NetworkSerialize(serializer);

        int nameCount = animationNames?.Count ?? 0;
        serializer.SerializeValue(ref nameCount);
        if (serializer.IsReader)
            animationNames = new List<string>(nameCount);
        for (int i = 0; i < nameCount; i++)
        {
            string animationName = serializer.IsWriter ? animationNames[i] : string.Empty;
            serializer.SerializeValue(ref animationName);
            if (serializer.IsReader)
                animationNames.Add(animationName);
        }

        int changeCount = smokeChanges?.Count ?? 0;
        serializer.SerializeValue(ref changeCount);
        if (serializer.IsReader)
            smokeChanges = new List<ReplaySmokeChange>(changeCount);
        for (int i = 0; i < changeCount; i++)
        {
            ReplaySmokeChange change = serializer.IsWriter ? smokeChanges[i] : new ReplaySmokeChange();
            serializer.SerializeValue(ref change.timeCentiseconds);
            ReplayRound.SerializeCells(serializer, ref change.cells);
            if (serializer.IsReader)
                smokeChanges.Add(change);
        }

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
    private readonly Dictionary<int, (int frames, int events, int tracks)> expected = new();

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

    /// <summary>
    /// Starts assembling a round. A copy already held stays readable until its replacement is
    /// whole, so the post-match reveal never pulls a round out from under the viewer.
    /// </summary>
    public void Receive(ReplayRoundHeader header)
    {
        expected[header.roundNumber] = (header.frameCount, header.eventCount, header.trackCount);
        pending[header.roundNumber] = new ReplayRound
        {
            roundNumber = header.roundNumber,
            units = header.units ?? new List<ReplayUnit>(),
            plans = header.plans ?? new List<ReplayPlan>(),
            smokeCells = header.smokeCells ?? new List<Vector2Int>(),
            smokeChanges = header.smokeChanges ?? new List<ReplaySmokeChange>(),
            animationNames = header.animationNames ?? new List<string>(),
            hasHill = header.hasHill,
            hill = header.hill,
            tracks = new List<ReplayTrack>(header.trackCount),
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

    public void Receive(ReplayTrackMessage message)
    {
        if (!pending.TryGetValue(message.roundNumber, out ReplayRound round) || message.track == null)
            return;

        round.tracks.Add(message.track);
        TryComplete(message.roundNumber);
    }

    private readonly Dictionary<int, ReplayRound> pending = new();

    private void TryComplete(int roundNumber)
    {
        if (
            !pending.TryGetValue(roundNumber, out ReplayRound round)
            || !expected.TryGetValue(roundNumber, out var counts)
            || round.frames.Count < counts.frames
            || round.events.Count < counts.events
            || round.tracks.Count < counts.tracks
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
