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
