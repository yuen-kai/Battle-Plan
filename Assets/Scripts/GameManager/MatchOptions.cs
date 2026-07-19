using System;
using Unity.Netcode;

public enum GameMode : byte
{
    Elimination = 0,
    KingOfTheHill = 1,
    CaptureTheFlag = 2,
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

    private static MatchOptions current = Default;

    public static MatchOptions Default =>
        new()
        {
            gameMode = GameMode.Elimination,
            opponentType = OpponentType.Player,
            fogOfWar = true,
        };

    public static MatchOptions Current => current;
    public bool IsBotMatch => opponentType == OpponentType.AI;

    public static void SetCurrent(MatchOptions options)
    {
        current = options.Sanitized();
    }

    public static void Reset()
    {
        current = Default;
    }

    public MatchOptions Sanitized()
    {
        MatchOptions sanitized = this;
        if (sanitized.gameMode != GameMode.Elimination)
            sanitized.gameMode = GameMode.Elimination;

        if (
            sanitized.opponentType != OpponentType.Player
            && sanitized.opponentType != OpponentType.AI
        )
        {
            sanitized.opponentType = OpponentType.Player;
        }

        return sanitized;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        byte serializedGameMode = (byte)gameMode;
        byte serializedOpponentType = (byte)opponentType;

        serializer.SerializeValue(ref serializedGameMode);
        serializer.SerializeValue(ref serializedOpponentType);
        serializer.SerializeValue(ref fogOfWar);

        if (!serializer.IsReader)
            return;

        gameMode = (GameMode)serializedGameMode;
        opponentType = (OpponentType)serializedOpponentType;
        this = Sanitized();
    }

    public bool Equals(MatchOptions other)
    {
        return gameMode == other.gameMode
            && opponentType == other.opponentType
            && fogOfWar == other.fogOfWar;
    }

    public override bool Equals(object obj)
    {
        return obj is MatchOptions other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(gameMode, opponentType, fogOfWar);
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
