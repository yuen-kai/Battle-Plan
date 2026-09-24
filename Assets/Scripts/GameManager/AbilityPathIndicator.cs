using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the path an ability travels — the Ramrod's rush across the board, the Soldier's
/// grenade arc, the Pogo Rider's jump — from points the ability itself sampled. Ground runs lie
/// flat and end in the same chevron a planned route does, so they read as movement; lobs are drawn
/// through the air over a faint track on the board, which is what tells you where an arc that has
/// left the ground is going to come back down.
/// <para>
/// The caller owns the host object and picks the colour, so one indicator serves the private
/// planning preview and any public telegraph without knowing which ability it is drawing.
/// </para>
/// </summary>
public class AbilityPathIndicator : MonoBehaviour
{
    private const float PathWidthCells = 0.085f;
    private const float GroundTrackWidthCells = 0.035f;
    private const float GroundTrackAlphaScale = 0.4f;
    private const float ChevronWidthScale = 0.8f;

    private LineRenderer path;
    private LineRenderer chevronLine;
    private LineRenderer groundTrack;
    private Material material;
    private readonly Vector3[] chevron = new Vector3[3];

    public static AbilityPathIndicator Create(Transform parent, string name)
    {
        GameObject host = new(name);
        host.transform.SetParent(parent, false);
        // Line positions are world space, so this rotation only orients the flat-aligned lines,
        // laying them against the board instead of billboarding them toward the camera.
        host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));

        AbilityPathIndicator indicator = host.AddComponent<AbilityPathIndicator>();
        indicator.Initialize();
        return indicator;
    }

    private void Initialize()
    {
        material = new Material(Shader.Find("Sprites/Default"))
        {
            name = "Ability Path (Runtime)",
            // Same reasoning as planned routes: the move-range tile is transparent at queue 3000
            // with its depth test disabled, so anything sharing that queue gets cut into by it.
            renderQueue = PlanPathStyle.RouteRenderQueue,
        };

        path = CreateLine("Path");
        path.widthCurve = AnimationCurve.Linear(0f, PlanPathStyle.StartWidthScale, 1f, 1f);
        path.numCornerVertices = 4;
        path.numCapVertices = 4;

        chevronLine = CreateLine("Chevron");
        chevronLine.numCornerVertices = 2;
        chevronLine.numCapVertices = 2;

        groundTrack = CreateLine("GroundTrack");

        Clear();
    }

    private LineRenderer CreateLine(string name)
    {
        GameObject host = new(name);
        host.transform.SetParent(transform, false);

        LineRenderer line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        line.positionCount = 0;
        return line;
    }

    /// <summary>
    /// Redraws for a path an ability sampled. A ground run is lifted onto the preview plane, so an
    /// ability is free to report its route at board level; a lob keeps every height it was sampled
    /// at, since that curve is the entire reason for drawing it.
    /// </summary>
    public void Show(IReadOnlyList<Vector3> sampled, AbilityPathKind kind, Color color)
    {
        Clear();
        int count = sampled?.Count ?? 0;
        if (kind == AbilityPathKind.None || count < 2)
            return;

        bool grounded = kind != AbilityPathKind.Lob;
        // An arc leaves the board, so it has to face the camera to keep its shape from collapsing
        // as the view turns. A run stays flat against the board like a route does.
        path.alignment = grounded ? LineAlignment.TransformZ : LineAlignment.View;
        path.startColor = path.endColor = color;
        path.widthMultiplier = PathWidthCells * GameLoop.cellSize;
        path.positionCount = count;
        for (int i = 0; i < count; i++)
            path.SetPosition(i, grounded ? OnPreviewPlane(sampled[i]) : sampled[i]);

        if (grounded)
            ShowChevron(sampled[count - 1], sampled[count - 1] - sampled[count - 2], color);
        else
            ShowGroundTrack(sampled[0], sampled[count - 1], color);
    }

    /// <summary>Caps a run the way a planned route is capped, pointing where the unit ends up.</summary>
    private void ShowChevron(Vector3 tip, Vector3 approach, Color color)
    {
        if (!PlanPathStyle.TryBuildEndChevron(OnPreviewPlane(tip), approach, chevron))
            return;

        chevronLine.startColor = chevronLine.endColor = color;
        chevronLine.widthMultiplier = PathWidthCells * GameLoop.cellSize * ChevronWidthScale;
        chevronLine.positionCount = chevron.Length;
        chevronLine.SetPositions(chevron);
    }

    /// <summary>
    /// The board track beneath a lob. Under this camera an arc drawn on its own gives no reliable
    /// read on which cells it passes over, so the throw is anchored to the board as well.
    /// </summary>
    private void ShowGroundTrack(Vector3 from, Vector3 to, Color color)
    {
        color.a *= GroundTrackAlphaScale;
        groundTrack.startColor = groundTrack.endColor = color;
        groundTrack.widthMultiplier = GroundTrackWidthCells * GameLoop.cellSize;
        groundTrack.positionCount = 2;
        groundTrack.SetPosition(0, OnPreviewPlane(from));
        groundTrack.SetPosition(1, OnPreviewPlane(to));
    }

    private static Vector3 OnPreviewPlane(Vector3 point)
    {
        point.y = PlanPathStyle.AbilityPathHeight;
        return point;
    }

    public void Clear()
    {
        path.positionCount = 0;
        chevronLine.positionCount = 0;
        groundTrack.positionCount = 0;
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
