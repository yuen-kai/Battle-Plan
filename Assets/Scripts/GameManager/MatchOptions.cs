using System;
using Unity.Netcode;

public enum GameMode : byte
{
    Elimination = 0,
    KingOfTheHill = 1,
}

public enum OpponentType : byte
{
    Player = 0,
    AI = 1,
}

[Serializable]
public struct MatchOptions : INetworkSerializable, IEquatable<MatchOptions>
{
    public GameMode gameMode;
    public OpponentType opponentType;
    public bool fogOfWar;
    public MapId mapId;

    private static MatchOptions current = Default;

    public static MatchOptions Default =>
        new()
        {
            gameMode = GameMode.Elimination,
            opponentType = OpponentType.Player,
            fogOfWar = true,
            mapId = MapId.Concourse,
        };

    public static MatchOptions Current => current;
    public bool IsBotMatch => opponentType == OpponentType.AI;
    public bool IsKingOfTheHill => gameMode == GameMode.KingOfTheHill;
    public string GameModeDisplayName =>
        IsKingOfTheHill ? "King of the Hill" : "Elimination";
    public MapDefinition Map => MapCatalog.ById(mapId);

    // The single point where the live board is chosen. Both peers reach this through the
    // replicated options, so the walls a client validates paths against are always the server's.
    public static void SetCurrent(MatchOptions options)
    {
        current = options.Sanitized();
        MapCatalog.SetActive(current.mapId);
    }

    public static void Reset()
    {
        current = Default;
        MapCatalog.SetActive(current.mapId);
    }

    public MatchOptions Sanitized()
    {
        MatchOptions sanitized = this;
        if (
            sanitized.gameMode != GameMode.Elimination
            && sanitized.gameMode != GameMode.KingOfTheHill
        )
        {
            sanitized.gameMode = GameMode.Elimination;
        }

        if (
            sanitized.opponentType != OpponentType.Player
            && sanitized.opponentType != OpponentType.AI
        )
        {
            sanitized.opponentType = OpponentType.Player;
        }

        if (!MapCatalog.IsKnown(sanitized.mapId))
            sanitized.mapId = MapCatalog.Fallback.Id;

        return sanitized;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        byte serializedGameMode = (byte)gameMode;
        byte serializedOpponentType = (byte)opponentType;
        byte serializedMapId = (byte)mapId;

        serializer.SerializeValue(ref serializedGameMode);
        serializer.SerializeValue(ref serializedOpponentType);
        serializer.SerializeValue(ref fogOfWar);
        serializer.SerializeValue(ref serializedMapId);

        if (!serializer.IsReader)
            return;

        gameMode = (GameMode)serializedGameMode;
        opponentType = (OpponentType)serializedOpponentType;
        mapId = (MapId)serializedMapId;
        this = Sanitized();
    }

    public bool Equals(MatchOptions other)
    {
        return gameMode == other.gameMode
            && opponentType == other.opponentType
            && fogOfWar == other.fogOfWar
            && mapId == other.mapId;
    }

    public override bool Equals(object obj)
    {
        return obj is MatchOptions other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(gameMode, opponentType, fogOfWar, mapId);
    }

    public static bool operator ==(MatchOptions left, MatchOptions right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MatchOptions left, MatchOptions right)
    {
        return !left.Equals(right);
    }
}
