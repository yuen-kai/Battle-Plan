using System.IO;
using UnityEditor;
using UnityEngine;

public static class MapPreviewGenerator
{
    public const string PreviewPath = "Assets/Images/MapPreview.png";
    public const int PreviewWidth = 1080;
    public const int PreviewHeight = 720;

    private const int BoardLeft = 90;
    private const int BoardBottom = 60;
    private const int CellSize = 60;

    private static readonly Color32 Background = new(5, 9, 15, 255);
    private static readonly Color32 Cell = new(18, 34, 46, 255);
    private static readonly Color32 HillCell = new(54, 44, 23, 255);
    private static readonly Color32 GridLine = new(7, 14, 22, 255);
    private static readonly Color32 Border = new(229, 160, 55, 255);
    private static readonly Color32 WallShadow = new(5, 10, 16, 255);
    private static readonly Color32 Wall = new(111, 126, 140, 255);
    private static readonly Color32 WallEdge = new(73, 89, 103, 255);
    private static readonly Color32 BlueSpawn = new(88, 184, 240, 255);
    private static readonly Color32 RedSpawn = new(255, 105, 101, 255);

    [MenuItem("Battle Plan/Regenerate Map Preview")]
    public static void Generate()
    {
        File.WriteAllBytes(PreviewPath, BuildPreviewPng());
        AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceUpdate);
        Debug.Log(
            $"[MapPreview] Regenerated {PreviewPath} for {RosterRules.UnitsPerPlayer} units per player."
        );
    }

    public static byte[] BuildPreviewPng()
    {
        Texture2D texture = new(PreviewWidth, PreviewHeight, TextureFormat.RGBA32, false);
        texture.name = "MapPreview";
        try
        {
            Color32[] pixels = new Color32[PreviewWidth * PreviewHeight];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = Background;
            texture.SetPixels32(pixels);

            DrawBoard(texture);
            texture.Apply(false, false);
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    private static void DrawBoard(Texture2D texture)
    {
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
                    GameLoop.KingOfTheHillCells.Contains(cell) ? HillCell : Cell
                );
            }
        }

        foreach (Vector2Int wall in GameLoop.wallLayout)
            DrawWall(texture, wall);

        DrawSpawns(texture, GameLoop.HostTeamIndex, BlueSpawn, false);
        DrawSpawns(texture, GameLoop.OpponentTeamIndex, RedSpawn, true);

        int boardWidth = GridSystem.ColumnCount * CellSize;
        int boardHeight = GridSystem.RowCount * CellSize;
        DrawRect(texture, BoardLeft - 4, BoardBottom - 4, boardWidth + 8, 4, Border);
        DrawRect(texture, BoardLeft - 4, BoardBottom + boardHeight, boardWidth + 8, 4, Border);
        DrawRect(texture, BoardLeft - 4, BoardBottom, 4, boardHeight, Border);
        DrawRect(texture, BoardLeft + boardWidth, BoardBottom, 4, boardHeight, Border);
    }

    private static void DrawWall(Texture2D texture, Vector2Int cell)
    {
        int left = BoardLeft + cell.x * CellSize;
        int bottom = BoardBottom + cell.y * CellSize;
        DrawRect(texture, left + 17, bottom + 9, 34, 36, WallShadow);
        DrawRect(texture, left + 13, bottom + 13, 34, 34, WallEdge);
        DrawRect(texture, left + 15, bottom + 15, 30, 30, Wall);
    }

    private static void DrawSpawns(
        Texture2D texture,
        int teamIndex,
        Color32 color,
        bool diamond
    )
    {
        Vector2Int[] spawns = GameLoop.CreateSpawnPositions(
            false,
            teamIndex,
            RosterRules.UnitsPerPlayer
        );
        foreach (Vector2Int spawn in spawns)
        {
            int centerX = BoardLeft + spawn.x * CellSize + CellSize / 2;
            int centerY = BoardBottom + spawn.y * CellSize + CellSize / 2;
            if (diamond)
            {
                DrawDiamond(texture, centerX, centerY, 17, color);
                DrawDiamond(texture, centerX, centerY, 8, Cell);
                DrawRect(
                    texture,
                    centerX - 13,
                    BoardBottom + GridSystem.RowCount * CellSize + 12,
                    26,
                    4,
                    Border
                );
            }
            else
            {
                DrawCircle(texture, centerX, centerY, 16, color);
                DrawCircle(texture, centerX, centerY, 8, Cell);
                DrawRect(texture, centerX - 13, BoardBottom - 16, 26, 4, Border);
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
