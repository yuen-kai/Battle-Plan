using System.Collections.Generic;
using UnityEngine;

public enum AbilityTargetValidationReason : byte
{
    Valid,
    NoTargetCell,
    TargetNotRequired,
    AbilityUnavailable,
    OutOfBounds,
    DirectionNotAdjacent,
    OutOfRange,
    WallBlocked,
    AimedAtOwnCell,
}

public partial class PlanMovement
{
    /// <summary>
    /// Planning-phase ability targeting: while a unit is in ability mode (card toggled yellow),
    /// clicks pick a target square inside the displayed range. Directional abilities use one of
    /// the eight adjacent cells as a direction anchor; self-targeted abilities need no square.
    /// The square is stored as the plan's second element: (true, [startCell, targetSquare]).
    /// Clicks that land on one of your own units are read as picking that unit instead; the
    /// square it stands on is still a square, and still aimable.
    /// </summary>
    void AbilitySelection()
    {
        Movement movement = selectedUnit != null ? selectedUnit.GetComponent<Movement>() : null;
        UnitData unitData = movement != null ? movement.unitData : null;
        if (!Input.GetMouseButtonDown(0))
            return;
        if (GameHUDController.IsPointerOverUI(Input.mousePosition))
            return;
        if (selectedUnit == null || unitData == null)
        {
            ShowInvalidTargetFeedback(AbilityTargetValidationReason.AbilityUnavailable);
            return;
        }

        // Pointing at a unit asks for that unit; pointing at the board names a square. What the
        // pointer is actually over decides which, so a team-mate standing inside an ability's
        // range can be given its orders without the click reading as an aim, while the ground it
        // stands on stays aimable — smoke underfoot is one of the Commander's better plays.
        //
        // Picking a unit and drawing its route is one press in movement mode, and aiming is no
        // reason for it to be two. The press on a team-mate goes straight to the route drag, which
        // finds and takes the unit itself and leaves the gesture live, so the press that moves the
        // selection off this unit is already laying the next one's route.
        GameObject pointedUnit = Mouse.GetFriendlyUnitUnderMouse();
        if (
            pointedUnit != null
            && pointedUnit != selectedUnit
            && PathSelection.Instance?.TryStartPath() == true
        )
        {
            return;
        }

        if (pointedUnit != null && TrySelectPlanningUnit(pointedUnit))
        {
            // The press landed on the unit that was aiming, which has just turned itself back to
            // movement. It gets a drag too, so that press draws a route rather than only deciding
            // what the next press will do.
            if (plans.TryGetValue(selectedUnit, out (bool, List<Vector3>) picked) && !picked.Item1)
                PathSelection.Instance?.BeginRouteDrag();
            return;
        }

        Vector3? clicked = Mouse.GetGridCellUnderMouse();
        if (clicked == null)
        {
            ShowInvalidTargetFeedback(AbilityTargetValidationReason.NoTargetCell);
            return;
        }

        Vector3 square = clicked.Value;
        Vector3 start = GridSystem.GetNearestGridCell(selectedUnit);
        Vector2Int startCell = GridSystem.ConvertToGridCoords(start);
        Vector2Int selectedCell = GridSystem.ConvertToGridCoords(square);
        AbilityTargetValidationReason validation = ValidateAbilityTarget(
            unitData,
            startCell,
            selectedCell,
            GameLoop.gridBounds.Contains(new Vector2(square.x, square.z)),
            GameLoop.wallLayout.Contains(selectedCell)
        );
        if (
            validation == AbilityTargetValidationReason.Valid
            && selectedUnit.GetComponent<Smoke>() != null
            && !GridSystem.IsSquareFootprintInBounds(selectedCell, Smoke.FootprintRadius)
        )
        {
            validation = AbilityTargetValidationReason.OutOfBounds;
        }

        if (validation == AbilityTargetValidationReason.TargetNotRequired)
        {
            GameHUDController.Instance?.SetTargetFeedback(
                GetAbilityTargetFeedback(validation),
                false
            );
            return;
        }

        if (validation != AbilityTargetValidationReason.Valid)
        {
            bool canStartMovement =
                validation == AbilityTargetValidationReason.DirectionNotAdjacent
                || validation == AbilityTargetValidationReason.OutOfRange;
            if (
                canStartMovement
                && selectedCell != startCell
                && PathSelection.Instance?.TryStartPath() == true
            )
            {
                GameHUDController.Instance?.ClearTargetFeedback();
                return;
            }
            ShowInvalidTargetFeedback(validation);
            return;
        }

        plans[selectedUnit] = (true, new List<Vector3> { start, square });
        currentPlan = plans[selectedUnit].Item2;
        RefreshAbilityIndicators();
        // An ability that carries its caster hands back the square it leaves and takes the one it
        // is aimed at, so the team's routes are redrawn against the cells this aim just changed.
        RefreshAllRibbons();
        GameHUDController.Instance?.SetTargetFeedback("Target locked.", false);
    }

