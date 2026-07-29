using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Paints one round of a <see cref="BattleReport"/> onto a schematic 15x10 board.
///
/// Everything inside the board is drawn with <see cref="Painter2D"/> rather than built from child
/// elements: a round is up to ten routes over 150 cells, and rebuilding that as a element tree on
/// every step of the round stepper would churn layout for a picture that never accepts input. The
/// only real children are the roster-slot badges, because Painter2D cannot draw text.
///
/// Colours are viewer-relative (<see cref="TeamPalette.ForViewer"/>): both seats read their own
/// crew as blue, matching the board they just played on.
/// </summary>
public sealed class BattleReportBoard
{
    private const float GridLineWidth = 1f;
    private const float StartMarkerRadius = 0.20f;
    private const float EndMarkerHalfSize = 0.17f;
    private const float PathWidth = 0.13f;
    private const float AbilityRingRadius = 0.34f;

    private readonly VisualElement board;
    private readonly List<Label> slotBadges = new();

    private BattleReport report;
    private BattleReportRound round;
    private int localTeamIndex;

    public BattleReportBoard(VisualElement board)
    {
        this.board = board;
        this.board.generateVisualContent += Paint;
        this.board.RegisterCallback<GeometryChangedEvent>(_ => LayoutBadges());
    }

    public void SetRound(BattleReport report, BattleReportRound round, int localTeamIndex)
    {
        this.report = report;
        this.round = round;
        this.localTeamIndex = localTeamIndex;
        RebuildBadges();
        board.MarkDirtyRepaint();
    }

    /// <summary>
    /// Painter2D state persists for the whole callback, so a dashed stroke would leak into every
    /// later one. An empty pattern is the solid case; a zero-length dash is not.
    /// </summary>
    private static void SetSolid(Painter2D painter)
    {
        painter.dashPattern = ReadOnlySpan<float>.Empty;
    }

    /// <summary>Board geometry for the current element size, letterboxed to a 15:10 aspect.</summary>
    private bool TryGetMetrics(out float cell, out Vector2 origin)
    {
        Rect rect = board.contentRect;
        cell = 0f;
        origin = Vector2.zero;
        if (rect.width <= 1f || rect.height <= 1f)
            return false;

        cell = Mathf.Min(rect.width / GridSystem.ColumnCount, rect.height / GridSystem.RowCount);
        origin = new Vector2(
            (rect.width - cell * GridSystem.ColumnCount) * 0.5f,
            (rect.height - cell * GridSystem.RowCount) * 0.5f
        );
        return true;
    }

    /// <summary>Grid row 0 is the bottom of the board but the top of the element.</summary>
    private static Vector2 CellCentre(Vector2Int cell, float size, Vector2 origin)
    {
        return new Vector2(
            origin.x + (cell.x + 0.5f) * size,
            origin.y + (GridSystem.RowCount - 1 - cell.y + 0.5f) * size
        );
    }

    private void Paint(MeshGenerationContext context)
    {
        if (!TryGetMetrics(out float cell, out Vector2 origin))
            return;

        Painter2D painter = context.painter2D;
        PaintGrid(painter, cell, origin);
        PaintHill(painter, cell, origin);
        PaintWalls(painter, cell, origin);

        if (round == null)
            return;

        // Enemy first so the local crew's routes read on top where they overlap.
        foreach (BattleReportEntry entry in round.entries)
        {
            if (entry.teamIndex != localTeamIndex)
                PaintEntry(painter, entry, cell, origin);
        }
        foreach (BattleReportEntry entry in round.entries)
        {
            if (entry.teamIndex == localTeamIndex)
                PaintEntry(painter, entry, cell, origin);
        }
    }

    private static void PaintGrid(Painter2D painter, float cell, Vector2 origin)
    {
        SetSolid(painter);
        painter.strokeColor = new Color(1f, 1f, 1f, 0.06f);
        painter.lineWidth = GridLineWidth;
        painter.BeginPath();

        for (int x = 0; x <= GridSystem.ColumnCount; x++)
        {
            float px = origin.x + x * cell;
            painter.MoveTo(new Vector2(px, origin.y));
            painter.LineTo(new Vector2(px, origin.y + GridSystem.RowCount * cell));
        }
        for (int y = 0; y <= GridSystem.RowCount; y++)
        {
            float py = origin.y + y * cell;
            painter.MoveTo(new Vector2(origin.x, py));
            painter.LineTo(new Vector2(origin.x + GridSystem.ColumnCount * cell, py));
        }

        painter.Stroke();
    }

    private void PaintHill(Painter2D painter, float cell, Vector2 origin)
    {
        if (report == null || report.gameMode != GameMode.KingOfTheHill)
            return;

        bool controlled =
            round != null && round.hillControllerTeamIndex != GameLoop.NoHillController;
        Color tint = controlled
            ? TeamPalette
                .ForViewer(round.hillControllerTeamIndex == localTeamIndex)
                .WithAlpha(0.22f)
            : TeamPalette.HillUnclaimed.WithAlpha(0.10f);

        painter.fillColor = tint;
        foreach (Vector2Int hillCell in GameLoop.KingOfTheHillCells)
            FillCell(painter, hillCell, cell, origin, 0.5f);
    }

    private static void PaintWalls(Painter2D painter, float cell, Vector2 origin)
    {
        painter.fillColor = new Color(0.10f, 0.086f, 0.106f, 1f);
        foreach (Vector2Int wall in GameLoop.wallLayout)
            FillCell(painter, wall, cell, origin, 0.42f);
    }

