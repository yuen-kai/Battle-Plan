using System.IO;
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
                "route-label",
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
                "join-code-input",
                "join-match-button",
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
                "match-type-label",
                "fog-label",
                "hill-status-label",
                "unit-cards",
                "unit-card-0",
                "unit-card-1",
                "unit-card-2",
                "deployment-overlay",
                "deployment-status",
                "results-overlay",
                "results-panel",
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
                "unit-card-ability",
                "unit-card-state",
                "unit-card-move",
                "unit-card-ability-button",
                "unit-card-ability-charge",
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
        "Assets/UI/Title/TitleScreen.uss",
        "Assets/UI/Join/JoinGame.uss",
        "Assets/UI/Home/CharacterSelection.uss",
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
            "Assets/UI/Shared/BattlePlanRuntime.tss",
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
    public void UnitCardAbilityCopyIsMatchLongWithoutPerRound()
    {
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset card = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(cardPath);
        Assert.That(card, Is.Not.Null, $"Could not import {cardPath}.");

        Label chargeLabel = card.Instantiate().Q<Label>("unit-card-ability-charge");
        Assert.That(
            chargeLabel,
            Is.Not.Null,
            "UnitCard must surface the match-long ability charge copy."
        );
        string charge = (chargeLabel.text ?? string.Empty).ToLowerInvariant();
        Assert.That(charge, Does.Contain("match"), "Ability charge copy must be match-scoped.");
        Assert.That(charge, Does.Not.Contain("round"), "Charges are match-long, never per round.");
        Assert.That(charge, Does.Not.Contain("turn"), "Charges are match-long, never per turn.");

        // The runtime copy builder must not describe charges as per-round either.
        string source = File.ReadAllText("Assets/Scripts/Menus/UnitCardElement.cs")
            .ToLowerInvariant();
        Assert.That(
            source,
            Does.Contain("this match"),
            "Runtime ability copy should state the match-long scope."
        );
        Assert.That(source, Does.Not.Contain("per round"));
        Assert.That(source, Does.Not.Contain("per turn"));
        Assert.That(source, Does.Not.Contain("each round"));
    }

    [Test]
    public void UnitOptionExposesSelectionAndAvailabilityStateClasses()
    {
        const string stylePath = "Assets/UI/Home/CharacterSelection.uss";
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
