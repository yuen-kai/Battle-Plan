using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a plan view of a board as one mode will set it up: deck, cover, both deployments, and
/// the objective pad if the mode has one.
///
/// This lives in runtime code rather than the editor because the character-select screen has to
/// show whichever board the lobby picked, and a single baked thumbnail can only ever be right for
/// one of them. <c>MapPreviewGenerator</c> bakes the shipped PNG from the same routine, so the
/// menu art and the in-game panel can never disagree.
///
/// Nothing here decides where a crew stands or where the objective is. All of it comes from the
/// same routines the match runs — <see cref="GameLoop.CreateSpawnLayout"/>,
/// <see cref="GameLoop.EscortExtractionCellsFor(GameMode, int, int)"/> and the board's own
/// <see cref="MapDefinition.HillCells"/> — because a preview drawn from a second copy of the rules
/// is a preview that will eventually lie. What this file owns is how those facts are coloured in.
/// </summary>
public static class MapPreviewImage
{
    public const int PreviewWidth = 1080;
    public const int PreviewHeight = 720;

    private const int BoardLeft = 90;
    private const int BoardBottom = 60;
    private const int CellSize = 60;

    // The plate used to be a dark miniature, on the reasoning that a bright deck would punch a
    // hole in the dark character-select panel. That reasoning had it backwards: the thumbnail's
    // job is to show the player the board they are about to play on, and it was showing them a
    // board that does not exist. Punching a hole in the panel is the correct outcome — a bright
    // plate inside dark chrome reads as a viewport onto the world, which is the same relationship
    // the HUD has with the board in the match.
    //
    // Every board value below is the display-sRGB hex ArenaBuilder.MapPalette writes into the map
    // materials, and the token name travels with it so a re-grade stays a lookup. Albedo is used
    // directly rather than an estimate of the lit result because the arena's exposure is set so
    // the deck lands back on its own albedo (#A8B3B8 = linear 0.40, which is the value the bloom
    // threshold and the mid-tone window are both written against).
    //
    // No vignette or sun shading is simulated. The board has both, but they fall hardest on the
    // corners, which is exactly where the spawn markers sit — the one thing this image has to
    // communicate. A plan view is allowed to be flat-lit; the cover gets its depth from the
    // cap/body/shadow steps instead.
    private static readonly Color32 Background = new(18, 21, 22, 255); // --toy-inset
    private static readonly Color32 DeckLight = new(168, 179, 184, 255); // --bp-ground-a   #A8B3B8
    private static readonly Color32 DeckDark = new(159, 170, 176, 255); // --bp-ground-b   #9FAAB0
    private static readonly Color32 GridLine = new(110, 122, 128, 255); // --bp-ground-line #6E7A80
    private static readonly Color32 CoverBody = new(70, 82, 90, 255); // --bp-cover      #46525A
    private static readonly Color32 CoverCap = new(173, 199, 209, 255); // Map_WallCap     #ADC7D1
    private static readonly Color32 CoverShadow = new(126, 138, 144, 255); // deck 40% to --bp-shadow
    private static readonly Color32 TableRim = new(94, 83, 70, 255); // --bp-table      #5E5346

    // The hill pad is TeamPalette's own unclaimed cream, which is the board's --bp-ground-paint.
    // Painted only for King of the Hill, because that is the only mode that paints it in the
    // match: GameLoop draws the pad at runtime and skips it otherwise. Showing it on every mode
    // told an Elimination player their board had an objective on it.
    private static readonly Color32 HillCell = TeamPalette.HillUnclaimed;

    // How far an extraction pad is mixed from the deck toward the crew that owns it. The solid
    // team colour is already spoken for — it is the unit marker — so a pad that took it would
    // read as five more units standing on the far rank. Half way is unmistakably that crew's
    // colour and unmistakably floor.
    private const float ExtractionPadMix = 0.5f;

    /// <summary>
    /// The leg the preview draws. Escort alternates which crew escorts and which digs in, and a
    /// lobby is always looking at the start of the series.
    /// </summary>
    private const int PreviewLegNumber = 1;

    // Team identity, and the preview is where a player first learns which colour is theirs. Shape
    // carries the same information (circle friendly, diamond enemy), which is the one place in the
    // game that redundancy exists.
    //
    // Split by what each marker sits on, which is what TeamPalette's two tiers are for. On the
    // bright deck the base pair is both the literal unit body colour and the higher-contrast
    // choice (3.0:1 and 1.8:1 against the deck, against 1.6:1 and 1.3:1 for the lifted pair — the
    // lifted red all but vanishes there). Out in the dark margin the ranks invert, so the rank
    // ticks take the lifted pair and clear 5:1.
    private static readonly Color32 BlueSpawn = TeamPalette.Friendly;
    private static readonly Color32 RedSpawn = TeamPalette.Enemy;
    private static readonly Color32 BlueRankTick = TeamPalette.FriendlyBright;
    private static readonly Color32 RedRankTick = TeamPalette.EnemyBright;

