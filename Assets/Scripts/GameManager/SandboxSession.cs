using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lifetime and board setup for the character sandbox: a one-unit-per-side match between the
/// character being tested and a stationary target dummy, launched from the Editor's Character
/// Sandbox window. Structured as a session rather than a script that pokes a running match, for the
/// same reason <see cref="TutorialSession"/> is: the number of units fielded, where they stand, and
/// how long planning lasts are all decided *before* the board is built, so nothing has to be undone
/// afterwards. The sandbox player is always the loopback host, so the match reads this state
/// statically rather than replicating it.
///
/// Consumed by exactly the seams the tutorial already established:
/// <c>GameLoop.UnitsPerTeamThisMatch</c>, <c>GameLoop.CreateSpawnLayout</c>, the planning/dodge
/// window lengths, and <c>NetworkHandler.TryAdvance</c>'s skip past character selection.
/// </summary>
public static class SandboxSession
{
    /// <summary>
    /// One unit a side — the character under test and its dummy. Rosters stay the configured crew
    /// length so roster validation and <c>GameLoop.ConfigureTeam</c> are untouched; only the number
    /// of units spawned changes.
    /// </summary>
    public const int UnitsPerTeam = 1;

    /// <summary>
    /// How many dummies the enemy side fields. One is the default — the plain "does this kit hurt a
    /// target" case — but an ability that reaches several enemies at once cannot be judged against a
    /// single one: Voltaic's Chain Surge caps at five targets, and with one dummy on the board four
    /// fifths of the ability is invisible. Set per launch from the Character Sandbox window.
    /// </summary>
    public static int EnemyCount { get; private set; } = 1;

    /// <summary>The most dummies the enemy side can field, bounded by the roster it is built from.</summary>
    public static int MaxEnemyCount => EnemySpawns.Length;

    /// <summary>
    /// Where the dummies stand, in the order they are fielded. All within three cells of
    /// <see cref="PlayerSpawn"/> so a radius ability centred on the character under test actually
    /// covers them — a cluster placed further out would make a capped multi-target ability look
    /// broken rather than tested. Verified against Concourse (clear of walls, in bounds, distinct)
    /// by CharacterSandboxEditModeTests.
    /// </summary>
    public static readonly Vector2Int[] EnemySpawns =
    {
        new(7, 6),
        new(7, 5),
        new(6, 5),
        new(8, 5),
        new(6, 4),
    };

    /// <summary>
    /// How many units the player's own side fields, including the character under test. One is the
    /// default. More exists for the same reason <see cref="EnemyCount"/> does, pointed the other way:
    /// an ability that acts on ALLIES cannot be judged with nobody to act on — Outrider's Run And Gun
    /// lends its trait to nearby teammates, and with a lone unit on the board the entire ability is a
    /// no-op.
    /// </summary>
    public static int AllyCount { get; private set; } = 1;

    /// <summary>The most units the player's side can field.</summary>
    public static int MaxAllyCount => AllySpawns.Length + 1;

    /// <summary>
    /// Where the extra teammates stand, after <see cref="PlayerSpawn"/>. All within three cells of
    /// the character under test so an ally-radius ability actually covers them, clear of walls, and
    /// distinct from every <see cref="EnemySpawns"/> cell. Verified by
    /// CharacterSandboxEditModeTests.
    /// </summary>
    public static readonly Vector2Int[] AllySpawns =
    {
        new(6, 3),
        new(8, 3),
        new(7, 4),
        new(8, 4),
    };

    /// <summary>
    /// Long enough that planning never expires while the designer is reading the board or lining up
    /// an ability by hand. Matches the tutorial's reasoning, and the same value keeps the dodge
    /// window open too.
    /// </summary>
    public const float PlanningSeconds = 600f;

    /// <summary>
    /// Column 7 carries a wall at rows 2 and 7 on Concourse, so each side spawns directly against
    /// real cover — the "wall nearby" the sandbox is meant to provide, without a bespoke board.
    /// Three cells apart on a clear column: inside every current unit's weapon reach, with line of
    /// sight, so a kit's damage output is visible from round one.
    /// </summary>
    public static readonly Vector2Int PlayerSpawn = new(7, 3);
    /// <summary>The first dummy's cell. Derived from <see cref="EnemySpawns"/> so the single-dummy
    /// case and the crowd case can never disagree about where the front one stands.</summary>
    public static Vector2Int EnemySpawn => EnemySpawns[0];

