using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crop convention for <see cref="ModelHeadshotRenderer"/>: the deepest the shot is allowed to
/// reach down a model, plus how much breathing room to leave around the subject it finds. A field
/// left at its default (zero) is replaced with the tuned default in
/// <see cref="ModelHeadshotRenderer"/>, so callers can write <c>default</c> for "the usual
/// headshot" or override just the field they care about.
/// </summary>
[Serializable]
public struct HeadshotFraming
{
    /// <summary>
    /// Most of the model's total height the shot will ever take, measured down from the top. The
    /// crop itself is found per model, from where that model's own gear hangs; this only stops a
    /// prop that reaches the floor — the pogo stick its rider stands on — from turning a portrait
    /// into a full-body shot. Set past the deepest real weapon on the roster, which is Salvo's
    /// launcher blade at just under seven tenths.
    /// </summary>
    public float MaxHeightFraction;

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
        MaxHeightFraction = 0.7f,
        Margin = 0.04f,
        Yaw = 22f,
    };

    /// <summary>Fills in any zero-valued field with <see cref="Default"/>'s value.</summary>
    internal HeadshotFraming Resolved()
    {
        return new HeadshotFraming
        {
            MaxHeightFraction = MaxHeightFraction > 0f ? MaxHeightFraction : Default.MaxHeightFraction,
            Margin = Margin > 0f ? Margin : Default.Margin,
            Yaw = Mathf.Abs(Yaw) > 0f ? Yaw : Default.Yaw,
        };
    }
}

