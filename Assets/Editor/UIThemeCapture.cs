using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Renders every screen's interface to a PNG without entering Play mode.
///
/// Play mode is the expensive way to look at this interface: it costs a domain reload, a network
/// session and a real match before the HUD has anything on it. This opens each scene in the editor,
/// points the runtime panel at a RenderTexture, stages the content a live match would have produced,
/// and composites the result over the scene camera. What comes out is what a player would see,
/// at a fixed 1920x1080, reproducibly, in a few seconds.
///
/// Staging is the part worth reading. The HUD's unit cards, the roster's options and the character
/// gallery's filters are all built by their controllers at runtime, and controllers do not run in
/// edit mode — captured raw, three of the five screens would be empty chrome. Each entry below
/// therefore fills in a plausible mid-match or mid-selection state by hand.
/// </summary>
public static class UIThemeCapture
{
    private const string OutputDirectory = "Captures/OverwatchPass";
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>Editor frames to let the panel lay out and paint before the read-back.</summary>
    private const int SettleFrames = 12;

    private sealed class Shot
    {
        public string Name;
        public string ScenePath;
        public Action<VisualElement> Stage;
    }

    private static readonly List<Shot> Shots = new()
    {
        new Shot { Name = "01-title", ScenePath = "Assets/Scenes/Title Screen.unity" },
        new Shot { Name = "02-join", ScenePath = "Assets/Scenes/JoinGame.unity", Stage = StageJoin },
        new Shot
        {
            Name = "03-roster",
            ScenePath = "Assets/Scenes/HomeScreen.unity",
            Stage = StageRoster,
        },
        new Shot
        {
            Name = "04-characters",
            ScenePath = "Assets/Scenes/Characters.unity",
            Stage = StageCharacters,
        },
        new Shot { Name = "05-hud", ScenePath = "Assets/Scenes/Game.unity", Stage = StageHud },
        new Shot
        {
            Name = "06-results",
            ScenePath = "Assets/Scenes/Game.unity",
            Stage = StageResults,
        },
        new Shot
        {
            Name = "07-title-settings",
            ScenePath = "Assets/Scenes/Title Screen.unity",
            Stage = StageTitleSettings,
        },
    };

    // The panel only paints when the editor ticks its runtime panels, which it will not do while a
    // menu command is still on the stack. The run is therefore spread across EditorApplication
    // updates: set a scene up, let it breathe for SettleFrames, read it back, move to the next.
    private static int shotIndex;
    private static int framesWaited;
    private static UIDocument activeDocument;
    private static PanelSettings authoredPanel;
    private static PanelSettings capturePanel;
    private static RenderTexture uiTarget;
    private static System.Text.StringBuilder report;

    [MenuItem("Battle Plan/Capture UI Theme Screenshots")]
    public static void CaptureAll()
    {
        Directory.CreateDirectory(OutputDirectory);
        report = new System.Text.StringBuilder();
        shotIndex = -1;
        framesWaited = 0;
        PushRenderScale();
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-report.txt"), "running\n");
        EditorApplication.update -= Step;
        EditorApplication.update += Step;
    }

    // The shipped URP asset supersamples at 2x. Camera.Render() into a texture honours that scale
    // for the rasterised geometry but not for the texture it lands in, so the stage arrives at half
    // size in one corner of an otherwise correctly cleared frame. Pinned to 1 for the run and put
    // back afterwards; the value is restored rather than saved, so the asset on disk never moves.
    private static float pushedRenderScale = -1f;

    private static System.Reflection.PropertyInfo RenderScaleProperty(out object pipeline)
    {
        pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        return pipeline?.GetType().GetProperty("renderScale");
    }

    private static void PushRenderScale()
    {
        System.Reflection.PropertyInfo property = RenderScaleProperty(out object pipeline);
        if (property == null || !property.CanWrite)
            return;
        pushedRenderScale = (float)property.GetValue(pipeline);
        property.SetValue(pipeline, 1f);
    }

    private static void PopRenderScale()
    {
        if (pushedRenderScale < 0f)
            return;
        System.Reflection.PropertyInfo property = RenderScaleProperty(out object pipeline);
        property?.SetValue(pipeline, pushedRenderScale);
        pushedRenderScale = -1f;
    }

