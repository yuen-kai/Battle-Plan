using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crop convention for <see cref="ModelHeadshotRenderer"/>: how much of a model's total
/// bounding-box height the shot frames, measured down from the top, plus how much breathing room
/// to leave around that crop. A field left at its default (zero) is replaced with the tuned
/// default in <see cref="ModelHeadshotRenderer"/> — mirrors how <c>PlaceholderModelSpec</c> treats
/// an unset <c>HeightScale</c>/<c>WidthScale</c> — so callers can write <c>default</c> for "the
/// usual headshot" or override just the field they care about.
/// </summary>
[Serializable]
public struct HeadshotFraming
{
    /// <summary>Fraction of the model's total height, from the top down, that the shot frames.</summary>
    public float TopHeightFraction;

    /// <summary>Extra room around the crop, as a fraction of the crop's own size, so the subject never touches the frame edge.</summary>
    public float Margin;

    public static readonly HeadshotFraming Default = new()
    {
        TopHeightFraction = 0.4f,
        Margin = 0.18f,
    };

    /// <summary>Fills in any zero-valued field with <see cref="Default"/>'s value.</summary>
    internal HeadshotFraming Resolved()
    {
        return new HeadshotFraming
        {
            TopHeightFraction = TopHeightFraction > 0f ? TopHeightFraction : Default.TopHeightFraction,
            Margin = Margin > 0f ? Margin : Default.Margin,
        };
    }
}

/// <summary>
/// Renders a bust/shoulders-up "headshot" of a 3D model by standing up a throwaway camera and
/// light, pointing them at the top slice of the model's own bounding box, and reading the result
/// back into a <see cref="Texture2D"/>. Built for <c>PlaceholderModelBuilder</c> silhouettes so a
/// new character can get a portrait that is literally a photo of its placeholder body rather than a
/// flat icon, while AI image generation is unavailable — but it only looks at renderer bounds, so
/// it works on any model, placeholder or not.
/// <para>
/// Editor-only and stateless: every temporary object (model instance, camera, light, render
/// texture) is created and torn down inside a single call, and nothing is left in whatever scene
/// happens to be open. Safe to call outside Play mode, which is the expected use — authoring a
/// portrait for a <c>UnitData</c> asset, not anything that ships.
/// </para>
/// </summary>
public static class ModelHeadshotRenderer
{
    /// <summary>Default portrait resolution, matching the hand-made PNGs in Assets/Images/Portraits.</summary>
    public const int DefaultResolution = 512;

    // Tucked far below the world so a temp instance can never overlap anything a live scene camera
    // would otherwise be pointed at, even though nothing renders it but our own throwaway camera.
    private static readonly Vector3 IsolatedOrigin = new(0f, -8000f, 0f);

    /// <summary>
    /// Renders <paramref name="modelRoot"/> as a headshot and returns the pixels. The caller owns
    /// the returned texture and is responsible for destroying it when done.
    /// </summary>
    public static Texture2D RenderHeadshot(
        GameObject modelRoot,
        int resolution = DefaultResolution,
        HeadshotFraming framing = default
    )
    {
        if (modelRoot == null)
            throw new ArgumentNullException(nameof(modelRoot));
        if (resolution <= 0)
            throw new ArgumentOutOfRangeException(nameof(resolution));

        HeadshotFraming resolved = framing.Resolved();

        GameObject instance = null;
        GameObject rigRoot = null;
        Camera camera = null;
        RenderTexture renderTexture = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            instance = UnityEngine.Object.Instantiate(modelRoot);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = modelRoot.name + "_HeadshotTemp";
            // Reset to a known transform: the source may be a live scene instance with an arbitrary
            // position/rotation, and framing below assumes the model's own local axes (it "faces
            // +Z", per PlaceholderModelBuilder) line up with world axes.
            instance.transform.SetPositionAndRotation(IsolatedOrigin, Quaternion.identity);

            Bounds bounds = ComputeWorldBounds(instance, IsolatedOrigin);
            Vector3 cropCenter = ComputeCropCenter(bounds, resolved.TopHeightFraction);
            float orthographicSize = ComputeOrthographicSize(bounds, resolved);

            rigRoot = new GameObject("ModelHeadshotRig") { hideFlags = HideFlags.HideAndDontSave };
            rigRoot.transform.position = IsolatedOrigin;

            camera = BuildCamera(rigRoot.transform, cropCenter, bounds, orthographicSize);
            BuildLights(rigRoot.transform, cropCenter);

            renderTexture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "ModelHeadshotTarget",
                antiAliasing = 1,
            };
            renderTexture.Create();
            camera.targetTexture = renderTexture;
            camera.ResetAspect();
            camera.Render();

