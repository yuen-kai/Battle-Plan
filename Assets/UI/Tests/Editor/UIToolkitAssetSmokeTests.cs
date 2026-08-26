using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class UIToolkitAssetSmokeTests
{
    private const string BodyFontPath = "Assets/Fonts/Jost/Jost-Regular UI SDF.asset";
    private const string DisplayFontPath =
        "Assets/Fonts/OswaldCaps/OswaldCaps-Regular UI SDF.asset";
    private const string DisplayFontSourcePath =
        "Assets/Fonts/OswaldCaps/OswaldCaps-Regular.ttf";
    private const string BodyFontReference =
        "project://database/Assets/Fonts/Jost/Jost-Regular UI SDF.asset";
    private const string DisplayFontReference =
        "project://database/Assets/Fonts/OswaldCaps/OswaldCaps-Regular UI SDF.asset";

    private static readonly object[] ScreenContracts =
    {
        new object[]
        {
            "Assets/UI/Title/TitleScreen.uxml",
            new[]
            {
                "screen",
                "play-button",
                "tutorial-button",
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
                "unit-cards",
                "hud-dock",
                "target-feedback-label",
                "coach-prompt",
                "coach-prompt-label",
                "lesson-popup",
                "lesson-popup-label",
                "lesson-popup-button",
                "planning-commit",
                "planning-commit-status",
                "lock-in-button",
                "exit-match-button",
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
                "report-empty",
                "report-reveal",
                "report-prev-button",
                "report-round-label",
                "report-next-button",
                "report-board",
                "report-summary",
                "report-orders",
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

    /// <summary>
    /// Both families ship as dynamic SDF font assets and are referenced as assets, never as the
    /// raw TrueType file: a .ttf reference renders through a second, non-SDF path that ignores
    /// every size and weight the interface is tuned at.
    /// </summary>
    [Test]
    public void SharedStylesUseSdfFontAssets()
    {
        string[] fontPaths =
        {
            BodyFontPath,
            DisplayFontPath,
            "Assets/Fonts/OswaldCaps/OswaldCapsObl-Regular UI SDF.asset",
        };
        string[] stylePaths =
        {
            "Assets/UI/Shared/BattlePlan.uss",
        };

        foreach (string fontPath in fontPaths)
        {
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
        }

        foreach (string stylePath in stylePaths)
        {
            string source = File.ReadAllText(stylePath);
            Assert.That(source, Does.Contain(BodyFontReference));
            Assert.That(source, Does.Contain(DisplayFontReference));
            Assert.That(
                source,
                Does.Not.Contain("Jost-Regular.ttf").And.Not.Contain("OswaldCaps-Regular.ttf"),
                $"{stylePath} should use the SDF assets instead of the raw fonts."
            );
        }
    }

    /// <summary>
    /// Two families, split by job. Prose reads in Jost; chrome — titles, controls, labels, figures
    /// — is set in the condensed caps face. The default has to be the prose one: the gameplay unit
    /// cards sit outside .toybox-ui scope on purpose and inherit from :root, so a display default
    /// here would set the whole in-match HUD, including its sentences, in condensed capitals.
    /// </summary>
    [Test]
    public void InterfaceTypeDefaultsToProseAndReservesTheDisplayFaceForFigures()
    {
        string reset = File.ReadAllText("Assets/UI/Shared/BattlePlan.uss");

        int rootStart = reset.IndexOf(":root {");
        Assert.That(rootStart, Is.GreaterThanOrEqualTo(0));
        string rootRule = reset.Substring(rootStart, reset.IndexOf('}', rootStart) - rootStart);
        Assert.That(
            rootRule,
            Does.Contain(BodyFontReference),
            "The reading face is the default; the display face is opted into."
        );
        Assert.That(rootRule, Does.Not.Contain(DisplayFontReference));

        int monoStart = reset.IndexOf(".mono {");
        Assert.That(monoStart, Is.GreaterThanOrEqualTo(0), "The figures role needs a named class.");
        string monoRule = reset.Substring(monoStart, reset.IndexOf('}', monoStart) - monoStart);
        Assert.That(monoRule, Does.Contain(DisplayFontReference));

        string card = File.ReadAllText("Assets/UI/Shared/Templates/UnitCard.uxml");
        Assert.That(
            card,
            Does.Contain("unit-card__health-value mono"),
            "Health counts need even figures so the digits do not jitter as damage lands."
        );
    }

    /// <summary>
    /// The display face is an all-caps cut: its lowercase codepoints are remapped to the uppercase
    /// glyphs. That is what lets every heading, button and label in the game render in capitals
    /// without a ToUpper() in the controllers or a shouted string in a UXML — and it is invisible
    /// from the USS, so swapping in a stock Oswald would quietly un-capitalise the whole interface
    /// with nothing else failing.
    /// </summary>
    [Test]
    public void DisplayFaceMapsLowercaseOntoCapitals()
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(DisplayFontSourcePath);
        Assert.That(source, Is.Not.Null, $"Could not import {DisplayFontSourcePath}.");

        // Probed on a throwaway clone rather than the shipped asset: the project asset populates
        // its atlas dynamically, and asking it for glyphs would dirty it from a test run.
        UnityEngine.TextCore.Text.FontAsset probe =
            UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(
                source,
                32,
                4,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                256,
                256,
                UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic,
                true
            );
        try
        {
            Assert.That(probe.TryAddCharacters("aAzZ"), Is.True);
            Assert.That(
                probe.characterLookupTable['a'].glyphIndex,
                Is.EqualTo(probe.characterLookupTable['A'].glyphIndex),
                "The display cut must draw lowercase with the capital glyph."
            );
            Assert.That(
                probe.characterLookupTable['z'].glyphIndex,
                Is.EqualTo(probe.characterLookupTable['Z'].glyphIndex)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }
    }

    [Test]
    public void TacticalToyboxUsesTheDisplaySdfFontAndVectorIcons()
    {
        const string stylePath = "Assets/UI/Shared/TacticalToybox.uss";
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
            "Assets/UI/Shared/Icons/hex.svg",
            "Assets/UI/Shared/Icons/hex-frame.svg",
            "Assets/UI/Shared/Icons/ring.svg",
            "Assets/UI/Shared/Icons/slant-left.svg",
            "Assets/UI/Shared/Icons/slant-right.svg",
            "Assets/UI/Shared/Icons/chevron-down.svg",
        };

        UnityEngine.TextCore.Text.FontAsset fontAsset =
            AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(DisplayFontPath);
        Assert.That(fontAsset, Is.Not.Null, $"Could not import {DisplayFontPath}.");
        Assert.That(
            fontAsset.atlasRenderMode,
            Is.EqualTo(UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA)
        );
        Assert.That(
            fontAsset.atlasPopulationMode,
            Is.EqualTo(UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic)
        );

        string style = File.ReadAllText(stylePath);
        Assert.That(style, Does.Contain(DisplayFontReference));
        Assert.That(style, Does.Not.Contain("OswaldCaps-Regular.ttf"));
        string runtimeTheme = File.ReadAllText("Assets/UI/Shared/BattlePlanRuntime.tss");
        Assert.That(runtimeTheme, Does.Contain(DisplayFontReference));

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
        // Tokens are named for the role they fill, not the colour they hold. Colour-literal names
        // (ink, cream, teal, sky) survive exactly one repaint before they start lying.
        foreach (
            string role in new[]
            {
                "--toy-surface:",
                "--toy-text:",
                "--toy-text-on-accent:",
                "--toy-edge:",
                "--toy-primary:",
                "--toy-select:",
                "--toy-danger:",
                "--toy-focus:",
            }
        )
        {
            Assert.That(tokens, Does.Contain(role), $"Missing semantic token {role}");
        }
        foreach (string hue in new[] { "--toy-ink", "--toy-cream", "--toy-teal", "--toy-sky" })
        {
            Assert.That(
                tokens,
                Does.Not.Contain(hue),
                $"{hue} names a colour rather than a role; use the semantic token instead."
            );
        }
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
        Assert.That(style, Does.Contain(".phase-notch .phase-cluster"));
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
            Does.Contain(".phone.short .hill-status-readout {\n"),
            "Constrained layouts must still place the hill readout in the top bar."
        );
        int notchStart = style.IndexOf(".phase-notch {");
        Assert.That(notchStart, Is.GreaterThanOrEqualTo(0));
        int notchEnd = style.IndexOf("}", notchStart);
        string notchRule = style.Substring(notchStart, notchEnd - notchStart);
        Assert.That(
            notchRule,
            Does.Contain("bottom: -31px;").And.Contain("height: 39px;"),
            "The phase tab hangs below the reserved top bar and onto the board. The camera "
                + "viewport only reserves the opaque bars, so deepening it covers more grid."
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
    public void GameHudBarsReserveCameraSpaceInsteadOfCoveringTheBoard()
    {
        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");
        string styles = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        string scene = File.ReadAllText("Assets/Scenes/Game.unity");

        Assert.That(
            controller,
            Does.Contain("FitBoardViewport").And.Contain("boardCamera.rect = fitted"),
            "The board camera must be fitted to the band the HUD bars leave free."
        );
        Assert.That(
            controller,
            Does.Contain("enemyStatusStrip?.RegisterCallback<GeometryChangedEvent>")
                .And.Contain("hudDock?.RegisterCallback<GeometryChangedEvent>"),
            "Bar heights move with breakpoints and content, so the fit must follow their geometry."
        );

        int rectStart = scene.IndexOf("m_NormalizedViewPortRect:");
        Assert.That(rectStart, Is.GreaterThanOrEqualTo(0));
        string rectBlock = scene.Substring(rectStart, 120);
        Assert.That(
            rectBlock,
            Does.Contain("x: 0").And.Contain("y: 0").And.Contain("width: 1").And.Contain("height: 1"),
            "Game.unity must author a full-screen viewport. GameHUDController owns the insets at "
                + "runtime, and a stale authored crop would hide board the HUD no longer covers."
        );

        foreach (string bar in new[] { ".hud-dock {", ".enemy-status-strip {" })
        {
            int start = styles.IndexOf(bar);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing rule for {bar}");
            string rule = styles.Substring(start, styles.IndexOf('}', start) - start);
            Assert.That(
                rule,
                Does.Contain("background-color: var(--toy-surface);"),
                $"{bar} reserves camera space, so it must be fully opaque; the camera does not "
                    + "paint the band behind it."
            );
        }

        int compactDock = styles.IndexOf(".compact .hud-dock {");
        Assert.That(compactDock, Is.GreaterThanOrEqualTo(0));
        string compactRule = styles.Substring(
            compactDock,
            styles.IndexOf('}', compactDock) - compactDock
        );
        Assert.That(
            compactRule,
            Does.Not.Match(@"(?m)^\s+(left|right|bottom):"),
            "Insetting the dock would leave an unpainted margin outside the camera viewport."
        );
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
            Does.Contain("border-color: var(--toy-focus);"),
            "Focused controls need a cue distinct from the selected fill."
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
                .And.Contain("background-color: var(--toy-select-pressed);")
        );
        Assert.That(
            sharedStyle,
            Does.Contain(".status-line.label--danger")
                .And.Contain(
                    ".button--selected:focus {\n    background-color: var(--toy-select-pressed);"
                )
                .And.Contain("border-width: var(--toy-focus-emphasis);"),
            "The flat theme rings focus with a uniform border instead of thickening three sides "
                + "around an offset bottom edge."
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
    public void JoinOffersEveryPlayableModeWithNoDisabledButtons()
    {
        const string assetPath = "Assets/UI/Join/JoinGame.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        Button elimination = tree.Q<Button>("elimination-button");
        Button king = tree.Q<Button>("king-button");
        Button escort = tree.Q<Button>("escort-button");

        Assert.That(elimination, Is.Not.Null);
        Assert.That(king, Is.Not.Null);
        Assert.That(escort, Is.Not.Null);
        Assert.That(elimination.enabledSelf, Is.True);
        Assert.That(king.enabledSelf, Is.True);
        Assert.That(escort.enabledSelf, Is.True);

        // Named rather than counted: this assembly cannot reference GameMode.
        List<Button> modeOptions = tree.Query<Button>(className: "mode-option").ToList();
        Assert.That(
            modeOptions.Select(option => option.name).ToArray(),
            Is.EquivalentTo(new[] { "elimination-button", "king-button", "escort-button" }),
            "The mode row must advertise exactly the modes JoinGameUIController handles."
        );
        Assert.That(
            modeOptions.TrueForAll(option => option.enabledSelf),
            Is.True,
            "A disabled mode button advertises a mode that does not exist."
        );
        Assert.That(
            modeOptions.Count(option => option.ClassListContains("mode-option--last")),
            Is.EqualTo(1),
            "Exactly one tile closes the row, and it is the one that drops its right margin."
        );
    }

    /// <summary>
    /// The reveal is built and tested but deliberately not shown to players: a static per-round
    /// summary was judged the weaker half of the idea, and the replay that would replace it is
    /// blocked on determinism work. This pins "present but dormant" so it cannot switch itself back
    /// on, and so the markup is not quietly deleted while the recording pipeline still feeds it.
    /// </summary>
    [Test]
    public void GameHudBattleReportRevealIsBuiltButNotShownToPlayers()
    {
        const string assetPath = "Assets/UI/Game/GameHUD.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");

        TemplateContainer tree = asset.Instantiate();
        ScrollView resultsScroll = tree.Q<ScrollView>("results-scroll");
        Assert.That(resultsScroll, Is.Not.Null);

        // Querying from the scroll view rather than the tree proves containment, so re-enabling is
        // one class away rather than a re-layout.
        VisualElement reveal = resultsScroll.Q<VisualElement>("report-reveal");
        Label empty = resultsScroll.Q<Label>("report-empty");

        Assert.That(reveal, Is.Not.Null);
        Assert.That(empty, Is.Not.Null);
        Assert.That(reveal.Q<VisualElement>("report-board"), Is.Not.Null);
        Assert.That(reveal.Q<VisualElement>("report-orders"), Is.Not.Null);
        Assert.That(reveal.Q<Label>("report-round-label"), Is.Not.Null);
        Assert.That(reveal.Q<Label>("report-summary"), Is.Not.Null);
        Assert.That(reveal.Q<Button>("report-prev-button"), Is.Not.Null);
        Assert.That(reveal.Q<Button>("report-next-button"), Is.Not.Null);

        Assert.That(
            reveal.ClassListContains("hidden"),
            Is.True,
            "The reveal is on hold; the results overlay must open as a verdict plus two buttons."
        );
        Assert.That(empty.ClassListContains("hidden"), Is.True);

        string controller = File.ReadAllText("Assets/Scripts/Menus/GameHUDController.cs");
        Assert.That(
            controller,
            Does.Not.Match(@"ShowResults\([^)]*BattleReport"),
            "Nothing on the match-end path may feed the reveal while it is on hold."
        );

        string styles = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        Assert.That(styles, Does.Contain(".report-board"));
        Assert.That(
            styles,
            Does.Contain(".report-board__badge"),
            "Roster-slot badges are the only link between a painted route and its order row."
        );
    }

    [Test]
    public void BattleReportRevealIsWithheldUntilTheMatchEnds()
    {
        string gameLoop = File.ReadAllText("Assets/Scripts/GameManager/GameLoop.cs");

        Assert.That(
            gameLoop,
            Does.Contain("SendBattleReportClientRpc"),
            "The reveal reaches clients through a dedicated RPC."
        );

        int recordCall = gameLoop.IndexOf("RecordBattleReportPlans(paths)", StringComparison.Ordinal);
        int sendCall = gameLoop.IndexOf("SendBattleReportClientRpc(battleReport)", StringComparison.Ordinal);
        int finishGame = gameLoop.IndexOf("private void FinishGame(", StringComparison.Ordinal);

        Assert.That(recordCall, Is.GreaterThan(-1), "Plans must be recorded during the round.");
        Assert.That(sendCall, Is.GreaterThan(-1));
        Assert.That(
            sendCall,
            Is.GreaterThan(finishGame),
            "Replicating committed orders before FinishGame would hand a client the enemy's plans "
                + "mid-match, which is exactly what fog of war exists to deny."
        );
        Assert.That(
            gameLoop.IndexOf("SendBattleReportClientRpc", recordCall, StringComparison.Ordinal),
            Is.EqualTo(sendCall),
            "The only send site must be the one inside FinishGame."
        );
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
    public void AbilityCardChargeDiscExposesRoundCooldownState()
    {
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset card = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(cardPath);
        Assert.That(card, Is.Not.Null, $"Could not import {cardPath}.");

        TemplateContainer tree = card.Instantiate();
        VisualElement indicator = tree.Q<VisualElement>("unit-card-flip-indicator");
        VisualElement icon = tree.Q<VisualElement>("unit-card-flip-icon");
        Label cooldownLabel = tree.Q<Label>("unit-card-cooldown");
        Assert.That(indicator, Is.Not.Null, "UnitCard must expose the ability charge disc.");
        Assert.That(icon, Is.Not.Null, "The charge disc must expose its charged pip.");
        Assert.That(cooldownLabel, Is.Not.Null, "The charge disc must expose cooldown rounds.");
        Assert.That(cooldownLabel.ClassListContains("hidden"), Is.True);
        Assert.That(
            tree.Query<Button>().ToList().Count,
            Is.EqualTo(1),
            "An ability card must expose one whole-card button."
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
        Assert.That(styles, Does.Contain("ability-flare.svg"));
        Assert.That(styles, Does.Contain(".unit-card--ability"));
        Assert.That(
            styles,
            Does.Not.Contain("flip-card.svg"),
            "The charge disc replaced the flip glyph; an ability card has only one face."
        );
        Assert.That(
            styles,
            Does.Contain(".unit-card__flip-indicator--cooldown .unit-card__flip-icon"),
            "A recharging disc must drop its charged pip so only the round count reads."
        );
    }

    /// <summary>
    /// The dock and the contact strip share one template, so the only thing keeping them from
    /// reading as the same component at two sizes is that each fully styles itself from its own
    /// modifier. An ability card leads with the ability; a contact card leads with the unit.
    /// </summary>
    [Test]
    public void AbilityCardLeadsWithTheAbilityAndContactCardLeadsWithTheUnit()
    {
        string styles = File.ReadAllText("Assets/UI/Game/GameHUD.uss");
        string element = File.ReadAllText("Assets/Scripts/Menus/UnitCardElement.cs");

        Assert.That(
            element,
            Does.Contain("unit-card--friendly"),
            "Your own crew's cards must carry their own modifier, not merely lack the enemy one."
        );
        Assert.That(styles, Does.Contain(".unit-card--friendly .unit-card__ability"));
        Assert.That(styles, Does.Contain(".unit-card--friendly .unit-card__name"));
        Assert.That(styles, Does.Contain(".unit-card--enemy .unit-card__ability"));
        Assert.That(styles, Does.Contain(".unit-card--enemy .unit-card__name"));

        // Selection and "ability ordered" are separate facts and must not both be the accent, or
        // the loudest colour on the screen stops answering which orders have been given.
        int selectedRule = styles.IndexOf(".unit-card--selected {", StringComparison.Ordinal);
        Assert.That(selectedRule, Is.GreaterThan(-1), "The selected card must still be styled.");
        string selectedBody = styles.Substring(
            selectedRule,
            styles.IndexOf('}', selectedRule) - selectedRule
        );
        Assert.That(
            selectedBody,
            Does.Not.Contain("--toy-primary"),
            "Selection must not spend the accent; that belongs to the ordered card."
        );
        Assert.That(selectedBody, Does.Contain("--toy-select"));
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

        // One press orders the ability. It used to take two, the first spent only on selecting the
        // unit, so reaching an ability from the dock meant already knowing the card had a back.
        Assert.That(planner, Does.Contain("TrySetSelectionMode(unit, true)"));
        Assert.That(card, Does.Contain("Order "));
        Assert.That(
            card,
            Does.Not.Contain("Select again"),
            "The card names the order a press gives, not how many presses it has taken."
        );
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