    public static AbilityTargetValidationReason ValidateAbilityTarget(
        UnitData unitData,
        Vector2Int startCell,
        Vector2Int targetCell,
        bool isInBounds,
        bool isWall
    )
    {
        if (unitData == null)
            return AbilityTargetValidationReason.AbilityUnavailable;
        if (!unitData.selectAbilitySquare)
            return AbilityTargetValidationReason.TargetNotRequired;
        if (!isInBounds)
            return AbilityTargetValidationReason.OutOfBounds;

        if (unitData.selectAbilityDirection)
        {
            return unitData.abilityFixedDistance > 0
                && GridSystem.TryGetAdjacentDirection(startCell, targetCell, out _)
                ? AbilityTargetValidationReason.Valid
                : AbilityTargetValidationReason.DirectionNotAdjacent;
        }

        if (!unitData.CanTargetOwnCell && targetCell == startCell)
            return AbilityTargetValidationReason.AimedAtOwnCell;

        int manhattanCells =
            Mathf.Abs(targetCell.x - startCell.x) + Mathf.Abs(targetCell.y - startCell.y);
        if (manhattanCells > unitData.abilitySquareRange)
            return AbilityTargetValidationReason.OutOfRange;
        if (!unitData.responseDistLine && isWall)
            return AbilityTargetValidationReason.WallBlocked;
        return AbilityTargetValidationReason.Valid;
    }

    public static string GetAbilityTargetFeedback(AbilityTargetValidationReason reason)
    {
        return reason switch
        {
            AbilityTargetValidationReason.NoTargetCell =>
                "Choose a highlighted target cell.",
            AbilityTargetValidationReason.TargetNotRequired =>
                "This ability activates on its caster.",
            AbilityTargetValidationReason.AbilityUnavailable =>
                "This unit has no targetable ability.",
            AbilityTargetValidationReason.OutOfBounds =>
                "Choose a target inside the battlefield.",
            AbilityTargetValidationReason.DirectionNotAdjacent =>
                "Choose one of the eight adjacent direction cells.",
            AbilityTargetValidationReason.OutOfRange =>
                "Target is outside this ability's range.",
            AbilityTargetValidationReason.WallBlocked =>
                "That ability cannot target a wall cell.",
            AbilityTargetValidationReason.AimedAtOwnCell =>
                "Aim away from the square you are standing on.",
            _ => string.Empty,
        };
    }

    private static void ShowInvalidTargetFeedback(AbilityTargetValidationReason reason)
    {
        GameHUDController.Instance?.SetTargetFeedback(GetAbilityTargetFeedback(reason), true);
    }

    // Runtime-generated ability target visuals (AOE disc, single/multi-cell square outline, or
    // preview line), mirroring how AreaLock builds its laser at runtime — no prefab/scene
    // references needed. Held per unit like route ribbons are, so an ability plan stays on the
    // board while its owner sits in the background and another unit takes its orders.
    private Dictionary<GameObject, GameObject> abilityVisuals = new();

    // The blast disc is a filled shape rather than a line, so it needs to sit further back than
    // the outlines it is drawn among to avoid swamping them.
    private const float AbilityDiscAlphaScale = 0.55f;

    private static readonly List<Vector3> abilityPathPoints = new();

    /// <summary>
    /// The colour a unit's ability plan draws in. This is the same per-roster-slot colour its
    /// movement route uses, so a plan on the board — move or ability — carries its owner's
    /// identity and shape alone signals move-versus-ability, leaving no need to read a label to
    /// tell whose grenade is landing where.
    /// </summary>
    private static Color GetAbilityPreviewColor(int rosterSlot, bool selected)
    {
        return PlanPathStyle.GetRouteColor(rosterSlot, selected);
    }

    // MoveOverlayCell's root plane is opaque (URP Lit, ZWrite on) at world y=0.2, and its "Inner"
    // highlight sits at y=0.227; both are shown under the target cell whenever an ability's range
    // is displayed. A transparent marker placed at or below that height fails the depth test
    // against the opaque tile and is fully hidden, even at full alpha. Keep ability target
    // markers clearly above both so they render on top of the range overlay.
    private const float AbilityIndicatorHeight = 0.26f;

