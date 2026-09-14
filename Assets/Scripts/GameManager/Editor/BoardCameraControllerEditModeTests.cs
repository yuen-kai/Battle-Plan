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
}
