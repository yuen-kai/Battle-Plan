using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How an ability travels to its target, which is what decides how a preview of it has to be
/// drawn: flat against the board for anything crossing the ground, up in the air for anything
/// thrown over it.
/// </summary>
public enum AbilityPathKind
{
    /// <summary>Nothing travels; the ability takes effect where it was aimed.</summary>
    None,

    /// <summary>A run across the board.</summary>
    Ground,

    /// <summary>A parabola through the air.</summary>
    Lob,
}

/// <summary>
/// The flight paths abilities follow. Execution and its preview both sample these, so the arc a
/// player is shown while planning is the one the grenade actually flies, rather than a look-alike
/// that drifts apart the first time a throw height is tuned.
/// </summary>
public static class AbilityTrajectory
{
    /// <summary>Segments a previewed lob is sampled into: enough that it reads as a curve.</summary>
    public const int LobSegments = 24;

    /// <summary>
    /// A lob <paramref name="progress"/> of the way through its flight: a straight line from
    /// <paramref name="start"/> to <paramref name="end"/> with a parabola laid over it, reaching
    /// <paramref name="apexHeight"/> above the halfway point.
    /// </summary>
    public static Vector3 SampleLob(Vector3 start, Vector3 end, float apexHeight, float progress)
    {
        Vector3 point = Vector3.Lerp(start, end, progress);
        point.y += apexHeight * 4f * progress * (1f - progress);
        return point;
    }

    /// <summary>
    /// Samples a whole lob into <paramref name="points"/>, both endpoints included. False when the
    /// throw covers no ground, which is what an ability aimed at its own caster amounts to.
    /// </summary>
    public static bool BuildLob(Vector3 start, Vector3 end, float apexHeight, List<Vector3> points)
    {
        points.Clear();
        if (!CoversGround(start, end))
            return false;

        for (int step = 0; step <= LobSegments; step++)
            points.Add(SampleLob(start, end, apexHeight, step / (float)LobSegments));
        return true;
    }

    /// <summary>
    /// A ground run as its two endpoints. Abilities that cross the board are carried straight to
    /// where they stop, so none of the cornering a walked route needs applies to them.
    /// </summary>
    public static bool BuildGroundRun(Vector3 start, Vector3 end, List<Vector3> points)
    {
        points.Clear();
        if (!CoversGround(start, end))
            return false;

        points.Add(start);
        points.Add(end);
        return true;
    }

    /// <summary>
    /// Whether two points are far enough apart across the board to draw a path between them.
    /// Height is ignored: a lob that goes straight up lands back where it started.
    /// </summary>
    private static bool CoversGround(Vector3 start, Vector3 end)
    {
        Vector3 travel = end - start;
        travel.y = 0f;
        return travel.sqrMagnitude > 1e-6f;
    }
}
