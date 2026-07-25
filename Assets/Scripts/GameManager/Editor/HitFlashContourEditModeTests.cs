using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The tripwire under <see cref="HitFlash.IsContourValue"/>, which decides whether a renderer is ink
/// linework that must hold at rest or body that lifts toward paper during an impact frame.
///
/// WHY THIS EXISTS, AND WHAT IT ALREADY CAUGHT. The predicate compares a material's authored
/// <c>_BaseColor</c> against a linear luminance ceiling, which only works if the colour is converted
/// on the way in. <c>Material.GetColor</c> returns the sRGB value serialised in the <c>.mat</c> —
/// <c>Char_Black</c> comes back as <c>(0.137, 0.153, 0.180)</c>, the literal #23272E, luminance
/// 0.1516 — while a property-block <c>SetColor</c> reaches the shader as linear. The first version of
/// the predicate assumed both ends shared one space and compared the gamma measurement straight
/// against the linear ceiling, so it matched NOTHING, including the one material it exists to catch.
///
/// That failure is silent. Hit flashes still fired, still stayed in gamut, still cleared their
/// property blocks, and still looked like a plausible impact frame — while lifting the ink contour
/// along with the body it outlines. On a frozen frame the puck rim went 0.1640 to 0.7448 and
/// rim-to-puck contrast collapsed from 2.082:1 to 1.086:1, worse than drawing no flash at all. That
/// is the whole justification for this effect brightening rather than darkening, inverted, with no
/// symptom anywhere else.
///
/// So the assertions are not range checks on a colour. They check that the predicate still separates
/// the two materials it has to separate, calling the real <see cref="HitFlash.IsContourValue"/> and
/// the real <see cref="HitFlash.AuthoredAlbedoToLinear"/> rather than reimplementing either — a test
/// that rebuilt the weighting, the ceiling, or the space step could pass while the predicate drifted
/// out from under it. Every message reports the raw and converted readings side by side, because
/// having both is what made the original cause obvious on sight instead of an investigation.
///
/// Edit-mode only, and in this folder rather than the UI test assembly for the same reason as the
/// portrait fixture: that asmdef has no references and cannot see <see cref="HitFlash"/>.
/// </summary>
[TestFixture]
[Category("HitFlashContour")]
public class HitFlashContourEditModeTests
{
    /// <summary>
    /// The material on <c>BasePuckRim</c>, and the ink linework across every unit model — twelve
    /// renderers on the Sniper, ten on the Shotgunner. #23272E: linear luminance 0.0201, and 0.1516
    /// if read without converting.
    /// </summary>
    private const string ContourMaterialPath = "Assets/Materials/Characters/Char_Black.mat";

    /// <summary>
    /// The darkest material on the far side of the ceiling, and so the one that decides whether the
    /// gap is real. #2C3849: linear luminance 0.0384. If this ever tested as contour the flash would
    /// stop reaching most of the body.
    /// </summary>
    private const string DarkestBodyMaterialPath = "Assets/Materials/Characters/Char_CoatNavy.mat";

    [Test]
    public void InkLinework_TestsAsContour_AndSoHoldsThroughAFlash()
    {
        Material contour = Load(ContourMaterialPath);

        Assert.That(
            HitFlash.IsContourValue(contour.GetColor("_BaseColor")),
            Is.True,
            "Char_Black no longer tests as contour, so HitFlash's contour predicate now matches "
                + "NOTHING and every hit flash lifts the ink linework along with the body it outlines. "
                + "The puck rim goes from 0.164 to about 0.74 and its contrast against the puck "
                + "collapses to roughly 1.09:1, against 2.08:1 at rest — worse than drawing no flash, "
                + "on the exact frame the contour is doing the most work. The flash will still look "
                + "plausible and nothing else will report this.\n"
                + Reading(contour)
                + "\nIf the converted reading is near 0.15 rather than 0.02, the sRGB-to-linear step in "
                + "HitFlash.AuthoredAlbedoToLinear has been removed or bypassed. Restore the "
                + "conversion; do not move the ceiling, which is canonical in linear because the "
                + "palette states the whole gap that way."
        );
    }

