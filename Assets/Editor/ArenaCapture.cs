using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders the board preview and the unit portraits from the real assets rather than drawing
/// approximations of them.
///
/// The point is correctness that survives change: a portrait rendered from the actual prefab under
/// the actual materials cannot desync from the unit on the field, and a board preview rendered from
/// the actual scene cannot claim a layout the game does not have. Both re-run after any art change
/// and both are reproducible — every camera, light, framing and output value below is a named
/// constant, and nothing is hand-placed.
/// </summary>
public static class ArenaCapture
{
    // ================================================================================
    // CAPTURE SETTINGS — camera, lighting, framing and output size for both rigs.
    // ================================================================================

    // ---- board preview ----------------------------------------------------------
    // Dimensions are fixed by the consumer: the character-select map plate and the
    // smoke test both expect 1080 x 720.
    internal const string PreviewPath = "Assets/Images/MapPreview.png";
    internal const int PreviewWidth = 1080;
    internal const int PreviewHeight = 720;

    // A three-quarter view, not the gameplay camera. The game camera sits at 73 degrees
    // because that is the angle that makes grid cells unambiguous to click; a preview has
    // the opposite job, which is to show at a glance that the board has height, cover and
    // a contested middle. Dropping to 46 degrees and swinging 24 off-axis puts cover in
    // profile against its own cast shadow, which is what communicates "blocker".
    private const float PreviewPitch = 46f;
    private const float PreviewYaw = 24f;
    private const float PreviewFov = 38f;
    // Fraction of the frame left empty around the board's bounding sphere.
    private const float PreviewMargin = 1.06f;

    // ---- unit portraits ---------------------------------------------------------
    // 4:3 to match the .unit-option__portrait plate, rendered at well over its 150 px
    // display height so the same file serves the card, the selected slot and the HUD.
    private const string PortraitFolder = "Assets/Images/Portraits/";
    private const int PortraitWidth = 512;
    private const int PortraitHeight = 384;

    // Three-quarter again, and for the same reason §11.5 asks for silhouette-only
    // identification: a straight-on view is the one angle at which a rifle, a launcher
    // and a pogo spring all read as a vertical line. Swinging round shows profile and
    // front at once, which is where the differences live.
    private const float PortraitYaw = 28f;
    private const float PortraitPitch = 10f;
    private const float PortraitFov = 30f;
    private const float PortraitMargin = 1.18f;

    // The portrait rig reproduces the board's own light rather than inventing a studio
    // one, so a unit looks in the card the way it looks on the field. Only the key's
    // bearing changes: it is expressed relative to the camera so every unit is lit the
    // same way regardless of which way its mesh happens to face.
    private const float PortraitKeyYawOffset = -35f;
    private const float PortraitKeyPitch = 38f;
    private const float PortraitFillIntensity = 0.35f;
    private const float PortraitFillPitch = -10f;
    private const float PortraitFillYawOffset = 130f;

    // Rendered onto nothing, so the card's own plate colour shows through and the
    // portrait cannot fight whatever the UI puts behind it.
    private static readonly Color PortraitClear = new(0f, 0f, 0f, 0f);

    // Somewhere the scene is not. The rig lives in its own additive scene, but keeping it
    // off the origin also keeps it clear of anything a loaded scene has near zero.
    private static readonly Vector3 RigOrigin = new(0f, 1000f, 0f);

    // Things the unit prefab carries for play, not for the figure. The vision cone in
    // particular is a large ground-plane fan that is enabled on every variant — left in, it
    // both appears in the card and dominates the bounding box, pushing the camera so far back
    // the unit becomes a speck. The health canvas and the base puck go for the same reason:
    // a portrait is of the model, and these are the board's furniture around it.
    private static readonly string[] PortraitHiddenObjects =
    {
        "VisionCone",
        "UnitCanvas",
        "BasePuck",
        "BasePuckRim",
    };

    // ---- silhouette contact sheet -----------------------------------------------
    // ArtDirection §11.5 requires all five units to be identifiable by silhouette alone,
    // at game-camera distance, with colour removed. A desaturated portrait does not run
    // that test — it still carries value, material and internal detail, all of which the
    // eye will use. A flat fill removes every channel except outline, which is the only
    // way to see whether the shapes are actually distinct.
    private const string SilhouettePath = "Assets/Images/Portraits/SilhouetteSheet.png";
    private const int SilhouetteCellWidth = 256;
    private const int SilhouetteCellHeight = 384;

