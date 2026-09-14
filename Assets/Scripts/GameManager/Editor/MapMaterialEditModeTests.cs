using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The arena is painted in flat token colours, and the wall cap was the one surface that had
/// drifted off that rule: it kept a brushed-steel albedo from an abandoned texture pass, tiled
/// (2, 1) across a cube scaled 2.75 x 0.12 x 2.75, which striped the whole cover kit. These
/// assertions are on the shipped assets rather than on ArenaBuilder's table, because the striping
/// came from the asset disagreeing with the table.
/// </summary>
[TestFixture]
public class MapMaterialEditModeTests
{
    private static readonly string[] PaletteMaterials =
    {
        "Map_DeckA",
        "Map_DeckB",
        "Map_GridLine",
        "Map_Cover",
        "Map_WallCap",
        "Map_CoverPlate",
        "Map_Backdrop",
        "Map_Void",
    };

    [TestCaseSource(nameof(PaletteMaterials))]
    public void PaletteMaterialsArePaintedNotTextured(string materialName)
    {
        Material material = Load(materialName);

        Assert.That(
            material.GetTexture("_BaseMap"),
            Is.Null,
            $"{materialName} carries an albedo map; the arena is flat colour."
        );
        Assert.That(
            material.GetTextureScale("_BaseMap"),
            Is.EqualTo(Vector2.one),
            $"{materialName} has stale tiling that would stretch any future map."
        );
    }

    [TestCaseSource(nameof(PaletteMaterials))]
    public void PaletteMaterialsAreNeitherMetallicNorGlossy(string materialName)
    {
        Material material = Load(materialName);

        Assert.That(
            material.GetFloat("_Metallic"),
            Is.EqualTo(0f),
            $"{materialName} is metallic; on a bright board a highlight reads as an effect."
        );
        Assert.That(
            material.GetFloat("_Smoothness"),
            Is.LessThanOrEqualTo(0.18f),
            $"{materialName} is glossy enough to sparkle under the key."
        );
    }

    /// <summary>
    /// The cap is the brightest of the four steps from deck to shaded face, which is what makes a
    /// 2.0 m blocker read as solid from a 73-degree camera. A re-grade that darkened it below the
    /// deck would take that away silently.
    /// </summary>
    [Test]
    public void WallCapIsBrighterThanTheCoverBodyAndTheDeck()
    {
        float cap = Luminance(Load("Map_WallCap").GetColor("_BaseColor"));
        float body = Luminance(Load("Map_Cover").GetColor("_BaseColor"));
        float deck = Luminance(Load("Map_DeckA").GetColor("_BaseColor"));

        Assert.That(cap, Is.GreaterThan(deck));
        Assert.That(deck, Is.GreaterThan(body));
    }

    private static Material Load(string materialName)
    {
        string path = $"Assets/Materials/Map/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Assert.That(material, Is.Not.Null, $"Could not load {path}.");
        return material;
    }

    private static float Luminance(Color color) =>
        0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
}
