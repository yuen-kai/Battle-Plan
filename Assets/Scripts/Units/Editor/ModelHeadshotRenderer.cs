using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crop convention for <see cref="ModelHeadshotRenderer"/>: how much of a model's total
/// bounding-box height the shot considers, measured down from the top, plus how much breathing room
/// to leave around the subject it finds there. A field left at its default (zero) is replaced with
/// the tuned default in <see cref="ModelHeadshotRenderer"/>, so callers can write <c>default</c> for
/// "the usual headshot" or override just the field they care about.
/// </summary>
[Serializable]
public struct HeadshotFraming
{
    /// <summary>
    /// Fraction of the model's total height, from the top down, that the shot considers. Sized to
    /// clear the lowest thing a character holds: measured across the roster, the bottom of a
    /// slung launcher tube or a dropped shotgun grip sits just under 62% of the way down from the
    /// crown, so anything shallower cuts a weapon in half.
    /// </summary>
    public float TopHeightFraction;

    /// <summary>Extra room around the subject, as a fraction of its measured size, so it never touches the frame edge.</summary>
    public float Margin;

    /// <summary>
    /// Degrees the camera swings around the model, away from dead-on front. A posed character
    /// photographed square-on hides the whole depth of what it is holding — a rifle held across the
    /// chest collapses to a dot — so the hand-made portraits this stands in for are all shot from
    /// slightly off the shoulder.
    /// </summary>
    public float Yaw;

    public static readonly HeadshotFraming Default = new()
    {
        TopHeightFraction = 0.62f,
        Margin = 0.06f,
        Yaw = 22f,
    };

    /// <summary>Fills in any zero-valued field with <see cref="Default"/>'s value.</summary>
    internal HeadshotFraming Resolved()
    {
        return new HeadshotFraming
        {
            TopHeightFraction = TopHeightFraction > 0f ? TopHeightFraction : Default.TopHeightFraction,
            Margin = Margin > 0f ? Margin : Default.Margin,
            Yaw = Mathf.Abs(Yaw) > 0f ? Yaw : Default.Yaw,
        };
    }
}

/// <summary>
/// Renders a "headshot" of a 3D model by standing up a throwaway camera and light, pointing them at
/// the top slice of the model's own bounding box, and reading the result back into a
/// <see cref="Texture2D"/>. Used by <c>CharacterBuilder</c> to shoot the roster's portraits from the
/// characters themselves, so a portrait can never drift from the unit it names — but it only looks
/// at what renders, so it works on any model.
/// <para>
/// The shot is framed in two passes. The first renders the considered slice into a small probe
/// target and measures the alpha, which gives the subject's exact silhouette as the camera sees it;
/// the second re-aims and re-sizes onto that measurement and renders the portrait. Measuring beats
/// arithmetic on <c>Renderer.bounds</c> here because a skinned renderer's bounds are the bind
/// pose's conservative box rather than the pixels it draws — trusting them cropped the crown off
/// every helmet and left the subject sitting off-centre.
/// </para>
/// <para>
/// Editor-only and stateless: every temporary object (model instance, camera, light, render
/// texture) is created and torn down inside a single call, and nothing is left in whatever scene
/// happens to be open. Safe to call outside Play mode, which is the expected use — authoring a
/// portrait for a <c>UnitData</c> asset, not anything that ships.
/// </para>
/// </summary>
public static class ModelHeadshotRenderer
{
    /// <summary>
    /// Portrait aspect: 5:4, which is roughly what a character measures once the shot has to hold
    /// a hat, both shoulders and a weapon held across the chest. Every surface that shows a
    /// portrait scales it to fit rather than cropping it again, so shooting at the subject's own
    /// proportions is what keeps a tile, a card and a slot all filled by the same file.
    /// </summary>
    public const int DefaultWidth = 640;
    public const int DefaultHeight = 512;
    public const int DefaultResolution = 512;

    /// <summary>Probe target height. Big enough that one row is a fraction of a percent of the subject, small enough to be free.</summary>
    private const int ProbeHeight = 256;

