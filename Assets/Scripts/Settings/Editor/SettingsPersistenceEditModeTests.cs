using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[TestFixture]
public class SettingsPersistenceEditModeTests
{
    private const string SettingsSourcePath = "Assets/Scripts/Settings/GameSettings.cs";
    private const string AudioManagerSourcePath = "Assets/Scripts/Camera/AudioManager.cs";
    private const string ControllerSourcePath =
        "Assets/Scripts/Menus/TitleScreenUIController.cs";
    private const string HudControllerSourcePath = "Assets/Scripts/Menus/GameHUDController.cs";
    private const string GameLoopSourcePath = "Assets/Scripts/GameManager/GameLoop.cs";
    private const string BinderSourcePath = "Assets/Scripts/Settings/SettingsPanelBinder.cs";
    private const string TitleMarkupPath = "Assets/UI/Title/TitleScreen.uxml";
    private const string TitleStylePath = "Assets/UI/Title/TitleScreen.uss";
    private const string HudMarkupPath = "Assets/UI/Game/GameHUD.uxml";
    private const string ToyboxStylePath = "Assets/UI/Shared/TacticalToybox.uss";
    private const string SettingsStylePath = "Assets/UI/Shared/Settings.uss";
    private const string TokenStylePath = "Assets/UI/Shared/TacticalToyboxTokens.uss";
    private const string LifetimeSourcePath = "Assets/Scripts/Settings/GameSettingsLifetime.cs";
    private const string MusicPrefabPath = "Assets/Prefabs/Music.prefab";
    private const string EffectsPrefabPath = "Assets/Prefabs/SFX.prefab";
    private const string AssetRoot = "Assets";
    private const string ScriptRoot = "Assets/Scripts";

    private const string TestSourcePath =
        "Assets/Scripts/Settings/Editor/SettingsPersistenceEditModeTests.cs";

    /// <summary>The only prefabs whose AudioSource is authored into a scene, not spawned.</summary>
    private static readonly string[] AudioPrefabPaths = { MusicPrefabPath, EffectsPrefabPath };

    /// <summary>The files allowed to name the detached call, because they warn against it.</summary>
    private static readonly string[] DetachedPlayExemptPaths =
    {
        AudioManagerSourcePath,
        TestSourcePath,
    };

    private static readonly string[] VolumeSliderNames =
    {
        "master-volume-slider",
        "music-volume-slider",
        "sfx-volume-slider",
    };

    private static readonly string[] VolumeReadoutNames =
    {
        "master-volume-value",
        "music-volume-value",
        "sfx-volume-value",
    };

    /// <summary>
    /// The title panel and the in-match sheet are the same panel, so every contract below is asserted
    /// against both. Sharing the element names is what lets one binder wire either one.
    /// </summary>
    private static readonly object[] SettingsPanels =
    {
        new object[] { TitleMarkupPath, "settings-modal" },
        new object[] { HudMarkupPath, "settings-overlay" },
    };

    private readonly List<GameObject> temporaryObjects = new();

    private GameSettings storedSettings;
    private bool hadStoredSettings;
    private float editorListenerVolume;
    private int editorQualityLevel;

    /// <summary>
    /// These tests write the same PlayerPrefs and global audio state the editor itself uses, so the
    /// developer's own choices are captured and put back afterwards.
    /// </summary>
    [SetUp]
    public void StartFromAFirstRun()
    {
        editorListenerVolume = AudioListener.volume;
        editorQualityLevel = QualitySettings.GetQualityLevel();
        hadStoredSettings = PlayerPrefs.HasKey(GameSettings.MasterVolumeKey);
        storedSettings = GameSettings.Current;

        DeleteStoredSettings();
        GameSettings.Reload();
        AudioManager.ClearRuntimeState();
    }

    [TearDown]
    public void RestoreTheDevelopersSettings()
    {
        foreach (GameObject temporary in temporaryObjects)
        {
            if (temporary != null)
                UnityEngine.Object.DestroyImmediate(temporary);
        }

        temporaryObjects.Clear();
        AudioManager.ClearRuntimeState();

        if (hadStoredSettings)
        {
            PlayerPrefs.SetFloat(GameSettings.MasterVolumeKey, storedSettings.masterVolume);
            PlayerPrefs.SetFloat(GameSettings.MusicVolumeKey, storedSettings.musicVolume);
            PlayerPrefs.SetFloat(GameSettings.SfxVolumeKey, storedSettings.sfxVolume);
            PlayerPrefs.SetInt(GameSettings.QualityLevelKey, storedSettings.qualityLevel);
            PlayerPrefs.Save();
        }
        else
        {
            DeleteStoredSettings();
        }

        GameSettings.Reload();
        QualitySettings.SetQualityLevel(editorQualityLevel, false);
        AudioListener.volume = editorListenerVolume;
    }

