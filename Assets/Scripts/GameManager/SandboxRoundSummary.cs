#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// The sandbox board and this round's orders, written out as plain text for the clipboard. A board
/// worth turning into a test case is described here rather than replayed in the sandbox: the
/// designer hands this to a coding agent, which writes the test against it.
///
/// It reads the live board rather than <see cref="SandboxSession"/>'s setup, because a round is
/// planned on the units as they stand — moved, hurt and recharging — not on the cells a rebuild
/// would put them back on.
/// </summary>
public static class SandboxRoundSummary
{
    public static string Build()
    {
        GameLoop loop = GameLoop.Instance;
        MatchOptions options = loop != null ? loop.Options : MatchOptions.Current;
        bool fog = loop != null ? loop.FogOfWarEnabled : SandboxSession.FogOfWar;

        StringBuilder text = new();
        text.AppendLine("=== Battle Plan sandbox round ===");
        text.AppendLine(
            $"map={MapCatalog.Active.DisplayName} mode={options.GameModeDisplayName} "
                + $"fog={(fog ? "on" : "off")} round={loop?.RoundNumber ?? 0} "
                + $"phase={GameLoop.currentPhase}"
        );
        text.AppendLine(
            $"grid={GridSystem.ColumnCount}x{GridSystem.RowCount}, cells are (column,row)"
        );
        text.AppendLine(DescribeOrderSource());

        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
            AppendTeam(text, teamIndex);

        AppendCells(text, "cover destroyed", DestroyedCoverCells(options));
        AppendCells(text, "smoke", loop?.ActiveSmokeCells);
        return text.ToString();
    }

    /// <summary>
    /// Which question the orders below answer. The planner holds routes during planning and dives
    /// during a dodge window, and the two are read very differently by anyone rebuilding the round.
    /// </summary>
    private static string DescribeOrderSource()
    {
        if (PlanMovement.Instance == null)
            return "orders: none authored yet";
        return GameLoop.currentPhase == GameLoop.Phase.Dodging
            ? "orders: dodge-window dives"
            : "orders: planning-phase orders";
    }

    private static void AppendTeam(StringBuilder text, int teamIndex)
    {
        SandboxCrew crew = SandboxSession.Crew(teamIndex);
        string teamName =
            teamIndex < GameLoop.teamNames.Count
                ? GameLoop.teamNames[teamIndex]
                : $"team {teamIndex}";

        text.AppendLine();
        text.AppendLine($"Team {teamIndex} ({teamName}) immortal={(crew.immortal ? "yes" : "no")}");

        IEnumerable<GameObject> units = GameLoop
            .GetTeamUnits(teamIndex)
            .Where(unit => unit != null)
            .OrderBy(unit => unit.GetComponent<Unit>()?.RosterSlot ?? int.MaxValue);
        foreach (GameObject unit in units)
            AppendUnit(text, unit, crew);
    }

    private static void AppendUnit(StringBuilder text, GameObject unit, SandboxCrew crew)
    {
        Unit identity = unit.GetComponent<Unit>();
        UnitData data = unit.GetComponent<Movement>()?.unitData;
        int slot = identity != null ? identity.RosterSlot : -1;
        Vector2Int cell = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        string catalog = crew.HasSlot(slot) ? $" (catalog {crew.unitIndices[slot]})" : string.Empty;
        string name = data != null ? data.unitName : unit.name;

        text.AppendLine(
            $"  [{slot}] {name}{catalog} cell=({cell.x},{cell.y}) {DescribeHealth(unit, data)} "
                + $"{DescribeAbility(identity, data)}{(unit.activeSelf ? string.Empty : " DEAD")}"
        );
        text.AppendLine($"      {DescribeOrder(unit, data)}");
    }

    private static string DescribeHealth(GameObject unit, UnitData data)
    {
        Health health = unit.GetComponent<Health>();
        if (health == null)
            return "hp=?";
        float max = data != null ? data.maxHealth : health.MaxHealth;
        return $"hp={health.CurrentHealth:0.#}/{max:0.#}";
    }

    private static string DescribeAbility(Unit identity, UnitData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.abilityName))
            return "ability=none";

        string state = "ready";
        if (identity != null && identity.AbilityCooldownRoundsRemaining > 0)
        {
            int rounds = identity.AbilityCooldownRoundsRemaining;
            state = $"recharging {rounds} {(rounds == 1 ? "round" : "rounds")}";
        }
        return $"ability=\"{data.abilityName}\" {state}";
    }

    /// <summary>
    /// What this unit was told to do with the round. A plan is one order or the other — a route or
    /// an ability — so a unit with neither is spending the round holding its square.
    /// </summary>
    private static string DescribeOrder(GameObject unit, UnitData data)
    {
        PathsDict plans = PlanMovement.Instance?.plans;
        if (plans == null || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return "order: hold";

        List<Vector2Int> cells = (plan.Item2 ?? new List<Vector3>())
            .Select(GridSystem.ConvertToGridCoords)
            .ToList();

        if (!plan.Item1)
        {
            return cells.Count > 1
                ? $"order: move {string.Join(" -> ", cells.Select(FormatCell))}"
                : "order: hold";
        }

        string abilityName = data != null ? data.abilityName : "ability";
        if (data != null && !data.selectAbilitySquare && !data.selectAbilityDirection)
            return $"order: ability \"{abilityName}\" (self-cast, no target)";
        if (cells.Count < 2)
            return $"order: ability \"{abilityName}\" (target not chosen yet)";

        Vector2Int target = cells[1];
        if (data != null && data.selectAbilityDirection)
        {
            Vector2Int direction = target - cells[0];
            return $"order: ability \"{abilityName}\" direction=({direction.x},{direction.y}) "
                + $"anchor={FormatCell(target)}";
        }
        return $"order: ability \"{abilityName}\" target={FormatCell(target)}";
    }

    /// <summary>
    /// Cover the round has already blown open. The map names the rest of the walls, so only the
    /// holes cut in it since it was loaded need saying.
    /// </summary>
    private static IEnumerable<Vector2Int> DestroyedCoverCells(MatchOptions options)
    {
        HashSet<Vector2Int> pristine = MapCatalog.ById(options.mapId).Walls;
        return pristine.Except(GameLoop.wallLayout);
    }

    private static void AppendCells(StringBuilder text, string label, IEnumerable<Vector2Int> cells)
    {
        List<Vector2Int> ordered = (cells ?? Enumerable.Empty<Vector2Int>())
            .OrderBy(cell => cell.y)
            .ThenBy(cell => cell.x)
            .ToList();
        if (ordered.Count == 0)
            return;

        text.AppendLine();
        text.AppendLine($"{label}: {string.Join(" ", ordered.Select(FormatCell))}");
    }

    private static string FormatCell(Vector2Int cell) => $"({cell.x},{cell.y})";
}
#endif