    /// <summary>Alpha at or above which a probe pixel counts as subject rather than as the edge of an antialiased nothing.</summary>
    private const byte SubjectAlpha = 8;

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
        return RenderHeadshot(modelRoot, resolution, resolution, framing);
    }

    public static Texture2D RenderHeadshot(
        GameObject modelRoot,
        int width,
        int height,
        HeadshotFraming framing = default
    )
    {
        if (modelRoot == null)
            throw new ArgumentNullException(nameof(modelRoot));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

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
            // +Z") line up with world axes.
            instance.transform.SetPositionAndRotation(IsolatedOrigin, Quaternion.identity);

            Bounds bounds = ComputeWorldBounds(instance, IsolatedOrigin);

            rigRoot = new GameObject("ModelHeadshotRig") { hideFlags = HideFlags.HideAndDontSave };
            rigRoot.transform.position = IsolatedOrigin;

            camera = BuildCamera(rigRoot.transform, bounds, resolved.Yaw);
            BuildLights(rigRoot.transform, bounds.center);
            FrameSubject(camera, bounds, resolved, (float)width / height);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "ModelHeadshotTarget",
                antiAliasing = 1,
            };
            renderTexture.Create();
            camera.targetTexture = renderTexture;
            camera.ResetAspect();
            camera.Render();

            RenderTexture.active = renderTexture;
            Texture2D texture = new(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = modelRoot.name + "_Headshot",
            };
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
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
        return RenderAndSaveHeadshot(modelRoot, assetPath, resolution, resolution, framing);
    }

    public static string RenderAndSaveHeadshot(
        GameObject modelRoot,
        string assetPath,
        int width,
        int height,
        HeadshotFraming framing = default
    )
    {
        if (string.IsNullOrEmpty(assetPath))
            throw new ArgumentException("assetPath must be a non-empty Assets/-relative path.", nameof(assetPath));

        Texture2D texture = RenderHeadshot(modelRoot, width, height, framing);
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

    /// <summary>
    /// Union of the world-space bounds of everything that will actually draw; falls back to a small
    /// box around <paramref name="fallbackCenter"/> if the model has none. Disabled renderers and
    /// renderers with no geometry are skipped because the camera skips them too: the hand-made units
    /// each carry a switched-off duplicate of their own model, and counting those stretched the box
    /// a third of a unit past the silhouette in every direction.
    /// </summary>
    private static Bounds ComputeWorldBounds(GameObject root, Vector3 fallbackCenter)
    {
        Bounds bounds = default;
        bool any = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (renderer.bounds.size.sqrMagnitude <= 0f)
                continue;

            if (any)
                bounds.Encapsulate(renderer.bounds);
            else
                (bounds, any) = (renderer.bounds, true);
        }

        return any ? bounds : new Bounds(fallbackCenter, Vector3.one);
    }

    /// <summary>
    /// Aims and sizes <paramref name="camera"/> at the subject standing in the top slice of
    /// <paramref name="bounds"/>: probe the slice, then fit the frame to the silhouette that came
    /// back, padded by the margin and widened or deepened to <paramref name="aspect"/>.
    /// <para>
    /// Height slack goes below the subject rather than being split around it. Air over a character's
    /// head reads as a mistake, where more of their chest reads as the shot being a portrait.
    /// </para>
    /// </summary>
    private static void FrameSubject(Camera camera, Bounds bounds, HeadshotFraming framing, float aspect)
    {
        float halfSlice = Mathf.Max(bounds.size.y * Mathf.Clamp01(framing.TopHeightFraction), 0.01f) * 0.5f;
        float halfSpan = Mathf.Max(
            0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z),
            halfSlice * 0.25f
        );
        Vector3 sliceCenter = new(bounds.center.x, bounds.max.y - halfSlice, bounds.center.z);

        float distance = StandoffDistance(bounds);
        camera.orthographicSize = halfSlice;
        AimAt(camera, sliceCenter, distance);

        int probeWidth = Mathf.Clamp(Mathf.CeilToInt(ProbeHeight * halfSpan / halfSlice), 16, 4096);
        RectInt subject = MeasureSubject(camera, probeWidth, ProbeHeight);

        float offsetX = 0f;
        float offsetY = 0f;
        float halfWidth = halfSpan;
        float halfHeight = halfSlice;

        if (subject.width > 0 && subject.height > 0)
        {
            float perPixel = 2f * halfSlice / ProbeHeight;
            offsetX = (subject.x + subject.width * 0.5f - probeWidth * 0.5f) * perPixel;
            offsetY = (subject.y + subject.height * 0.5f - ProbeHeight * 0.5f) * perPixel;
            halfWidth = subject.width * 0.5f * perPixel;
            halfHeight = subject.height * 0.5f * perPixel;
        }

        float padding = 1f + Mathf.Max(framing.Margin, 0f);
        halfWidth *= padding;
        halfHeight *= padding;

        float framedHalfHeight = Mathf.Max(halfHeight, halfWidth / aspect);
        offsetY -= framedHalfHeight - halfHeight;

        camera.orthographicSize = framedHalfHeight;
        AimAt(camera, sliceCenter + camera.transform.right * offsetX + Vector3.up * offsetY, distance);
    }

    /// <summary>
    /// Renders the camera's current frame into a throwaway probe target and returns the tight pixel
    /// box of everything the model drew into it, or an empty box if it drew nothing. Pixel rows run
    /// bottom-up, matching <see cref="Texture2D.GetPixels32"/>.
    /// </summary>
    private static RectInt MeasureSubject(Camera camera, int probeWidth, int probeHeight)
    {
        RenderTexture probe = null;
        Texture2D readback = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            probe = new RenderTexture(probeWidth, probeHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "ModelHeadshotProbe",
                antiAliasing = 1,
            };
            probe.Create();
            camera.targetTexture = probe;
            camera.ResetAspect();
            camera.Render();

            RenderTexture.active = probe;
            readback = new Texture2D(probeWidth, probeHeight, TextureFormat.RGBA32, mipChain: false);
            readback.ReadPixels(new Rect(0f, 0f, probeWidth, probeHeight), 0, 0, recalculateMipMaps: false);
            readback.Apply(updateMipmaps: false);

            Color32[] pixels = readback.GetPixels32();
            int minX = probeWidth;
            int minY = probeHeight;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < probeHeight; y++)
            {
                int row = y * probeWidth;
                for (int x = 0; x < probeWidth; x++)
                {
                    if (pixels[row + x].a < SubjectAlpha)
                        continue;

                    if (x < minX)
                        minX = x;
                    if (x > maxX)
                        maxX = x;
                    if (y < minY)
                        minY = y;
                    if (y > maxY)
                        maxY = y;
                }
            }

            return maxX < minX ? default : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally
        {
            RenderTexture.active = previousActive;
            camera.targetTexture = null;

            if (readback != null)
                UnityEngine.Object.DestroyImmediate(readback);
            if (probe != null)
            {
                probe.Release();
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }

    /// <summary>Slides the camera along its own view axis so <paramref name="target"/> sits dead centre of the frame.</summary>
    private static void AimAt(Camera camera, Vector3 target, float distance)
    {
        camera.transform.position = target - camera.transform.forward * distance;
    }

    /// <summary>How far back the camera stands. Irrelevant to framing under an orthographic projection, so it only has to keep the whole model between the clip planes wherever the shot is aimed.</summary>
    private static float StandoffDistance(Bounds bounds)
    {
        return Mathf.Max(bounds.size.magnitude, 1f) * 2f;
    }

    /// <summary>
    /// A camera on the model's +Z side looking back at -Z: a character faces +Z, so standing on that
    /// side and looking back is what frames the front of the face rather than the back of the head.
    /// Clip planes are sized off the whole model so <see cref="FrameSubject"/> is free to aim it
    /// anywhere within the silhouette without anything falling out of range.
    /// </summary>
    private static Camera BuildCamera(Transform rig, Bounds bounds, float yaw)
    {
        GameObject cameraObject = new("ModelHeadshotCamera") { hideFlags = HideFlags.HideAndDontSave };
        cameraObject.transform.SetParent(rig, worldPositionStays: false);
        cameraObject.transform.rotation = Quaternion.LookRotation(
            Quaternion.Euler(0f, yaw, 0f) * Vector3.back,
            Vector3.up
        );

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = StandoffDistance(bounds) * 2f;
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
    private static void BuildLights(Transform rig, Vector3 center)
    {
        GameObject keyObject = new("ModelHeadshotKeyLight") { hideFlags = HideFlags.HideAndDontSave };
        keyObject.transform.SetParent(rig, worldPositionStays: false);
        keyObject.transform.position = center;
        keyObject.transform.rotation = Quaternion.Euler(35f, -150f, 0f);
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = Color.white;
        key.intensity = 1.3f;
        key.shadows = LightShadows.None;

        GameObject fillObject = new("ModelHeadshotFillLight") { hideFlags = HideFlags.HideAndDontSave };
        fillObject.transform.SetParent(rig, worldPositionStays: false);
        fillObject.transform.position = center;
        fillObject.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = Color.white;
        fill.intensity = 0.6f;
        fill.shadows = LightShadows.None;
    }
}