/// <summary>
/// Renders a "headshot" of a 3D model by standing up a throwaway camera and light, pointing them at
/// the model from the crown down to the bottom of whatever it is holding, and reading the result
/// back into a <see cref="Texture2D"/>. Used by <c>CharacterBuilder</c> to shoot the roster's
/// portraits from the characters themselves, so a portrait can never drift from the unit it names —
/// but it only looks at what renders, so it works on any model.
/// <para>
/// Three renders go into one portrait. A side-on profile finds where the gear hangs, which is where
/// the crop ends; a small front probe measures the alpha of that crop, which gives the crown and the
/// horizontal centre exactly as the camera sees them; the third is the portrait. Measuring beats
/// arithmetic on <c>Renderer.bounds</c> here because a skinned renderer's bounds are the bind pose's
/// conservative box rather than the pixels it draws — trusting them cropped the crown off every
/// helmet and left the subject sitting off-centre.
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
    /// Portrait aspect: 4:3. The crop is decided by the character's own height — crown down to the
    /// bottom of the gun — so this only sets how much room either side of them comes with it.
    /// Sentinel is the widest thing on the roster, shield and all, and wants 1.25; this clears that
    /// with a little to spare while staying near enough to the near-square wells the cards and
    /// roster slots give a portrait. The 16:9 it replaces spent a third of every file on empty air
    /// and then showed up as a letterboxed strip everywhere but the one tile it was cut for.
    /// </summary>
    public const int DefaultWidth = 640;
    public const int DefaultHeight = 480;
    public const int DefaultResolution = 512;

    /// <summary>Probe target height. Big enough that one row is a fraction of a percent of the subject, small enough to be free.</summary>
    private const int ProbeHeight = 256;

    /// <summary>Side-profile probe height, in rows across the model's whole height. Sets how finely the bottom of a gun can be found.</summary>
    private const int ProfileHeight = 384;

    /// <summary>
    /// How far clear of the body's median front surface a row has to reach, as a fraction of the
    /// model's height, before it counts as something held rather than as the body itself. Small,
    /// because a pistol grip tucked against the chest barely clears the chest.
    /// </summary>
    private const float BodySurfaceTolerance = 0.02f;

    /// <summary>
    /// How far below the lowest row of held gear the crop lands, as a fraction of the model's
    /// height. The profile finds where a weapon stops standing clear of the body, which is a row or
    /// two above where the weapon actually ends once its grip tucks back in.
    /// </summary>
    private const float GearClearance = 0.02f;

    /// <summary>Shortest crop allowed, as a fraction of the model's height. A model holding nothing still gets a portrait rather than a slice of face.</summary>
    private const float MinCropFraction = 0.35f;

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
            FrameSubject(camera, bounds, resolved);

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
    /// Aims and sizes <paramref name="camera"/> at the subject: find where this model's own gear
    /// hangs, then frame exactly from there to the crown, plus the margin.
    /// <para>
    /// The height is the whole shot. Width follows from the target's own aspect and whatever that
    /// gives is what you get, so shoulders and elbows run off the sides of a close portrait rather
    /// than the frame pulling back to collect them — pulling back is what buries the gun halfway up
    /// a full-body shot instead of leaving it sitting on the bottom edge where it belongs.
    /// </para>
    /// </summary>
    private static void FrameSubject(Camera camera, Bounds bounds, HeadshotFraming framing)
    {
        float distance = StandoffDistance(bounds);
        float sliceBottom = FindHeldGearBottom(camera, bounds, framing.MaxHeightFraction, distance);
        float halfSlice = Mathf.Max((bounds.max.y - sliceBottom) * 0.5f, 0.01f);
        float halfSpan = Mathf.Max(
            0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z),
            halfSlice * 0.25f
        );
        Vector3 sliceCenter = new(bounds.center.x, bounds.max.y - halfSlice, bounds.center.z);

        camera.orthographicSize = halfSlice;
        AimAt(camera, sliceCenter, distance);

        int probeWidth = Mathf.Clamp(Mathf.CeilToInt(ProbeHeight * halfSpan / halfSlice), 16, 4096);
        RectInt subject = MeasureSilhouette(CapturePixels(camera, probeWidth, ProbeHeight), probeWidth, ProbeHeight);

        float offsetX = 0f;
        float offsetY = 0f;
        float halfHeight = halfSlice;

        if (subject.width > 0 && subject.height > 0)
        {
            float perPixel = 2f * halfSlice / ProbeHeight;
            offsetX = (subject.x + subject.width * 0.5f - probeWidth * 0.5f) * perPixel;
            offsetY = (subject.y + subject.height * 0.5f - ProbeHeight * 0.5f) * perPixel;
            halfHeight = subject.height * 0.5f * perPixel;
        }

        camera.orthographicSize = halfHeight * (1f + Mathf.Max(framing.Margin, 0f));
        AimAt(camera, sliceCenter + camera.transform.right * offsetX + Vector3.up * offsetY, distance);
    }

    /// <summary>
    /// World Y of the bottom of whatever this model is holding, which is where its portrait ends.
    /// Profiled from a side-on probe: for every row of the model's height, how far forward the
    /// silhouette reaches. A body's front is a smooth, shallow surface and the median row describes
    /// it, so anything standing clear of that median — a rifle held across the chest, a pistol, a
    /// launcher tube, and the face — is gear rather than body, and the lowest row of it is the
    /// bottom of the gun.
    /// <para>
    /// Profiling beats naming the weapon: half the roster carries its gun as a separate child
    /// object, half has it welded into one merged body mesh, and two of them have it baked into a
    /// skinned mesh alongside the character. All three look identical from the side.
    /// </para>
    /// <para>
    /// Clamped between <see cref="MinCropFraction"/> and <paramref name="capFraction"/> of the
    /// model's height, so neither a model holding nothing nor a prop that reaches the floor can
    /// take the shot somewhere absurd.
    /// </para>
    /// </summary>
    private static float FindHeldGearBottom(Camera camera, Bounds bounds, float capFraction, float distance)
    {
        float perPixel = bounds.size.y / ProfileHeight;
        float floorY = bounds.max.y - bounds.size.y * MinCropFraction;
        float capY = bounds.max.y - bounds.size.y * Mathf.Clamp(capFraction, MinCropFraction, 1f);
        if (perPixel <= 0f)
            return capY;

        Quaternion portraitRotation = camera.transform.rotation;
        int profileWidth = Mathf.Clamp(Mathf.CeilToInt(ProfileHeight * bounds.size.z / bounds.size.y), 8, 4096);

        try
        {
            // Looking down -X with world +Z to the right, so a pixel column is a depth and a pixel
            // row is a height.
            camera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            camera.orthographicSize = bounds.extents.y;
            AimAt(camera, bounds.center, distance);

            Color32[] pixels = CapturePixels(camera, profileWidth, ProfileHeight);
            int[] front = new int[ProfileHeight];
            int occupied = 0;

            for (int row = 0; row < ProfileHeight; row++)
            {
                front[row] = -1;
                int offset = row * profileWidth;
                for (int column = profileWidth - 1; column >= 0; column--)
                {
                    if (pixels[offset + column].a < SubjectAlpha)
                        continue;

                    front[row] = column;
                    occupied++;
                    break;
                }
            }

            if (occupied == 0)
                return capY;

            int[] surface = new int[occupied];
            for (int row = 0, next = 0; row < ProfileHeight; row++)
            {
                if (front[row] >= 0)
                    surface[next++] = front[row];
            }
            Array.Sort(surface);
            int bodySurface = surface[occupied / 2];
            int gearSurface = bodySurface + Mathf.CeilToInt(ProfileHeight * BodySurfaceTolerance);

            int capRow = Mathf.Clamp(Mathf.FloorToInt((capY - bounds.min.y) / perPixel), 0, ProfileHeight - 1);
            for (int row = capRow; row < ProfileHeight; row++)
            {
                if (front[row] <= gearSurface)
                    continue;

                float gearBottom = bounds.min.y + row * perPixel - bounds.size.y * GearClearance;
                return Mathf.Clamp(gearBottom, capY, floorY);
            }

            return capY;
        }
        finally
        {
            camera.transform.rotation = portraitRotation;
        }
    }

    /// <summary>Tight pixel box of everything <paramref name="pixels"/> holds, or an empty box if it holds nothing.</summary>
    private static RectInt MeasureSilhouette(Color32[] pixels, int width, int height)
    {
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
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

    /// <summary>
    /// Renders the camera's current frame into a throwaway target and hands back the pixels. Rows
    /// run bottom-up, matching <see cref="Texture2D.GetPixels32"/>.
    /// </summary>
    private static Color32[] CapturePixels(Camera camera, int width, int height)
    {
        RenderTexture probe = null;
        Texture2D readback = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            probe = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "ModelHeadshotProbe",
                antiAliasing = 1,
            };
            probe.Create();
            camera.targetTexture = probe;
            camera.ResetAspect();
            camera.Render();

            RenderTexture.active = probe;
            readback = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            readback.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            readback.Apply(updateMipmaps: false);
            return readback.GetPixels32();
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
