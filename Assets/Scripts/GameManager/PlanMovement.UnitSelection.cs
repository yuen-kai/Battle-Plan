using System.Collections.Generic;
using UnityEngine;

public partial class PlanMovement
{
    public void SwitchToUnit(int unitIndex)
    {
        SelectUnit(unitIndex);
    }

    /// <summary>
    /// Presses a unit's ability card. The card is the ability, so one press orders it: the unit is
    /// taken over if it was not the one being edited, and its round is spent on the ability instead
    /// of on movement. Pressing the card of a unit already set to its ability puts that unit back
    /// on movement.
    /// </summary>
    /// <remarks>
    /// This used to take two presses, the first only selecting the unit, because the card was a
    /// roster entry that happened to have an ability on its back. A player who wanted an ability
    /// had to know the card turned over. Selecting a unit to move it is what the board is for —
    /// <see cref="TrySelectPlanningUnit"/> handles the click that lands on one — so the dock is
    /// free to be the abilities and nothing else.
    /// </remarks>
    public bool TryActivateUnitCard(int unitIndex)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return false;

        GameObject unit = teamCharacters[unitIndex];
        if (!IsPlanningUnitAvailable(unit))
            return false;

        bool abilityMode =
            plans.TryGetValue(unit, out (bool, List<Vector3>) plan) && plan.Item1;
        if (abilityMode)
            return TrySetSelectionMode(unit, false);

        bool hadRoute = (plan.Item2?.Count ?? 0) > 1;

        // Picked up first, and deliberately before the attempt rather than after it. A press on a
        // unit whose ability is still recharging is worth something even though it cannot order
        // anything — that unit is now the one taking a route — and selecting afterwards would wipe
        // the very message explaining why the ability did not fire.
        if (selectedUnit != unit)
            SwitchToUnit(unit);

        if (!TrySetSelectionMode(unit, true))
            return false;

        // The two orders are exclusive, so arming the ability throws the route away. The press
        // named which one the player wants, but a route that vanishes without a word reads as the
        // card having eaten it, so the swap is said out loud.
        if (hadRoute)
        {
            string abilityName =
                unit.GetComponent<Movement>()?.unitData?.abilityName ?? "This ability";
            GameHUDController.Instance?.SetTargetFeedback(
                $"{abilityName} replaces this unit's route.",
                false
            );
        }
        return true;
    }

    public void SelectUnit(int unitIndex)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return;

        GameObject unit = teamCharacters[unitIndex];
        if (IsPlanningUnitAvailable(unit))
            SwitchToUnit(unit);
    }

    public void SetSelectionMode(int unitIndex, bool abilityMode)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return;

        TrySetSelectionMode(teamCharacters[unitIndex], abilityMode);
    }

    private bool TrySetSelectionMode(GameObject unit, bool abilityMode)
    {
        if (!CanEditPlan || !IsPlanningUnitAvailable(unit))
            return false;

        if (abilityMode)
        {
            Unit identity = unit.GetComponent<Unit>();
            if (unit.GetComponent<Ability>() == null || identity == null)
            {
                GameHUDController.Instance?.SetTargetFeedback(
                    "This unit can move only.",
                    true
                );
                return false;
            }
            if (!identity.CanUseAbility)
            {
                int rounds = identity.AbilityCooldownRoundsRemaining;
                string roundText = rounds == 1 ? "round" : "rounds";
                GameHUDController.Instance?.SetTargetFeedback(
                    $"{unit.GetComponent<Movement>()?.unitData?.abilityName ?? "Ability"} recharges in {rounds} {roundText}.",
                    true
                );
                return false;
            }
        }

        if (selectedUnit != unit)
            SwitchToUnit(unit);

        if (!plans.ContainsKey(unit))
        {
            plans[unit] = (false, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
        }

        if (plans[unit].Item1 != abilityMode)
        {
            PathSelection.Instance?.CancelCurrentDrag();
            plans[unit] = (abilityMode, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
            // Only this unit's target is being abandoned; the rest of the team keeps its plans.
            ClearAbilityIndicator(unit);
            ResetVisualPlan();
        }

        GameHUDController.Instance?.ClearTargetFeedback();
        ApplySelectedUnitModeVisuals();
        RefreshUnitCards();
        return true;
    }

    public void SwitchToUnit(GameObject newSelectedUnit, int range = -1)
    {
        if (!CanEditPlan)
            return;
        if (newSelectedUnit != null && !IsPlanningUnitAvailable(newSelectedUnit))
            return;

        GameObject previousUnit = selectedUnit;
        selectedUnit = newSelectedUnit;
        GameHUDController.Instance?.ClearTargetFeedback();
        if (selectedUnit == null)
        {
            Destroy(moveOverlay);
            RefreshAllRibbons();
            // Deselecting dims the team's ability plans rather than erasing them.
            RefreshAbilityIndicators();
            RefreshUnitCards();
            return;
        }
        if (previousUnit != selectedUnit)
            PathSelection.Instance?.CancelCurrentDrag();

        if (!plans.ContainsKey(selectedUnit))
        {
            plans[selectedUnit] = (
                false,
                new List<Vector3> { GridSystem.GetNearestGridCell(selectedUnit) }
            );
            ResetVisualPlan();
        }

        ApplySelectedUnitModeVisuals(range);
        RefreshUnitCards();
    }

    private void ApplySelectedUnitModeVisuals(int range = -1)
    {
        if (selectedUnit == null || !plans.ContainsKey(selectedUnit))
            return;

        // Show the range for the card's current movement or ability face.
        UnitData unitData = selectedUnit.GetComponent<Movement>().unitData;
        bool abilityMode = plans[selectedUnit].Item1;
        int movementRange =
            range != -1
                ? range
                : (planningRangeOverride != -1 ? planningRangeOverride : unitData.moveDist);
        if (abilityMode && unitData.selectAbilityDirection)
            DisplayAbilityDirections();
        else if (abilityMode && !unitData.selectAbilitySquare)
        {
            // A self-cast ability has nothing to aim — no target square exists to highlight, so
            // drawing one (even a single own-cell marker) falsely invites a click that
            // ValidateAbilityTarget only rejects as TargetNotRequired.
            Destroy(moveOverlay);
            moveOverlay = null;
        }
        else
            // The same overlay prefab draws both faces of the card, so the mode has to pick the
            // tint here — otherwise "where I can walk" and "where I can aim" are the same colour.
            DisplayMoveRange(
                abilityMode ? unitData.abilitySquareRange : movementRange,
                abilityMode ? TeamPalette.AbilityRange : TeamPalette.MoveRange,
                !abilityMode || unitData.CanTargetOwnCell
            );

        currentPlan = plans[selectedUnit].Item2;
        RefreshAllRibbons();
        RefreshAbilityIndicators();
    }

    private void RefreshUnitCards()
    {
        if (!useUnitCards || GameLoop.Instance == null)
            return;

        int count = teamCharacters.Count;
        for (int i = 0; i < count; i++)
        {
            bool selected = teamCharacters[i] == selectedUnit;
            bool abilityMode =
                plans.TryGetValue(teamCharacters[i], out (bool, List<Vector3>) plan) && plan.Item1;

            GameHUDController.Instance?.SetCardPlanningState(i, selected, abilityMode);
        }
    }
}
