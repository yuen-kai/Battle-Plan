using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class MobileDisplayEditModeTests
{
    [Test]
    public void PanelReferenceResolution_LeavesDesktopOnItsAuthoredReference()
    {
        Vector2Int authored = new(1920, 1080);

        Vector2Int reference = MobileDisplay.PanelReferenceResolution(
            DeviceType.Desktop,
            new Vector2Int(2560, 1440),
            109f,
            authored
        );

        Assert.That(reference, Is.EqualTo(authored));
    }

    [Test]
    public void PanelReferenceResolution_MeasuresAHandheldInDensityIndependentUnits()
    {
        Vector2Int reference = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(2340, 1080),
            400f,
            new Vector2Int(1920, 1080)
        );

        Assert.That(reference, Is.EqualTo(new Vector2Int(936, 432)));
    }

    /// <summary>
    /// The phone-sized panel is the whole point: the 44px touch targets and 184px cards the phone
    /// breakpoint authors only come out at their intended physical size once the panel is small
    /// enough for that breakpoint to engage at all.
    /// </summary>
    [Test]
    public void PanelReferenceResolution_PutsAPhoneInsideThePhoneBreakpoint()
    {
        Vector2Int reference = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(2556, 1179),
            460f,
            new Vector2Int(1920, 1080)
        );

        Assert.That(reference.x, Is.LessThan(1120));
        Assert.That(reference.y, Is.LessThan(960));
    }

    [Test]
    public void PanelReferenceResolution_KeepsATabletRoomierThanAPhone()
    {
        Vector2Int phone = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(2556, 1179),
            460f,
            new Vector2Int(1920, 1080)
        );
        Vector2Int tablet = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(2732, 2048),
            264f,
            new Vector2Int(1920, 1080)
        );

        Assert.That(tablet.x, Is.GreaterThan(phone.x));
    }

    /// <summary>
    /// A mobile browser hands Unity CSS pixels, which are already density-independent, and often
    /// no density at all. Scaling those by a phone's density factor would shrink the panel to a
    /// third of anything the phone breakpoint was drawn for, so the clamp has to catch it.
    /// </summary>
    [Test]
    public void PanelReferenceResolution_KeepsAMobileBrowserInsideTheAuthoredLayout()
    {
        Vector2Int reference = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(852, 393),
            0f,
            new Vector2Int(1920, 1080)
        );

        Assert.That(reference.x, Is.EqualTo(Mathf.RoundToInt(MobileDisplay.MinHandheldPanelWidth)));
        Assert.That(
            reference.y / (float)reference.x,
            Is.EqualTo(393f / 852f).Within(0.01f),
            "the reference has to stay proportional to the screen or the two axes scale apart"
        );
    }

    [Test]
    public void PanelReferenceResolution_NeverGoesWiderThanTheAuthoredReference()
    {
        Vector2Int authored = new(1920, 1080);

        Vector2Int reference = MobileDisplay.PanelReferenceResolution(
            DeviceType.Handheld,
            new Vector2Int(2560, 1600),
            120f,
            authored
        );

        Assert.That(reference.x, Is.EqualTo(authored.x));
    }

    [Test]
    public void HandheldPanelScale_FallsBackWhenDensityIsNotReported()
    {
        Assert.That(
            MobileDisplay.HandheldPanelScale(0f),
            Is.EqualTo(MobileDisplay.FallbackHandheldScale)
        );
    }

    [Test]
    public void HandheldPanelScale_NeverShrinksUiBelowDesktopDensity()
    {
        Assert.That(
            MobileDisplay.HandheldPanelScale(72f),
            Is.EqualTo(MobileDisplay.MinHandheldScale)
        );
    }

    [Test]
    public void HandheldPanelScale_CapsAnImplausiblyHighDensity()
    {
        Assert.That(
            MobileDisplay.HandheldPanelScale(4000f),
            Is.EqualTo(MobileDisplay.MaxHandheldScale)
        );
    }

    [Test]
    public void SafeAreaInsetPixels_IsZeroOnADisplayWithoutACutout()
    {
        Vector4 insets = MobileDisplay.SafeAreaInsetPixels(
            new Rect(0f, 0f, 2340f, 1080f),
            new Vector2(2340f, 1080f)
        );

        Assert.That(insets, Is.EqualTo(Vector4.zero));
    }

    [Test]
    public void SafeAreaInsetPixels_ReadsEachEdgeFromTheTopLeft()
    {
        Vector4 insets = MobileDisplay.SafeAreaInsetPixels(
            new Rect(132f, 21f, 2340f - 132f - 12f, 1080f - 21f - 40f),
            new Vector2(2340f, 1080f)
        );

        Assert.That(insets.x, Is.EqualTo(132f).Within(0.001f), "left");
        Assert.That(insets.y, Is.EqualTo(40f).Within(0.001f), "top");
        Assert.That(insets.z, Is.EqualTo(12f).Within(0.001f), "right");
        Assert.That(insets.w, Is.EqualTo(21f).Within(0.001f), "bottom");
    }

    [Test]
    public void SafeAreaInsetPixels_IgnoresAnUnreportedSafeArea()
    {
        Vector4 insets = MobileDisplay.SafeAreaInsetPixels(
            new Rect(0f, 0f, 0f, 0f),
            new Vector2(2340f, 1080f)
        );

        Assert.That(insets, Is.EqualTo(Vector4.zero));
    }
}
