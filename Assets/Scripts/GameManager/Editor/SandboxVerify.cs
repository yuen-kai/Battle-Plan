#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// DEV: drives one character through its ability in the Character Sandbox and records what actually
/// happened, so a kit can be verified against the running game rather than against its own unit
/// tests. Editor-only.
/// <para>
/// This exists because the edit-mode suite structurally cannot cover the interesting half of an
/// ability: <c>Shooting.FireBulletInDirection</c> reaches a <c>[ClientRpc]</c> (so it needs a spawned
/// NetworkObject), damage flows through <c>Health</c> on live units, wall destruction needs a real
/// <c>GameLoop</c>, and none of that is available outside Play mode. Extracting pure helpers and
/// asserting on those instead proves the helpers work, not the ability — so the claims that actually
/// matter ("the dummy lost health", "the wall is gone", "the rounds went through cover") are checked
/// here, in a match.
/// </para>
/// <para>
/// Self-driving and self-stopping by design: it polls observable state rather than sleeping for a
/// fixed duration, logs a single <c>[VERIFY]</c> line per step and a final <c>RESULT:</c> sentinel,
/// so an agent can start it and then wait for the sentinel instead of idling in Play mode deciding
/// what to do next.
/// </para>
/// </summary>
public static class SandboxVerify
{
    /// <summary>What one character's run observed. Read back after <see cref="Report"/> is set.</summary>
    public sealed class Result
    {
        public string UnitName;
        public bool AbilityResolved;
        public float DummyHealthBefore;
        public float DummyHealthAfter;
        public int WallCountBefore;
        public int WallCountAfter;
        public bool DummyStunned;
        public Vector2Int DummyCellBefore;
        public Vector2Int DummyCellAfter;
        public string Notes = "";

        public float DamageDealt => DummyHealthBefore - DummyHealthAfter;
        public int WallsDestroyed => WallCountBefore - WallCountAfter;

        public override string ToString()
        {
            return $"{UnitName}: resolved={AbilityResolved} damage={DamageDealt:0.#} "
                + $"wallsDestroyed={WallsDestroyed} stunned={DummyStunned} "
                + $"dummyCell={DummyCellBefore}->{DummyCellAfter} {Notes}";
        }
    }

    public static string Report { get; private set; }
    public static bool Running { get; private set; }

    private static readonly List<Result> results = new();

    public static IReadOnlyList<Result> Results => results;

    /// <summary>
    /// Runs the character at <paramref name="catalogIndex"/> through its ability once. Call from
    /// Play mode with a sandbox match already live (see <c>CharacterSandbox.Launch</c>).
    /// <paramml name="abilityTarget"/> is the cell to aim at, or null for a self-cast ability.
    /// </summary>
    public static void Run(int catalogIndex, Vector2Int? abilityTarget)
    {
        GameLoop loop = GameLoop.Instance;
        if (loop == null)
        {
            Debug.LogWarning("[VERIFY] No GameLoop; start a sandbox match first.");
            return;
        }
        loop.StartCoroutine(RunRoutine(catalogIndex, abilityTarget));
    }

    private static IEnumerator RunRoutine(int catalogIndex, Vector2Int? abilityTarget)
    {
        Running = true;
        Report = null;
        Result result = new();
        results.Add(result);

        // Dev mode drives the round loop off explicit submissions instead of a wall clock. It is set
        // HERE rather than before the launch because the sandbox routes through the join screen,
        // which clears it on the way past (JoinGameUIController.CreateMatch).
        DevInput.SetDevMode(true);

        // Wait for the board: the two units spawn a frame or more after the scene loads.
        while (
            GameLoop.Instance == null
            || GameLoop.GetTeamUnits(GameLoop.HostTeamIndex).Length == 0
            || GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex).Length == 0
        )
        {
            yield return null;
        }

        GameObject caster = GameLoop.GetTeamUnits(GameLoop.HostTeamIndex)[0];
        GameObject dummy = GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex)[0];
        Health dummyHealth = dummy.GetComponent<Health>();
        Unit dummyIdentity = dummy.GetComponent<Unit>();

        result.UnitName = caster.name.Replace("(Clone)", "");
        result.DummyHealthBefore = dummyHealth != null ? dummyHealth.CurrentHealth : -1f;
        result.WallCountBefore = GameLoop.wallLayout.Count;
        result.DummyCellBefore = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(dummy)
        );

        Debug.Log(
            $"[VERIFY] {result.UnitName}: casting at {(abilityTarget.HasValue ? abilityTarget.Value.ToString() : "self")}; "
                + $"dummy hp={result.DummyHealthBefore} walls={result.WallCountBefore}"
        );

        // Wait until the round loop is actually accepting plans.
        while (GameLoop.currentPhase != "planning")
            yield return null;

        if (abilityTarget.HasValue)
            DevInput.SetAbility(0, 0, abilityTarget.Value.x, abilityTarget.Value.y);
        else
            DevInput.SetAbility(0, 0);
        DevInput.SubmitPlans();

        // Poll for the ability to resolve rather than sleeping a fixed time. Two signals: the
        // dummy's health/board state changing, or the round coming back around to planning (which
        // means execution finished either way). The frame cap is a backstop against a hang, not a
        // timing assumption.
        int guardFrames = 0;
        const int MaxFrames = 6000;
        bool sawExecution = false;
        while (guardFrames++ < MaxFrames)
        {
            if (GameLoop.currentPhase == "executing" || GameLoop.currentPhase == "dodging")
                sawExecution = true;
            if (sawExecution && GameLoop.currentPhase == "planning")
                break;
            if (dummyIdentity != null && dummyIdentity.IsStunned)
                result.DummyStunned = true;
            yield return null;
        }

        result.AbilityResolved = sawExecution;
        result.DummyHealthAfter = dummyHealth != null ? dummyHealth.CurrentHealth : -1f;
        result.WallCountAfter = GameLoop.wallLayout.Count;
        result.DummyCellAfter = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(dummy)
        );
        if (guardFrames >= MaxFrames)
            result.Notes += "TIMED OUT waiting for the round to resolve. ";

        Debug.Log($"[VERIFY] {result}");
        Report = result.ToString();
        Running = false;
    }

    /// <summary>Every run so far, for one final read-back at the end of a session.</summary>
    public static string Summary()
    {
        StringBuilder sb = new();
        foreach (Result result in results)
            sb.AppendLine(result.ToString());
        sb.AppendLine($"RESULT: {results.Count} character(s) exercised.");
        return sb.ToString();
    }

    public static void Clear()
    {
        results.Clear();
        Report = null;
    }
}
#endif
