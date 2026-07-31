using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws one unit's planned route as a single lane-aware ribbon capped with a chevron on the cell
/// it ends on. Replaces the per-cell node and edge objects so overlapping plans stay readable, and
/// carries no colliders — route clicks are resolved from pointer position by
/// <see cref="PathSelection"/>.
/// </summary>
public class PathRibbon : MonoBehaviour
{
    private LineRenderer route;
    private LineRenderer destination;
    private Material ribbonMaterial;
    private int rosterSlot;
    private bool selected;
    private bool endBlocked;
    private readonly List<Vector3> lanePoints = new();
    private readonly List<LanePlacement> arrivalLanes = new();
    private readonly List<LanePlacement> departureLanes = new();
    private readonly List<int> vertexIndices = new();
    private readonly Vector3[] chevron = new Vector3[3];

    public static PathRibbon Create(Transform parent, string name, int rosterSlot)
    {
        GameObject host = new(name);
        host.transform.SetParent(parent, false);
        // LineRenderer positions are world space; TransformZ alignment then lays the ribbon flat
        // against the board instead of billboarding it toward the camera.
        host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));

        PathRibbon ribbon = host.AddComponent<PathRibbon>();
        ribbon.Initialize(rosterSlot);
        return ribbon;
    }

    private void Initialize(int slot)
    {
        rosterSlot = slot;
        ribbonMaterial = new Material(Shader.Find("Sprites/Default"))
        {
            name = $"Plan Route {slot} (Runtime)",
            // The move-range tile is transparent at queue 3000 with its depth test disabled, so it
            // paints over anything drawn before it. Sharing that queue leaves the two sorting by
            // object distance and the tiles cut into the route; queue after them instead.
            renderQueue = PlanPathStyle.RouteRenderQueue,
        };

        route = gameObject.AddComponent<LineRenderer>();
        ConfigureLine(route);
        route.widthCurve = AnimationCurve.Linear(0f, PlanPathStyle.StartWidthScale, 1f, 1f);
        route.numCornerVertices = 4;
        route.numCapVertices = 4;

        GameObject destinationHost = new("Destination");
        destinationHost.transform.SetParent(transform, false);
        destination = destinationHost.AddComponent<LineRenderer>();
        ConfigureLine(destination);
        destination.numCornerVertices = 2;
        destination.numCapVertices = 2;

        ApplyStyle();
        Clear();
    }

    private void ConfigureLine(LineRenderer line)
    {
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = ribbonMaterial;
        line.positionCount = 0;
    }

    public void SetSelected(bool value)
    {
        if (selected == value)
            return;

        selected = value;
        ApplyStyle();
    }

    /// <summary>
    /// Marks the route as resting on a square it may cross but not stop on. A route is allowed to
    /// reach this state mid-drag, so the cap that says where the unit ends up is the part that has
    /// to disown it — the player should be able to see the destination will not hold before
    /// letting go of it.
    /// </summary>
    public void SetEndBlocked(bool value)
    {
        if (endBlocked == value)
            return;

        endBlocked = value;
        ApplyStyle();
    }

    /// <summary>
    /// Redraws the ribbon for a plan expressed as cell-centre world positions. Each cell is looked
    /// up once per adjoining run, so a route only leaves the centre on the runs it actually shares.
    /// A plan with fewer than two cells means the unit is holding position, which draws nothing.
    /// </summary>
    public void SetRoute(IReadOnlyList<Vector3> planCells, RouteLaneMap laneMap)
    {
        arrivalLanes.Clear();
        departureLanes.Clear();
        int count = planCells?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            arrivalLanes.Add(
                i > 0
                    ? GetPlacement(laneMap, planCells, i, planCells[i - 1], planCells[i])
                    : new LanePlacement(0f, 1)
            );
            departureLanes.Add(
                i < count - 1
                    ? GetPlacement(laneMap, planCells, i, planCells[i], planCells[i + 1])
                    : new LanePlacement(0f, 1)
            );
        }

        PlanPathStyle.BuildLanePolyline(
            planCells,
            arrivalLanes,
            departureLanes,
            PlanPathStyle.RouteHeight,
            lanePoints,
            vertexIndices
        );

        route.positionCount = lanePoints.Count;
        for (int i = 0; i < lanePoints.Count; i++)
            route.SetPosition(i, lanePoints[i]);

        if (lanePoints.Count >= 2)
            UpdateDestinationMarker();
        else
            destination.positionCount = 0;
    }

    private LanePlacement GetPlacement(
        RouteLaneMap laneMap,
        IReadOnlyList<Vector3> planCells,
        int index,
        Vector3 stepFrom,
        Vector3 stepTo
    )
    {
        return laneMap == null
            ? new LanePlacement(0f, 1)
            : laneMap.GetPlacement(
                rosterSlot,
                planCells[index],
                PlanPathStyle.GetAxis(stepFrom, stepTo)
            );
    }

    public void Clear()
    {
        lanePoints.Clear();
        vertexIndices.Clear();
        route.positionCount = 0;
        destination.positionCount = 0;
    }

    /// <summary>
    /// The world position this ribbon actually drew for a plan index, so pointer picking can match
    /// what is on screen instead of the underlying cell centre.
    /// </summary>
    public bool TryGetDrawnPoint(int planIndex, out Vector3 point)
    {
        if (planIndex < 0 || planIndex >= vertexIndices.Count)
        {
            point = Vector3.zero;
            return false;
        }

        point = lanePoints[vertexIndices[planIndex]];
        return true;
    }

    /// <summary>
    /// Caps the route with a chevron pointing the way the unit arrives. Sitting on the route's own
    /// lane rather than on the cell centre keeps it clear of any other unit finishing on that cell.
    /// </summary>
    private void UpdateDestinationMarker()
    {
        Vector3 tip = lanePoints[lanePoints.Count - 1];
        Vector3 approach = tip - lanePoints[lanePoints.Count - 2];
        if (!PlanPathStyle.TryBuildEndChevron(tip, approach, chevron))
        {
            destination.positionCount = 0;
            return;
        }

        destination.positionCount = chevron.Length;
        destination.SetPositions(chevron);
    }

    private void ApplyStyle()
    {
        Color color = PlanPathStyle.GetRouteColor(rosterSlot, selected);
        float width = PlanPathStyle.GetRouteWidth(selected);
        Color capColor = endBlocked ? PlanPathStyle.GetBlockedEndColor(selected) : color;

        route.startColor = color;
        route.endColor = capColor;
        route.widthMultiplier = width;

        destination.startColor = capColor;
        destination.endColor = capColor;
        destination.widthMultiplier = width * 0.8f;
    }

    private void OnDestroy()
    {
        if (ribbonMaterial != null)
            Destroy(ribbonMaterial);
    }
}