    [Test]
    public void FirstRunKeepsTheAuthoredMixAndTheProjectQualityLevel()
    {
        Assert.That(GameSettings.Current, Is.EqualTo(GameSettings.Default));
        Assert.That(GameSettings.Current.masterVolume, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(GameSettings.Current.musicVolume, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(GameSettings.Current.sfxVolume, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(
            GameSettings.Current.qualityLevel,
            Is.EqualTo(GameSettings.UnsetQualityLevel),
            "Nothing is stored until the player picks a level, so the project default stands."
        );
        Assert.That(
            GameSettings.Current.ResolvedQualityLevel,
            Is.EqualTo(QualitySettings.GetQualityLevel())
        );
        Assert.That(AudioListener.volume, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void VolumesAndQualityRoundTripThroughPlayerPrefs()
    {
        GameSettings.SetMasterVolume(0.42f);
        GameSettings.SetMusicVolume(0.15f);
        GameSettings.SetSfxVolume(0.9f);
        GameSettings.SetQualityLevel(0);
        GameSettings.Flush();

        GameSettings.Reload();

        Assert.That(GameSettings.Current.masterVolume, Is.EqualTo(0.42f).Within(0.0001f));
        Assert.That(GameSettings.Current.musicVolume, Is.EqualTo(0.15f).Within(0.0001f));
        Assert.That(GameSettings.Current.sfxVolume, Is.EqualTo(0.9f).Within(0.0001f));
        Assert.That(GameSettings.Current.qualityLevel, Is.EqualTo(0));
        Assert.That(
            QualitySettings.GetQualityLevel(),
            Is.EqualTo(0),
            "A stored level that is not applied on load is the bug this replaces."
        );
        Assert.That(AudioListener.volume, Is.EqualTo(0.42f).Within(0.0001f));
    }

    [Test]
    public void StoredKeysMatchTheShippedNames()
    {
        Assert.That(GameSettings.MasterVolumeKey, Is.EqualTo("BattlePlan.Audio.MasterVolume"));
        Assert.That(GameSettings.MusicVolumeKey, Is.EqualTo("BattlePlan.Audio.MusicVolume"));
        Assert.That(GameSettings.SfxVolumeKey, Is.EqualTo("BattlePlan.Audio.SfxVolume"));
        Assert.That(GameSettings.QualityLevelKey, Is.EqualTo("BattlePlan.Graphics.QualityLevel"));

        GameSettings.SetMusicVolume(0.5f);
        GameSettings.Flush();

        Assert.That(
            PlayerPrefs.GetFloat(GameSettings.MusicVolumeKey, -1f),
            Is.EqualTo(0.5f).Within(0.0001f),
            "Renaming a key silently resets every player, so the names are part of the contract."
        );
        Assert.That(
            PlayerPrefs.GetInt(GameSettings.QualityLevelKey, 99),
            Is.EqualTo(GameSettings.UnsetQualityLevel)
        );
    }

    [Test]
    public void OutOfRangeValuesAreClampedBeforeTheyReachTheMix()
    {
        GameSettings.SetMasterVolume(2.5f);
        Assert.That(GameSettings.Current.masterVolume, Is.EqualTo(1f).Within(0.0001f));

        GameSettings.SetMusicVolume(-1f);
        Assert.That(GameSettings.Current.musicVolume, Is.EqualTo(0f).Within(0.0001f));

        GameSettings.SetQualityLevel(99);
        Assert.That(
            GameSettings.Current.qualityLevel,
            Is.EqualTo(QualitySettings.names.Length - 1)
        );

        GameSettings.SetQualityLevel(-5);
        Assert.That(
            GameSettings.Current.qualityLevel,
            Is.EqualTo(GameSettings.UnsetQualityLevel)
        );
    }

    [Test]
    public void VolumesSnapToTheWholePercentTheReadoutShows()
    {
        GameSettings.SetMusicVolume(0.4249f);

        Assert.That(GameSettings.Current.musicVolume, Is.EqualTo(0.42f).Within(0.0001f));
        Assert.That(GameSettings.ToPercent(GameSettings.Current.musicVolume), Is.EqualTo(42));
    }

    [Test]
    public void ChangedFiresOnlyWhenAValueActuallyMoves()
    {
        int changes = 0;
        Action<GameSettings> counter = _ => changes++;
        GameSettings.Changed += counter;
        try
        {
            GameSettings.SetSfxVolume(0.5f);
            GameSettings.SetSfxVolume(0.5f);
            GameSettings.SetSfxVolume(0.503f);
        }
        finally
        {
            GameSettings.Changed -= counter;
        }

        Assert.That(
            changes,
            Is.EqualTo(1),
            "A slider reports every pointer move, so repeats must not reach the mixer."
        );
    }

    [Test]
    public void SettingsAreAppliedBeforeTheFirstSceneLoads()
    {
        List<RuntimeInitializeLoadType> loadTypes = new();
        foreach (
            MethodInfo method in typeof(GameSettingsLifetime).GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )
        )
        {
            RuntimeInitializeOnLoadMethodAttribute hook =
                method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            if (hook != null)
                loadTypes.Add(hook.loadType);
        }

        Assert.That(
            loadTypes,
            Does.Contain(RuntimeInitializeLoadType.BeforeSceneLoad),
            "Nothing may render or play a frame at a level the player did not choose."
        );
        Assert.That(
            loadTypes,
            Does.Contain(RuntimeInitializeLoadType.SubsystemRegistration),
            "Statics survive a reload-free Play session and must not inherit old subscribers."
        );

        string source = File.ReadAllText(SettingsSourcePath);
        Assert.That(source, Does.Contain("PlayerPrefs.GetFloat"));
        Assert.That(source, Does.Contain("AudioListener.volume = current.masterVolume"));
        Assert.That(source, Does.Contain("QualitySettings.SetQualityLevel"));
    }

    [Test]
    public void AuthoredSceneAudioStillClassifiesAsTrackAndEffects()
    {
        AudioSource track = LoadPrefabAudioSource(MusicPrefabPath);
        AudioSource effects = LoadPrefabAudioSource(EffectsPrefabPath);

        Assert.That(
            AudioManager.IsBackingTrack(track),
            Is.True,
            $"{MusicPrefabPath} must keep looping or auto-playing its own clip, or the music "
                + "slider will silently start driving it as an effect."
        );
        Assert.That(
            AudioManager.IsBackingTrack(effects),
            Is.False,
            $"{EffectsPrefabPath} is handed its clip when it fires, so it must not look like a track."
        );
    }

    [Test]
    public void AudioManagerAppliesLevelsOnAwakeAndDropsTheEmptyHooks()
    {
        string source = File.ReadAllText(AudioManagerSourcePath);

        Assert.That(
            source,
            Does.Contain("private void Awake()"),
            "Levels must land before the first frame plays a playOnAwake source."
        );
        Assert.That(
            source,
            Does.Not.Contain("void Start()").And.Not.Contain("void Update()"),
            "The empty template hooks were the whole of this component; a per-frame callback here "
                + "would mean the levels are being polled instead of pushed."
        );
        Assert.That(source, Does.Contain("GameSettings.Changed += ApplyToTrackedSources"));
        Assert.That(
            source,
            Does.Contain("GameSettings.Changed -= ApplyToTrackedSources"),
            "Each scene builds its own AudioManager, so the subscription is dropped before it is "
                + "re-made and cannot accumulate one subscriber per load."
        );
        Assert.That(
            source,
            Does.Contain("authoredVolume"),
            "Applying a level must scale the authored mix instead of compounding the last apply."
        );

        Assert.That(
            File.ReadAllText(LifetimeSourcePath),
            Does.Contain("AudioManager.ClearRuntimeState"),
            "The tracked sources outlive a Play session that skips the domain reload."
        );
    }

    [Test]
    public void ASourceThatArrivesAfterAwakeStillTakesTheEffectsLevel()
    {
        GameSettings.SetSfxVolume(0.15f);

        AudioSource gunfire = TemporarySource(1f);
        AudioManager.RegisterEffectsSource(gunfire);

        Assert.That(
            gunfire.volume,
            Is.EqualTo(0.15f).Within(0.0001f),
            "A source spawned mid-match has to open at the level already chosen. Master rides the "
                + "listener, so missing this reads as the effects slider working when it is not."
        );
    }

    [Test]
    public void ALevelChosenAfterASourceArrivesStillReachesIt()
    {
        AudioSource gunfire = TemporarySource(1f);
        AudioManager.RegisterEffectsSource(gunfire);

        GameSettings.SetSfxVolume(0.2f);

        Assert.That(gunfire.volume, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void RepeatedAppliesAndReRegistrationDoNotCompoundTheLevel()
    {
        AudioSource gunfire = TemporarySource(0.8f);
        AudioManager.RegisterEffectsSource(gunfire);

        GameSettings.SetSfxVolume(0.5f);
        Assert.That(gunfire.volume, Is.EqualTo(0.4f).Within(0.0001f));

        for (int step = 0; step < 5; step++)
            GameSettings.SetMasterVolume(0.9f - step * 0.1f);

        AudioManager.RegisterEffectsSource(gunfire);

        Assert.That(
            gunfire.volume,
            Is.EqualTo(0.4f).Within(0.0001f),
            "Scaling a level that was already applied is how a source fades away over a drag; the "
                + "authored level is the only thing a level may be applied to."
        );
    }

    [Test]
    public void AnAuthoredMixIsScaledRatherThanReplaced()
    {
        GameSettings.SetSfxVolume(0.5f);

        AudioSource quiet = TemporarySource(1f);
        AudioManager.RegisterEffectsSource(quiet, 0.6f);

        Assert.That(
            quiet.volume,
            Is.EqualTo(0.3f).Within(0.0001f),
            "A pooled source that resets its own volume still has to keep the mix it was built with."
        );
    }

    [Test]
    public void RuntimeMusicCanOptOutOfTheEffectsLevel()
    {
        GameSettings.SetMusicVolume(0.25f);
        GameSettings.SetSfxVolume(1f);

        AudioSource ambience = TemporarySource(1f);
        AudioManager.RegisterMusicSource(ambience);

        Assert.That(
            ambience.volume,
            Is.EqualTo(0.25f).Within(0.0001f),
            "Anything arriving after Awake is an effect unless it says otherwise, since a "
                + "playOnAwake projectile clip is indistinguishable from a track."
        );
    }

    /// <summary>
    /// The one hole the manager cannot close on its own: a source that spawns mid-match and plays
    /// without telling anyone. This fails the moment such a prefab or call appears, rather than
    /// leaving it to be noticed by ear.
    /// </summary>
    [Test]
    public void SpawnedAudioIsRoutedThroughTheManagerRatherThanPlayingItself()
    {
        List<string> unrouted = new();
        foreach (
            string path in Directory.GetFiles(AssetRoot, "*.prefab", SearchOption.AllDirectories)
        )
        {
            string assetPath = path.Replace('\\', '/');
            if (Array.IndexOf(AudioPrefabPaths, assetPath) >= 0)
                continue;

            if (File.ReadAllText(assetPath).Contains("\nAudioSource:"))
                unrouted.Add(assetPath);
        }

        Assert.That(
            unrouted,
            Is.Empty,
            "A prefab spawned during a match carries its AudioSource past every scan, so the "
                + "effects level never reaches it. Play the clip with AudioManager.PlayEffectAt, or "
                + "call AudioManager.RegisterEffectsSource as it spawns and list the prefab here."
        );

        List<string> detachedPlays = new();
        foreach (
            string path in Directory.GetFiles(ScriptRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            string scriptPath = path.Replace('\\', '/');
            if (Array.IndexOf(DetachedPlayExemptPaths, scriptPath) >= 0)
                continue;

            if (File.ReadAllText(scriptPath).Contains("PlayClipAtPoint("))
                detachedPlays.Add(scriptPath);
        }

        Assert.That(
            detachedPlays,
            Is.Empty,
            "PlayClipAtPoint hands its source to nobody, so no level can ever be applied to it. "
                + "AudioManager.PlayEffectAt is the same call with the effects level applied."
        );
    }

    [TestCaseSource(nameof(SettingsPanels))]
    public void SettingsPanelExposesVolumeAndQualityControls(string markupPath, string panelName)
    {
        VisualElement modal = LoadSettingsPanel(markupPath, panelName);
        Assert.That(
            modal.ClassListContains("hidden"),
            Is.True,
            "The panel opens from a button, never on load; in a match that would cover the board."
        );

        foreach (string sliderName in VolumeSliderNames)
        {
            Slider slider = modal.Q<Slider>(sliderName);
            Assert.That(slider, Is.Not.Null, $"Missing volume slider '{sliderName}'.");
            Assert.That(slider.lowValue, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(slider.highValue, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                slider.focusable,
                Is.True,
                "Volume has to be reachable with a keyboard or a stick."
            );
            Assert.That(
                slider.ClassListContains("toy-slider"),
                Is.True,
                "Slider chrome comes from the shared component library."
            );
            Assert.That(
                InToyboxScope(slider),
                Is.True,
                "The library scopes its chrome to .toybox-ui, so an unscoped slider is unstyled."
            );
        }

        foreach (string readoutName in VolumeReadoutNames)
        {
            Label readout = modal.Q<Label>(readoutName);
            Assert.That(readout, Is.Not.Null, $"Missing volume readout '{readoutName}'.");
            Assert.That(
                readout.text,
                Does.EndWith("%"),
                "The authored readout has to read as a level before the controller fills it in."
            );
        }

        Assert.That(modal.Q<DropdownField>("quality-dropdown"), Is.Not.Null);
        Assert.That(modal.Q<Button>("settings-close-button"), Is.Not.Null);

        ScrollView scroll = modal.Q<ScrollView>("settings-scroll");
        Assert.That(
            scroll,
            Is.Not.Null,
            "A short window must still be able to reach the last row."
        );
        Assert.That(scroll.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
        Assert.That(scroll.Q<Slider>("master-volume-slider"), Is.Not.Null);
        Assert.That(scroll.Q<DropdownField>("quality-dropdown"), Is.Not.Null);
    }

    [TestCaseSource(nameof(SettingsPanels))]
    public void SettingsControlsFollowReadingOrderForKeyboardAndGamepad(
        string markupPath,
        string panelName
    )
    {
        List<VisualElement> ordered = LoadSettingsPanel(markupPath, panelName)
            .Query<VisualElement>()
            .ToList();

        Assert.That(IndexOf(ordered, "master-volume-slider"), Is.GreaterThanOrEqualTo(0));
        Assert.That(
            IndexOf(ordered, "master-volume-slider"),
            Is.LessThan(IndexOf(ordered, "music-volume-slider"))
        );
        Assert.That(
            IndexOf(ordered, "music-volume-slider"),
            Is.LessThan(IndexOf(ordered, "sfx-volume-slider"))
        );
        Assert.That(
            IndexOf(ordered, "sfx-volume-slider"),
            Is.LessThan(IndexOf(ordered, "quality-dropdown"))
        );
        Assert.That(
            IndexOf(ordered, "quality-dropdown"),
            Is.LessThan(IndexOf(ordered, "settings-close-button")),
            "Close is the exit, so it comes after everything it closes over."
        );
    }

    [Test]
    public void EveryControlIsRoutedThroughTheSettingsStore()
    {
        string binder = File.ReadAllText(BinderSourcePath);

        Assert.That(binder, Does.Contain("GameSettings.SetMasterVolume"));
        Assert.That(binder, Does.Contain("GameSettings.SetMusicVolume"));
        Assert.That(binder, Does.Contain("GameSettings.SetSfxVolume"));
        Assert.That(binder, Does.Contain("GameSettings.SetQualityLevel"));
        Assert.That(
            binder,
            Does.Not.Contain("QualitySettings.SetQualityLevel"),
            "A level applied without being stored is exactly what reset on every launch."
        );
        Assert.That(
            binder,
            Does.Contain("RegisterValueChangedCallback(OnMasterVolumeChanged)")
                .And.Contain("RegisterValueChangedCallback(OnMusicVolumeChanged)")
                .And.Contain("RegisterValueChangedCallback(OnSfxVolumeChanged)"),
            "Volume follows the drag; there is no confirm step to apply it on."
        );
        Assert.That(
            binder,
            Does.Contain("SetValueWithoutNotify"),
            "Restoring a saved level must not report itself back as a change."
        );

        foreach (string screenPath in new[] { ControllerSourcePath, HudControllerSourcePath })
        {
            string controller = File.ReadAllText(screenPath);
            Assert.That(
                controller,
                Does.Contain("new SettingsPanelBinder("),
                $"{screenPath} draws the shared panel, so it must share its wiring rather than "
                    + "keep a second copy that can drift."
            );
            Assert.That(controller, Does.Contain("settings.Bind()"));
            Assert.That(controller, Does.Contain("settings.Unbind()"));
            Assert.That(
                controller,
                Does.Contain("settings.ShowStoredSettings()"),
                "Opening the panel shows what is stored without reporting it back as a change."
            );
            Assert.That(
                controller,
                Does.Contain("GameSettings.Flush"),
                "A drag streams values in; the disk write waits for the exit."
            );
            Assert.That(
                controller,
                Does.Contain("settings.FirstControl"),
                "Opening the panel lands focus on the first control it contains."
            );
        }
    }

    /// <summary>
    /// The in-match sheet borrows the dock and hands it back. It must not hand back a card that the
    /// match wanted disabled — a pending rejoin disables the cards while the player may well be in
    /// the settings sheet, and the two paths must not fight over the same flag.
    /// </summary>
    [Test]
    public void ClosingTheSheetHandsBackTheDockWithoutRevivingADisabledCard()
    {
        VisualElement dock = new();
        Button card = new();
        dock.Add(card);

        card.SetEnabled(false);
        dock.SetEnabled(false);
        dock.SetEnabled(true);

        Assert.That(dock.enabledSelf, Is.True, "The dock is usable again once the sheet closes.");
        Assert.That(
            card.enabledSelf,
            Is.False,
            "Disabling an ancestor and re-enabling it leaves each card's own flag untouched, which "
                + "is the whole reason the sheet may disable the dock instead of the cards."
        );
        Assert.That(card.enabledInHierarchy, Is.False);
    }

    [Test]
    public void TheInMatchSheetLeavesCardInteractabilityToTheMatch()
    {
        string controller = File.ReadAllText(HudControllerSourcePath);
        string sheet = Between(
            controller,
            "private void ShowSettingsOverlay()",
            "private static bool IsShowing("
        );

        Assert.That(
            sheet,
            Does.Not.Contain("SetCardsInteractable"),
            "Restoring cards from here would overwrite a rejoin-imposed disable. The sheet disables "
                + "the dock and lets ClearActiveOverlay hand that one flag back."
        );
        Assert.That(
            sheet,
            Does.Contain("ClearActiveOverlay(settingsOverlay,"),
            "The sheet releases the dock through the same path the controls sheet uses."
        );
        Assert.That(
            controller,
            Does.Contain("if (IsShowing(settingsOverlay))")
                .And.Contain("if (IsShowing(controlsOverlay))"),
            "Escape dismisses the sheet the player opened and is left alone otherwise, so it keeps "
                + "reaching the match during a round."
        );
        Assert.That(
            controller,
            Does.Not.Contain("Time.timeScale"),
            "The round runs on the server's clock; the sheet may not pause or slow the match."
        );
    }

    /// <summary>
    /// The dodge window is the shortest reaction in the game, so an open sheet is dismissed when the
    /// prompt arrives. It has to be hooked to something that actually runs on the client that owns
    /// the units: currentPhase is a server-set static and is never replicated, so keying off it would
    /// dismiss nothing on a remote seat.
    /// </summary>
    [Test]
    public void TheDodgeDismissalRidesAClientSignalRatherThanTheServerPhase()
    {
        string gameLoop = File.ReadAllText(GameLoopSourcePath);

        Assert.That(
            gameLoop,
            Does.Contain("[ClientRpc]\n    void StartDodgePlanningClientRpc("),
            "The hook has to be the prompt itself, which is the one thing that reaches whichever "
                + "seat is about to be asked to dodge."
        );

        string prompt = Between(
            gameLoop,
            "void StartDodgePlanningClientRpc(",
            "void SendDodgePathsToServerRpc("
        );
        Assert.That(
            prompt,
            Does.Contain("GameHUDController.Instance?.DismissOpenSheets()"),
            "Every path that opens a window — the round's own prompt, a rejoining seat's restored "
                + "prompt, and the dev extension — is re-issued through this one handler."
        );
        Assert.That(
            prompt.IndexOf("DismissOpenSheets", StringComparison.Ordinal),
            Is.LessThan(prompt.IndexOf("StartPlanning", StringComparison.Ordinal)),
            "The sheet closes before the dodge session opens, so the board is visible for all of it."
        );

        string serverSide = Between(
            gameLoop,
            "currentPhase = \"dodging\";",
            "void StartDodgePlanningClientRpc("
        );
        Assert.That(
            serverSide,
            Does.Not.Contain("DismissOpenSheets"),
            "A server-side assignment or a host-only branch looks replicated and is not."
        );
        Assert.That(
            Occurrences(gameLoop, "DismissOpenSheets"),
            Is.EqualTo(1),
            "One call site is all this needs; the rest of the loop is owned elsewhere."
        );
    }

    [Test]
    public void TheForcedDismissalIsTheSamePathAsEscapeAndLeavesTheMatchAlone()
    {
        string controller = File.ReadAllText(HudControllerSourcePath);
        string dismissal = Between(
            controller,
            "public void DismissOpenSheets()",
            "private bool TryDismissOpenSheet("
        );

        Assert.That(
            dismissal,
            Does.Contain("TryDismissOpenSheet(false)"),
            "One dismissal mechanism, shared with Escape and cancel. False because a forced close "
                + "must not park focus on the button that would reopen the sheet mid-dodge."
        );
        Assert.That(
            dismissal,
            Does.Contain("focused.Blur()"),
            "Focus is dropped instead, so an arrow key cannot still reach a slider inside a sheet "
                + "the player can no longer see."
        );
        Assert.That(
            dismissal,
            Does.Not.Contain("SetCardsInteractable"),
            "Cards belong to the match. During a dodge they are already disabled, and a rejoin "
                + "disables them too; the sheet only borrows the dock."
        );

        string guard = Between(
            controller,
            "private bool TryDismissOpenSheet(",
            "public static bool IsPointerOverUI("
        );
        Assert.That(
            guard,
            Does.Contain("return false;"),
            "With nothing open the dismissal reports so and returns before touching the dock, which "
                + "is what makes it safe to call on every prompt."
        );
        Assert.That(
            guard,
            Does.Not.Contain("SetCardsInteractable"),
            "Neither branch may hand back a card the match wanted disabled."
        );
    }

    /// <summary>
    /// A level chosen a moment before the window opened has to survive being closed out of the way,
    /// and has to still be showing when the player goes back in.
    /// </summary>
    [Test]
    public void AForcedCloseKeepsTheLevelThePlayerJustChose()
    {
        VisualElement panel = LoadSettingsPanel(HudMarkupPath, "settings-overlay");
        SettingsPanelBinder binder = new(panel, nameof(SettingsPersistenceEditModeTests));
        binder.Bind();
        try
        {
            GameSettings.SetSfxVolume(0.3f);
            // What closing the sheet does on the way out, forced or not.
            GameSettings.Flush();
            GameSettings.Reload();

            binder.ShowStoredSettings();

            Assert.That(
                panel.Q<Slider>("sfx-volume-slider").value,
                Is.EqualTo(0.3f).Within(0.0001f),
                "Reopening shows what is stored, so a forced close cannot read as a reset."
            );
            Assert.That(panel.Q<Label>("sfx-volume-value").text, Is.EqualTo("30%"));
        }
        finally
        {
            binder.Unbind();
        }
    }

    [Test]
    public void TheInMatchSheetIsReachedFromTheDockWithoutCoveringTheRound()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudMarkupPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {HudMarkupPath}.");
        Assert.That(asset.importedWithErrors, Is.False, $"{HudMarkupPath} imported with errors.");

        VisualElement tree = asset.Instantiate();
        Button entry = tree.Q<Button>("settings-button");
        VisualElement overlay = tree.Q<VisualElement>("settings-overlay");
        VisualElement controls = tree.Q<VisualElement>("controls-button");

        Assert.That(entry, Is.Not.Null, "The sheet needs an entry point in the dock.");
        Assert.That(
            entry.parent,
            Is.EqualTo(controls?.parent),
            "The settings button sits beside the controls button, where the player already looks "
                + "for match chrome."
        );
        Assert.That(
            overlay.ClassListContains("toybox-ui"),
            Is.True,
            "The shared slider and panel chrome is scoped to .toybox-ui."
        );
        Assert.That(
            overlay.ClassListContains("modal-shade"),
            Is.True,
            "The shade is what stops a click landing on the board behind the sheet."
        );

        // Two classes of scope, so the composition outranks the shared modal defaults.
        string panelRule = Between(
            File.ReadAllText("Assets/UI/Game/GameHUD.uss"),
            ".toybox-ui.settings-overlay .settings-panel {",
            "}"
        );
        Assert.That(
            panelRule,
            Does.Contain("max-height: 62%"),
            "The sheet stays shorter than the controls sheet so the phase banner and the dock are "
                + "still readable behind it while the round runs."
        );
    }

    [Test]
    public void SliderChromeLivesInTheTokenBackedComponentLibrary()
    {
        Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(ToyboxStylePath), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(TitleStylePath), Is.Not.Null);

        string toybox = File.ReadAllText(ToyboxStylePath);
        string title = File.ReadAllText(TitleStylePath);

        Assert.That(toybox, Does.Contain(".toybox-ui .toy-slider"));
        Assert.That(
            toybox,
            Does.Contain(".toybox-ui .toy-slider .unity-base-slider__tracker")
                .And.Contain(".toybox-ui .toy-slider .unity-base-slider__dragger"),
            "The built-in template positions both boxes absolutely, so both need geometry."
        );
        Assert.That(
            toybox,
            Does.Contain(".toybox-ui .toy-slider:focus .unity-base-slider__tracker"),
            "A ring on an 18px handle is not a cue you can find; the channel carries it too."
        );
        Assert.That(
            toybox,
            Does.Contain(".toybox-ui .toy-slider:disabled"),
            "An unavailable level must read as unavailable rather than merely dim."
        );

        Assert.That(
            title,
            Does.Not.Contain(".setting-row"),
            "Two definitions of one row is how the two screens would drift apart."
        );
    }

    /// <summary>
    /// The rows live in one sheet both screens load, so a column width can only be changed for both
    /// at once. The load order is part of that: the panel width relies on being read after the shared
    /// modal width it overrides.
    /// </summary>
    [Test]
    public void SettingRowsAreDefinedOnceForBothScreens()
    {
        Assert.That(
            AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsStylePath),
            Is.Not.Null,
            $"Could not import {SettingsStylePath}."
        );

        string shared = File.ReadAllText(SettingsStylePath);
        Assert.That(shared, Does.Contain(".setting-row__label"));
        Assert.That(shared, Does.Contain(".setting-row__value"));
        Assert.That(
            shared,
            Does.Contain(".setting-row--single .setting-row__control"),
            "Rows with no readout hold that column open so the controls share one right edge."
        );
        Assert.That(
            shared,
            Does.Contain(".phone .setting-row__label"),
            "The label and readout columns give up width before the control does."
        );
        Assert.That(
            shared,
            Does.Contain(".toybox-ui .settings-panel"),
            "One class of scope is what lets a screen sheet loaded later still have the last word."
        );

        string tokens = File.ReadAllText(TokenStylePath);
        foreach (
            System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(shared, @"var\((--toy-[a-z0-9-]+)\)")
        )
        {
            Assert.That(
                tokens,
                Does.Contain(match.Groups[1].Value + ":"),
                $"{SettingsStylePath} references undefined token {match.Groups[1].Value}."
            );
        }

        foreach (string markupPath in new[] { TitleMarkupPath, HudMarkupPath })
        {
            string markup = File.ReadAllText(markupPath);
            Assert.That(markup, Does.Contain("Shared/Settings.uss"));
            Assert.That(
                markup.IndexOf("TacticalToybox.uss"),
                Is.LessThan(markup.IndexOf("Settings.uss")),
                $"{markupPath} must read the panel rules after the component defaults they override."
            );
        }
    }

    private static void DeleteStoredSettings()
    {
        PlayerPrefs.DeleteKey(GameSettings.MasterVolumeKey);
        PlayerPrefs.DeleteKey(GameSettings.MusicVolumeKey);
        PlayerPrefs.DeleteKey(GameSettings.SfxVolumeKey);
        PlayerPrefs.DeleteKey(GameSettings.QualityLevelKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Stands in for a source that spawns mid-match. Kept out of the open scene so a test run never
    /// dirties whatever the developer has loaded.
    /// </summary>
    private AudioSource TemporarySource(float authoredVolume)
    {
        GameObject host = new("Late Audio Source") { hideFlags = HideFlags.HideAndDontSave };
        temporaryObjects.Add(host);

        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.volume = authoredVolume;
        return source;
    }

    private static AudioSource LoadPrefabAudioSource(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        Assert.That(prefab, Is.Not.Null, $"Could not import {assetPath}.");

        AudioSource source = prefab.GetComponent<AudioSource>();
        Assert.That(source, Is.Not.Null, $"{assetPath} no longer carries an AudioSource.");
        return source;
    }

    private static VisualElement LoadSettingsPanel(string markupPath, string panelName)
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(markupPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {markupPath}.");
        Assert.That(asset.importedWithErrors, Is.False, $"{markupPath} imported with errors.");

        VisualElement panel = asset.Instantiate().Q<VisualElement>(panelName);
        Assert.That(panel, Is.Not.Null, $"{markupPath} must expose '{panelName}'.");
        return panel;
    }

    private static bool InToyboxScope(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current.ClassListContains("toybox-ui"))
                return true;
        }

        return false;
    }

    private static int IndexOf(List<VisualElement> elements, string elementName)
    {
        return elements.FindIndex(element => element.name == elementName);
    }

    private static int Occurrences(string source, string value)
    {
        int count = 0;
        for (
            int at = source.IndexOf(value, StringComparison.Ordinal);
            at >= 0;
            at = source.IndexOf(value, at + value.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }

    /// <summary>Reads one region of a source file so an assertion can be about that region alone.</summary>
    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.That(from, Is.GreaterThanOrEqualTo(0), $"Could not find '{start}'.");

        int to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.That(to, Is.GreaterThan(from), $"Could not find '{end}' after '{start}'.");
        return source.Substring(from, to - from);
    }
}
