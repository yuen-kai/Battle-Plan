using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class BoardCameraControllerEditModeTests
{
    [Test]
    public void ClampedDollyDistance_StopsAtNearHeight()
    {
        float distance = BoardCameraController.ClampedDollyDistance(12f, -0.8f, 10f, 10f, 38f);

        Assert.That(distance, Is.EqualTo(2.5f).Within(0.0001f));
    }

    [Test]
    public void ClampedDollyDistance_StopsAtFarHeight()
    {
        float distance = BoardCameraController.ClampedDollyDistance(36f, -0.8f, -10f, 10f, 38f);

        Assert.That(distance, Is.EqualTo(-2.5f).Within(0.0001f));
    }

    [Test]
    public void ClampedDollyDistance_HorizontalCameraCannotDollyByHeight()
    {
        float distance = BoardCameraController.ClampedDollyDistance(20f, 0f, 4f, 10f, 38f);

        Assert.That(distance, Is.Zero);
    }

    [Test]
    public void ClampFocusToBoard_AllowsOnePaddingBandAndStopsBeyondIt()
    {
        Rect board = new(0f, 0f, 40f, 24f);

        Vector2 clamped = BoardCameraController.ClampFocusToBoard(
            new Vector2(-20f, 50f),
            board,
            3f
        );

        Assert.That(clamped, Is.EqualTo(new Vector2(-3f, 27f)));
    }

    private static readonly Quaternion SeatPitch = new(0.59543306f, 0f, 0f, 0.8034049f);
    private static readonly Rect Footprint = new(-1.35f, -1.35f, 40.5f, 27f);

    private static Vector2[] ProjectCorners(Vector3 position, Quaternion rotation, float aspect)
    {
        float halfTan = Mathf.Tan(30f * Mathf.Deg2Rad);
        Vector2[] projected = new Vector2[4];
        int i = 0;
        foreach (float x in new[] { Footprint.xMin, Footprint.xMax })
        {
            foreach (float z in new[] { Footprint.yMin, Footprint.yMax })
            {
                Vector3 local = Quaternion.Inverse(rotation) * (new Vector3(x, 0f, z) - position);
                projected[i++] = new Vector2(
                    local.x / (local.z * halfTan * aspect),
                    local.y / (local.z * halfTan)
                );
            }
        }
        return projected;
    }

    [Test]
    public void FrameBoard_HoldsEveryCornerInsideTheMarginAndBalancesTopAgainstBottom()
    {
        Vector3 position = BoardCameraController.FrameBoard(Footprint, SeatPitch, 60f, 2.2f, 0.08f);
        Vector2[] corners = ProjectCorners(position, SeatPitch, 2.2f);

        float top = float.MinValue;
        float bottom = float.MaxValue;
        foreach (Vector2 corner in corners)
        {
            Assert.That(Mathf.Abs(corner.x), Is.LessThanOrEqualTo(0.92f + 1e-4f));
            Assert.That(Mathf.Abs(corner.y), Is.LessThanOrEqualTo(0.92f + 1e-4f));
            top = Mathf.Max(top, corner.y);
            bottom = Mathf.Min(bottom, corner.y);
        }

        Assert.That(top, Is.EqualTo(0.92f).Within(1e-3f));
        Assert.That(bottom, Is.EqualTo(-0.92f).Within(1e-3f));
    }

    [Test]
    public void FrameBoard_BacksOffWhenTheViewportIsTooNarrowForTheBoardsWidth()
    {
        Vector3 wide = BoardCameraController.FrameBoard(Footprint, SeatPitch, 60f, 2.2f, 0.08f);
        Vector3 narrow = BoardCameraController.FrameBoard(Footprint, SeatPitch, 60f, 1.3f, 0.08f);

        Assert.That(narrow.y, Is.GreaterThan(wide.y));
        foreach (Vector2 corner in ProjectCorners(narrow, SeatPitch, 1.3f))
            Assert.That(Mathf.Abs(corner.x), Is.LessThanOrEqualTo(0.92f + 1e-4f));
    }

    [Test]
    public void FrameBoard_FramesTheOpposingSeatFromItsOwnSide()
    {
        Quaternion opposingSeat = new(0f, 0.80340505f, -0.5954329f, 0f);
        Vector3 position = BoardCameraController.FrameBoard(Footprint, opposingSeat, 60f, 2.2f, 0.08f);

        Assert.That(position.x, Is.EqualTo(Footprint.center.x).Within(1e-3f));
        Assert.That(position.z, Is.GreaterThan(Footprint.center.y));
        foreach (Vector2 corner in ProjectCorners(position, opposingSeat, 2.2f))
            Assert.That(Mathf.Abs(corner.y), Is.LessThanOrEqualTo(0.92f + 1e-4f));
    }
}
