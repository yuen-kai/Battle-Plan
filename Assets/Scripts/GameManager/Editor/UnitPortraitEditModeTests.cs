using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The five unit portraits, asserted end to end: the UnitData holds a Sprite, that Sprite resolves
/// to the intended file, and the file imports at the size the card layout is drawn against.
///
/// WHY THIS EXISTS. The portraits rendered blank for a long stretch while every individual step in
/// the chain reported success. The capture wrote a PNG and said so, the .asset carried a reference
/// and said so, and the file was on disk the entire time. What was actually wrong was one importer
/// field: a texture imported as Default produces no Sprite sub-asset, so the reference resolves to
/// nothing and the card silently falls back to an empty background. Nothing logs. "The file exists"
/// was true throughout and never once implied a visible portrait.
///
/// So nothing here asserts that an asset is present. Every assertion is on a value the renderer
/// actually consumes, and every failure message names what it measured, because the failure mode
/// this guards against is a chain of true statements adding up to a blank card.
///
/// <c>Sprite.rect.size</c> is the load-bearing line. It fails when the texture type regresses to
/// Default, it fails under a power-of-two rescale, and it fails if a card is ever repointed at the
/// retired 512x512 <c>Portrait_*</c> art still sitting in the same folder — three separate
/// regressions, one measurement.
///
/// Edit-mode only, and deliberately in this folder rather than the UI test assembly: that asmdef
/// has no references and cannot see UnitData.
/// </summary>
[TestFixture]
[Category("UnitPortraits")]
public class UnitPortraitEditModeTests
{
    private const string StatsFolder = "Assets/UnitStats";
    private const string PortraitFolder = "Assets/Images/Portraits";

    /// <summary>
    /// The capture resolution, and the aspect the unit card and the roster tile are laid out for.
    /// Not square: the retired art was 512x512 and swapping one back in is a regression this size
    /// is here to catch.
    /// </summary>
    private static readonly Vector2 PortraitPixelSize = new(512f, 384f);

    private static readonly string[] UnitNames =
    {
        "Commander",
        "PogoRider",
        "Shotgunner",
        "Sniper",
        "Soldier",
    };

    [TestCaseSource(nameof(UnitNames))]
    public void UnitData_CarriesItsPortraitAsALoadableSprite(string unitName)
    {
        string dataPath = $"{StatsFolder}/{unitName}.asset";
        string portraitPath = $"{PortraitFolder}/{unitName}.png";

        UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(dataPath);
        AssertLive(data, $"No UnitData could be loaded from {dataPath}.");

        // A Default-type import yields nothing usable here even though the reference and the file
        // both exist.
        AssertLive(
            data.unitSprite,
            $"{unitName}.unitSprite holds no usable Sprite. The reference and the PNG can both be "
                + $"present and this still fail: check that {portraitPath} imports as a Sprite."
        );
        AssertLive(
            data.abilitySprite,
            $"{unitName}.abilitySprite holds no usable Sprite. Same cause as unitSprite — the card "
                + "falls back to an empty background without logging anything."
        );

        string resolved = AssetDatabase.GetAssetPath(data.unitSprite);
        Assert.That(
            resolved,
            Is.EqualTo(portraitPath),
            $"{unitName}.unitSprite resolves to '{resolved}', expected '{portraitPath}'."
        );
        Assert.That(
            resolved,
            Does.Not.Contain("Portrait_"),
            $"{unitName}.unitSprite points at the retired Portrait_ art ('{resolved}'). That set is "
                + "512x512 and predates the current capture."
        );

        Vector2 measured = data.unitSprite.rect.size;
        Assert.That(
            measured,
            Is.EqualTo(PortraitPixelSize),
            $"{unitName}.unitSprite measures {measured.x}x{measured.y}, expected "
                + $"{PortraitPixelSize.x}x{PortraitPixelSize.y}. A wrong size here means a sprite-type "
                + "regression, a non-power-of-two rescale, or the old square art."
        );
    }

    [TestCaseSource(nameof(UnitNames))]
    public void Portrait_ImportsAsAnUnrescaledSingleSprite(string unitName)
    {
        string portraitPath = $"{PortraitFolder}/{unitName}.png";

        TextureImporter importer = AssetImporter.GetAtPath(portraitPath) as TextureImporter;
        AssertLive(
            importer,
            $"No TextureImporter at {portraitPath}. Either the file is missing or it did not import "
                + "as a texture at all."
        );

        Assert.That(
            importer.textureType,
            Is.EqualTo(TextureImporterType.Sprite),
            $"{unitName}.png imports as {importer.textureType}, expected Sprite. Anything else "
                + "produces no Sprite sub-asset and UnitData.unitSprite deserialises to null."
        );
        Assert.That(
            importer.spriteImportMode,
            Is.EqualTo(SpriteImportMode.Single),
            $"{unitName}.png imports in {importer.spriteImportMode} sprite mode, expected Single. A "
                + "portrait is one sprite, not an atlas."
        );
        Assert.That(
            importer.npotScale,
            Is.EqualTo(TextureImporterNPOTScale.None),
            $"{unitName}.png has npotScale {importer.npotScale}, expected None. 512x384 is not a "
                + "power of two on both axes, so any rescale silently resamples the portrait."
        );
    }

    /// <summary>
    /// Null check that goes through Unity's <c>==</c> rather than NUnit's <c>Is.Not.Null</c>,
    /// because the two disagree on precisely the case this fixture exists for.
    ///
    /// A serialized Sprite reference whose sub-asset no longer exists — which is what a Default-type
    /// import leaves behind — does not deserialise to a managed null. It deserialises to a live
    /// wrapper with a dead native object, the one the Inspector draws as "Missing (Sprite)".
    /// <c>Is.Not.Null</c> passes that happily; every renderer in the project treats it as nothing.
    /// A guard written the obvious way would therefore be green for the whole duration of the bug
    /// it was added to catch.
    /// </summary>
    private static void AssertLive(UnityEngine.Object reference, string message)
    {
        Assert.That(reference == null, Is.False, message);
    }
}