    [Test]
    public void DarkestBodyMaterial_TestsAsBody_AndSoStillLifts()
    {
        Material body = Load(DarkestBodyMaterialPath);

        Assert.That(
            HitFlash.IsContourValue(body.GetColor("_BaseColor")),
            Is.False,
            "Char_CoatNavy now tests as contour, so the ceiling has swallowed the darkest body "
                + "material and the impact frame no longer reaches the coat, the gunmetal, or anything "
                + "else in that value band — the victim flashes in patches.\n"
                + Reading(body)
                + "\nThe ceiling sits in a gap between 0.0201 (Char_Black, contour) and 0.0384 "
                + "(Char_CoatNavy, body). If a new material has been authored into that gap it needs a "
                + "value on one side of it, not a wider ceiling."
        );
    }

    /// <summary>
    /// Stated separately from the two predicate checks because it is the thing that actually has to
    /// stay true: the two materials must sit on opposite sides of the ceiling with room to spare. A
    /// ceiling that drifted to touch either one would still pass both checks above on the day it
    /// moved, and fail on the next material anyone authored.
    /// </summary>
    [Test]
    public void Ceiling_SitsInTheGapWithMarginOnBothSides()
    {
        float contour = Luminance(Load(ContourMaterialPath));
        float body = Luminance(Load(DarkestBodyMaterialPath));

        // A quarter of the gap at each end. Tighter than that and the next authored material lands
        // on the boundary rather than on one side of it.
        float margin = (body - contour) * 0.25f;

        Assert.That(
            HitFlash.ContourLuminanceCeiling,
            Is.GreaterThan(contour + margin).And.LessThan(body - margin),
            $"HitFlash's contour ceiling of {HitFlash.ContourLuminanceCeiling:F4} has drifted to the "
                + $"edge of the gap it has to sit in: Char_Black measures {contour:F5} and "
                + $"Char_CoatNavy {body:F5}, so the legal band is {contour + margin:F4} to "
                + $"{body - margin:F4}. It still separates those two today, but not with room for the "
                + "next material authored anywhere near that band, and the failure when that happens is "
                + "a hit flash that either eats the ink linework or skips part of the body — neither of "
                + "which reports itself."
        );
    }

    /// <summary>Both readings of a material's authored albedo, which is the pair that localises a
    /// colour-space regression immediately rather than after an investigation.</summary>
    private static string Reading(Material material)
    {
        Color authored = material.GetColor("_BaseColor");
        return $"  GetColor returned {authored} (luminance "
            + $"{HitFlash.LinearLuminance(authored):F4} unconverted), which converts to "
            + $"{HitFlash.LinearLuminance(HitFlash.AuthoredAlbedoToLinear(authored)):F5} linear, "
            + $"against a ceiling of {HitFlash.ContourLuminanceCeiling:F4}.";
    }

    /// <summary>The luminance the predicate itself works in, via the same two helpers it uses.</summary>
    private static float Luminance(Material material) =>
        HitFlash.LinearLuminance(HitFlash.AuthoredAlbedoToLinear(material.GetColor("_BaseColor")));

    private static Material Load(string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        // Unity's fake-null: a missing material deserialises to a live wrapper, which Is.Not.Null
        // accepts and GetColor then throws on.
        Assert.That(
            material == null,
            Is.False,
            $"No material could be loaded from {path}. HitFlash's contour predicate is keyed on the "
                + "palette value this file holds, so if it has moved the tripwire guarding that "
                + "predicate is no longer guarding anything — repoint this path, do not delete the test."
        );
        Assert.That(
            material.HasProperty("_BaseColor"),
            Is.True,
            $"{path} has no _BaseColor. HitFlash only considers renderers whose material carries that "
                + "property, so a shader change here silently removes this material from the flash "
                + "entirely, contour or not."
        );
        return material;
    }
}