    /// <summary>
    /// A texture of the given board as the given mode will actually set it up. The caller owns it
    /// and should destroy it when the view that shows it goes away.
    ///
    /// The mode is a parameter rather than read from <see cref="MatchOptions.Current"/> because
    /// both callers are lobbies: the options they are drawing are the ones being chosen, which
    /// have not been committed anywhere yet.
    /// </summary>
    public static Texture2D CreateTexture(MapDefinition map, GameMode gameMode)
    {
        Texture2D texture = new(PreviewWidth, PreviewHeight, TextureFormat.RGBA32, false)
        {
            name = $"MapPreview_{map?.Id.ToString() ?? "Active"}_{gameMode}",
        };

        Color32[] pixels = new Color32[PreviewWidth * PreviewHeight];
        for (int index = 0; index < pixels.Length; index++)
            pixels[index] = Background;
        texture.SetPixels32(pixels);

        // Deployment comes from GameLoop, which reads the live board, so the requested map is made
        // active for the duration of the draw rather than duplicating the spawn rules here.
        MapDefinition previous = MapCatalog.Active;
        try
        {
            if (map != null)
                MapCatalog.SetActive(map);
            DrawBoard(texture, MapCatalog.Active, gameMode);
        }
        finally
        {
            MapCatalog.SetActive(previous);
        }

        texture.Apply(false, false);
        return texture;
    }

    public static byte[] EncodePng(MapDefinition map, GameMode gameMode)
    {
        Texture2D texture = CreateTexture(map, gameMode);
        try
        {
            return texture.EncodeToPNG();
        }
        finally
        {
            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
        }
    }

    private static void DrawBoard(Texture2D texture, MapDefinition map, GameMode gameMode)
    {
        Dictionary<Vector2Int, Color32> objective = BuildObjectivePaint(map, gameMode);

        // Cells are drawn inset, so the field underneath is what shows through as grid lines.
        DrawRect(
            texture,
            BoardLeft,
            BoardBottom,
            GridSystem.ColumnCount * CellSize,
            GridSystem.RowCount * CellSize,
            GridLine
        );

        for (int row = 0; row < GridSystem.RowCount; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                Vector2Int cell = new(column, row);
                DrawRect(
                    texture,
                    BoardLeft + column * CellSize + 2,
                    BoardBottom + row * CellSize + 2,
                    CellSize - 4,
                    CellSize - 4,
                    objective.TryGetValue(cell, out Color32 paint) ? paint : DeckColour(cell)
                );
            }
        }

        foreach (Vector2Int wall in map.Walls)
            DrawWall(texture, wall);

        List<Vector2Int[]> layout = GameLoop.CreateSpawnLayout(gameMode, PreviewLegNumber);
        DrawSpawns(texture, layout, GameLoop.HostTeamIndex, BlueSpawn, BlueRankTick, false);
        DrawSpawns(texture, layout, GameLoop.OpponentTeamIndex, RedSpawn, RedRankTick, true);

