using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The preview's whole job is to be true about the board a player is about to be dropped onto, so
/// what it draws has to follow the mode. Two things did not: the hill pad was painted under every
/// mode including the two that have no hill, and both crews were drawn on their back ranks even in
/// Escort, where one of them deploys forward to guard its extraction zone.
///
/// Markers are found by sampling the cell centre and comparing against <see cref="TeamPalette"/>,
/// which is the same value the drawing fills them with, so these read the picture rather than
/// re-deriving it.
/// </summary>
public class MapPreviewImageEditModeTests
{
    private static readonly Color32 HillPad = TeamPalette.HillUnclaimed;

    private MapDefinition originalActiveMap;

    [SetUp]
    public void SetUp() => originalActiveMap = MapCatalog.Active;

    [TearDown]
    public void TearDown() => MapCatalog.SetActive(originalActiveMap);

    private static IEnumerable<GameMode> HillLessModes =>
        new[] { GameMode.Elimination, GameMode.EscortThePresident };

    [TestCaseSource(nameof(HillLessModes))]
    public void ModesWithoutAHillPaintNoPad(GameMode gameMode)
    {
        using PreviewPixels pixels = new(MapCatalog.Concourse, gameMode);

        Assert.That(
            pixels.CountMatching(HillPad),
            Is.Zero,
            $"{gameMode} has no hill, so the preview must not paint one."
        );
    }

    [Test]
    public void KingOfTheHillPaintsThePad()
    {
        using PreviewPixels pixels = new(MapCatalog.Concourse, GameMode.KingOfTheHill);

        Assert.That(pixels.CountMatching(HillPad), Is.GreaterThan(0));
    }

    /// <summary>
    /// Leg 1 is what a lobby is looking at: the host escorts from its back rank and the opponent
    /// digs in ahead of the zone it is guarding, which is nowhere near its own back rank.
    /// </summary>
    [Test]
    public void EscortDrawsMarkersOnTheEscortDeployment()
    {
        foreach (MapDefinition map in MapCatalog.All)
        {
            // Deployment is asked of the live board, so the board being previewed has to be the
            // live one while the expected layout is worked out.
            MapCatalog.SetActive(map);
            List<Vector2Int[]> escort = GameLoop.CreateSpawnLayout(
                GameMode.EscortThePresident,
                1
            );
            List<Vector2Int[]> standard = GameLoop.CreateSpawnLayout(GameMode.Elimination, 1);

            Assert.That(
                escort[GameLoop.OpponentTeamIndex],
                Is.Not.EqualTo(standard[GameLoop.OpponentTeamIndex]),
                $"{map.DisplayName}: the escort defence should not be on its back rank."
            );

            using PreviewPixels pixels = new(map, GameMode.EscortThePresident);
            for (int teamIndex = 0; teamIndex < escort.Count; teamIndex++)
            {
                Color32 expected = TeamPalette.ForTeamIndex(teamIndex);
                foreach (Vector2Int cell in escort[teamIndex])
                {
                    Assert.That(
                        pixels.AtCell(cell),
                        Is.EqualTo(expected),
                        $"{map.DisplayName}: no team {teamIndex} marker on escort spawn "
                            + $"({cell.x},{cell.y})."
                    );
                }
            }

            // The cells only the back-rank layout uses are left bare, so the picture is not simply
            // drawing both deployments at once.
            foreach (
                Vector2Int cell in standard[GameLoop.OpponentTeamIndex]
                    .Except(escort.SelectMany(cells => cells))
            )
            {
                Assert.That(
                    pixels.AtCell(cell),
                    Is.Not.EqualTo((Color32)TeamPalette.Enemy),
                    $"{map.DisplayName}: ({cell.x},{cell.y}) still carries a marker in Escort."
                );
            }
        }
    }

