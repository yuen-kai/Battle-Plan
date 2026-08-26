using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PresidentialRecall : Ability
{
    public const float DamageTakenMultiplier = 0.5f;
    public const int RecallRingRadiusCells = 1;

    public const float BeckonSeconds = 0.25f;
    public const float LeapStaggerSeconds = 0.12f;

    /// <summary>Travel speed rather than a fixed duration, so a far ally does not blur across.</summary>
    public const float LeapCellsPerSecond = 5f;
    public const float MinLeapSeconds = 0.45f;
    public const float MaxLeapSeconds = 1.3f;

    private const float LeapApexPerCell = 0.18f;
    private const float MinLeapApexCells = 0.13f;
    private const float MaxLeapApexCells = 0.6f;

    public override bool CancelsAlliedOrders => true;

    private readonly struct RecalledAlly
    {
        public readonly GameObject Unit;
        public readonly Vector2Int Cell;
        public readonly Vector3 From;
        public readonly Vector3 To;
        public readonly float ApexHeight;
        public readonly float StartDelay;
        public readonly float LeapSeconds;

        public RecalledAlly(GameObject unit, Vector2Int cell, Vector3 to, float startDelay)
        {
            Unit = unit;
            Cell = cell;
            From = unit.transform.position;
            To = to;
            float cells = Vector3.Distance(From, To) / GameLoop.cellSize;
            ApexHeight =
                Mathf.Clamp(cells * LeapApexPerCell, MinLeapApexCells, MaxLeapApexCells)
                * GameLoop.cellSize;
            StartDelay = startDelay;
            LeapSeconds = Mathf.Clamp(
                cells / LeapCellsPerSecond,
                MinLeapSeconds,
                MaxLeapSeconds
            );
        }

        public float EndsAt => StartDelay + LeapSeconds;
    }

    private readonly List<RecalledAlly> recalled = new();
    private readonly HashSet<GameObject> airborne = new();

    public static float MaxRecallSeconds(int detailCount) =>
        BeckonSeconds + MaxLeapSeconds + Mathf.Max(0, detailCount - 1) * LeapStaggerSeconds;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Unit identity = GetComponent<Unit>();
        if (identity == null)
            yield break;

        Vector2Int anchor = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        Movement movement = GetComponent<Movement>();
        movement?.PauseMovement();
        if (movement != null)
            movement.moving = false;
        transform.position = GameLoop.gridCoordToWorld(anchor) + Helper.heightOffset(transform);

        List<GameObject> detail = FindDetail(identity.TeamIndex);
        List<Vector2Int> ringCells = ResolveRingCells(anchor, identity.TeamIndex, detail.Count);

        recalled.Clear();
        airborne.Clear();
        for (int index = 0; index < detail.Count && index < ringCells.Count; index++)
        {
            GameObject ally = detail[index];
            CancelOrders(ally);
            recalled.Add(
                new RecalledAlly(
                    ally,
                    ringCells[index],
                    RestingPosition(ally, ringCells[index]),
                    index * LeapStaggerSeconds
                )
            );
        }

        ApplyGuard(gameObject);
        yield return new WaitForSeconds(BeckonSeconds);

        foreach (RecalledAlly ally in recalled)
            BeginLeap(ally);

        float travelSeconds = recalled.Count == 0 ? 0f : recalled.Max(ally => ally.EndsAt);
        float elapsed = 0f;
        while (elapsed < travelSeconds)
        {
            elapsed += Time.deltaTime;
            foreach (RecalledAlly ally in recalled)
            {
                if (!airborne.Contains(ally.Unit))
                    continue;

                float progress = Mathf.Clamp01((elapsed - ally.StartDelay) / ally.LeapSeconds);
                if (progress <= 0f)
                    continue;
                if (progress >= 1f)
                {
                    Land(ally);
                    continue;
                }

                // Symmetric easing, so the ally accelerates out of its cell and settles into the new
                // one while the arc still peaks at the halfway point.
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                ally.Unit.transform.SetPositionAndRotation(
                    AbilityTrajectory.SampleLob(ally.From, ally.To, ally.ApexHeight, eased),
                    FacingRotation(ally.Unit.transform.position, ally.Unit.transform.rotation)
                );
            }
            yield return null;
        }

        LandRemainingAllies();
        GameLoop.Instance?.HoldReturnFireWindow();
        movement?.transitionToShooting(onlyIfWeaponsStillFree: true);
    }

    protected override void OnAbilityInterrupted()
    {
        LandRemainingAllies();
    }

    private void BeginLeap(RecalledAlly ally)
    {
        Movement allyMovement = ally.Unit.GetComponent<Movement>();
        if (allyMovement != null)
            allyMovement.moving = true;

        Collider allyCollider = ally.Unit.GetComponent<Collider>();
        if (allyCollider != null)
            allyCollider.enabled = false;

        ally.Unit.GetComponent<AnimationHandler>()?.PlayAnimation("Moving");
        airborne.Add(ally.Unit);
    }

    private void Land(RecalledAlly ally)
    {
        if (!airborne.Remove(ally.Unit) || ally.Unit == null)
            return;

        ally.Unit.transform.SetPositionAndRotation(
            ally.To,
            FacingRotation(ally.To, ally.Unit.transform.rotation)
        );

        Collider allyCollider = ally.Unit.GetComponent<Collider>();
        if (allyCollider != null)
            allyCollider.enabled = true;

        Movement allyMovement = ally.Unit.GetComponent<Movement>();
        if (allyMovement != null)
            allyMovement.moving = false;

        ApplyGuard(ally.Unit);
        allyMovement?.transitionToShooting(onlyIfWeaponsStillFree: true);
    }

    private void LandRemainingAllies()
    {
        foreach (RecalledAlly ally in recalled.Where(ally => airborne.Contains(ally.Unit)).ToList())
            Land(ally);
        airborne.Clear();
    }

    private List<GameObject> FindDetail(int teamIndex)
    {
        return GameLoop
            .GetTeamUnits(teamIndex)
            .Where(unit => unit != null && unit != gameObject && IsLivingUnit(unit))
            .OrderBy(unit => unit.GetComponent<Unit>()?.RosterSlot ?? int.MaxValue)
            .ToList();
    }

    private List<Vector2Int> ResolveRingCells(Vector2Int anchor, int teamIndex, int needed)
    {
        HashSet<Vector2Int> blocked = new(GameLoop.wallLayout) { anchor };
        foreach (GameObject enemy in GameLoop.GetTeamUnits(GameLoop.GetEnemyTeamIndex(teamIndex)))
        {
            if (enemy != null && IsLivingUnit(enemy))
            {
                blocked.Add(
                    GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(enemy))
                );
            }
        }

        List<Vector2Int> cells = new();
        for (
            int radius = RecallRingRadiusCells;
            cells.Count < needed && radius <= Mathf.Max(GridSystem.ColumnCount, GridSystem.RowCount);
            radius++
        )
        {
            List<Vector2Int> footprint = GridSystem
                .GetSquareFootprint(anchor, radius)
                .Where(cell =>
                    GridSystem.IsCellInBounds(cell)
                    && !blocked.Contains(cell)
                    && !cells.Contains(cell)
                )
                .ToList();

            footprint.Sort(
                (left, right) =>
                {
                    int byDistance = GridSystem
                        .GetGridDistance(anchor, left)
                        .CompareTo(GridSystem.GetGridDistance(anchor, right));
                    return byDistance != 0
                        ? byDistance
                        : GridSystem.CompareCellsRowMajor(left, right);
                }
            );
            cells.AddRange(footprint);
        }
        return cells;
    }

    private void CancelOrders(GameObject ally)
    {
        Ability allyAbility = ally.GetComponent<Ability>();
        if (allyAbility != null && allyAbility.CancelForDisplacement())
            GameLoop.Instance?.RefundAbilityCharge(ally);

        Movement allyMovement = ally.GetComponent<Movement>();
        if (allyMovement != null)
        {
            allyMovement.PauseMovement();
            allyMovement.moving = false;
        }
        ally.GetComponent<Shooting>()?.PauseShooting();
        GameLoop.Instance?.UnregisterUnitBeingShoved(ally);
    }

    private static Vector3 RestingPosition(GameObject ally, Vector2Int cell)
    {
        return GameLoop.gridCoordToWorld(cell) + Helper.heightOffset(ally.transform);
    }

    private Quaternion FacingRotation(Vector3 from, Quaternion fallback)
    {
        Vector3 facing = transform.position - from;
        facing.y = 0f;
        return facing.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(facing, Vector3.up)
            : fallback;
    }

    private static void ApplyGuard(GameObject unit)
    {
        unit?.GetComponent<Health>()?.ApplyDamageReductionForRound(DamageTakenMultiplier);
    }

    private static bool IsLivingUnit(GameObject unit)
    {
        Health health = unit.GetComponent<Health>();
        return health != null ? health.IsAlive : unit.activeSelf;
    }
}
