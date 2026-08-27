using NUnit.Framework;
using UnityEditor;

[TestFixture]
public class FrameRatePolicyEditModeTests
{
    private const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

    [Test]
    public void HighRefreshDisplaysArePresentedEverySecondVblank()
    {
        Assert.That(FrameRatePolicy.ResolveVSyncCount(120d), Is.EqualTo(2));
        Assert.That(FrameRatePolicy.ResolveVSyncCount(144d), Is.EqualTo(2));
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(90d),
            Is.EqualTo(2),
            "90Hz still halves to 45, which is closer to the target than 90 is."
        );
    }

    [Test]
    public void OrdinaryDisplaysAreLeftAlone()
    {
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(60d),
            Is.EqualTo(1),
            "Halving 60 would land at 30 and the board would read as stepped."
        );
        Assert.That(FrameRatePolicy.ResolveVSyncCount(75d), Is.EqualTo(1));
        Assert.That(FrameRatePolicy.ResolveVSyncCount(0d), Is.EqualTo(1));
    }

    [Test]
    public void TheIdleRateIsPresentedEverySecondVblankOnAnOrdinaryDisplay()
    {
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(60d, FrameRatePolicy.IdleFrameRate),
            Is.EqualTo(2),
            "60Hz halved is the 30 a still board is paced at."
        );
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(120d, FrameRatePolicy.IdleFrameRate),
            Is.EqualTo(4)
        );
    }

    [Test]
    public void TheVSyncIntervalIsNeverZeroOrPastWhatUnityAccepts()
    {
        Assert.That(FrameRatePolicy.ResolveVSyncCount(0d, 30), Is.EqualTo(1));
        Assert.That(FrameRatePolicy.ResolveVSyncCount(60d, 0), Is.EqualTo(1));
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(240d, 10),
            Is.EqualTo(4),
            "Unity takes 0 through 4 and nothing else."
        );
        Assert.That(
            FrameRatePolicy.ResolveVSyncCount(60d, 240),
            Is.EqualTo(1),
            "A target above the refresh rate cannot ask for a fraction of a vblank."
        );
    }

    [Test]
    public void OnlyThePhasesWithSomethingMovingInThemGetEveryFrame()
    {
        Assert.That(FrameRatePolicy.PhaseAnimates("executing"), Is.True);
        Assert.That(FrameRatePolicy.PhaseAnimates("dodging"), Is.True);
        Assert.That(FrameRatePolicy.PhaseAnimates("planning"), Is.False);
        Assert.That(FrameRatePolicy.PhaseAnimates("idle"), Is.False);
        Assert.That(FrameRatePolicy.PhaseAnimates("waiting"), Is.False);
    }

    [Test]
    public void AnUnrecognisedPhaseIsTreatedAsStill()
    {
        Assert.That(
            FrameRatePolicy.PhaseAnimates("a phase nobody has written yet"),
            Is.False,
            "Guessing still costs a new phase half its frames; guessing moving costs every phase the point of this."
        );
        Assert.That(FrameRatePolicy.PhaseAnimates(null), Is.False);
    }

    [Test]
    public void AStillBoardNobodyIsTouchingGetsTheIdleRate()
    {
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, false, "planning", 5f),
            Is.EqualTo(FrameRatePolicy.IdleFrameRate)
        );
    }

    [Test]
    public void AGestureHoldsTheFullRateAcrossTheGapsBetweenEvents()
    {
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, false, "planning", 0f),
            Is.EqualTo(FrameRatePolicy.FocusedFrameRate)
        );
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, false, "planning", 0.5f),
            Is.EqualTo(FrameRatePolicy.FocusedFrameRate),
            "Half a second without a mouse event is still the middle of a drag."
        );
    }

    [Test]
    public void AMovingBoardGetsTheFullRateWithNobodyTouchingIt()
    {
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, false, "executing", 60f),
            Is.EqualTo(FrameRatePolicy.FocusedFrameRate)
        );
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, false, "dodging", 60f),
            Is.EqualTo(FrameRatePolicy.FocusedFrameRate)
        );
    }

    [Test]
    public void BehindSomethingElseBeatsEveryOtherReasonToDrawFast()
    {
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(false, false, "executing", 0f),
            Is.EqualTo(FrameRatePolicy.UnfocusedFrameRate)
        );
    }

    [Test]
    public void TheDevToolsHoldOnTheFullRateThroughAStillBoard()
    {
        Assert.That(
            FrameRatePolicy.ResolveTargetFrameRate(true, true, "planning", 60f),
            Is.EqualTo(FrameRatePolicy.FocusedFrameRate),
            "A screenshot of a frame that was never drawn is the previous frame."
        );
    }

    [Test]
    public void IdleIsSlowerThanFocusedAndFasterThanUnfocused()
    {
        Assert.That(FrameRatePolicy.IdleFrameRate, Is.LessThan(FrameRatePolicy.FocusedFrameRate));
        Assert.That(
            FrameRatePolicy.IdleFrameRate,
            Is.GreaterThan(FrameRatePolicy.UnfocusedFrameRate)
        );
    }

    [Test]
    public void RenderFrameIntervalLandsNearTheTargetRate()
    {
        Assert.That(
            FrameRatePolicy.ResolveRenderFrameInterval(120d, FrameRatePolicy.UnfocusedFrameRate),
            Is.EqualTo(12)
        );
        Assert.That(
            FrameRatePolicy.ResolveRenderFrameInterval(60d, FrameRatePolicy.UnfocusedFrameRate),
            Is.EqualTo(6)
        );
        Assert.That(
            FrameRatePolicy.ResolveRenderFrameInterval(60d, FrameRatePolicy.FocusedFrameRate),
            Is.EqualTo(1)
        );
    }

    [Test]
    public void RenderFrameIntervalIsNeverZeroWhateverItIsAsked()
    {
        Assert.That(FrameRatePolicy.ResolveRenderFrameInterval(0d, 60), Is.EqualTo(1));
        Assert.That(FrameRatePolicy.ResolveRenderFrameInterval(60d, 0), Is.EqualTo(1));
        Assert.That(FrameRatePolicy.ResolveRenderFrameInterval(-1d, -1), Is.EqualTo(1));
        Assert.That(
            FrameRatePolicy.ResolveRenderFrameInterval(30d, 240),
            Is.EqualTo(1),
            "A target above the refresh rate cannot ask for a fraction of a frame."
        );
    }

    [Test]
    public void UnfocusedIsSlowerThanFocused()
    {
        Assert.That(
            FrameRatePolicy.UnfocusedFrameRate,
            Is.LessThan(FrameRatePolicy.FocusedFrameRate)
        );
        Assert.That(
            FrameRatePolicy.UnfocusedFrameRate,
            Is.GreaterThan(0),
            "A backgrounded client still has a match running on it and must keep answering."
        );
    }

    /// <summary>
    /// The policy overrides this at runtime, but the level is what applies before it loads — and a
    /// quality level that uncaps the frame rate is the setting a player reaches for when their
    /// laptop is already hot.
    /// </summary>
    [Test]
    public void NoQualityLevelShipsWithVSyncOff()
    {
        string[] lines = System.IO.File.ReadAllLines(QualitySettingsPath);
        string levelName = null;
        int checkedLevels = 0;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("name: "))
                levelName = trimmed["name: ".Length..];
            if (!trimmed.StartsWith("vSyncCount: "))
                continue;

            checkedLevels++;
            Assert.That(
                trimmed,
                Is.Not.EqualTo("vSyncCount: 0"),
                $"Quality level '{levelName}' presents without waiting for a vblank."
            );
        }

        Assert.That(checkedLevels, Is.GreaterThan(0), $"Read no levels from {QualitySettingsPath}.");
    }
}