    /// <summary>
    /// Escort's objective is the zone the escorting crew walks its president into, and it is that
    /// crew's own colour in the match, so it is here too. On leg 1 that is exactly one pad, on the
    /// far side of the board from the crew that owns it.
    /// </summary>
    [Test]
    public void EscortPaintsTheExtractionZoneOfWhoeverIsEscorting()
    {
        foreach (MapDefinition map in MapCatalog.All)
        {
            MapCatalog.SetActive(map);
            using PreviewPixels pixels = new(map, GameMode.EscortThePresident);
            // Elimination paints no objective at all, so the same cell there is the reading for
            // "nothing was drawn here" without this test naming a single deck colour.
            using PreviewPixels bare = new(map, GameMode.Elimination);

            int padded = 0;
            for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
            {
                HashSet<Vector2Int> zone = GameLoop.EscortExtractionCellsFor(
                    GameMode.EscortThePresident,
                    1,
                    teamIndex
                );
                if (zone.Count == 0)
                    continue;

                padded++;
                Assert.That(
                    zone.All(cell => cell.y == EscortSeries.ExtractionRowFor(teamIndex)),
                    Is.True,
                    $"{map.DisplayName}: team {teamIndex}'s zone left its extraction row."
                );
                foreach (Vector2Int cell in zone)
                {
                    Color32 drawn = pixels.AtCell(cell);
                    Assert.That(
                        drawn,
                        Is.Not.EqualTo(bare.AtCell(cell)),
                        $"{map.DisplayName}: extraction cell ({cell.x},{cell.y}) is unpainted."
                    );
                    Assert.That(
                        drawn,
                        Is.Not.EqualTo((Color32)TeamPalette.HillUnclaimed),
                        $"{map.DisplayName}: the extraction pad took the hill's cream."
                    );
                    Assert.That(
                        drawn,
                        Is.Not.EqualTo((Color32)TeamPalette.ForTeamIndex(teamIndex)),
                        $"{map.DisplayName}: the pad took the solid marker colour."
                    );
                    // Owned, though: nearer that crew's colour than the other crew's.
                    Assert.That(
                        Distance(drawn, TeamPalette.ForTeamIndex(teamIndex)),
                        Is.LessThan(
                            Distance(
                                drawn,
                                TeamPalette.ForTeamIndex(GameLoop.GetEnemyTeamIndex(teamIndex))
                            )
                        ),
                        $"{map.DisplayName}: team {teamIndex}'s pad is not that team's colour."
                    );
                }
            }

            Assert.That(padded, Is.EqualTo(1), $"{map.DisplayName}: leg 1 has one escorting crew.");
        }
    }

    [Test]
    public void OnlyEscortPaintsAnExtractionZone()
    {
        foreach (GameMode gameMode in new[] { GameMode.Elimination, GameMode.KingOfTheHill })
        {
            for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
            {
                Assert.That(
                    GameLoop.EscortExtractionCellsFor(gameMode, 1, teamIndex),
                    Is.Empty,
                    $"{gameMode} has no extraction zone to paint."
                );
            }
        }
    }

    private static float Distance(Color32 a, Color b)
    {
        Color32 other = b;
        return Mathf.Abs(a.r - other.r) + Mathf.Abs(a.g - other.g) + Mathf.Abs(a.b - other.b);
    }

    /// <summary>
    /// Drawing makes a board active to ask it about walls and deployment. A lobby redraws on every
    /// click, so leaking that would repoint the live board from a menu.
    /// </summary>
    [Test]
    public void DrawingRestoresTheLiveBoardAndOptions()
    {
        MapDefinition activeBefore = MapCatalog.Active;
        MatchOptions optionsBefore = MatchOptions.Current;

        foreach (GameMode gameMode in System.Enum.GetValues(typeof(GameMode)).Cast<GameMode>())
        {
            using PreviewPixels pixels = new(MapCatalog.Bastion, gameMode);
        }

        Assert.That(MapCatalog.Active, Is.SameAs(activeBefore));
        Assert.That(MatchOptions.Current, Is.EqualTo(optionsBefore));
    }

    /// <summary>One rendered preview, read back as pixels and disposed with the texture.</summary>
    private sealed class PreviewPixels : System.IDisposable
    {
        private readonly Texture2D texture;
        private readonly Color32[] pixels;

        public PreviewPixels(MapDefinition map, GameMode gameMode)
        {
            texture = MapPreviewImage.CreateTexture(map, gameMode);
            pixels = texture.GetPixels32();
        }

        public Color32 AtCell(Vector2Int cell)
        {
            Vector2Int pixel = MapPreviewImage.CellCenterPixel(cell);
            return pixels[pixel.y * MapPreviewImage.PreviewWidth + pixel.x];
        }

        public int CountMatching(Color32 wanted) => pixels.Count(pixel => Same(pixel, wanted));

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        public void Dispose() => Object.DestroyImmediate(texture);
    }
}