    private static void Step()
    {
        if (activeDocument == null)
        {
            shotIndex++;
            if (shotIndex >= Shots.Count)
            {
                EditorApplication.update -= Step;
                PopRenderScale();
                report.AppendLine("done");
                File.WriteAllText(
                    Path.Combine(OutputDirectory, "capture-report.txt"),
                    report.ToString()
                );
                Debug.Log("[UIThemeCapture]\n" + report);
                return;
            }

            try
            {
                Begin(Shots[shotIndex]);
            }
            catch (Exception e)
            {
                report.AppendLine($"{Shots[shotIndex].Name}: SETUP FAILED {e.Message}");
                Release();
            }
            framesWaited = 0;
            return;
        }

        framesWaited++;
        if (framesWaited < SettleFrames)
        {
            activeDocument.rootVisualElement?.MarkDirtyRepaint();
            return;
        }

        try
        {
            Finish(Shots[shotIndex]);
        }
        catch (Exception e)
        {
            report.AppendLine($"{Shots[shotIndex].Name}: CAPTURE FAILED {e.Message}");
        }
        Release();
    }

    private static void Begin(Shot shot)
    {
        EditorSceneManager.OpenScene(shot.ScenePath, OpenSceneMode.Single);

        UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        if (documents.Length == 0)
            throw new InvalidOperationException("no UIDocument in " + shot.ScenePath);

        activeDocument = documents[0];
        authoredPanel = activeDocument.panelSettings;

        uiTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        uiTarget.Create();

        capturePanel = UnityEngine.Object.Instantiate(authoredPanel);
        capturePanel.name = "UIThemeCapturePanel";
        capturePanel.targetTexture = uiTarget;
        capturePanel.clearColor = true;
        capturePanel.colorClearValue = new Color(0f, 0f, 0f, 0f);
        activeDocument.panelSettings = capturePanel;

        // Breakpoint classes are applied by each controller from a GeometryChangedEvent that never
        // fires here. 1920x1080 is above every threshold, so the desktop layout is the correct one.
        shot.Stage?.Invoke(activeDocument.rootVisualElement);
    }

    /// <summary>
    /// Drives the runtime panel by hand.
    ///
    /// A panel pointed at a RenderTexture is an "offscreen" panel, and outside Play mode nothing
    /// asks those to draw — the editor only pumps the panels the Game view is showing. Ticking the
    /// layout pass and then the offscreen render pass directly is the difference between a capture
    /// and a transparent frame. Both entry points are internal, hence the reflection.
    /// </summary>
    private static void ForceRender()
    {
        Type utility = typeof(PanelSettings).Assembly.GetType(
            "UnityEngine.UIElements.UIElementsRuntimeUtility"
        );
        if (utility == null)
            return;

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        utility.GetMethod("UpdatePanels", flags)?.Invoke(null, null);

        object panel = activeDocument?.rootVisualElement?.panel;
        if (panel != null)
        {
            utility.GetMethod("RenderPanel", flags)?.Invoke(null, new[] { panel, (object)true });
            utility.GetMethod("RepaintPanel", flags)?.Invoke(null, new[] { panel });
        }

        utility.GetMethod("RenderOffscreenPanels", flags)?.Invoke(null, null);
    }

    private static void Finish(Shot shot)
    {
        // Twice: the first pass resolves the layout the second one paints.
        ForceRender();
        ForceRender();

        Texture2D ui = ReadBack(uiTarget);
        Texture2D background = RenderSceneCamera();
        Texture2D composite = Composite(background, ui);

        string path = Path.Combine(OutputDirectory, shot.Name + ".png");
        File.WriteAllBytes(path, composite.EncodeToPNG());
        report.AppendLine($"{shot.Name}: wrote {path} (ui coverage {Coverage(ui):P0})");

        UnityEngine.Object.DestroyImmediate(ui);
        UnityEngine.Object.DestroyImmediate(composite);
        if (background != null)
            UnityEngine.Object.DestroyImmediate(background);
    }

    /// <summary>Fraction of the frame the panel actually painted; zero means it never rendered.</summary>
    private static float Coverage(Texture2D ui)
    {
        Color[] pixels = ui.GetPixels();
        int painted = 0;
        for (int i = 0; i < pixels.Length; i += 37)
        {
            if (pixels[i].a > 0.02f)
                painted++;
        }
        return painted / (float)((pixels.Length + 36) / 37);
    }

    private static void Release()
    {
        if (activeDocument != null && authoredPanel != null)
            activeDocument.panelSettings = authoredPanel;
        if (capturePanel != null)
            UnityEngine.Object.DestroyImmediate(capturePanel);
        if (uiTarget != null)
        {
            uiTarget.Release();
            UnityEngine.Object.DestroyImmediate(uiTarget);
        }
        activeDocument = null;
        authoredPanel = null;
        capturePanel = null;
        uiTarget = null;
    }

