using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The character sandbox's picker screen: every roster-eligible character in AllUnits.asset, listed
/// with its portrait and ability, one click each to drop it onto a board against a stationary dummy.
/// Also the sandbox's live control panel — the dummy's crew and whether it shoots back can both be
/// changed while a match is running.
///
/// An <see cref="EditorWindow"/> rather than an in-game screen because this is a development tool
/// for choosing what to load, so it must be usable before Play mode has started, and because it can
/// then stay docked beside the Game view to restart with a different character without leaving Play
/// mode. Reads the catalog through <see cref="AssetDatabase"/> so the list is correct with no match
/// running and no scene open.
/// </summary>
public class CharacterSandboxWindow : EditorWindow
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";
    private const float PortraitSize = 56f;

    private UnitDatabase catalog;
    private Vector2 scroll;
    private int dummyUnitIndex = CharacterSandbox.DefaultDummyUnitIndex;
    private bool dummyFightsBack;
    private int enemyCount = 1;
    private int allyCount = 1;

    [MenuItem("Battle Plan/Character Sandbox")]
    public static void Open()
    {
        CharacterSandboxWindow window = GetWindow<CharacterSandboxWindow>("Char Sandbox");
        window.minSize = new Vector2(320f, 360f);
        window.Show();
    }

    private void OnEnable()
    {
        catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        // Repaint on Play-mode transitions so the status line and the live controls are never stale.
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange change) => Repaint();

    private void OnGUI()
    {
        if (catalog == null || catalog.units == null || catalog.units.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No unit catalog at {CatalogPath}, so there is nothing to test.",
                MessageType.Error
            );
            return;
        }

        DrawStatus();
        EditorGUILayout.Space();
        DrawDummyControls();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Pick a character to test", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int unitIndex = 0; unitIndex < catalog.units.Count; unitIndex++)
            DrawCharacterRow(unitIndex);
        EditorGUILayout.EndScrollView();
    }

    private void DrawStatus()
    {
        if (CharacterSandbox.IsRunning)
        {
            EditorGUILayout.HelpBox(
                $"Testing {NameOf(SandboxSession.TestUnitIndex)} at "
                    + $"{SandboxSession.PlayerSpawn} vs {NameOf(SandboxSession.DummyUnitIndex)} at "
                    + $"{SandboxSession.EnemySpawn}. Walls at {SandboxSession.PlayerAdjacentWall} "
                    + $"and {SandboxSession.EnemyAdjacentWall}. Plan, dodge and execute rounds "
                    + "through the normal HUD; the planning timer is effectively open-ended.",
                MessageType.Info
            );
            if (GUILayout.Button("Stop (back to title screen)"))
                GameLoop.Instance?.ExitToMainMenu();
            return;
        }

        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "No sandbox match running. Pick a character below."
                : "Picking a character enters Play mode and loads straight onto the board.",
            MessageType.None
        );
    }

    private void DrawDummyControls()
    {
        EditorGUILayout.LabelField("Target dummy", EditorStyles.boldLabel);

        int liveDummyIndex = CharacterSandbox.IsRunning
            ? SandboxSession.DummyUnitIndex
            : dummyUnitIndex;
        int picked = EditorGUILayout.Popup(
            "Unit",
            Mathf.Clamp(liveDummyIndex, 0, catalog.units.Count - 1),
            BuildUnitNames()
        );
        if (picked != liveDummyIndex)
        {
            dummyUnitIndex = picked;
            // Which unit the dummy is gets decided before the board is built, so changing it
            // mid-match means restarting with the same character under test.
            if (CharacterSandbox.IsRunning)
                CharacterSandbox.Launch(
                    SandboxSession.TestUnitIndex,
                    picked,
                    dummyFightsBack,
                    enemyCount,
                    allyCount
                );
        }

        int liveEnemyCount = CharacterSandbox.IsRunning ? SandboxSession.EnemyCount : enemyCount;
        int pickedCount = EditorGUILayout.IntSlider(
            "How many",
            Mathf.Clamp(liveEnemyCount, 1, SandboxSession.MaxEnemyCount),
            1,
            SandboxSession.MaxEnemyCount
        );
        if (pickedCount != liveEnemyCount)
        {
            enemyCount = pickedCount;
            // Crew size is fixed when the board is built, so this also needs a restart.
            if (CharacterSandbox.IsRunning)
                CharacterSandbox.Launch(
                    SandboxSession.TestUnitIndex,
                    dummyUnitIndex,
                    dummyFightsBack,
                    pickedCount,
                    allyCount
                );
        }
        EditorGUILayout.LabelField(
            " ",
            "More than one is for abilities that hit several enemies at once.",
            EditorStyles.miniLabel
        );

        int liveAllyCount = CharacterSandbox.IsRunning ? SandboxSession.AllyCount : allyCount;
        int pickedAllies = EditorGUILayout.IntSlider(
            "Teammates",
            Mathf.Clamp(liveAllyCount, 1, SandboxSession.MaxAllyCount),
            1,
            SandboxSession.MaxAllyCount
        );
        if (pickedAllies != liveAllyCount)
        {
            allyCount = pickedAllies;
            if (CharacterSandbox.IsRunning)
                CharacterSandbox.Launch(
                    SandboxSession.TestUnitIndex,
                    dummyUnitIndex,
                    dummyFightsBack,
                    enemyCount,
                    pickedAllies
                );
        }
        EditorGUILayout.LabelField(
            " ",
            "Counts the character under test. More is for abilities that buff nearby allies.",
            EditorStyles.miniLabel
        );

        bool liveFightsBack = CharacterSandbox.IsRunning
            ? SandboxSession.DummyFightsBack
            : dummyFightsBack;
        bool toggled = EditorGUILayout.Toggle("Shoots back", liveFightsBack);
        if (toggled != liveFightsBack)
        {
            dummyFightsBack = toggled;
            // Unlike the crew, this one takes effect immediately: SandboxDirector re-applies it
            // every frame, so it can be flipped in the middle of a round.
            if (CharacterSandbox.IsRunning)
                SandboxSession.DummyFightsBack = toggled;
        }
        EditorGUILayout.LabelField(
            " ",
            "The dummy never moves and never uses an ability.",
            EditorStyles.miniLabel
        );
    }

    private void DrawCharacterRow(int unitIndex)
    {
        UnitData unit = catalog.units[unitIndex];
        if (unit == null)
            return;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            Rect portrait = GUILayoutUtility.GetRect(
                PortraitSize,
                PortraitSize,
                GUILayout.Width(PortraitSize),
                GUILayout.Height(PortraitSize)
            );
            DrawPortrait(portrait, unit);

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(unit.unitName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"{unit.abilityName}  ·  {unit.maxHealth:0} HP  ·  {unit.damage} dmg",
                    EditorStyles.miniLabel
                );
                if (!unit.IsRosterEligible)
                {
                    EditorGUILayout.LabelField(
                        "Not roster-eligible — cannot be fielded.",
                        EditorStyles.miniLabel
                    );
                }
            }

            using (new EditorGUI.DisabledScope(!unit.IsRosterEligible))
            {
                bool isUnderTest =
                    CharacterSandbox.IsRunning && SandboxSession.TestUnitIndex == unitIndex;
                if (
                    GUILayout.Button(
                        isUnderTest ? "Restart" : "Test",
                        GUILayout.Width(64f),
                        GUILayout.Height(PortraitSize)
                    )
                )
                {
                    CharacterSandbox.Launch(
                        unitIndex,
                        CharacterSandbox.IsRunning ? SandboxSession.DummyUnitIndex : dummyUnitIndex,
                        CharacterSandbox.IsRunning
                            ? SandboxSession.DummyFightsBack
                            : dummyFightsBack,
                        CharacterSandbox.IsRunning ? SandboxSession.EnemyCount : enemyCount,
                        CharacterSandbox.IsRunning ? SandboxSession.AllyCount : allyCount
                    );
                }
            }
        }
    }

    /// <summary>
    /// Sprites are drawn through their own texture rect rather than with a plain
    /// <c>GUI.DrawTexture</c>, because a portrait packed into an atlas would otherwise draw the
    /// whole atlas.
    /// </summary>
    private static void DrawPortrait(Rect rect, UnitData unit)
    {
        Sprite sprite = unit.unitSprite;
        if (sprite == null || sprite.texture == null)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.2f));
            EditorGUI.LabelField(rect, "no art", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        Rect textureRect = sprite.textureRect;
        GUI.DrawTextureWithTexCoords(
            rect,
            sprite.texture,
            new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height
            )
        );
    }

    private string[] BuildUnitNames()
    {
        List<string> names = new(catalog.units.Count);
        for (int unitIndex = 0; unitIndex < catalog.units.Count; unitIndex++)
            names.Add(NameOf(unitIndex));
        return names.ToArray();
    }

    private string NameOf(int unitIndex)
    {
        if (catalog?.units == null || unitIndex < 0 || unitIndex >= catalog.units.Count)
            return $"#{unitIndex}";
        UnitData unit = catalog.units[unitIndex];
        return unit == null ? $"#{unitIndex}" : unit.unitName;
    }
}