    /// <summary>
    /// Redraws the whole team's ability plans. Every unit holding a target keeps its preview on the
    /// board, not just the selected one, so a turn can be judged as a whole — whether the grenade
    /// lands clear of where the Pogo Rider is about to come down — instead of one card at a time.
    /// The unit being edited draws bright and the rest step back.
    /// </summary>
    private void RefreshAbilityIndicators()
    {
        ClearAbilityIndicators();
        foreach (KeyValuePair<GameObject, (bool, List<Vector3>)> entry in plans)
        {
            GameObject unit = entry.Key;
            (bool abilityMode, List<Vector3> plan) = entry.Value;
            if (!abilityMode || plan == null || plan.Count < 1 || !IsPlanningUnitAvailable(unit))
                continue;

            Movement movement = unit.GetComponent<Movement>();
            UnitData unitData = movement != null ? movement.unitData : null;
            if (unitData == null)
                continue;

            // A self-cast ability names no square, so its plan is just [start] — which used to be
            // filtered out here twice over (plan.Count < 2, and the selectAbilitySquare check), so
            // an ability centred on its own caster drew nothing at all during planning. It still has
            // a footprint worth showing whenever it has a radius: the player needs to see how far a
            // self-centred zap or an ally buff actually reaches before committing to it.
            bool selfCast = !unitData.selectAbilitySquare;
            if (selfCast)
            {
                if (unitData.abilityRadius <= 0f)
                    continue; // nothing to draw: a self-only effect has no area to outline
            }
            else if (plan.Count < 2)
            {
                continue; // a square-targeted ability has no target yet
            }

            // Mirrors GameLoop's own square resolution for an activation
            // (!selectAbilitySquare || plan.Count < 2 ? plan[0] : plan[^1]) so the disc previewed
            // during planning is centred exactly where the ability will actually resolve.
            Vector3 square = selfCast || plan.Count < 2 ? plan[0] : plan[^1];

            abilityVisuals[unit] = BuildAbilityIndicator(
                unit,
                plan[0],
                square,
                unitData,
                unit == HighlightedUnit
            );
        }
    }

    /// <summary>
    /// Builds one unit's ability preview: the path the ability travels, plus a marker on whatever
    /// it arrives at. Everything hangs off a single host so one unit's plan can be dropped or
    /// rebuilt without disturbing anyone else's.
    /// </summary>
    GameObject BuildAbilityIndicator(
        GameObject unit,
        Vector3 start,
        Vector3 square,
        UnitData unitData,
        bool selected
    ) =>
        BuildAbilityPreview(
            planVisualsFolder != null ? planVisualsFolder.transform : null,
            unit,
            start,
            square,
            unitData,
            GetRosterSlot(unit),
            selected
        );

    /// <summary>
    /// The board half of <see cref="BuildAbilityIndicator"/>, with the planning screen's own state
    /// handed in rather than read off this component. Tooling that has to put a plan on the board
    /// without a planning screen behind it draws the shipping telegraph through here instead of a
    /// lookalike built to match it.
    /// </summary>
    public static GameObject BuildAbilityPreview(
        Transform parent,
        GameObject unit,
        Vector3 start,
        Vector3 square,
        UnitData unitData,
        int rosterSlot,
        bool selected
    )
    {
        GameObject host = new($"AbilityPlan_{rosterSlot}");
        host.transform.SetParent(parent, false);
        Color color = GetAbilityPreviewColor(rosterSlot, selected);

        Vector3? abilityPathEnd = ShowAbilityPathPreview(host, unit, square, unitData, color);

        if (unit.GetComponent<Smoke>() != null)
        {
            CreateSquareFootprintIndicator(host, square, Smoke.FootprintRadius, color);
            return host;
        }

        if (
            unitData.selectAbilityDirection
            && GridSystem.TryGetAdjacentDirection(
                GridSystem.ConvertToGridCoords(start),
                GridSystem.ConvertToGridCoords(square),
                out Vector2Int abilityDirection
            )
        )
        {
            Vector2Int startCoords = GridSystem.ConvertToGridCoords(start);
            Vector2Int destination = GridSystem.GetDirectionalDestination(
                startCoords,
                abilityDirection,
                unitData.abilityFixedDistance,
                GameLoop.wallLayout
            );
            // A wall right against the caster resolves the travel back to the caster's own cell.
            // Marking that cell reads as "aimed at myself"; keep the picked cell so the telegraph
            // still points at the thing being shot/leapt into.
            if (destination != startCoords)
                square = GameLoop.gridCoordToWorld(destination);
        }

        if (unitData.responseDistLine && abilityPathEnd == null)
        {
            Vector3 casterPos = unit.transform.position;
            Vector3 end = GameLoop.ResolveLineAbilityEndpoint(unit, square);
            if (end == casterPos)
                return host;

            LineRenderer lr = host.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = 0.12f;
            lr.positionCount = 2;
            lr.SetPosition(0, casterPos);
            lr.SetPosition(1, end);
        }
        else if (unitData.abilityRadius > 0f)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.transform.parent = host.transform;
            float diameter = 2f * unitData.abilityRadius * cellSize;
            Vector3 markerPosition = abilityPathEnd ?? square;
            markerPosition.y = square.y + AbilityIndicatorHeight;
            marker.transform.position = markerPosition;
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            Color discColor = color;
            discColor.a *= AbilityDiscAlphaScale;
            Renderer rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = discColor;
        }
        else
        {
            // Point-target abilities have no blast radius to draw as an AOE disc (e.g. the Pogo
            // Rider's Jump — it lands on exactly one cell). Outline that single target square
            // instead so the selection reads clearly, matching the other units' visible markers.
            CreateSquareFootprintIndicator(host, square, 0, color);
        }