    private static Texture2D ReadBack(RenderTexture source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        Texture2D result = new(source.width, source.height, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        return result;
    }

    /// <summary>
    /// The stage behind the interface. Menus are composed against lit 3D characters, so a capture
    /// of the panel alone would judge white text against nothing.
    /// </summary>
    private static Texture2D RenderSceneCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Camera[] all = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
            if (all.Length == 0)
                return null;
            camera = all[0];
        }

        RenderTexture target = new(Width, Height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture previousTarget = camera.targetTexture;
        Rect previousRect = camera.rect;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        // Without this the camera keeps whatever aspect the Game view happens to be at and renders
        // a letterboxed sliver into a 16:9 texture.
        camera.aspect = Width / (float)Height;
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = previousTarget;
        camera.rect = previousRect;
        camera.ResetAspect();

        Texture2D result = ReadBack(target);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return result;
    }

    private static Texture2D Composite(Texture2D background, Texture2D ui)
    {
        Texture2D result = new(ui.width, ui.height, TextureFormat.RGB24, false);
        Color[] top = ui.GetPixels();
        Color[] under =
            background != null
                ? background.GetPixels()
                : new Color[top.Length];
        for (int i = 0; i < top.Length; i++)
        {
            float a = Mathf.Clamp01(top[i].a);
            Color blended = new(
                top[i].r * a + under[i].r * (1f - a),
                top[i].g * a + under[i].g * (1f - a),
                top[i].b * a + under[i].b * (1f - a),
                1f
            );
            top[i] = blended;
        }
        result.SetPixels(top);
        result.Apply();
        return result;
    }

    // ----------------------------------------------------------------- staging

    private static void Show(VisualElement root, string name) =>
        root.Q<VisualElement>(name)?.RemoveFromClassList("hidden");

    private static void Hide(VisualElement root, string name) =>
        root.Q<VisualElement>(name)?.AddToClassList("hidden");

    private static void SetText(VisualElement root, string name, string text)
    {
        Label label = root.Q<Label>(name);
        if (label != null)
            label.text = text;
    }