        // The board's own edge, in the colour of the table it sits on, rather than the amber it
        // used to carry. Amber means "the objective" everywhere else in the game, and a frame is
        // chrome; the table also gives the bright plate something to end on before the dark margin.
        // Six pixels because the thumbnail is shown at roughly 0.29 scale and four would land under
        // a pixel.
        int boardWidth = GridSystem.ColumnCount * CellSize;
        int boardHeight = GridSystem.RowCount * CellSize;
        DrawRect(texture, BoardLeft - 6, BoardBottom - 6, boardWidth + 12, 6, TableRim);
        DrawRect(texture, BoardLeft - 6, BoardBottom + boardHeight, boardWidth + 12, 6, TableRim);
        DrawRect(texture, BoardLeft - 6, BoardBottom, 6, boardHeight, TableRim);
        DrawRect(texture, BoardLeft + boardWidth, BoardBottom, 6, boardHeight, TableRim);
    }

    /// <summary>
    /// Which cells carry objective paint, and in what colour. Every mode with an objective gets a
    /// flat fill rather than the thin boundary the match outlines the same cells with: that
    /// boundary is 0.12 m on a 2.7 m cell, which lands under a pixel once the plate is shown at
    /// the size a lobby shows it.
    ///
    /// Elimination returns nothing, which is the point — it has no objective, and painting one
    /// told a player their board had something on it to fight over.
    /// </summary>
    private static Dictionary<Vector2Int, Color32> BuildObjectivePaint(
        MapDefinition map,
        GameMode gameMode
    )
    {
        Dictionary<Vector2Int, Color32> paint = new();

        if (gameMode == GameMode.KingOfTheHill)
        {
            foreach (Vector2Int cell in map.HillCells)
                paint[cell] = HillCell;
            return paint;
        }

        // An extraction zone belongs to whoever is escorting, and it takes that crew's colour for
        // the reason the match does: the pad is one side's finish line and the other side's last
        // stand, and a neutral pad would say neither. On the first leg only one crew is escorting,
        // so only one pad exists — which, read together with a defence deployed in front of it, is
        // the whole shape of the mode in one picture.
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            Color32 pad = ExtractionPadColour(teamIndex);
            foreach (
                Vector2Int cell in GameLoop.EscortExtractionCellsFor(
                    gameMode,
                    PreviewLegNumber,
                    teamIndex
                )
            )
            {
                paint[cell] = pad;
            }
        }
        return paint;
    }

    private static Color32 ExtractionPadColour(int teamIndex) =>
        Blend(DeckLight, TeamPalette.ForTeamIndex(teamIndex), ExtractionPadMix);

    private static Color32 Blend(Color32 from, Color32 to, float amount) =>
        new(
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.r, to.r, amount)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.g, to.g, amount)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.b, to.b, amount)),
            255
        );

    /// <summary>
    /// Where a board cell lands in the image, so a caller can ask what got drawn on a given square
    /// without knowing the plate's margins. Only the tests need this; it is here rather than
    /// duplicated there so the margins stay in one place.
    /// </summary>
    public static Vector2Int CellCenterPixel(Vector2Int cell) =>
        new(
            BoardLeft + cell.x * CellSize + CellSize / 2,
            BoardBottom + cell.y * CellSize + CellSize / 2
        );

    /// <summary>
    /// The checker parity ArenaBuilder.BuildDeck lays the deck out with. A is the lighter square.
    /// </summary>
    private static Color32 DeckColour(Vector2Int cell) =>
        (cell.x + cell.y) % 2 == 1 ? DeckDark : DeckLight;

    /// <summary>
    /// Cast shadow, then the dark vertical faces, then the light horizontal cap — the same order
    /// the eye resolves a blocker in the match. The body ring widened from two pixels to five: on
    /// the old dark plate a light cap alone was enough to find a wall, but a light cap on a light
    /// deck is only separated by the dark ring around it, and two pixels is under half a pixel once
    /// the thumbnail is scaled down.
    /// </summary>
    private static void DrawWall(Texture2D texture, Vector2Int cell)
    {
        int left = BoardLeft + cell.x * CellSize;
        int bottom = BoardBottom + cell.y * CellSize;
        DrawRect(texture, left + 16, bottom + 8, 36, 38, CoverShadow);
        DrawRect(texture, left + 12, bottom + 12, 36, 36, CoverBody);
        DrawRect(texture, left + 17, bottom + 17, 26, 26, CoverCap);
    }

    /// <summary>
    /// Markers are solid now, not rings. A ring survived on the old dark plate because it was
    /// twenty pixels across at display size; at this thumbnail's scale its hole and its stroke are
    /// both under five pixels and collapse into each other. A filled token behind a cover-coloured
    /// rim keeps the shape readable and holds the red against a deck it is only 1.8:1 from.
    ///
    /// The tick stays in the margin at the marker's column even when the marker itself is inboard,
    /// which is where an escort defence deploys. Its job is the colour key, not the row.
    /// </summary>
    private static void DrawSpawns(
        Texture2D texture,
        List<Vector2Int[]> layout,
        int teamIndex,
        Color32 color,
        Color32 rankTick,
        bool diamond
    )
    {
        Vector2Int[] spawns =
            teamIndex >= 0 && teamIndex < layout.Count
                ? layout[teamIndex]
                : System.Array.Empty<Vector2Int>();
        foreach (Vector2Int spawn in spawns)
        {
            int centerX = BoardLeft + spawn.x * CellSize + CellSize / 2;
            int centerY = BoardBottom + spawn.y * CellSize + CellSize / 2;
            if (diamond)
            {
                DrawDiamond(texture, centerX, centerY, 20, CoverBody);
                DrawDiamond(texture, centerX, centerY, 17, color);
                DrawRect(
                    texture,
                    centerX - 13,
                    BoardBottom + GridSystem.RowCount * CellSize + 14,
                    26,
                    4,
                    rankTick
                );
            }
            else
            {
                DrawCircle(texture, centerX, centerY, 19, CoverBody);
                DrawCircle(texture, centerX, centerY, 16, color);
                DrawRect(texture, centerX - 13, BoardBottom - 18, 26, 4, rankTick);
            }
        }
    }

    private static void DrawCircle(
        Texture2D texture,
        int centerX,
        int centerY,
        int radius,
        Color32 color
    )
    {
        int radiusSquared = radius * radius;
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= radiusSquared)
                    SetPixel(texture, centerX + x, centerY + y, color);
            }
        }
    }

    private static void DrawDiamond(
        Texture2D texture,
        int centerX,
        int centerY,
        int radius,
        Color32 color
    )
    {
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(y) <= radius)
                    SetPixel(texture, centerX + x, centerY + y, color);
            }
        }
    }

    private static void DrawRect(
        Texture2D texture,
        int left,
        int bottom,
        int width,
        int height,
        Color32 color
    )
    {
        for (int y = bottom; y < bottom + height; y++)
        {
            for (int x = left; x < left + width; x++)
                SetPixel(texture, x, y, color);
        }
    }

    private static void SetPixel(Texture2D texture, int x, int y, Color32 color)
    {
        if (x >= 0 && x < texture.width && y >= 0 && y < texture.height)
            texture.SetPixel(x, y, color);
    }
}