    // Framed at the game camera's own field of view and pitch, because §11.5 asks the
    // question at game-camera distance and a shape that separates in a portrait three-
    // quarter view may not separate from above.
    private const float SilhouettePitch = 73.1f;
    private const float SilhouetteYaw = 0f;
    private const float SilhouetteFov = 60f;
    private const float SilhouetteMargin = 1.10f;

    private static readonly Color SilhouetteFill = new(0.082f, 0.102f, 0.125f, 1f);   // --bp-ink
    private static readonly Color SilhouetteGround = new(0.624f, 0.667f, 0.690f, 1f); // --bp-ground-a

    private static readonly string[] UnitPrefabs =
    {
        "Assets/Prefabs/Units/Commander.prefab",
        "Assets/Prefabs/Units/PogoRider.prefab",
        "Assets/Prefabs/Units/Shotgunner.prefab",
        "Assets/Prefabs/Units/Sniper.prefab",
        "Assets/Prefabs/Units/Soldier.prefab",
    };

    // ============================ end capture settings ==============================

    [MenuItem("Battle Plan/Art/Capture Board Preview", false, 40)]
    public static void CaptureBoardPreview()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ArenaBuilder.ScenePath)
        {
            EditorUtility.DisplayDialog(
                "Board preview",
                $"Open {ArenaBuilder.ScenePath} first. The preview renders the real board, so it has "
                + "to be looking at it.",
                "OK");
            return;
        }

        Bounds board = BoardBounds();
        var rigRoot = new GameObject("~PreviewRig") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // The scene's own sun, materials and volume are left alone — the preview is a
            // photograph of the board as it stands, not a restaging of it. Only the camera
            // is ours, and it clears to the sky so the board sits in its own daylight.
            Camera cam = CreateCamera(rigRoot, PreviewFov, RenderSettings.ambientSkyColor,
                CameraClearFlags.SolidColor);
            FrameBounds(cam, board, PreviewPitch, PreviewYaw, PreviewFov, PreviewMargin,
                PreviewWidth / (float)PreviewHeight);

            byte[] png = Render(cam, PreviewWidth, PreviewHeight);
            // A plain texture: the plate samples this directly and wants no Sprite sub-asset.
            WritePng(PreviewPath, png, TextureImporterType.Default, alphaIsTransparency: false);
        }
        finally
        {
            Object.DestroyImmediate(rigRoot);
        }

        Debug.Log($"[Capture] Board preview written to {PreviewPath} at {PreviewWidth}x{PreviewHeight}. "
                  + "The smoke test asserts the imported size and that the file holds a continuous-tone "
                  + "render rather than a flat fill, so re-run it after any recapture.");
    }

    [MenuItem("Battle Plan/Art/Capture Unit Portraits", false, 41)]
    public static void CaptureUnitPortraits()
    {
        EnsureFolder(PortraitFolder);

        Scene previousActive = SceneManager.GetActiveScene();
        Scene rig = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var written = new List<string>();

        try
        {
            SceneManager.SetActiveScene(rig);
            ApplyRigEnvironment();

            var rigRoot = new GameObject("PortraitRig");
            SceneManager.MoveGameObjectToScene(rigRoot, rig);
            rigRoot.transform.position = RigOrigin;

            Camera cam = CreateCamera(rigRoot, PortraitFov, PortraitClear, CameraClearFlags.SolidColor);

            foreach (string prefabPath in UnitPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[Capture] No unit prefab at {prefabPath}.");
                    continue;
                }

                // A plain copy rather than a prefab instance: hiding the play-only objects
                // below would otherwise register as instance overrides, and there is no reason
                // for a capture to be able to touch the asset at all. This also matches how the
                // existing edit-mode tests instantiate unitModel.
                var instance = Object.Instantiate(prefab);
                try
                {
                    SceneManager.MoveGameObjectToScene(instance, rig);
                    instance.transform.SetPositionAndRotation(RigOrigin, Quaternion.identity);
                    HidePlayOnlyObjects(instance);

                    if (!TryGetRendererBounds(instance, out Bounds bounds, out int rendererCount))
                    {
                        Debug.LogWarning($"[Capture] '{prefab.name}' has no active renderers; skipped. "
                                         + "A unit with nothing to draw will also be invisible on the board.");
                        continue;
                    }

                    FrameBounds(cam, bounds, PortraitPitch, PortraitYaw, PortraitFov, PortraitMargin,
                        PortraitWidth / (float)PortraitHeight);
                    AimRigLights(rigRoot, cam);

                    byte[] png = Render(cam, PortraitWidth, PortraitHeight);
                    string path = PortraitFolder + prefab.name + ".png";
                    // A Sprite: UnitData.unitSprite and .abilitySprite are Sprite fields.
                    WritePng(path, png, TextureImporterType.Sprite, alphaIsTransparency: true);

                    // The renderer count is the evidence that the nested model instances were
                    // picked up: a unit reduced to its base prefab would report almost none.
                    written.Add($"{prefab.name} ({rendererCount} renderers, "
                                + $"{bounds.size.y:F2} m tall)");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }
        finally
        {
            if (previousActive.IsValid())
                SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(rig, true);
        }

        Debug.Log($"[Capture] Portraits written for {written.Count} of {UnitPrefabs.Length} units "
                  + $"({string.Join(", ", written)}) at {PortraitWidth}x{PortraitHeight} into {PortraitFolder}.");
    }

    [MenuItem("Battle Plan/Art/Capture Silhouette Sheet", false, 42)]
    public static void CaptureSilhouetteSheet()
    {
        EnsureFolder(PortraitFolder);

        Scene previousActive = SceneManager.GetActiveScene();
        Scene rig = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var flat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var cells = new List<Color32[]>();
        var names = new List<string>();

        try
        {
            SceneManager.SetActiveScene(rig);
            flat.SetColor("_BaseColor", SilhouetteFill);

            var rigRoot = new GameObject("SilhouetteRig");
            SceneManager.MoveGameObjectToScene(rigRoot, rig);
            rigRoot.transform.position = RigOrigin;
            Camera cam = CreateCamera(rigRoot, SilhouetteFov, SilhouetteGround,
                CameraClearFlags.SolidColor);

            foreach (string prefabPath in UnitPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var instance = Object.Instantiate(prefab);
                try
                {
                    SceneManager.MoveGameObjectToScene(instance, rig);
                    instance.transform.SetPositionAndRotation(RigOrigin, Quaternion.identity);
                    HidePlayOnlyObjects(instance);
                    Flatten(instance, flat);

                    if (!TryGetRendererBounds(instance, out Bounds bounds, out _)) continue;

                    FrameBounds(cam, bounds, SilhouettePitch, SilhouetteYaw, SilhouetteFov,
                        SilhouetteMargin, SilhouetteCellWidth / (float)SilhouetteCellHeight);

                    cells.Add(RenderPixels(cam, SilhouetteCellWidth, SilhouetteCellHeight));
                    names.Add(prefab.name);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(flat);
            if (previousActive.IsValid())
                SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(rig, true);
        }

        if (cells.Count == 0)
        {
            Debug.LogError("[Capture] No units rendered; silhouette sheet not written.");
            return;
        }

        // A review artefact, read in the Inspector rather than bound by the UI.
        WritePng(SilhouettePath, Tile(cells), TextureImporterType.Default, alphaIsTransparency: false);
        Debug.Log($"[Capture] Silhouette sheet written to {SilhouettePath} — {cells.Count} units "
                  + $"({string.Join(", ", names)}) at the game camera's 73.1° pitch and 60° FOV. "
                  + "§11.5 passes only if all five read as different shapes here.");
    }

    /// <summary>
    /// Replaces every material on the instance with one flat fill, so the only surviving channel is
    /// outline. Submesh slots are filled individually — a renderer left with its original second
    /// material would reintroduce exactly the internal detail this is trying to remove.
    /// </summary>
    private static void Flatten(GameObject root, Material flat)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var slots = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = flat;
            renderer.sharedMaterials = slots;
        }
    }

    /// <summary>Lays the rendered cells out in one row, left to right in roster order.</summary>
    private static byte[] Tile(List<Color32[]> cells)
    {
        int width = SilhouetteCellWidth * cells.Count;
        var sheet = new Texture2D(width, SilhouetteCellHeight, TextureFormat.RGBA32, false, false);
        try
        {
            var pixels = new Color32[width * SilhouetteCellHeight];
            for (int cell = 0; cell < cells.Count; cell++)
            {
                for (int y = 0; y < SilhouetteCellHeight; y++)
                {
                    System.Array.Copy(cells[cell], y * SilhouetteCellWidth,
                        pixels, y * width + cell * SilhouetteCellWidth, SilhouetteCellWidth);
                }
            }
            sheet.SetPixels32(pixels);
            sheet.Apply(false, false);
            return sheet.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(sheet);
        }
    }

    // ================================================================= irradiance probe

    // The orientation transfer table is the one thing in the art direction that cannot be settled
    // by argument. Its dominant term is how Unity treats the gradient-ambient colours — reading
    // them as linear or as authored sRGB moves the shaded row by 1.75x, which is larger than the
    // whole horizontal-to-vertical effect the table exists to describe. So it gets measured off the
    // live scene instead of derived, and the derivation is checked against the measurement.
    private const float ProbeAlbedoGrey = 0.5f;
    private const int ProbeResolution = 16;
    private const float ProbeQuadSize = 4f;
    private const float ProbeCameraDistance = 6f;

    private static readonly (string Name, Vector3 Normal)[] ProbeFaces =
    {
        ("horizontal (up)",        Vector3.up),
        ("vertical, sun-facing",   Vector3.left),
        ("vertical, side-on",      Vector3.forward),
        ("vertical, away from sun", Vector3.right),
    };

    [MenuItem("Battle Plan/Art/Measure Irradiance Transfer", false, 43)]
    public static void MeasureIrradianceTransfer()
    {
        // Opened rather than synthesised: the point is to measure the sun, ambient and colour space
        // the board actually has, not the ones this file believes it has.
        Scene scene = EditorSceneManager.OpenScene(ArenaBuilder.ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Probe] Could not open {ArenaBuilder.ScenePath}.");
            return;
        }

        var rig = new GameObject("IrradianceProbe");
        var flat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var readings = new List<(string Name, float Value)>();

        try
        {
            rig.transform.position = RigOrigin;
            flat.SetColor("_BaseColor", new Color(ProbeAlbedoGrey, ProbeAlbedoGrey, ProbeAlbedoGrey, 1f));
            flat.SetFloat("_Metallic", 0f);
            flat.SetFloat("_Smoothness", 0f);
            flat.SetFloat("_SpecularHighlights", 0f);
            flat.SetFloat("_EnvironmentReflections", 0f);

            foreach ((string name, Vector3 normal) in ProbeFaces)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var camGo = new GameObject("ProbeCamera");
                try
                {
                    quad.transform.SetParent(rig.transform, false);
                    quad.transform.localScale = Vector3.one * ProbeQuadSize;
                    // Unity's Quad mesh carries a -Z normal, so aiming its forward at -n puts the
                    // lit face along +n. Getting this backwards would measure the unlit side and
                    // silently report ambient-only for every row.
                    quad.transform.rotation = Quaternion.LookRotation(-normal);
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.GetComponent<MeshRenderer>().sharedMaterial = flat;

                    camGo.transform.SetParent(rig.transform, false);
                    var cam = camGo.AddComponent<Camera>();
                    cam.transform.position = RigOrigin + normal * ProbeCameraDistance;
                    cam.transform.rotation = Quaternion.LookRotation(-normal);
                    cam.orthographic = true;
                    cam.orthographicSize = ProbeQuadSize * 0.25f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                    cam.allowHDR = false;
                    // Post would grade the readback and make it a measurement of the colour grade
                    // rather than of the lighting.
                    var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
                    urp.renderPostProcessing = false;
                    urp.antialiasing = AntialiasingMode.None;

                    Color32[] pixels = RenderPixels(cam, ProbeResolution, ProbeResolution);
                    // The Color32 -> Color conversion already normalises to 0..1; dividing again
                    // drops every reading into the sRGB toe, where GammaToLinearSpace is linear
                    // and the factors silently degenerate into un-linearised display ratios.
                    Color centre = pixels[ProbeResolution * (ProbeResolution / 2) + ProbeResolution / 2];
                    readings.Add((name, centre.r));
                }
                finally
                {
                    Object.DestroyImmediate(camGo);
                    Object.DestroyImmediate(quad);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(flat);
            Object.DestroyImmediate(rig);
        }

        if (readings.Count == 0 || readings[0].Value <= 0f)
        {
            Debug.LogError("[Probe] Horizontal reference read as black; nothing to normalise against.");
            return;
        }

        float reference = Mathf.GammaToLinearSpace(readings[0].Value);
        var report = new System.Text.StringBuilder(
            $"[Probe] Measured orientation transfer off {Path.GetFileName(ArenaBuilder.ScenePath)}, "
            + $"albedo {ProbeAlbedoGrey:0.00} grey, post disabled:\n");
        foreach ((string name, float value) in readings)
        {
            float factor = Mathf.GammaToLinearSpace(value) / reference;
            report.AppendLine($"    {name,-24} display {value:0.000}   factor {factor:0.000}");
        }
        report.AppendLine(
            "  Discriminator: a side-on factor near 0.28 means Unity reads the gradient-ambient "
            + "colours as linear; near 0.16 means it reads them as authored sRGB. Those are the two "
            + "candidate derivations, and they are far enough apart that one reading settles it.");
        Debug.Log(report.ToString());
    }

    // ================================================================= rig construction

    /// <summary>
    /// Mirrors the board's environment into the portrait scene. A portrait lit by Unity's default
    /// grey ambient would show a different material than the one the player meets on the field,
    /// which is the exact desync rendering from the real assets is meant to prevent.
    /// </summary>
    private static void ApplyRigEnvironment()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ArenaBuilder.RecordedAmbientSky;
        RenderSettings.ambientEquatorColor = ArenaBuilder.RecordedAmbientEquator;
        RenderSettings.ambientGroundColor = ArenaBuilder.RecordedAmbientGround;
        RenderSettings.ambientIntensity = ArenaBuilder.RecordedAmbientIntensity;
        RenderSettings.fog = false;
    }

    private static Camera CreateCamera(GameObject parent, float fov, Color background,
        CameraClearFlags clearFlags)
    {
        var go = new GameObject("CaptureCamera");
        go.transform.SetParent(parent.transform, false);

        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.clearFlags = clearFlags;
        cam.backgroundColor = background;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        return cam;
    }

    /// <summary>
    /// Key and fill, both placed relative to the camera so the lighting is identical for every
    /// subject no matter which way its mesh faces. Intensity and colour come from the board's sun.
    /// </summary>
    private static void AimRigLights(GameObject parent, Camera cam)
    {
        float cameraYaw = cam.transform.eulerAngles.y;

        Light key = FindOrCreateLight(parent, "PortraitKey");
        key.transform.rotation = Quaternion.Euler(PortraitKeyPitch, cameraYaw + PortraitKeyYawOffset, 0f);
        key.color = ArenaBuilder.RecordedSunColour;
        key.intensity = ArenaBuilder.RecordedSunIntensity;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = ArenaBuilder.SunShadowStrength;

        Light fill = FindOrCreateLight(parent, "PortraitFill");
        fill.transform.rotation = Quaternion.Euler(PortraitFillPitch, cameraYaw + PortraitFillYawOffset, 0f);
        fill.color = ArenaBuilder.RecordedAmbientSky;
        fill.intensity = PortraitFillIntensity;
        fill.shadows = LightShadows.None;
    }

    private static Light FindOrCreateLight(GameObject parent, string name)
    {
        Transform existing = parent.transform.Find(name);
        if (existing != null)
            return existing.GetComponent<Light>();

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        return light;
    }

    // ================================================================= framing

    /// <summary>
    /// Pulls the camera back to exactly the distance at which the subject's bounding box fits, by
    /// solving each of the eight corners against both frustum axes.
    ///
    /// A bounding sphere would be simpler but wrong here: the board is a flat 15x10 rectangle, so a
    /// sphere around it is driven by a diagonal that nothing occupies and the render comes back
    /// two-thirds empty sky. Fitting the box keeps the framing tight while staying entirely derived,
    /// which means it re-solves itself if the grid or the kit ever changes size.
    /// </summary>
    private static void FrameBounds(Camera cam, Bounds bounds, float pitch, float yaw, float fov,
        float margin, float aspect)
    {
        cam.aspect = aspect;
        cam.fieldOfView = fov;

        float tanVertical = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        float tanHorizontal = tanVertical * aspect;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 extents = bounds.extents;

        float distance = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);

            Vector3 local = inverse * offset;
            distance = Mathf.Max(distance, Mathf.Abs(local.y) / tanVertical - local.z);
            distance = Mathf.Max(distance, Mathf.Abs(local.x) / tanHorizontal - local.z);
        }
        distance *= margin;

        cam.transform.SetPositionAndRotation(
            bounds.center - rotation * Vector3.forward * distance,
            rotation);

        float radius = extents.magnitude;
        cam.nearClipPlane = Mathf.Max(0.05f, distance - radius * 2f);
        cam.farClipPlane = distance + radius * 4f;
    }

    /// <summary>
    /// The board's real extent, taken from the grid rather than from whatever renderers happen to
    /// exist, so backdrop silhouettes and the rail cannot pull the framing off the playable area.
    /// </summary>
    private static Bounds BoardBounds()
    {
        const float Cell = ArenaBuilder.Cell;
        float width = GridSystem.ColumnCount * Cell;
        float depth = GridSystem.RowCount * Cell;
        var centre = new Vector3((GridSystem.ColumnCount - 1) * Cell * 0.5f, 0.6f,
            (GridSystem.RowCount - 1) * Cell * 0.5f);
        // A little vertical extent so cover and the rail are inside the sphere, not clipped by it.
        return new Bounds(centre, new Vector3(width, 3.2f, depth));
    }

    /// <summary>
    /// Deactivates the play-only objects by name across the whole composed hierarchy. Walking every
    /// transform rather than the root's direct children matters because the models arrive as nested
    /// prefab instances, so depth is not something this can assume.
    /// </summary>
    private static void HidePlayOnlyObjects(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(PortraitHiddenObjects, child.name) >= 0)
                child.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Bounds over every active renderer in the composed hierarchy. This deliberately does not
    /// care how deep a renderer sits or whether it came from a nested prefab instance — the unit
    /// models are attached that way, and anything that special-cased depth would miss them.
    /// </summary>
    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds, out int count)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy)
            .ToArray();

        count = renderers.Length;
        if (count == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < count; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // ================================================================= output

    private static byte[] Render(Camera cam, int width, int height)
    {
        Texture2D readback = RenderToTexture(cam, width, height);
        try { return readback.EncodeToPNG(); }
        finally { Object.DestroyImmediate(readback); }
    }

    private static Color32[] RenderPixels(Camera cam, int width, int height)
    {
        Texture2D readback = RenderToTexture(cam, width, height);
        try { return readback.GetPixels32(); }
        finally { Object.DestroyImmediate(readback); }
    }

    private static Texture2D RenderToTexture(Camera cam, int width, int height)
    {
        var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
        {
            msaaSamples = 8,
            sRGB = true,
        };

        // Apple Silicon Metal caps this descriptor at 4 samples. Asking for 8 anyway gets an
        // attachment with 4 while URP still requests 8, and the resulting mismatch aborts the
        // pass and reads back pure black rather than failing loudly.
        descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);

        RenderTexture target = RenderTexture.GetTemporary(descriptor);
        RenderTexture previous = RenderTexture.active;
        var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, false);

        try
        {
            cam.targetTexture = target;
            cam.Render();

            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            readback.Apply(false, false);
            return readback;
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>
    /// Writes the bytes and then states the import, because a capture is only as good as the asset
    /// it becomes.
    ///
    /// The texture type is a parameter rather than a constant because the two consumers want
    /// genuinely different assets and neither is a sensible default for the other: the board plate
    /// is sampled as a plain texture, while <c>UnitData.unitSprite</c> is a <c>Sprite</c> field and
    /// resolves to null unless the import produces a Sprite sub-asset. Hardcoding one type here is
    /// what left the portraits unusable.
    /// </summary>
    private static void WritePng(string assetPath, byte[] png, TextureImporterType textureType,
        bool alphaIsTransparency)
    {
        File.WriteAllBytes(assetPath, png);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = textureType;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = alphaIsTransparency;
        importer.alphaSource = alphaIsTransparency
            ? TextureImporterAlphaSource.FromInput
            : TextureImporterAlphaSource.None;
        // Every rig here composes to an aspect the consumer depends on, and none of the outputs is
        // a power of two. Left at ToNearest, a 512x384 portrait imports as 512x512 and a 1080x720
        // plate as 1024x512 — the framing solved for in FrameBounds is resampled away, silently,
        // after the render succeeded.
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;

        if (textureType == TextureImporterType.Sprite)
        {
            // Single is the mode that emits the sprite the UI binds to; Multiple would emit named
            // sub-assets instead and Polygon would generate its own geometry.
            importer.spriteImportMode = SpriteImportMode.Single;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            // A tight mesh trims the transparent margin, which on a portrait is most of the frame.
            // Full rect keeps the sprite exactly the rectangle the camera composed, so the subject
            // stays where the framing put it instead of being re-centred on its own silhouette.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }

        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder.TrimEnd('/')))
            return;

        string parent = Path.GetDirectoryName(folder.TrimEnd('/'))?.Replace('\\', '/');
        string leaf = Path.GetFileName(folder.TrimEnd('/'));
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
