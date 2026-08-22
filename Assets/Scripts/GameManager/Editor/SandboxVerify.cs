#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class SandboxVerify
{
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

        DevInput.SetDevMode(true);

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

        while (GameLoop.currentPhase != "planning")
            yield return null;

        if (abilityTarget.HasValue)
            DevInput.SetAbility(0, 0, abilityTarget.Value.x, abilityTarget.Value.y);
        else
            DevInput.SetAbility(0, 0);
        DevInput.SubmitPlans();

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
