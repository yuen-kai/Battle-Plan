using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the shipped thumbnail. The drawing itself lives in <see cref="MapPreviewImage"/> so the
/// character-select panel can render whichever board the lobby picked from the same routine; this
/// class only owns the menu item and the on-disk asset.
/// </summary>
public static class MapPreviewGenerator
{
    public const string PreviewPath = "Assets/Images/MapPreview.png";
    public const int PreviewWidth = MapPreviewImage.PreviewWidth;
    public const int PreviewHeight = MapPreviewImage.PreviewHeight;

    /// <summary>
    /// The baked asset is the fallback board. Every other board is drawn at runtime, so only this
    /// one needs to exist as a file.
    /// </summary>
    private static MapDefinition BakedMap => MapCatalog.Fallback;

    [MenuItem("Battle Plan/Regenerate Map Preview")]
    public static void Generate()
    {
        File.WriteAllBytes(PreviewPath, BuildPreviewPng());
        AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceUpdate);
        Debug.Log(
            $"[MapPreview] Regenerated {PreviewPath} for {BakedMap.DisplayName} "
                + $"at {RosterRules.UnitsPerPlayer} units per player."
        );
    }

    public static byte[] BuildPreviewPng() => MapPreviewImage.EncodePng(BakedMap);
}
