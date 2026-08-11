using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the sandboxed tutorial match: sequences the coaching copy off live match state and supplies
/// the scripted opponent's orders. <see cref="GameLoop"/> attaches this at runtime when a
/// <see cref="TutorialSession"/> is active, and only ever on the loopback host, so it reads server
/// state and the local planner directly instead of going through RPCs.
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    // Instructions: what to do right now. These ride the small line above the dock.
    public const string DrawRoutePrompt = "Drag your unit to draw a route.";
    public const string MoveCloserPrompt = "Move toward the enemy.";
    public const string LockInPrompt = "Lock in.";
    public const string UseAbilityPrompt = "Press your unit's ability card to use it.";
    public const string PickTargetPrompt = "Pick a cell. It hits everything nearby.";
    public const string DodgeNowPrompt = "They aimed at you. Drag your unit clear.";

    // Lessons: the points worth stopping to read. These get the larger card.
    public const string AutoShootLesson =
        "Units shoot on their own once they stop. You choose where they stand, not who they shoot.";
    public const string EnemyDodgedLesson =
        "Enemies can dodge your abilities; use them to force advantageous positions.";
    public const string ClosingLesson =
        "Real matches are five a side. Drag a unit to draw its route, or press its ability card instead.";

    /// <summary>
    /// The script advances on what the player has actually done, never on a round number, so
    /// ignoring a prompt stalls that lesson instead of desyncing the script from the board.
    /// </summary>
    private enum Stage
    {
        Move,
        Ability,
        Dodge,
        Closing,
        Done,
    }

    /// <summary>
    /// A player who walks out of the scripted opponent's throwing range would otherwise hold the
    /// dodge lesson open forever. After this many rounds waiting for the shot, the tutorial gives
    /// up on it and moves on.
    /// </summary>
    private const int MaxRoundsWaitingToThrow = 3;

    private Stage stage = Stage.Move;
    private string lastPhase = string.Empty;
    private string activeInstruction = string.Empty;
    private string activeLesson = string.Empty;
    private bool playerQueuedAbility;
    private bool enemyWasAlerted;
    private bool playerWasAlerted;
    private bool hudPrepared;
    private int roundsWaitingToThrow;

    private void Update()
    {
        if (!TutorialSession.IsActive)
            return;

        PrepareHud();

        string phase = GameLoop.currentPhase;
        if (phase != lastPhase)
        {
            OnPhaseEntered(phase);
            lastPhase = phase;
        }

        if (phase == "planning")
        {
            playerQueuedAbility |= HasQueuedAbility();
            ShowInstruction(PlanningInstruction());
        }
        else if (phase == "dodging")
        {
            LatchDodgeAlerts();
            ShowInstruction(
                stage == Stage.Dodge && playerWasAlerted ? DodgeNowPrompt : string.Empty
            );
        }
    }

    /// <summary>
    /// The HUD comes up on its own schedule relative to the match spawn, so the sandbox's chrome
    /// changes are applied on the first frame it exists rather than fired once and hoped for.
    /// </summary>
    private void PrepareHud()
    {
        if (hudPrepared)
            return;

        GameHUDController hud = GameHUDController.Instance;
        if (hud == null)
            return;

        hud.SuppressTimer(true);
        hud.SetFieldedCardCount(TutorialSession.UnitsPerTeam);
        // The sandbox cannot be won or lost and its clock is hidden, so it never reaches the
        // results overlay every other match is left from. Without this there is no way out of it
        // short of closing the game.
        hud.ShowExitMatch("Exit tutorial", LeaveTutorial);
        hudPrepared = true;
    }

    /// <summary>
    /// Leaves partway through. The session is not marked completed — a tutorial walked out of has
    /// not been taken — and the title screen ends the session on arrival, so nothing here has to
    /// unwind the sandbox by hand.
    /// </summary>
    private static void LeaveTutorial()
    {
        GameHUDController.Instance?.SetCoachPrompt(string.Empty);
        GameHUDController.Instance?.SetLessonPopup(string.Empty);
        GameLoop.Instance?.ExitToMainMenu();
    }

    private void OnPhaseEntered(string phase)
    {
        switch (phase)
        {
            case "planning":
                playerQueuedAbility = false;
                enemyWasAlerted = false;
                playerWasAlerted = false;
                if (stage == Stage.Closing)
                    ShowLesson(ClosingLesson);
                break;

            case "dodging":
                LatchDodgeAlerts();
                ShowInstruction(string.Empty);
                break;

            case "executing":
                // A dodge window can open and close without ever surfacing to Update, so the round
                // record is read again here rather than relying on having seen the phase.
                LatchDodgeAlerts();
                ShowInstruction(string.Empty);
                ShowLesson(ExecutionLesson());
                AdvanceStage();
                break;

            default:
                ShowInstruction(string.Empty);
                break;
        }
    }

    /// <summary>
    /// The sandbox exists to teach three things, so it closes when the last card is dismissed
    /// rather than running on until a crew dies.
    /// </summary>
    private void OnClosingLessonDismissed()
    {
        activeLesson = string.Empty;
        stage = Stage.Done;
        GameLoop.Instance?.FinishTutorial();
    }

    private string PlanningInstruction()
    {
        switch (stage)
        {
            case Stage.Move:
                return TryGetLocalPlan(out (bool ability, List<Vector3> cells) movePlan)
                    && !movePlan.ability
                    && movePlan.cells.Count > 1
                    ? AdvanceOrLockIn(movePlan.cells)
                    : DrawRoutePrompt;

            case Stage.Ability:
                // Out of throwing reach the ability instruction is a dead end, so the student is
                // steered back into range instead.
                if (!IsEnemyWithinAbilityReach())
                    return ApproachInstruction();
                if (!TryGetLocalPlan(out (bool ability, List<Vector3> cells) plan) || !plan.ability)
                    return UseAbilityPrompt;
                return plan.cells.Count >= 2 ? LockInPrompt : PickTargetPrompt;

            default:
                return string.Empty;
        }
    }

    private string ApproachInstruction()
    {
        return TryGetLocalPlan(out (bool ability, List<Vector3> cells) plan)
            && !plan.ability
            && plan.cells.Count > 1
            ? AdvanceOrLockIn(plan.cells)
            : MoveCloserPrompt;
    }

    /// <summary>
    /// Withholding the lock-in prompt from a route that backs away is the whole nudge toward the
    /// opponent. Nothing is blocked: a player who insists can still commit it, and the next round
    /// asks again.
    /// </summary>
    private string AdvanceOrLockIn(List<Vector3> route)
    {
        return RouteClosesOnEnemy(route) ? LockInPrompt : MoveCloserPrompt;
    }

    private string ExecutionLesson()
    {
        if (stage == Stage.Move)
            return AutoShootLesson;
        if (stage == Stage.Ability && playerQueuedAbility && enemyWasAlerted)
            return EnemyDodgedLesson;
        return string.Empty;
    }

    private void AdvanceStage()
    {
        switch (stage)
        {
            case Stage.Move:
                // A student who threw during the movement lesson has already had the next one, and
                // the cooldown would otherwise hold the tutorial open for rounds.
                stage = playerQueuedAbility ? Stage.Dodge : Stage.Ability;
                break;

            case Stage.Ability:
                if (playerQueuedAbility)
                {
                    stage = Stage.Dodge;
                    roundsWaitingToThrow = 0;
                }
                break;

            case Stage.Dodge:
                // The opponent throws during this round's execution, so the lesson is spent either
                // way: the player either answered the alert or watched the blast land.
                if (playerWasAlerted || ++roundsWaitingToThrow >= MaxRoundsWaitingToThrow)
                    stage = Stage.Closing;
                break;
        }
    }

    /// <summary>
    /// A window every alerted team answers at once (the opponent always does) can open and close
    /// inside a single frame, so who was warned comes from the round-scoped record rather than from
    /// the live alert sets.
    /// </summary>
    private void LatchDodgeAlerts()
    {
        GameLoop loop = GameLoop.Instance;
        if (loop == null)
            return;

        enemyWasAlerted |= loop.WasTeamAlertedToDodgeThisRound(GameLoop.OpponentTeamIndex);
        playerWasAlerted |= loop.WasTeamAlertedToDodgeThisRound(GameLoop.HostTeamIndex);
    }

    /// <summary>
    /// True once the student has an ability order with a target on it. Latched by the caller, since
    /// the plan is cleared as the round resolves.
    /// </summary>
    private bool HasQueuedAbility()
    {
        return TryGetLocalPlan(out (bool ability, List<Vector3> cells) plan)
            && plan.ability
            && plan.cells.Count >= 2;
    }

    private bool RouteClosesOnEnemy(List<Vector3> route)
    {
        GameObject player = FirstLivingUnit(GameLoop.HostTeamIndex);
        GameObject enemy = FirstLivingUnit(GameLoop.OpponentTeamIndex);
        if (player == null || enemy == null || route == null || route.Count == 0)
            return true;

        Vector2Int target = CellOf(enemy);
        return GridDistance(GridSystem.ConvertToGridCoords(route[^1]), target)
            < GridDistance(CellOf(player), target);
    }

    private bool IsEnemyWithinAbilityReach()
    {
        GameObject player = FirstLivingUnit(GameLoop.HostTeamIndex);
        GameObject enemy = FirstLivingUnit(GameLoop.OpponentTeamIndex);
        UnitData data = player != null ? player.GetComponent<Movement>()?.unitData : null;
        if (player == null || enemy == null || data == null)
            return true;

        return GridDistance(CellOf(player), CellOf(enemy)) <= data.abilitySquareRange;
    }

    private bool TryGetLocalPlan(out (bool ability, List<Vector3> cells) plan)
    {
        plan = default;
        GameObject unit = FirstLivingUnit(GameLoop.HostTeamIndex);
        PlanMovement planner = PlanMovement.Instance;
        if (unit == null || planner == null)
            return false;
        if (!planner.plans.TryGetValue(unit, out (bool, List<Vector3>) stored))
            return false;

        plan = (stored.Item1, stored.Item2);
        return plan.cells != null;
    }

    private void ShowInstruction(string instruction)
    {
        if (instruction == activeInstruction)
            return;

        activeInstruction = instruction;
        GameHUDController.Instance?.SetCoachPrompt(instruction);
    }

    /// <summary>
    /// Lesson cards have no clock: they stay up across phase changes until the player dismisses
    /// them, which is why an empty lesson is ignored rather than treated as "clear the card".
    /// </summary>
    private void ShowLesson(string lesson)
    {
        if (string.IsNullOrEmpty(lesson) || lesson == activeLesson)
            return;

        activeLesson = lesson;
        bool closing = lesson == ClosingLesson;
        GameHUDController.Instance?.SetLessonPopup(
            lesson,
            closing ? "Finish" : "Got it",
            closing ? OnClosingLessonDismissed : ClearActiveLesson
        );
    }

    private void ClearActiveLesson()
    {
        activeLesson = string.Empty;
    }

    /// <summary>
    /// The scripted opponent's orders for the round about to be planned. It holds position through
    /// the movement and ability lessons so the student always has a still target, then throws its
    /// own Grenade at them to open the dodge window. Server-only.
    /// </summary>
    public PathsDict CreateEnemyPlan()
    {
        PathsDict plans = new();
        GameObject enemy = FirstLivingUnit(GameLoop.OpponentTeamIndex);
        if (enemy == null)
            return plans;

        Vector3 start = GridSystem.GetNearestGridCell(enemy);
        if (stage == Stage.Dodge && TryGetThrowTarget(enemy, out Vector3 target))
        {
            plans[enemy] = (true, new List<Vector3> { start, target });
            return plans;
        }

        plans[enemy] = (false, new List<Vector3> { start });
        return plans;
    }

    /// <summary>
    /// The scripted opponent's answer to an incoming ability. It dives clear of the blast but
    /// biases toward the student, so the round it throws back opens from a range that actually
    /// reaches them instead of from wherever "away" happened to land it. Server-only.
    /// </summary>
    public PathsDict CreateEnemyDodge(
        IReadOnlyCollection<GameObject> alerted,
        IReadOnlyList<(GameObject unit, Vector3 square, UnitData data)> activations,
        int diveRange
    )
    {
        PathsDict dives = new();
        GameObject player = FirstLivingUnit(GameLoop.HostTeamIndex);
        if (player == null || alerted == null)
            return dives;

        Vector2Int playerCell = CellOf(player);
        foreach (GameObject unit in alerted)
        {
            if (unit == null)
                continue;

            Vector2Int from = CellOf(unit);
            FindNearestBlast(from, activations, out Vector2Int blast, out float blastRadius);
            if (TryPlanDive(from, blast, blastRadius, playerCell, diveRange, out List<Vector3> dive))
                dives[unit] = (false, dive);
        }
        return dives;
    }

    /// <summary>
    /// Picks the dive that clears the blast and finishes nearest the student. Searched breadth
    /// first rather than as four straight runs, because the cell that both escapes and closes the
    /// gap is usually round a corner: stepping straight at the student is blocked by the student.
    /// </summary>
    private static bool TryPlanDive(
        Vector2Int from,
        Vector2Int blast,
        float blastRadius,
        Vector2Int playerCell,
        int diveRange,
        out List<Vector3> dive
    )
    {
        dive = null;
        Dictionary<Vector2Int, Vector2Int> cameFrom = new() { [from] = from };
        List<Vector2Int> reachable = new();
        Queue<(Vector2Int cell, int depth)> frontier = new();
        frontier.Enqueue((from, 0));

        while (frontier.Count > 0)
        {
            (Vector2Int cell, int depth) = frontier.Dequeue();
            if (depth >= diveRange)
                continue;

            foreach (Vector2Int step in Steps)
            {
                Vector2Int next = cell + step;
                if (
                    cameFrom.ContainsKey(next)
                    || !GridSystem.IsCellInBounds(next)
                    || GameLoop.wallLayout.Contains(next)
                    || next == playerCell
                )
                {
                    continue;
                }

                cameFrom[next] = cell;
                reachable.Add(next);
                frontier.Enqueue((next, depth + 1));
            }
        }

        int bestEscaped = -1;
        int bestCloseness = int.MaxValue;
        Vector2Int best = from;
        foreach (Vector2Int cell in reachable)
        {
            // The blast resolves on a radius, not on step count, so a diagonal neighbour is still
            // inside it however many moves it took to get there.
            int escaped = Vector2Int.Distance(cell, blast) > blastRadius ? 1 : 0;
            int closeness = GridDistance(cell, playerCell);
            if (escaped < bestEscaped || (escaped == bestEscaped && closeness >= bestCloseness))
                continue;

            bestEscaped = escaped;
            bestCloseness = closeness;
            best = cell;
        }

        if (bestEscaped < 0)
            return false;

        List<Vector2Int> cells = new();
        for (Vector2Int cursor = best; cursor != from; cursor = cameFrom[cursor])
            cells.Add(cursor);
        cells.Add(from);
        cells.Reverse();

        dive = new List<Vector3>(cells.Count);
        foreach (Vector2Int cell in cells)
            dive.Add(GameLoop.gridCoordToWorld(cell));
        return dive.Count > 1;
    }

    private static void FindNearestBlast(
        Vector2Int from,
        IReadOnlyList<(GameObject unit, Vector3 square, UnitData data)> activations,
        out Vector2Int blast,
        out float blastRadius
    )
    {
        blast = from;
        blastRadius = 0f;
        int best = int.MaxValue;
        if (activations == null)
            return;

        foreach ((GameObject _, Vector3 square, UnitData data) in activations)
        {
            Vector2Int cell = GridSystem.ConvertToGridCoords(square);
            int distance = GridDistance(from, cell);
            if (distance >= best)
                continue;

            best = distance;
            blast = cell;
            blastRadius = data != null ? data.abilityRadius : 0f;
        }
    }

    private static readonly Vector2Int[] Steps =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right,
    };

    private bool TryGetThrowTarget(GameObject enemy, out Vector3 target)
    {
        target = default;
        GameObject player = FirstLivingUnit(GameLoop.HostTeamIndex);
        UnitData data = enemy.GetComponent<Movement>()?.unitData;
        if (player == null || data == null || enemy.GetComponent<Unit>()?.CanUseAbility != true)
            return false;

        Vector2Int to = CellOf(player);
        if (GridDistance(CellOf(enemy), to) > data.abilitySquareRange)
            return false;

        target = GameLoop.gridCoordToWorld(to);
        return true;
    }

    private static int GridDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    private static Vector2Int CellOf(GameObject unit)
    {
        return GridSystem.ConvertToGridCoords(unit.transform.position);
    }

    private static GameObject FirstLivingUnit(int teamIndex)
    {
        foreach (GameObject unit in GameLoop.GetTeamUnits(teamIndex))
        {
            if (unit != null && unit.GetComponent<Health>()?.IsAlive == true)
                return unit;
        }
        return null;
    }
}
