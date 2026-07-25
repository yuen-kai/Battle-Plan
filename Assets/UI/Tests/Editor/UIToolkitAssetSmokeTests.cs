using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
                "unit-cards",
                "hud-dock",
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

    // The character-select map plate. ArenaCapture writes this file at the same size.
    private const string PreviewPath = "Assets/Images/MapPreview.png";
    private const int PreviewWidth = 1080;
    private const int PreviewHeight = 720;

    // Sampling and thresholds for AssertCaptureHasContent. Every sixteenth pixel is plenty to
    // tell a picture from a fill, and both bars sit far below what the current board capture
    // measures (spread 0.85, 139 distinct colours over 3060 samples).
    private const int CaptureSampleStride = 16;
    private const float MinimumLuminanceSpread = 0.15f;
    private const int MinimumDistinctColours = 24;

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

    // The plate was once a diagram drawn in C# by MapPreviewGenerator, and this test redrew it
    // and compared bytes. It is now a GPU render of the real board (Battle Plan ▸ Art ▸ Capture
    // Board Preview), which is not byte-reproducible across machines, drivers or quality
    // settings: a byte comparison would pass here and fail on the next machine, which costs
    // more to diagnose than having no test at all. What is asserted instead is everything the
    // plate actually depends on -- the caption and the board agree, the file is at plate size,
    // and it holds a picture rather than a cleared frame.
    [Test]
    public void CharacterSelectionPreviewShipsCapturedBoardArt()
    {
        const string assetPath = "Assets/UI/Home/CharacterSelection.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        Texture2D preview = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewPath);
        Assert.That(asset, Is.Not.Null, $"Could not import {assetPath}.");
        Assert.That(preview, Is.Not.Null, $"Could not import {PreviewPath} as a texture.");

        TemplateContainer tree = asset.Instantiate();
        Label caption = tree.Q<Label>(className: "map-caption");
        Assert.That(caption, Is.Not.Null);
        Assert.That(caption.text, Does.Contain("15 × 10"));
        Assert.That(caption.text, Does.Not.Contain("9 × 10"));

        // The imported dimensions are what the plate samples, so an importer change that
        // downsizes or squares the texture is caught here and not on someone's screen.
        Assert.That(
            new Vector2Int(preview.width, preview.height),
            Is.EqualTo(new Vector2Int(PreviewWidth, PreviewHeight)),
            $"{PreviewPath} imports at {preview.width}×{preview.height}. The map plate is "
                + $"authored against {PreviewWidth}×{PreviewHeight}; re-run Battle Plan ▸ Art ▸ "
                + "Capture Board Preview, and check nPOTScale and maxTextureSize on the importer."
        );
        AssertCaptureHasContent(PreviewPath, PreviewWidth, PreviewHeight);
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
            "Assets/UI/Shared/Icons/chevron-down.svg",
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

    /// Every 24-grid glyph must sit on one stroke weight. A single off-weight
    /// icon is the most visible tell of hand-assembled UI at 24px.
    [Test]
    public void IconSetSharesOneStrokeWeightOnTheTwentyFourGrid()
    {
        foreach (string iconPath in Directory.GetFiles("Assets/UI/Shared/Icons", "*.svg"))
        {
            string source = File.ReadAllText(iconPath);
            if (iconPath.EndsWith("ability-flare.svg"))
                continue;

            Assert.That(
                source,
                Does.Contain("viewBox=\"0 0 24 24\""),
                $"{iconPath} must be authored on the 24 grid."
            );
            Assert.That(
                source,
                Does.Contain("stroke-width=\"2\"").And.Not.Contain("stroke-width=\"2."),
                $"{iconPath} must use the 2px system stroke."
            );
            Assert.That(
                source,
                Does.Contain("stroke-linecap=\"round\"")
                    .And.Contain("stroke-linejoin=\"round\""),
                $"{iconPath} must keep the set's round caps and joins."
            );
            Assert.That(
                source,
                Does.Contain("stroke=\"#fff\""),
                $"{iconPath} must ship untinted; colour is applied by USS."
            );
        }

        Assert.That(
            File.Exists("Assets/UI/Shared/Icons/LICENSE-lucide.txt"),
            Is.True,
            "chevron-down is lifted from Lucide, so the ISC notice ships beside it."
        );
    }

    /// Saira Condensed carries every display string. Without the generated SDF
    /// assets the USS silently falls back to Rubik and the hierarchy disappears
    /// with no error, so this is asserted rather than eyeballed.
    [Test]
    public void DisplayTypeUsesGeneratedSairaCondensedFontAssets()
    {
        string[] fontPaths =
        {
            "Assets/Fonts/SairaCondensed/SairaCondensed-SemiBold UI SDF.asset",
            "Assets/Fonts/SairaCondensed/SairaCondensed-ExtraBold UI SDF.asset",
        };

        foreach (string fontPath in fontPaths)
        {
            UnityEngine.TextCore.Text.FontAsset fontAsset =
                AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(fontPath);
            Assert.That(
                fontAsset,
                Is.Not.Null,
                $"Could not import {fontPath}. Generate it with Window ▸ TextMeshPro ▸ "
                    + "Font Asset Creator (SDFAA, padding 9, 1024×1024, ASCII + Extended "
                    + "ASCII plus · — … ×)."
            );
            Assert.That(
                fontAsset.atlasRenderMode,
                Is.EqualTo(UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA)
            );
        }

        Assert.That(
            File.Exists("Assets/Fonts/SairaCondensed/OFL.txt"),
            Is.True,
            "Shipping the licence file beside the TTFs is not optional."
        );

        string baseStyle = File.ReadAllText("Assets/UI/Shared/BattlePlan.uss");
        foreach (string fontPath in fontPaths)
        {
            Assert.That(
                baseStyle,
                Does.Contain("project://database/" + fontPath),
                "The type scale must bind the generated SDF asset, not the raw TTF."
            );
        }

        Assert.That(baseStyle, Does.Not.Contain("SairaCondensed-SemiBold.ttf"));
        Assert.That(baseStyle, Does.Not.Contain("SairaCondensed-ExtraBold.ttf"));
    }

    // transition-timing-function takes a keyword, never a cubic-bezier(). If the palette ever
    // reinstates curves, USS cannot parse them and every transition silently falls back to
    // `ease` -- motion still plays, so nothing looks broken and nobody notices. Both halves of
    // the contract are asserted here because the failure is invisible at runtime.
    [Test]
    public void EasingTokensShipKeywordsAndConsumersReferenceThem()
    {
        const string tokenPath = "Assets/UI/Shared/TacticalToyboxTokens.uss";
        string tokens = File.ReadAllText(tokenPath);

        // Unity's keyword set: ease, linear, ease-in/out/in-out, and the -sine/-cubic/-circ/
        // -elastic/-back/-bounce families.
        foreach (string easing in new[] { "--bp-ease-out", "--bp-ease-in", "--bp-ease-snap" })
        {
            Match declared = Regex.Match(
                tokens,
                $@"^\s+{Regex.Escape(easing)}:\s*(?<v>[^;]+);",
                RegexOptions.Multiline
            );
            Assert.That(declared.Success, Is.True, $"{easing} must be declared in {tokenPath}.");

            string value = declared.Groups["v"].Value.Trim();
            Assert.That(
                value,
                Does.Not.Contain("cubic-bezier"),
                $"{easing} is '{value}'. USS transition-timing-function cannot parse "
                    + "cubic-bezier(); the declaration is dropped and the transition silently "
                    + "falls back to `ease`. Easing tokens must ship a Unity keyword."
            );
            Assert.That(
                Regex.IsMatch(value, @"^(linear|ease(-(in|out|in-out))?"
                    + @"(-(sine|cubic|circ|elastic|back|bounce|quad|quart|quint|expo))?)$"),
                Is.True,
                $"{easing} is '{value}', which is not a Unity easing keyword."
            );
        }

        // StyleSheetPaths omits the runtime theme, which also drives transitions.
        foreach (
            string stylePath in StyleSheetPaths.Append("Assets/UI/Shared/BattlePlanRuntime.tss")
        )
        {
            if (stylePath == tokenPath)
            {
                continue;
            }

            foreach (
                Match used in Regex.Matches(
                    File.ReadAllText(stylePath),
                    @"transition-timing-function:\s*(?<v>[^;]+);"
                )
            )
            {
                string value = used.Groups["v"].Value.Trim();

                // `linear` marks a colour cross-fade with no motion to ease. It is the one
                // literal allowed, because the palette defines no token for it.
                if (value == "linear")
                {
                    continue;
                }

                Assert.That(
                    value,
                    Does.StartWith("var(--bp-ease-"),
                    $"{stylePath} hardcodes easing '{value}'. Motion curves come from the "
                        + "Palette §9.6 tokens so the art director can retune them in one place."
                );
            }
        }
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
        string[] tokenConsumerPaths =
        {
            "Assets/UI/Shared/BattlePlan.uss",
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
            Assert.That(
                consumer,
                Does.Not.Contain("rgb(").And.Not.Contain("rgba("),
                $"{consumerPath} must consume semantic tokens instead of raw colour literals. "
                    + "ArtBible-Palette.md is the only place a colour value is authored."
            );
            Assert.That(
                System.Text.RegularExpressions.Regex.IsMatch(consumer, @"#[0-9a-fA-F]{3,8}\b"),
                Is.False,
                $"{consumerPath} must not author a hex literal."
            );

            var tokenMatches = System.Text.RegularExpressions.Regex.Matches(
                consumer,
                @"var\((--(?:toy|bp)-[a-z0-9-]+)\)"
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
            Does.Contain("top: 92px;").And.Contain("height: 34px;"),
            "The phase readout is a free-floating pill 12px below the enemy contact row, "
                + "not a tab hanging off a top bar."
        );
        Assert.That(
            style,
            Does.Contain("full-screen"),
            "Collapsing the HUD bars into floating widgets only works if Game.unity's camera "
                + "viewport rect is full-screen; the stylesheet must keep saying so."
        );
        foreach (string frame in new[] { ".enemy-status-strip {", ".hud-dock {" })
        {
            int frameStart = style.IndexOf(frame);
            Assert.That(frameStart, Is.GreaterThanOrEqualTo(0));
            string frameRule = style.Substring(
                frameStart,
                style.IndexOf("}", frameStart) - frameStart
            );
            Assert.That(
                frameRule,
                Does.Contain("background-color: var(--bp-transparent);"),
                $"{frame} is a positioning frame for floating widgets, not a full-bleed bar."
            );
        }
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

    // The old HUD reserved screen space with two full-bleed bars and Game.unity's camera was
    // inset to match (y 0.112, height 0.83). The floating-widget HUD draws over a full-bleed
    // board instead, so the scene has to give that space back or the board stays letterboxed
    // against transparent frames.
    [Test]
    public void GameSceneCameraRendersFullBleedBehindFloatingHud()
    {
        const string scenePath = "Assets/Scenes/Game.unity";
        Assert.That(
            File.Exists(scenePath),
            Is.True,
            $"{scenePath} must exist to validate the HUD's camera contract."
        );

        string scene = File.ReadAllText(scenePath);
        MatchCollection blocks = Regex.Matches(
            scene,
            @"m_NormalizedViewPortRect:[^\n]*\n(?<body>(?:[ \t]+[^\n]*\n){5})"
        );
        Assert.That(
            blocks.Count,
            Is.GreaterThan(0),
            "Could not find a camera viewport rect in Game.unity."
        );

        foreach (Match block in blocks)
        {
            string body = block.Groups["body"].Value;
            foreach (var field in new[] { ("x", 0f), ("y", 0f), ("width", 1f), ("height", 1f) })
            {
                Match value = Regex.Match(
                    body,
                    $@"^[ \t]+{field.Item1}:[ \t]*(?<v>[-\d.eE+]+)[ \t]*$",
                    RegexOptions.Multiline
                );
                Assert.That(
                    value.Success,
                    Is.True,
                    $"Camera viewport rect in Game.unity is missing '{field.Item1}'."
                );
                Assert.That(
                    float.Parse(value.Groups["v"].Value, CultureInfo.InvariantCulture),
                    Is.EqualTo(field.Item2).Within(0.0001f),
                    "Game.unity's camera viewport rect must be full-screen "
                        + $"(x 0, y 0, width 1, height 1) but '{field.Item1}' is "
                        + $"{value.Groups["v"].Value}. The HUD no longer reserves layout space "
                        + "with opaque bars, so an inset rect letterboxes the board behind "
                        + "transparent frames."
                );
            }
        }
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
            Does.Contain("border-color: var(--bp-focus-light);"),
            "Focus is a high-contrast ring, not a hue: team blue may not double as a focus cue."
        );
        Assert.That(
            sharedStyle,
            Does.Not.Contain("var(--toy-sky)"),
            "Team blue is reserved for friendly state; it must not be reintroduced as chrome."
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
            Does.Contain(".narrow .roster-main {\n    height: 300px;"),
            "Once the three columns stack, the unit library still needs a bounded height."
        );
        Assert.That(
            rosterStyle,
            Does.Contain(".selected-slot:disabled:hover")
                .And.Contain("border-left-color: var(--bp-blue);"),
            "A filled crew slot keeps its blue state rule while the crew is locked."
        );
        Assert.That(
            sharedStyle,
            Does.Contain(".status-line.label--danger")
                .And.Contain(".button--selected:focus {\n    border-width: var(--bp-bw-focus);")
                .And.Contain("border-color: var(--bp-focus-light);"),
            "A selected control must still show the focus ring on all four sides."
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
        Assert.That(checkmarkRule, Does.Contain("width: 20px"));
        Assert.That(checkmarkRule, Does.Contain("height: 20px"));
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

    /// <summary>
    /// Asserts a captured PNG contains an image, at the source resolution and independently of
    /// how the texture happens to be imported.
    ///
    /// The file is decoded from disk rather than read off the imported Texture2D because the
    /// captures import with Read/Write disabled and platform compression on: GetPixels would
    /// throw, and the compressed pixels would not be the ones that were rendered. Decoding also
    /// means the size assert here is against what was written, not against what the importer
    /// chose to keep.
    ///
    /// The two content bars are deliberately coarse. Colour is quantised to five bits per
    /// channel so driver-level rounding cannot move the count, and the thresholds only separate
    /// a continuous-tone render from a flat one. That catches the failures worth catching: a
    /// black frame, a cleared frame, a fully transparent frame -- and a flat-shaded diagram,
    /// which is what the plate would become if the drawn generator ever overwrote it again.
    /// </summary>
    private static void AssertCaptureHasContent(string assetPath, int width, int height)
    {
        Assert.That(File.Exists(assetPath), Is.True, $"{assetPath} is missing.");

        Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Assert.That(
                decoded.LoadImage(File.ReadAllBytes(assetPath), false),
                Is.True,
                $"{assetPath} is not a decodable PNG. A capture that failed part way through "
                    + "leaves a truncated file behind."
            );
            Assert.That(
                new Vector2Int(decoded.width, decoded.height),
                Is.EqualTo(new Vector2Int(width, height)),
                $"{assetPath} was written at {decoded.width}×{decoded.height}, not "
                    + $"{width}×{height}."
            );

            Color32[] pixels = decoded.GetPixels32();
            HashSet<int> colours = new();
            float darkest = 1f;
            float brightest = 0f;
            int opaque = 0;

            for (int y = 0; y < decoded.height; y += CaptureSampleStride)
            {
                for (int x = 0; x < decoded.width; x += CaptureSampleStride)
                {
                    Color32 pixel = pixels[(y * decoded.width) + x];

                    // A transparent pixel carries no colour, so it must not drag the range.
                    if (pixel.a < 8)
                        continue;

                    opaque++;
                    float luminance =
                        ((0.2126f * pixel.r) + (0.7152f * pixel.g) + (0.0722f * pixel.b)) / 255f;
                    darkest = Mathf.Min(darkest, luminance);
                    brightest = Mathf.Max(brightest, luminance);
                    colours.Add(((pixel.r >> 3) << 10) | ((pixel.g >> 3) << 5) | (pixel.b >> 3));
                }
            }

            Assert.That(
                opaque,
                Is.GreaterThan(0),
                $"{assetPath} is fully transparent. The capture cleared its target and drew "
                    + "nothing into it."
            );

            float spread = brightest - darkest;
            Assert.That(
                spread,
                Is.GreaterThanOrEqualTo(MinimumLuminanceSpread),
                $"{assetPath} spans {spread:F3} of luminance across {opaque} samples, which is "
                    + "a flat fill rather than a rendered image. Re-run the capture and confirm "
                    + "it is looking at the board."
            );
            Assert.That(
                colours.Count,
                Is.GreaterThanOrEqualTo(MinimumDistinctColours),
                $"{assetPath} holds {colours.Count} distinct colours across {opaque} samples. "
                    + "The plate must be a render of the real board, which is continuous-tone; "
                    + "a handful of colours means a flat fill or a flat-shaded diagram."
            );
        }
        finally
        {
            Object.DestroyImmediate(decoded);
        }
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