            RenderTexture.active = renderTexture;
            Texture2D texture = new(resolution, resolution, TextureFormat.RGBA32, mipChain: false)
            {
                name = modelRoot.name + "_Headshot",
            };
            texture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0, recalculateMipMaps: false);
            texture.Apply(updateMipmaps: false);
            return texture;
        }
        finally
        {
            RenderTexture.active = previousActive;

            if (camera != null)
                camera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            if (rigRoot != null)
                UnityEngine.Object.DestroyImmediate(rigRoot);
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Renders <paramref name="modelRoot"/> and writes the result to <paramref name="assetPath"/>
    /// (a project-relative path under <c>Assets/</c>) as an imported sprite texture, matching the
    /// import settings of the hand-made portrait PNGs it stands in for. Returns
    /// <paramref name="assetPath"/> for convenience.
    /// </summary>
    public static string RenderAndSaveHeadshot(
        GameObject modelRoot,
        string assetPath,
        int resolution = DefaultResolution,
        HeadshotFraming framing = default
    )
    {
        if (string.IsNullOrEmpty(assetPath))
            throw new ArgumentException("assetPath must be a non-empty Assets/-relative path.", nameof(assetPath));

        Texture2D texture = RenderHeadshot(modelRoot, resolution, framing);
        try
        {
            byte[] png = ImageConversion.EncodeToPNG(texture);

            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ApplyPortraitImporterSettings(assetPath);

            return assetPath;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// Mirrors the importer settings baked into the hand-made Assets/Images/Portraits/*.png sprites
    /// (single sprite, 100 px/unit, alpha-from-input, no mip chain, non-readable, clamp-wrapped) so a
    /// generated headshot behaves identically once wired into a UnitData sprite field.
    /// </summary>
    private static void ApplyPortraitImporterSettings(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.sRGBTexture = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.crunchedCompression = false;

        importer.SaveAndReimport();
    }

    /// <summary>Union of every renderer's world-space bounds; falls back to a small box around <paramref name="fallbackCenter"/> if the model has none.</summary>
    private static Bounds ComputeWorldBounds(GameObject root, Vector3 fallbackCenter)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(fallbackCenter, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>Center of the top slice of the bounds that the headshot frames.</summary>
    private static Vector3 ComputeCropCenter(Bounds bounds, float topHeightFraction)
    {
        float cropHeight = bounds.size.y * Mathf.Clamp01(topHeightFraction);
        float cropCenterY = bounds.max.y - cropHeight * 0.5f;
        return new Vector3(bounds.center.x, cropCenterY, bounds.center.z);
    }

    /// <summary>
    /// Half-height of the orthographic view: the larger of the crop's own height and the model's
    /// full width, so neither a tall crop nor a wide silhouette (e.g. a wide-shouldered
    /// PlaceholderModelSpec) clips out of frame, plus a margin so nothing touches the frame edge.
    /// </summary>
    private static float ComputeOrthographicSize(Bounds bounds, HeadshotFraming framing)
    {
        float cropHeight = bounds.size.y * Mathf.Clamp01(framing.TopHeightFraction);
        float halfHeight = cropHeight * 0.5f;
        float halfWidth = bounds.size.x * 0.5f;
        float size = Mathf.Max(halfHeight, halfWidth, 0.05f);
        return size * (1f + Mathf.Max(framing.Margin, 0f));
    }

    /// <summary>
    /// A camera on the model's +Z side looking back at -Z: PlaceholderModelBuilder's own convention
    /// is that a character "faces +Z", so standing on that side and looking back is what frames the
    /// front of the face rather than the back of the head.
    /// </summary>
    private static Camera BuildCamera(Transform rig, Vector3 cropCenter, Bounds bounds, float orthographicSize)
    {
        float depth = Mathf.Max(bounds.size.z, 0.5f);
        float distance = depth * 2f + orthographicSize * 2f + 1f;

        GameObject cameraObject = new("ModelHeadshotCamera") { hideFlags = HideFlags.HideAndDontSave };
        cameraObject.transform.SetParent(rig, worldPositionStays: false);
        cameraObject.transform.SetPositionAndRotation(
            cropCenter + Vector3.forward * distance,
            Quaternion.LookRotation(Vector3.back, Vector3.up)
        );

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = orthographicSize;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = distance + bounds.extents.z + 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        // Transparent clear: portraits composite into UI as sprites, and the existing hand-made
        // PNGs are alpha-cut rather than filled with a flat backdrop.
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.cullingMask = ~0;
        camera.allowHDR = false;
        camera.allowMSAA = false;

        // A fresh Camera picks up URP's global Volume stack (color grading, bloom, etc.) by
        // default. A portrait needs to look the same regardless of what post effects happen to be
        // active in whatever scene is currently open, so this camera opts out of all of it.
        UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.antialiasing = AntialiasingMode.None;

        return camera;
    }

    /// <summary>
    /// A key/fill directional pair rather than touching scene RenderSettings.ambient*, so the
    /// model's Lit-shader materials get real, even light without mutating anything about the
    /// currently open scene's own lighting environment.
    /// </summary>
    private static void BuildLights(Transform rig, Vector3 cropCenter)
    {
        GameObject keyObject = new("ModelHeadshotKeyLight") { hideFlags = HideFlags.HideAndDontSave };
        keyObject.transform.SetParent(rig, worldPositionStays: false);
        keyObject.transform.position = cropCenter;
        keyObject.transform.rotation = Quaternion.Euler(35f, -150f, 0f);
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = Color.white;
        key.intensity = 1.3f;
        key.shadows = LightShadows.None;

        GameObject fillObject = new("ModelHeadshotFillLight") { hideFlags = HideFlags.HideAndDontSave };
        fillObject.transform.SetParent(rig, worldPositionStays: false);
        fillObject.transform.position = cropCenter;
        fillObject.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = Color.white;
        fill.intensity = 0.6f;
        fill.shadows = LightShadows.None;
    }
}