    private static void FillCell(
        Painter2D painter,
        Vector2Int cell,
        float size,
        Vector2 origin,
        float halfExtent
    )
    {
        Vector2 centre = CellCentre(cell, size, origin);
        float half = size * halfExtent;
        painter.BeginPath();
        painter.MoveTo(new Vector2(centre.x - half, centre.y - half));
        painter.LineTo(new Vector2(centre.x + half, centre.y - half));
        painter.LineTo(new Vector2(centre.x + half, centre.y + half));
        painter.LineTo(new Vector2(centre.x - half, centre.y + half));
        painter.ClosePath();
        painter.Fill();
    }

    private void PaintEntry(
        Painter2D painter,
        BattleReportEntry entry,
        float cell,
        Vector2 origin
    )
    {
        if (entry.order == BattleReportOrder.Eliminated)
            return;

        bool friendly = entry.teamIndex == localTeamIndex;
        Color team = TeamPalette.ForViewer(friendly);
        Color bright = TeamPalette.BrightForViewer(friendly);
        Vector2 start = CellCentre(entry.startCell, cell, origin);

        if (entry.order == BattleReportOrder.Ability && entry.hasAbilityTarget)
            PaintAbility(painter, entry, cell, origin, start);
        else if (entry.path != null && entry.path.Count >= 2)
        {
            // A dodge was not the plan — the dashed stroke is what separates it from a route the
            // player actually drew during planning.
            PaintRoute(
                painter,
                entry.path,
                cell,
                origin,
                bright,
                dashed: entry.order == BattleReportOrder.Dodge
            );
        }

        painter.fillColor = team;
        painter.BeginPath();
        painter.Arc(start, cell * StartMarkerRadius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.Fill();

        Vector2Int finalCell =
            entry.path != null && entry.path.Count >= 2 ? entry.path[^1] : entry.endCell;
        if (finalCell != entry.startCell)
        {
            painter.fillColor = bright;
            FillCell(painter, finalCell, cell, origin, EndMarkerHalfSize);
        }

        if (entry.diedThisRound)
            PaintElimination(painter, CellCentre(finalCell, cell, origin), cell);
    }

    private static void PaintAbility(
        Painter2D painter,
        BattleReportEntry entry,
        float cell,
        Vector2 origin,
        Vector2 start
    )
    {
        Vector2 target = CellCentre(entry.abilityTarget, cell, origin);

        painter.strokeColor = TeamPalette.AbilityTelegraph;
        painter.lineWidth = cell * PathWidth * 0.8f;
        painter.lineCap = LineCap.Round;
        painter.SetDashPattern(cell * 0.16f, cell * 0.12f);
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(target);
        painter.Stroke();

        SetSolid(painter);
        painter.strokeColor = TeamPalette.AbilityTelegraph;
        painter.lineWidth = cell * 0.09f;
        painter.BeginPath();
        painter.Arc(target, cell * AbilityRingRadius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.Stroke();
    }

    private static void PaintRoute(
        Painter2D painter,
        List<Vector2Int> path,
        float cell,
        Vector2 origin,
        Color color,
        bool dashed
    )
    {
        painter.strokeColor = color;
        painter.lineWidth = cell * PathWidth;
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;
        if (dashed)
            painter.SetDashPattern(cell * 0.18f, cell * 0.14f);
        else
            SetSolid(painter);

        painter.BeginPath();
        painter.MoveTo(CellCentre(path[0], cell, origin));
        for (int i = 1; i < path.Count; i++)
            painter.LineTo(CellCentre(path[i], cell, origin));
        painter.Stroke();

        if (dashed)
            SetSolid(painter);
    }

    private static void PaintElimination(Painter2D painter, Vector2 centre, float cell)
    {
        SetSolid(painter);
        float arm = cell * 0.26f;
        painter.strokeColor = TeamPalette.HealthCritical;
        painter.lineWidth = cell * 0.11f;
        painter.lineCap = LineCap.Round;
        painter.BeginPath();
        painter.MoveTo(new Vector2(centre.x - arm, centre.y - arm));
        painter.LineTo(new Vector2(centre.x + arm, centre.y + arm));
        painter.MoveTo(new Vector2(centre.x + arm, centre.y - arm));
        painter.LineTo(new Vector2(centre.x - arm, centre.y + arm));
        painter.Stroke();
    }

    // === roster-slot badges ===
    // Painter2D has no text, and without a number on each start marker there is no way to tell
    // which route in the picture belongs to which row in the order list beside it.

    private void RebuildBadges()
    {
        int needed = round?.entries.Count ?? 0;

        while (slotBadges.Count < needed)
        {
            Label badge = new() { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("report-board__badge");
            slotBadges.Add(badge);
            board.Add(badge);
        }

        for (int i = 0; i < slotBadges.Count; i++)
        {
            bool used = round != null && i < round.entries.Count;
            BattleReportEntry entry = used ? round.entries[i] : default;
            bool visible = used && entry.order != BattleReportOrder.Eliminated;

            slotBadges[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                continue;

            slotBadges[i].text = (entry.rosterSlot + 1).ToString();
            slotBadges[i].EnableInClassList(
                "report-board__badge--enemy",
                entry.teamIndex != localTeamIndex
            );
        }

        LayoutBadges();
    }

    private void LayoutBadges()
    {
        if (round == null || !TryGetMetrics(out float cell, out Vector2 origin))
            return;

        float size = Mathf.Max(12f, cell * 0.52f);
        for (int i = 0; i < slotBadges.Count && i < round.entries.Count; i++)
        {
            BattleReportEntry entry = round.entries[i];
            if (entry.order == BattleReportOrder.Eliminated)
                continue;

            Vector2 centre = CellCentre(entry.startCell, cell, origin);
            Label badge = slotBadges[i];
            badge.style.width = size;
            badge.style.height = size;
            badge.style.fontSize = size * 0.62f;
            badge.style.left = centre.x - size * 0.5f;
            badge.style.top = centre.y - size * 0.5f;
        }
    }
}
