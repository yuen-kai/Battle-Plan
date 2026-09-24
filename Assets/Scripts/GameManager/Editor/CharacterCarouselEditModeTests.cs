using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class CharacterCarouselEditModeTests
{
    private const float Spacing = 5.5f;

    [Test]
    public void RadiusForCount_OneUnitSitsAtTheOrigin()
    {
        Assert.That(CharacterCarousel.RadiusForCount(1, Spacing), Is.Zero);
        Assert.That(CharacterCarousel.RadiusForCount(0, Spacing), Is.Zero);
    }

    [Test]
    public void RadiusForCount_HoldsTheNeighbourGap()
    {
        const int count = 8;
        float radius = CharacterCarousel.RadiusForCount(count, Spacing);
        float chord = 2f * radius * Mathf.Sin(Mathf.PI / count);

        Assert.That(chord, Is.EqualTo(Spacing).Within(0.0001f));
    }

    [Test]
    public void RadiusForCount_GrowsTheRingInsteadOfPackingTighter()
    {
        float two = CharacterCarousel.RadiusForCount(2, Spacing);
        float eight = CharacterCarousel.RadiusForCount(8, Spacing);
        float thirteen = CharacterCarousel.RadiusForCount(13, Spacing);

        Assert.That(eight, Is.GreaterThan(two));
        Assert.That(thirteen, Is.GreaterThan(eight));
        Assert.That(eight, Is.GreaterThan(4.7f));
    }
}