    private static List<Texture2D> LoadPortraits()
    {
        List<Texture2D> portraits = new();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Images/Portraits" }))
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AssetDatabase.GUIDToAssetPath(guid)
            );
            if (texture != null)
                portraits.Add(texture);
        }
        portraits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return portraits;
    }

    private static readonly string[] UnitNames =
    {
        "Soldier",
        "Ramrod",
        "Sniper",
        "Pogo Rider",
        "Commander",
    };

    private static readonly string[] AbilityNames =
    {
        "Grenade",
        "Shield Rush",
        "Area Lock",
        "Jump",
        "Smoke Screen",
    };

    private static readonly int[] MaxHealth = { 100, 140, 70, 90, 110 };

    private static void StageJoin(VisualElement root)
    {
        Show(root, "relay-code-panel");
        Show(root, "cancel-host-button");
        SetText(root, "relay-code-label", "BQ7HJN");
        SetText(root, "relay-status-label", "Waiting for an opponent to join.");
        SetText(root, "map-caption", "Concourse · 15 × 10 · two lanes and a contested middle");

        VisualElement mapRow = root.Q<VisualElement>("map-row");
        if (mapRow != null)
        {
            string[] boards = { "Concourse", "Foundry", "Terrace", "Switchyard" };
            for (int i = 0; i < boards.Length; i++)
            {
                Button option = new() { text = boards[i] };
                option.AddToClassList("button");
                option.AddToClassList("map-option");
                if (i == 0)
                    option.AddToClassList("button--selected");
                mapRow.Add(option);
            }
        }
    }

    private static void StageRoster(VisualElement root)
    {
        List<Texture2D> portraits = LoadPortraits();
        string[] descriptions =
        {
            "Jack of all trades.",
            "Bruiser that wants to be close.",
            "Punishes a straight line.",
            "Boing.",
            "Bends a fight around cover.",
        };

        var options = root.Q<ScrollView>("roster-options");
        VisualTreeAsset optionTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Shared/Templates/UnitOption.uxml"
        );
        if (options != null && optionTemplate != null)
        {
            for (int i = 0; i < UnitNames.Length; i++)
            {
                TemplateContainer instance = optionTemplate.Instantiate();
                Button button = instance.Q<Button>("unit-option-button");
                instance.style.flexShrink = 0f;
                SetText(instance, "unit-option-name", UnitNames[i]);
                SetText(instance, "unit-option-description", descriptions[i]);
                SetText(instance, "unit-option-ability", AbilityNames[i]);
                if (portraits.Count > 0)
                    instance.Q<VisualElement>("unit-option-portrait").style.backgroundImage =
                        new StyleBackground(portraits[i % portraits.Count]);
                if (i is 0 or 3)
                {
                    button?.AddToClassList("unit-option--selected");
                    Label status = instance.Q<Label>("unit-option-status");
                    status.text = i == 0 ? "×2" : "×1";
                    status.RemoveFromClassList("hidden");
                }
                if (i == 4)
                    button?.AddToClassList("unit-option--unavailable");
                options.Add(instance);
            }
        }

        VisualElement selected = root.Q<VisualElement>("selected-roster");
        VisualTreeAsset slotTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Shared/Templates/SelectedSlot.uxml"
        );
        if (selected != null && slotTemplate != null)
        {
            string[] filled = { "Soldier", "Soldier", "Pogo Rider", null, null };
            for (int i = 0; i < filled.Length; i++)
            {
                TemplateContainer instance = slotTemplate.Instantiate();
                Button button = instance.Q<Button>("selected-slot-button");
                SetText(instance, "selected-slot-index", (i + 1).ToString());
                if (filled[i] != null)
                {
                    button?.AddToClassList("selected-slot--filled");
                    SetText(instance, "selected-slot-name", filled[i]);
                    SetText(instance, "selected-slot-detail", "Tap to remove");
                    if (portraits.Count > 0)
                        instance.Q<VisualElement>("selected-slot-portrait").style.backgroundImage =
                            new StyleBackground(portraits[i % portraits.Count]);
                }
                selected.Add(instance);
            }
        }

        SetText(root, "roster-count-label", "Pick 2 more");
        SetText(root, "selection-status", "Two slots left.");
    }

    private static void StageCharacters(VisualElement root)
    {
        VisualElement filters = root.Q<VisualElement>("class-filters");
        if (filters != null)
        {
            string[] classes = { "All", "Assault", "Breacher", "Marksman", "Support" };
            for (int i = 0; i < classes.Length; i++)
            {
                Button filter = new() { text = classes[i] };
                filter.AddToClassList("class-filter");
                if (i == 1)
                    filter.AddToClassList("button--selected");
                filters.Add(filter);
            }
        }

        VisualElement dots = root.Q<VisualElement>("carousel-dots");
        if (dots != null)
        {
            for (int i = 0; i < 5; i++)
            {
                VisualElement dot = new();
                dot.AddToClassList("carousel-dot");
                if (i == 1)
                    dot.AddToClassList("carousel-dot--current");
                dots.Add(dot);
            }
        }

        VisualElement stats = root.Q<VisualElement>("stat-list");
        if (stats != null)
        {
            (string key, int filledPips)[] traits =
            {
                ("Health", 3),
                ("Damage", 3),
                ("Range", 4),
                ("Speed", 3),
            };
            foreach ((string key, int filledPips) in traits)
            {
                VisualElement row = new();
                row.AddToClassList("trait-row");
                Label label = new(key);
                label.AddToClassList("trait-row__key");
                row.Add(label);
                VisualElement scale = new();
                scale.AddToClassList("trait-scale");
                for (int i = 0; i < 5; i++)
                {
                    VisualElement pip = new();
                    pip.AddToClassList("trait-pip");
                    if (i < filledPips)
                        pip.AddToClassList("trait-pip--on");
                    scale.Add(pip);
                }
                row.Add(scale);
                stats.Add(row);
            }
        }
    }

    private static void StageHud(VisualElement root)
    {
        Hide(root, "deployment-overlay");
        Show(root, "planning-commit");
        Show(root, "hill-status-readout");
        root.Q<VisualElement>("screen")?.AddToClassList("koth");

        Label phase = root.Q<Label>("phase-label");
        if (phase != null)
        {
            phase.text = "Plan your orders";
            phase.AddToClassList("phase-label--friendly");
        }
        SetText(root, "timer-label", "0:18");

        Label hill = root.Q<Label>("hill-status-label");
        if (hill != null)
        {
            hill.text = "Blue holds · 2/3";
            hill.AddToClassList("hill-status--blue");
        }

        Label feedback = root.Q<Label>("target-feedback-label");
        if (feedback != null)
        {
            feedback.text = "Area Lock will cover the east lane on execution.";
            feedback.RemoveFromClassList("hidden");
            feedback.AddToClassList("target-feedback--success");
        }

        SetText(root, "planning-commit-status", "READY");
        root.Q<VisualElement>("planning-commit")?.AddToClassList("planning-commit--ready");

        FillCards(root.Q<ScrollView>("unit-cards"), false);
        FillCards(root.Q<ScrollView>("enemy-unit-cards"), true);
    }

    private static void FillCards(ScrollView container, bool enemy)
    {
        VisualTreeAsset template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Shared/Templates/UnitCard.uxml"
        );
        if (container == null || template == null)
            return;

        List<Texture2D> portraits = LoadPortraits();
        float[] fractions = { 0.72f, 1f, 0.34f, 0.9f, 0f };

        for (int i = 0; i < UnitNames.Length; i++)
        {
            VisualElement host = new();
            host.AddToClassList("unit-card-host");
            if (enemy)
                host.AddToClassList("enemy-unit-card-host");
            if (i == UnitNames.Length - 1)
                host.AddToClassList(enemy ? "enemy-unit-card-host--last" : "unit-card-host--last");

            TemplateContainer instance = template.Instantiate();
            instance.style.flexGrow = 1f;
            VisualElement card = instance.Q<VisualElement>("unit-card-root");
            card.AddToClassList(enemy ? "unit-card--enemy" : "unit-card--friendly");

            // One card per state each strip can be in, so a capture is a state sheet rather than
            // the same card photographed five times.
            bool eliminated = i == 4;
            if (enemy)
            {
                if (i == 1)
                    card.AddToClassList("unit-card--enemy-active");
                if (eliminated)
                    card.AddToClassList("unit-card--enemy-eliminated");
            }
            else
            {
                if (i == 0)
                    card.AddToClassList("unit-card--selected");
                if (i == 2)
                    card.AddToClassList("unit-card--ability");
                if (eliminated)
                    card.AddToClassList("unit-card--disabled");
            }

            SetText(instance, "unit-card-name", UnitNames[i]);

            // Only the contact cards carry health. Your own crew's is on the board, over the unit,
            // and staging it here reported a row the dock has never actually drawn.
            if (enemy)
            {
                instance.Q<VisualElement>("unit-card-health").RemoveFromClassList("hidden");
                float fraction = fractions[i];
                VisualElement fill = instance.Q<VisualElement>("unit-card-health-fill");
                fill.style.width = new Length(fraction * 100f, LengthUnit.Percent);
                fill.AddToClassList(
                    "unit-card__health-fill--" + TeamPalette.HealthClassSuffix(fraction)
                );
                SetText(
                    instance,
                    "unit-card-health-value",
                    $"{Mathf.RoundToInt(fraction * MaxHealth[i])} / {MaxHealth[i]}"
                );
            }

            SetText(
                instance,
                "unit-card-ability",
                enemy ? AbilityNames[i] + " ready" : AbilityNames[i]
            );
            if (eliminated)
                SetText(instance, "unit-card-state", "ELIMINATED");
            if (portraits.Count > 0)
                instance.Q<VisualElement>("unit-card-portrait").style.backgroundImage =
                    new StyleBackground(portraits[i % portraits.Count]);

            VisualElement flip = instance.Q<VisualElement>("unit-card-flip-indicator");
            if (enemy)
            {
                flip.AddToClassList("hidden");
            }
            else if (i == 1)
            {
                flip.AddToClassList("unit-card__flip-indicator--cooldown");
                card.AddToClassList("unit-card--ability-cooldown");
                Label cooldown = instance.Q<Label>("unit-card-cooldown");
                cooldown.text = "2";
                cooldown.RemoveFromClassList("hidden");
            }

            host.Add(instance);
            container.Add(host);
        }
    }

    private static void StageResults(VisualElement root)
    {
        StageHud(root);
        // The match is over, so the HUD must not still be running a round underneath the verdict —
        // the same state ShowResults puts the dock into at runtime.
        Hide(root, "planning-commit");
        Hide(root, "target-feedback-label");
        SetText(root, "phase-label", "Match complete");
        SetText(root, "timer-label", string.Empty);
        Show(root, "results-overlay");
        SetText(root, "results-status", "You win!");
        root.Q<Label>("results-status")?.AddToClassList("results-status--victory");
    }

    private static void StageTitleSettings(VisualElement root)
    {
        Show(root, "settings-modal");
    }
}