    /// <summary>Wall cells flanking the two spawns, surfaced by the sandbox window so the designer
    /// knows where the cover is without counting rows.</summary>
    public static readonly Vector2Int PlayerAdjacentWall = new(7, 2);
    public static readonly Vector2Int EnemyAdjacentWall = new(7, 7);

    public static bool IsActive { get; private set; }

    /// <summary>Catalog index (AllUnits.asset order) of the character being tested.</summary>
    public static int TestUnitIndex { get; private set; }

    /// <summary>Catalog index of the target dummy fielded against it.</summary>
    public static int DummyUnitIndex { get; private set; }

    /// <summary>
    /// Whether the dummy is allowed to shoot back. Off by default so the designer can watch a kit
    /// work without being killed by return fire, and live-togglable mid-match from the sandbox
    /// window — <see cref="SandboxDirector"/> re-applies it every frame, so flipping it takes effect
    /// on the next shooting cycle either way. The dummy never moves or uses an ability regardless
    /// (see <see cref="BotFrozenUnits"/>).
    /// </summary>
    public static bool DummyFightsBack;

    /// <summary>
    /// Set once the join screen has kicked off this session's loopback host, so a scene re-enable
    /// cannot start a second one. Mirrors <see cref="TutorialSession.HostStartRequested"/>.
    /// </summary>
    public static bool HostStartRequested { get; set; }

    public static void Begin(
        int testUnitIndex,
        int dummyUnitIndex,
        int enemyCount = 1,
        int allyCount = 1
    )
    {
        IsActive = true;
        HostStartRequested = false;
        TestUnitIndex = Mathf.Max(0, testUnitIndex);
        DummyUnitIndex = Mathf.Max(0, dummyUnitIndex);
        EnemyCount = Mathf.Clamp(enemyCount, 1, MaxEnemyCount);
        AllyCount = Mathf.Clamp(allyCount, 1, MaxAllyCount);

        // The dummy is frozen at the team level rather than per unit, because a per-GameObject
        // freeze cannot be registered until the unit exists — a frame after the bot plans the
        // opening round. See BotFrozenUnits.
        BotFrozenUnits.SetTeamFrozen(GameLoop.OpponentTeamIndex, true);
    }

    public static void End()
    {
        IsActive = false;
        HostStartRequested = false;
        EnemyCount = 1;
        AllyCount = 1;
        BotFrozenUnits.ClearAll();
    }

    /// <summary>
    /// How many units the given team fields. The two sides are sized independently here, unlike a
    /// normal match, so a kit can be put in front of however many bodies it needs to be judged
    /// against — several enemies for a multi-target ability, or several teammates for one that buffs
    /// them.
    /// </summary>
    public static int UnitsForTeam(int teamIndex)
    {
        return teamIndex == GameLoop.OpponentTeamIndex ? EnemyCount : AllyCount;
    }

    /// <summary>
    /// Fog off (the designer needs to see the dummy at all times) and a bot opponent, so the match
    /// is a single-client loopback host exactly like the tutorial's.
    /// </summary>
    public static MatchOptions BuildMatchOptions()
    {
        return new MatchOptions
        {
            gameMode = GameMode.Elimination,
            opponentType = OpponentType.AI,
            fogOfWar = false,
            mapId = MapId.Concourse,
        }.Sanitized();
    }

    public static int[] BuildHostRoster() => RosterRules.BuildPreferredRoster(TestUnitIndex);

    public static int[] BuildOpponentRoster() => RosterRules.BuildPreferredRoster(DummyUnitIndex);

    public static List<Vector2Int[]> CreateSpawnLayout()
    {
        List<Vector2Int[]> layout = new(GameLoop.TeamCount);
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            if (teamIndex == GameLoop.HostTeamIndex)
            {
                // The character under test always stands on PlayerSpawn; any extra teammates fill
                // AllySpawns in order behind it.
                int allies = Mathf.Clamp(AllyCount, 1, MaxAllyCount);
                Vector2Int[] friendly = new Vector2Int[allies];
                friendly[0] = PlayerSpawn;
                for (int i = 1; i < allies; i++)
                    friendly[i] = AllySpawns[i - 1];
                layout.Add(friendly);
                continue;
            }

            Vector2Int[] enemies = new Vector2Int[Mathf.Clamp(EnemyCount, 1, MaxEnemyCount)];
            System.Array.Copy(EnemySpawns, enemies, enemies.Length);
            layout.Add(enemies);
        }
        return layout;
    }
}