        return host;
    }

    /// <summary>
    /// Draws the route the ability itself will travel, from points the ability samples off its own
    /// execution maths — the Ramrod's rush, the Soldier's grenade arc, the Pogo Rider's jump.
    /// Abilities that reach their target without travelling report nothing and draw nothing, so no
    /// new ability needs this method changed to be previewed.
    /// </summary>
    static Vector3? ShowAbilityPathPreview(
        GameObject host,
        GameObject unit,
        Vector3 selectedSquare,
        UnitData unitData,
        Color color
    )
    {
        Ability ability = unit.GetComponent<Ability>();
        if (ability == null)
            return null;

        AbilityPathKind kind = ability.BuildPlannedPath(
            selectedSquare,
            unitData,
            abilityPathPoints
        );
        if (kind == AbilityPathKind.None || abilityPathPoints.Count < 2)
            return null;

        AbilityPathIndicator
            .Create(host.transform, "AbilityPath")
            .Show(abilityPathPoints, kind, color);

        return abilityPathPoints[^1];
    }

    static void CreateSquareFootprintIndicator(
        GameObject host,
        Vector3 square,
        int radius,
        Color previewColor
    )
    {
        Vector2Int center = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(square)
        );
        Material previewMaterial = new(Shader.Find("Sprites/Default"));
        float halfCell = cellSize * 0.46f;
        float lineWidth = Mathf.Max(0.04f, cellSize * 0.04f);

        foreach (Vector2Int cell in GridSystem.GetSquareFootprint(center, radius))
        {
            GameObject cellOutline = new($"AbilityTargetCell_{cell.x}_{cell.y}");
            cellOutline.transform.SetParent(host.transform, true);

            Vector3 cellCenter =
                GameLoop.gridCoordToWorld(cell) + new Vector3(0, AbilityIndicatorHeight, 0);
            LineRenderer outline = cellOutline.AddComponent<LineRenderer>();
            outline.material = previewMaterial;
            outline.startColor = outline.endColor = previewColor;
            outline.startWidth = outline.endWidth = lineWidth;
            outline.positionCount = 5;
            outline.SetPositions(
                new[]
                {
                    cellCenter + new Vector3(-halfCell, 0, -halfCell),
                    cellCenter + new Vector3(-halfCell, 0, halfCell),
                    cellCenter + new Vector3(halfCell, 0, halfCell),
                    cellCenter + new Vector3(halfCell, 0, -halfCell),
                    cellCenter + new Vector3(-halfCell, 0, -halfCell),
                }
            );
        }
    }

    /// <summary>Drops one unit's ability preview, leaving the rest of the team's plans drawn.</summary>
    void ClearAbilityIndicator(GameObject unit)
    {
        if (unit == null || !abilityVisuals.TryGetValue(unit, out GameObject indicator))
            return;

        DestroyAbilityIndicator(indicator);
        abilityVisuals.Remove(unit);
    }

    void ClearAbilityIndicators()
    {
        foreach (GameObject indicator in abilityVisuals.Values)
            DestroyAbilityIndicator(indicator);
        abilityVisuals.Clear();
    }

    private void DestroyAbilityIndicator(GameObject indicator)
    {
        if (indicator == null)
            return;

        // Destroy is deferred; hide the private planning preview before the phase can advance.
        indicator.SetActive(false);
        Destroy(indicator);
    }
}
