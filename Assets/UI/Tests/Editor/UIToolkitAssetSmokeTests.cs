using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class UIToolkitAssetSmokeTests
{
    private static readonly object[] ScreenContracts =
    {
        new object[]
        {
            "Assets/UI/Title/TitleScreen.uxml",
            new[]
            {
                "screen",
                "play-button",
                "settings-button",
                "controls-button",
                "credits-button",
                "quit-button",
                "settings-modal",
                "controls-modal",
                "credits-modal",
                "quality-dropdown",
                "settings-close-button",
                "controls-close-button",
                "credits-close-button",
            },
        },
        new object[]
        {
            "Assets/UI/Join/JoinGame.uxml",
            new[]
            {
                "screen",
                "create-panel",
                "join-panel",
                "show-create-button",
                "show-join-button",
                "title-button",
                "elimination-button",
                "king-button",
                "flag-button",
                "player-opponent-button",
                "ai-opponent-button",
                "local-multiplayer-row",
                "local-multiplayer-toggle",
                "fog-toggle",
                "create-match-button",
                "create-error-label",
                "join-code-input",
                "join-match-button",
                "join-status-label",
                "cancel-join-button",
                "relay-code-panel",
                "connection-code-heading",
                "relay-code-label",
                "relay-status-label",
                "cancel-host-button",
            },
        },
        new object[]
        {
            "Assets/UI/Home/CharacterSelection.uxml",
            new[]
            {
                "screen",
                "roster-options",
                "selected-roster",
                "roster-instruction",
                "roster-count-label",
                "confirm-selection-button",
                "selection-status",
                "mode-summary",
                "opponent-summary",
                "fog-summary",
                "map-preview",
            },
        },
        new object[]
        {
            "Assets/UI/Game/GameHUD.uxml",
            new[]
            {
                "screen",
                "hud-flash",
                "phase-label",
                "timer-label",
                "hill-status-readout",
                "hill-status-label",
                "enemy-unit-cards",
                "enemy-contact-summary",
                "unit-cards",
                "hud-dock",
                "planning-help",
                "target-feedback-label",
                "planning-commit",
                "planning-commit-status",
                "lock-in-button",
                "controls-button",
                "controls-overlay",
                "controls-scroll",
                "controls-close-button",
                "deployment-overlay",
                "deployment-status",
                "results-overlay",
                "results-panel",
                "results-scroll",
                "results-status",
                "play-again-button",
                "main-menu-button",
            },
        },
    };

    private static readonly object[] TemplateContracts =
    {
        new object[]
        {
            "Assets/UI/Shared/Templates/UnitCard.uxml",
            new[]
            {
                "unit-card-root",
                "unit-card-select",
                "unit-card-portrait",
                "unit-card-name",
                "unit-card-health",
                "unit-card-health-fill",
                "unit-card-health-value",
                "unit-card-ability",
                "unit-card-state",
                "unit-card-ability-vignette",
                "unit-card-flip-indicator",
                "unit-card-flip-icon",
                "unit-card-cooldown",
            },
        },
        new object[]
        {
            "Assets/UI/Shared/Templates/UnitOption.uxml",
            new[]
            {
                "unit-option-button",
                "unit-option-portrait",
                "unit-option-name",
                "unit-option-description",
                "unit-option-ability",
                "unit-option-status",
            },
        },
        new object[]
        {
            "Assets/UI/Shared/Templates/SelectedSlot.uxml",
            new[]
            {
                "selected-slot-button",
                "selected-slot-index",
                "selected-slot-portrait",
                "selected-slot-name",
                "selected-slot-detail",
            },
        },
    };

    private static readonly string[] StyleSheetPaths =
    {
        "Assets/UI/Shared/BattlePlan.uss",
        "Assets/UI/Shared/TacticalToyboxTokens.uss",
        "Assets/UI/Shared/TacticalToybox.uss",
        "Assets/UI/Title/TitleScreen.uss",
        "Assets/UI/Join/JoinGame.uss",
        "Assets/UI/Home/CharacterSelectionToybox.uss",
        "Assets/UI/Game/GameHUD.uss",
    };

    [TestCaseSource(nameof(ScreenContracts))]
    public void ScreenVisualTreesImportAndExposeControllerContracts(
        string assetPath,
        string[] requiredNames
    )
    {
        AssertVisualTreeContract(assetPath, requiredNames);
    }

    [TestCaseSource(nameof(TemplateContracts))]
    public void SharedTemplatesImportAndExposeExpectedElements(
        string assetPath,
        string[] requiredNames
    )
    {
        AssertVisualTreeContract(assetPath, requiredNames);
    }

    [Test]
    public void GameHudGeneratesCardsFromUnitsPerPlayer()
    {
        string markup = File.ReadAllText("Assets/UI/Game/GameHUD.uxml");
        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");

        Assert.That(
            markup,
            Does.Not.Contain("name=\"unit-card-"),
            "The HUD asset should expose one empty card container, not a fixed set of card instances."
        );
        Assert.That(
            controller,
            Does.Contain("new UnitCardElement[RosterRules.UnitsPerPlayer]"),
            "Runtime card capacity must derive from the authoritative roster size."
        );
        Assert.That(
            controller,
            Does.Contain("private readonly UnitCardElement[] enemyCards"),
            "Enemy status capacity must derive from the same authoritative roster size."
        );
        Assert.That(
            controller,
            Does.Contain("for (int i = 0; i < cards.Length; i++)"),
            "The HUD should generate every configured card at runtime."
        );
        Assert.That(
            controller,
            Does.Contain("for (int i = 0; i < enemyCards.Length; i++)"),
            "The HUD should generate one read-only status card per enemy unit."
        );
        Assert.That(
            controller,
            Does.Contain("unitCardTemplate.Instantiate()"),
            "Generated cards should use the authored UnitCard template."
        );
    }

    [Test]
    public void GameHudLockInExposesReversibleWaitingState()
    {
        string markup = File.ReadAllText("Assets/UI/Game/GameHUD.uxml");
        string styles = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");

        Assert.That(markup, Does.Contain("unlock while waiting"));
        Assert.That(controller, Does.Contain("ShowPlanningCommitUnlocking"));
        Assert.That(controller, Does.Contain("\"Unlock\""));
        Assert.That(controller, Does.Contain("planner.TryUnlock()"));
        Assert.That(styles, Does.Contain(".planning-commit--unlocking"));
    }

    [Test]
    public void CharacterSelectionGeneratesSlotsFromUnitsPerPlayer()
    {
        string markup = File.ReadAllText("Assets/UI/Home/CharacterSelection.uxml");
        string controller = File.ReadAllText(
            "Assets/Scripts/Menus/CharacterSelectionUIController.cs"
        );

        Assert.That(
            markup,
            Does.Not.Contain("template=\"selected-slot-template\""),
            "Character selection should expose one empty roster container, not fixed slot instances."
        );
        Assert.That(controller, Does.Contain("new int[UnitsPerPlayer]"));
        Assert.That(controller, Does.Contain("for (int i = 0; i < UnitsPerPlayer; i++)"));
    }

    [Test]
    public void StyleSheetsImportWithoutMissingAssets()
    {
        foreach (string assetPath in StyleSheetPaths)
        {
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(assetPath);
            Assert.That(styleSheet, Is.Not.Null, $"Could not import {assetPath}.");
        }
    }

    [Test]
    public void CharacterSelectionPreviewMatchesExpandedMap()
    {
        const string assetPath = "Assets/UI/Home/CharacterSelection.uxml";
        const string previewPath = "Assets/Images/MapPreview.png";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Texture2D preview = AssetDatabase.LoadAssetAtPath<Texture2D>(previewPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");
        Assert.That(preview, Is.Not.Null, $"Could not import {previewPath}.");

        TemplateContainer tree = asset.Instantiate();
        Label caption = tree.Q<Label>(className: "map-caption");
        Assert.That(caption, Is.Not.Null);
        Assert.That(caption.text, Does.Contain("15 × 10"));
        Assert.That(caption.text, Does.Not.Contain("9 × 10"));
        Assert.That(preview.width, Is.EqualTo(1080));
        Assert.That(preview.height, Is.EqualTo(720));

        using SHA256 sha = SHA256.Create();
        string actualHash = System.Convert.ToBase64String(
            sha.ComputeHash(File.ReadAllBytes(previewPath))
        );
        System.Type generatorType = System.Type.GetType(
            "MapPreviewGenerator, Assembly-CSharp-Editor"
        );
        Assert.That(generatorType, Is.Not.Null);
        byte[] generatedPreview = (byte[])
            generatorType
                .GetMethod(
                    "BuildPreviewPng",
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static
                )
                .Invoke(null, null);
        string generatedHash = System.Convert.ToBase64String(
            sha.ComputeHash(generatedPreview)
        );
        Assert.That(
            actualHash,
            Is.EqualTo(generatedHash),
            "MapPreview.png is stale. Run Battle Plan/Regenerate Map Preview."
        );
    }

    [Test]
    public void SharedStylesUseSdfFontAsset()
    {
        const string fontPath = "Assets/Fonts/CascadiaCode-VariableFont_wght UI SDF.asset";
        const string fontReference =
            "project://database/Assets/Fonts/CascadiaCode-VariableFont_wght UI SDF.asset";
        string[] stylePaths =
        {
            "Assets/UI/Shared/BattlePlan.uss",
        };

        UnityEngine.TextCore.Text.FontAsset fontAsset =
            AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(fontPath);
        Assert.That(fontAsset, Is.Not.Null, $"Could not import {fontPath}.");
        Assert.That(
            fontAsset.atlasRenderMode,
            Is.EqualTo(UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA)
        );
        Assert.That(
            fontAsset.atlasPopulationMode,
            Is.EqualTo(UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic)
        );

        foreach (string stylePath in stylePaths)
        {
            string source = File.ReadAllText(stylePath);
            Assert.That(source, Does.Contain(fontReference));
            Assert.That(
                source,
                Does.Not.Contain("CascadiaCode-VariableFont_wght.ttf"),
                $"{stylePath} should use the SDF asset instead of the raw font."
            );
        }
    }

    [Test]
    public void TacticalToyboxUsesRubikSdfAndVectorIcons()
    {
        const string fontPath = "Assets/Fonts/Rubik/Rubik-VariableFont_wght UI SDF.asset";
        const string stylePath = "Assets/UI/Shared/TacticalToybox.uss";
        const string fontReference =
            "project://database/Assets/Fonts/Rubik/Rubik-VariableFont_wght UI SDF.asset";
        string[] iconPaths =
        {
            "Assets/UI/Shared/Icons/play.svg",
            "Assets/UI/Shared/Icons/controls.svg",
            "Assets/UI/Shared/Icons/settings.svg",
            "Assets/UI/Shared/Icons/credits.svg",
            "Assets/UI/Shared/Icons/back.svg",
            "Assets/UI/Shared/Icons/create.svg",
            "Assets/UI/Shared/Icons/join.svg",
            "Assets/UI/Shared/Icons/elimination.svg",
            "Assets/UI/Shared/Icons/hill.svg",
            "Assets/UI/Shared/Icons/flag.svg",
            "Assets/UI/Shared/Icons/player.svg",
            "Assets/UI/Shared/Icons/bot.svg",
            "Assets/UI/Shared/Icons/fog.svg",
            "Assets/UI/Shared/Icons/check.svg",
        };

        UnityEngine.TextCore.Text.FontAsset fontAsset =
            AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(fontPath);
        Assert.That(fontAsset, Is.Not.Null, $"Could not import {fontPath}.");
        Assert.That(
            fontAsset.atlasRenderMode,
            Is.EqualTo(UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA)
        );
        Assert.That(
            fontAsset.atlasPopulationMode,
            Is.EqualTo(UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic)
        );

        string style = File.ReadAllText(stylePath);
        Assert.That(style, Does.Contain(fontReference));
        Assert.That(style, Does.Not.Contain("Rubik-VariableFont_wght.ttf"));
        string runtimeTheme = File.ReadAllText("Assets/UI/Shared/BattlePlanRuntime.tss");
        Assert.That(runtimeTheme, Does.Contain(fontReference));

        foreach (string iconPath in iconPaths)
        {
            VectorImage icon = AssetDatabase.LoadAssetAtPath<VectorImage>(iconPath);
            Assert.That(icon, Is.Not.Null, $"Could not import {iconPath} as a VectorImage.");
        }

        const string joinStagePath = "Assets/Images/JoinToyboxStage.png";
        Texture2D joinStage = AssetDatabase.LoadAssetAtPath<Texture2D>(joinStagePath);
        Assert.That(joinStage, Is.Not.Null, $"Could not import {joinStagePath}.");
        Assert.That(joinStage.width, Is.EqualTo(1579));
        Assert.That(joinStage.height, Is.EqualTo(885));
        string joinStyle = File.ReadAllText("Assets/UI/Join/JoinGame.uss");
        Assert.That(joinStyle, Does.Contain("JoinToyboxStage.png"));
        Assert.That(joinStyle, Does.Not.Contain("Unity_g0I8iFX1Y3.png"));
    }

    [Test]
    public void TacticalToyboxUsesOneTokenBackedComponentLibrary()
    {
        const string tokenPath = "Assets/UI/Shared/TacticalToyboxTokens.uss";
        const string componentPath = "Assets/UI/Shared/TacticalToybox.uss";
        const string runtimeThemePath = "Assets/UI/Shared/BattlePlanRuntime.tss";
        string tokens = File.ReadAllText(tokenPath);
        string components = File.ReadAllText(componentPath);
        string runtimeTheme = File.ReadAllText(runtimeThemePath);

        Assert.That(
            AssetDatabase.LoadAssetAtPath<StyleSheet>(tokenPath),
            Is.Not.Null,
            "The Toybox token source must import as a stylesheet."
        );
        Assert.That(tokens, Does.Contain("--toy-ink:"));
        Assert.That(tokens, Does.Contain("--toy-target:"));
        Assert.That(tokens, Does.Contain("--toy-focus-emphasis:"));
        Assert.That(tokens, Does.Contain("--toy-motion-fast:"));
        Assert.That(components, Does.Contain("@import url(\"TacticalToyboxTokens.uss\")"));
        Assert.That(components, Does.Contain(".toybox-ui"));
        Assert.That(runtimeTheme, Does.Contain("@import url(\"TacticalToyboxTokens.uss\")"));
        Assert.That(
            components,
            Does.Not.Contain("rgb(").And.Not.Contain("rgba("),
            "Shared components must consume semantic tokens instead of duplicating colors."
        );
        Assert.That(
            runtimeTheme,
            Does.Not.Contain("rgb(").And.Not.Contain("rgba("),
            "Runtime dropdown chrome must consume the same semantic tokens."
        );

        string[] tokenConsumerPaths =
        {
            componentPath,
            runtimeThemePath,
            "Assets/UI/Title/TitleScreen.uss",
            "Assets/UI/Join/JoinGame.uss",
            "Assets/UI/Home/CharacterSelectionToybox.uss",
            "Assets/UI/Game/GameHUD.uss",
        };
        foreach (string consumerPath in tokenConsumerPaths)
        {
            string consumer = File.ReadAllText(consumerPath);
            var tokenMatches = System.Text.RegularExpressions.Regex.Matches(
                consumer,
                @"var\((--toy-[a-z0-9-]+)\)"
            );
            foreach (System.Text.RegularExpressions.Match match in tokenMatches)
            {
                Assert.That(
                    tokens,
                    Does.Contain(match.Groups[1].Value + ":"),
                    $"{consumerPath} references undefined token {match.Groups[1].Value}."
                );
            }
        }

        string[] menuPaths =
        {
            "Assets/UI/Title/TitleScreen.uxml",
            "Assets/UI/Join/JoinGame.uxml",
            "Assets/UI/Home/CharacterSelection.uxml",
        };
        foreach (string menuPath in menuPaths)
        {
            string markup = File.ReadAllText(menuPath);
            Assert.That(markup, Does.Contain("toybox-ui"));
            Assert.That(markup, Does.Contain("BattlePlan.uss"));
            Assert.That(markup, Does.Contain("TacticalToybox.uss"));
            Assert.That(
                markup.IndexOf("BattlePlan.uss"),
                Is.LessThan(markup.IndexOf("TacticalToybox.uss")),
                $"{menuPath} must load the reset before the Toybox component library."
            );
        }

        Assert.That(
            components,
            Does.Not.Match(@"(?m)^\.toy-icon--"),
            "Toybox icon selectors must not leak outside the component-library scope."
        );

        string rosterMarkup = File.ReadAllText("Assets/UI/Home/CharacterSelection.uxml");
        Assert.That(rosterMarkup, Does.Not.Contain("CharacterSelection.uss"));
        Assert.That(
            File.Exists("Assets/UI/Home/CharacterSelection.uss"),
            Is.False,
            "The roster must have one screen stylesheet owner."
        );
    }

    [Test]
    public void GameHudScopesToyboxAwayFromUnitAndAbilityCards()
    {
        const string assetPath = "Assets/UI/Game/GameHUD.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        VisualElement hudTop = tree.Q<VisualElement>(className: "hud-top");
        VisualElement hillReadout = tree.Q<VisualElement>("hill-status-readout");
        VisualElement deployment = tree.Q<VisualElement>("deployment-overlay");
        VisualElement controls = tree.Q<VisualElement>("controls-overlay");
        VisualElement results = tree.Q<VisualElement>("results-overlay");
        VisualElement unitCards = tree.Q<VisualElement>("unit-cards");
        VisualElement enemyCards = tree.Q<VisualElement>("enemy-unit-cards");

        Assert.That(hudTop, Is.Not.Null);
        Assert.That(hillReadout, Is.Not.Null);
        Assert.That(hillReadout.ClassListContains("hidden"), Is.True);
        Assert.That(tree.Q<Label>("match-type-label"), Is.Null);
        Assert.That(tree.Q<Label>("fog-label"), Is.Null);
        Assert.That(deployment, Is.Not.Null);
        Assert.That(controls, Is.Not.Null);
        Assert.That(results, Is.Not.Null);
        Assert.That(unitCards, Is.Not.Null);
        Assert.That(enemyCards, Is.Not.Null);
        Assert.That(hudTop.ClassListContains("toybox-ui"), Is.True);
        Assert.That(deployment.ClassListContains("toybox-ui"), Is.True);
        Assert.That(controls.ClassListContains("toybox-ui"), Is.True);
        Assert.That(results.ClassListContains("toybox-ui"), Is.True);
        Assert.That(hudTop.Q<VisualElement>("unit-cards"), Is.Null);
        Assert.That(hudTop.Q<VisualElement>("enemy-unit-cards"), Is.Null);
        Assert.That(unitCards.ClassListContains("toybox-ui"), Is.False);
        Assert.That(enemyCards.ClassListContains("toybox-ui"), Is.False);
        for (VisualElement current = unitCards.parent; current != null; current = current.parent)
        {
            Assert.That(
                current.ClassListContains("toybox-ui"),
                Is.False,
                "The bottom unit and ability card subtree must stay outside Toybox scope."
            );
        }

        string markup = File.ReadAllText(assetPath);
        Assert.That(markup, Does.Contain("../Shared/TacticalToybox.uss"));
        Assert.That(markup, Does.Not.Contain("match-type-label").And.Not.Contain("fog-label"));
        string style = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        Assert.That(style, Does.Contain(".hud-top .phase-cluster"));
        Assert.That(style, Does.Contain(".hud-top .hill-status-readout__value"));
        Assert.That(style, Does.Contain(".hud-top .hill-status--contested"));
        int phoneReadoutStart = style.IndexOf(".phone .hill-status-readout {");
        Assert.That(phoneReadoutStart, Is.GreaterThanOrEqualTo(0));
        int phoneReadoutEnd = style.IndexOf("}", phoneReadoutStart);
        Assert.That(phoneReadoutEnd, Is.GreaterThan(phoneReadoutStart));
        string phoneReadoutRule = style.Substring(
            phoneReadoutStart,
            phoneReadoutEnd - phoneReadoutStart
        );
        Assert.That(
            phoneReadoutRule,
            Does.Contain("flex-direction: column;").And.Not.Contain("display: flex;"),
            "Constrained layouts must preserve the hidden state until a live hill objective is active."
        );
        Assert.That(
            style,
            Does.Contain(".phone.short .hud-top {\n")
                .And.Contain(".phone.short .phase-cluster {\n")
                .And.Contain(".phone.short .hill-status-readout {\n"),
            "Short landscape layouts must place the two top readouts side by side."
        );
        Assert.That(
            style,
            Does.Contain(".toybox-ui.deployment-overlay .deployment-overlay__content")
                .And.Contain(".toybox-ui.results-overlay .results-panel")
                .And.Contain(".results-scroll"),
            "Overlay composition must outrank shared component defaults and keep results scrollable."
        );

        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");
        Assert.That(controller, Does.Contain("RegisterCallback<FocusInEvent>(OnFocusIn"));
        Assert.That(
            controller,
            Does.Contain("RegisterCallback<NavigationCancelEvent>")
                .And.Contain("OnNavigationCancel")
        );
        Assert.That(controller, Does.Contain("activeOverlay.Contains(focused)"));
        Assert.That(controller, Does.Contain("ActivateOverlay(resultsOverlay, playAgainButton)"));
        Assert.That(controller, Does.Contain("hudDock?.SetEnabled(false)"));
        Assert.That(controller, Does.Contain("!isActiveAndEnabled"));
    }

    [Test]
    public void TacticalToyboxKeepsNarrowProgressionScrollableAndFocusVisible()
    {
        VisualTreeAsset joinAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Join/JoinGame.uxml"
        );
        VisualTreeAsset rosterAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Home/CharacterSelection.uxml"
        );
        Assert.That(joinAsset, Is.Not.Null);
        Assert.That(rosterAsset, Is.Not.Null);
        ScrollView joinScroll = joinAsset.Instantiate().Q<ScrollView>(
            className: "join-content"
        );
        ScrollView rosterScroll = rosterAsset.Instantiate().Q<ScrollView>(
            className: "roster-frame"
        );
        Assert.That(
            joinScroll,
            Is.Not.Null,
            "Join options need a vertical ScrollView for phone and short layouts."
        );
        Assert.That(
            rosterScroll,
            Is.Not.Null,
            "Roster progression needs a vertical ScrollView for phone and short layouts."
        );
        Assert.That(
            joinScroll.horizontalScrollerVisibility,
            Is.EqualTo(ScrollerVisibility.Hidden)
        );
        Assert.That(
            rosterScroll.horizontalScrollerVisibility,
            Is.EqualTo(ScrollerVisibility.Hidden)
        );

        string sharedStyle = File.ReadAllText("Assets/UI/Shared/TacticalToybox.uss");
        Assert.That(sharedStyle, Does.Contain(".phone .toybox-ui .button"));
        Assert.That(sharedStyle, Does.Contain(".button:disabled .toy-button__label"));
        Assert.That(sharedStyle, Does.Contain(".button:disabled .toy-icon"));
        Assert.That(
            sharedStyle,
            Does.Contain(".toggle:checked:focus > .unity-toggle__input"),
            "A checked toggle must keep its focus cue after the checked-state rule."
        );
        Assert.That(
            sharedStyle,
            Does.Contain("border-color: var(--toy-sky);"),
            "Focused controls need a cue distinct from selected cream borders."
        );
    }

    [Test]
    public void TacticalToyboxScreenVariantsOutrankSharedComponentDefaults()
    {
        string sharedStyle = File.ReadAllText("Assets/UI/Shared/TacticalToybox.uss");
        string titleStyle = File.ReadAllText("Assets/UI/Title/TitleScreen.uss");
        string joinStyle = File.ReadAllText("Assets/UI/Join/JoinGame.uss");
        string rosterStyle = File.ReadAllText(
            "Assets/UI/Home/CharacterSelectionToybox.uss"
        );
        string joinController = File.ReadAllText(
            "Assets/Scripts/Menus/JoinGameUIController.cs"
        );

        Assert.That(titleStyle, Does.Contain(".toybox-ui.title-screen .title-play"));
        Assert.That(joinStyle, Does.Contain(".toybox-ui.join-screen .mode-option"));
        Assert.That(
            rosterStyle,
            Does.Contain(".toybox-ui.roster-screen .roster-heading")
        );
        Assert.That(
            rosterStyle,
            Does.Contain(".compact .roster-body {\n    height: 300px;")
        );
        Assert.That(
            rosterStyle,
            Does.Contain(".selected-slot:disabled:hover")
                .And.Contain("background-color: var(--toy-teal-pressed);")
        );
        Assert.That(
            sharedStyle,
            Does.Contain(".status-line.label--danger")
                .And.Contain(".button--selected:focus {\n    background-color: var(--toy-teal-pressed);")
                .And.Contain("border-left-width: var(--toy-focus-emphasis);")
        );
        Assert.That(joinController, Does.Contain("\"status-line--danger\""));
    }

    [Test]
    public void JoinTogglesUseInlineTextWithoutStretchingBaseFieldLabels()
    {
        const string assetPath = "Assets/UI/Join/JoinGame.uxml";
        const string sharedStylePath = "Assets/UI/Shared/BattlePlan.uss";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        Toggle localMultiplayer = tree.Q<Toggle>("local-multiplayer-toggle");
        Toggle fog = tree.Q<Toggle>("fog-toggle");

        Assert.That(localMultiplayer, Is.Not.Null);
        Assert.That(fog, Is.Not.Null);
        Assert.That(localMultiplayer.label, Is.Null.Or.Empty);
        Assert.That(fog.label, Is.Null.Or.Empty);
        Assert.That(localMultiplayer.text, Is.EqualTo("Local multiplayer (dev)"));
        Assert.That(fog.text, Is.EqualTo("Fog of war"));

        string sharedStyle = File.ReadAllText(sharedStylePath);
        int inputRuleStart = sharedStyle.IndexOf(".toggle > .unity-toggle__input {");
        int checkmarkRuleStart = sharedStyle.IndexOf(
            ".toggle > .unity-toggle__input > .unity-toggle__checkmark {"
        );
        Assert.That(inputRuleStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(checkmarkRuleStart, Is.GreaterThan(inputRuleStart));

        string inputRule = sharedStyle.Substring(
            inputRuleStart,
            checkmarkRuleStart - inputRuleStart
        );
        Assert.That(
            inputRule,
            Does.Not.Contain("flex-grow: 0"),
            "The input contains the inline text and must retain width."
        );

        string checkmarkRule = sharedStyle.Substring(
            checkmarkRuleStart,
            sharedStyle.IndexOf('}', checkmarkRuleStart) - checkmarkRuleStart
        );
        Assert.That(checkmarkRule, Does.Contain("width: 22px"));
        Assert.That(checkmarkRule, Does.Contain("height: 22px"));
    }

    [Test]
    public void JoinEnablesKingOfTheHillButKeepsCaptureTheFlagUnavailable()
    {
        const string assetPath = "Assets/UI/Join/JoinGame.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        Button king = tree.Q<Button>("king-button");
        Button flag = tree.Q<Button>("flag-button");

        Assert.That(king, Is.Not.Null);
        Assert.That(flag, Is.Not.Null);
        Assert.That(king.enabledSelf, Is.True);
        Assert.That(flag.enabledSelf, Is.False);
    }

    [Test]
    public void GameHudDeploymentOverlayStartsVisible()
    {
        const string assetPath = "Assets/UI/Game/GameHUD.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        VisualElement overlay = asset.Instantiate().Q<VisualElement>("deployment-overlay");
        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay.ClassListContains("hidden"), Is.False);
    }

    [TestCase("Assets/Scripts/Menus/TitleScreenUIController.cs")]
    [TestCase("Assets/Scripts/Menus/JoinGameUIController.cs")]
    [TestCase("Assets/Scripts/Menus/CharacterSelectionUIController.cs")]
    [TestCase("Assets/Scripts/Menus/GameHUDController.cs")]
    public void ResponsiveBreakpointsUsePanelLayoutCoordinates(string assetPath)
    {
        string source = File.ReadAllText(assetPath);

        Assert.That(source, Does.Contain("evt.newRect.width"));
        Assert.That(source, Does.Contain("evt.newRect.height"));
        Assert.That(
            source,
            Does.Not.Contain("scaledPixelsPerPoint"),
            "USS breakpoints and GeometryChangedEvent rectangles must use the same panel coordinate space."
        );
    }

    [Test]
    public void PanelSettingsUseSharedRuntimeThemeAndReferenceResolution()
    {
        const string panelPath = "Assets/UI/Shared/BattlePlanPanelSettings.asset";
        const string themePath = "Assets/UI/Shared/BattlePlanRuntime.tss";
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
        ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);

        Assert.That(panelSettings, Is.Not.Null, $"Could not import {panelPath}.");
        Assert.That(theme, Is.Not.Null, $"Could not import {themePath}.");
        Assert.That(panelSettings.themeStyleSheet, Is.EqualTo(theme));
        Assert.That(panelSettings.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
        Assert.That(panelSettings.referenceResolution, Is.EqualTo(new Vector2Int(1920, 1080)));
        Assert.That(panelSettings.match, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void GameHudExposesTargetFeedbackAndControlsElements()
    {
        const string assetPath = "Assets/UI/Game/GameHUD.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        Assert.That(
            tree.Q<Label>("target-feedback-label"),
            Is.Not.Null,
            "GameHUDController binds target-feedback-label as a Label for ability/target feedback."
        );
        Assert.That(
            tree.Q<Button>("controls-button"),
            Is.Not.Null,
            "GameHUDController wires controls-button to open the in-match controls overlay."
        );
        Assert.That(
            tree.Q<VisualElement>("controls-overlay"),
            Is.Not.Null,
            "GameHUDController shows and hides controls-overlay."
        );
        Assert.That(
            tree.Q<Button>("controls-close-button"),
            Is.Not.Null,
            "GameHUDController wires controls-close-button to dismiss the overlay."
        );
        ScrollView controlsScroll = tree.Q<ScrollView>("controls-scroll");
        ScrollView resultsScroll = tree.Q<ScrollView>("results-scroll");
        Assert.That(controlsScroll, Is.Not.Null);
        Assert.That(resultsScroll, Is.Not.Null);
        Assert.That(controlsScroll.focusable, Is.True);
        Assert.That(resultsScroll.focusable, Is.True);
    }

    [Test]
    public void TitleControlsModalExposesControlGuidanceContent()
    {
        const string assetPath = "Assets/UI/Title/TitleScreen.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        VisualElement controlsModal = tree.Q<VisualElement>("controls-modal");
        Assert.That(controlsModal, Is.Not.Null, "TitleScreen must expose the controls modal.");
        Assert.That(
            controlsModal.Q<Button>("controls-close-button"),
            Is.Not.Null,
            "The controls modal must be dismissible."
        );

        var labels = controlsModal.Query<Label>().ToList();
        int populated = 0;
        foreach (Label label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label.text))
                populated++;
        }
        Assert.That(
            populated,
            Is.GreaterThanOrEqualTo(4),
            "The controls modal must document the core controls, not sit empty."
        );
    }

    [Test]
    public void UnitCardFlipIndicatorExposesRoundCooldownState()
    {
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset card = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(cardPath);
        Assert.That(card, Is.Not.Null, $"Could not import {cardPath}.");

        TemplateContainer tree = card.Instantiate();
        VisualElement indicator = tree.Q<VisualElement>("unit-card-flip-indicator");
        VisualElement icon = tree.Q<VisualElement>("unit-card-flip-icon");
        Label cooldownLabel = tree.Q<Label>("unit-card-cooldown");
        Assert.That(
            indicator,
            Is.Not.Null,
            "UnitCard must expose the whole-card mode indicator."
        );
        Assert.That(icon, Is.Not.Null, "The mode indicator must expose the flip-card icon.");
        Assert.That(cooldownLabel, Is.Not.Null, "The mode indicator must expose cooldown rounds.");
        Assert.That(cooldownLabel.ClassListContains("hidden"), Is.True);
        Assert.That(
            tree.Query<Button>().ToList().Count,
            Is.EqualTo(1),
            "A friendly unit card must expose one whole-card button."
        );
        Assert.That(
            tree.Q<Button>("unit-card-move"),
            Is.Null,
            "Move must no longer be a separate card button."
        );
        Assert.That(
            tree.Q<Button>("unit-card-ability-button"),
            Is.Null,
            "Ability must no longer be a separate card button."
        );

        string source = File.ReadAllText("Assets/Scripts/Menus/UnitCardElement.cs")
            .ToLowerInvariant();
        Assert.That(source, Does.Contain("ready"));
        Assert.That(source, Does.Contain("cooldown"));
        Assert.That(source, Does.Contain("completed round"));
        Assert.That(source, Does.Not.Contain("spent this match"));

        string styles = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        Assert.That(styles, Does.Contain("flip-card.svg"));
        Assert.That(styles, Does.Contain("ability-flare.svg"));
        Assert.That(styles, Does.Contain(".unit-card--ability"));
        Assert.That(
            styles,
            Does.Contain(".unit-card--selected .unit-card__flip-indicator--cooldown"),
            "Cooldown styling must override the selected-card accent."
        );
    }

    [Test]
    public void UnitCardActivationUsesPlannerStateAsAuthority()
    {
        string card = File.ReadAllText("Assets/Scripts/Menus/UnitCardElement.cs");
        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");
        string planner = File.ReadAllText("Assets/Scripts/GameManager/PlanMovement.cs");

        Assert.That(card, Does.Contain("activateAction?.Invoke()"));
        Assert.That(card, Does.Not.Contain("moveAction"));
        Assert.That(card, Does.Not.Contain("abilityAction"));
        Assert.That(controller, Does.Contain("TryActivateUnitCard(cardIndex)"));
        Assert.That(planner, Does.Contain("public bool TryActivateUnitCard(int unitIndex)"));
        Assert.That(planner, Does.Contain("plans.TryGetValue(unit"));
    }

    [Test]
    public void UnitOptionExposesSelectionAndAvailabilityStateClasses()
    {
        const string stylePath = "Assets/UI/Home/CharacterSelectionToybox.uss";
        string style = File.ReadAllText(stylePath);
        Assert.That(
            style,
            Does.Contain(".unit-option--selected"),
            "Selected roster options need a visual state hook."
        );
        Assert.That(
            style,
            Does.Contain(".unit-option--unavailable"),
            "Unavailable roster options need a visual state hook."
        );
        Assert.That(
            style,
            Does.Contain(".unit-option__status"),
            "The unit-option status badge needs styling."
        );
    }

    private static void AssertVisualTreeContract(string assetPath, string[] requiredNames)
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");
        Assert.That(asset.importedWithErrors, Is.False, $"{assetPath} imported with errors.");

        TemplateContainer tree = asset.Instantiate();
        foreach (string elementName in requiredNames)
        {
            Assert.That(
                tree.Q<VisualElement>(elementName),
                Is.Not.Null,
                $"{assetPath} is missing named element '{elementName}'."
            );
        }
    }
}
