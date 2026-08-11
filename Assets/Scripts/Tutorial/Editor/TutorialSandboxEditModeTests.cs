using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("Tutorial")]
public class TutorialSandboxEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";

    [TearDown]
    public void TearDown()
    {
        TutorialSession.End();
    }

    private static UnitData LoadTutorialUnit()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        Assert.That(catalog?.units, Is.Not.Null, $"Could not load {CatalogPath}.");
        Assert.That(
            catalog.units.Count,
            Is.GreaterThan(TutorialSession.TutorialUnitIndex),
            "The tutorial unit index must exist in the live catalog."
        );
        return catalog.units[TutorialSession.TutorialUnitIndex];
    }

    [Test]
    public void TutorialUnitIsTheSoldierAndItsAbilityOpensADodgeWindow()
    {
        UnitData unit = LoadTutorialUnit();

        // Roster indices are a serialized contract, so a catalog reorder must fail here rather
        // than silently hand the tutorial a different character.
        Assert.That(unit.unitName, Is.EqualTo("Soldier"));

        // The dodge lesson is taught from both sides with this one ability: the student watches the
        // opponent step clear of their throw, then answers the same telegraph. An ability with no
        // response range never opens a dodge window at all, which would delete half the script.
        Assert.That(
            unit.responseRange,
            Is.GreaterThan(0f),
            $"{unit.abilityName} must alert its targets for the tutorial's dodge lesson."
        );
        Assert.That(unit.selectAbilitySquare, Is.True, "The taught ability targets a cell.");
    }

    [Test]
    public void SpawnsStartOutsideRifleReachAndInsideAbilityReach()
    {
        UnitData unit = LoadTutorialUnit();
        Vector2Int player = TutorialSession.PlayerSpawn;
        Vector2Int enemy = TutorialSession.EnemySpawn;

        Assert.That(GridSystem.IsCellInBounds(player), Is.True);
        Assert.That(GridSystem.IsCellInBounds(enemy), Is.True);
        Assert.That(GameLoop.wallLayout, Has.No.Member(player));
        Assert.That(GameLoop.wallLayout, Has.No.Member(enemy));
        Assert.That(
            player.x,
            Is.EqualTo(enemy.x),
            "The crews share a column so the opening advance is a straight line."
        );

        int separation = Mathf.Abs(player.x - enemy.x) + Mathf.Abs(player.y - enemy.y);
        Assert.That(
            separation,
            Is.GreaterThan(unit.targetRange),
            "Starting inside rifle reach would open the match with gunfire the student did not "
                + "cause, which is the point of the first lesson."
        );
        Assert.That(
            separation,
            Is.LessThanOrEqualTo(unit.abilitySquareRange),
            "The scripted opponent throws from where it stands, so it must start in range."
        );

        // A wall between them would block both the advance and the throw.
        int low = Mathf.Min(player.y, enemy.y);
        int high = Mathf.Max(player.y, enemy.y);
        for (int row = low; row <= high; row++)
        {
            Assert.That(
                GameLoop.wallLayout,
                Has.No.Member(new Vector2Int(player.x, row)),
                $"The tutorial lane must stay clear; ({player.x}, {row}) is a wall."
            );
        }
    }

    [Test]
    public void RosterValidatesAndFieldsTheTaughtUnitInTheSpawnedSlot()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        int[] roster = TutorialSession.BuildRoster();

        Assert.That(
            roster.Length,
            Is.EqualTo(RosterRules.UnitsPerPlayer),
            "The sandbox keeps a full-length roster so ConfigureTeam and roster validation are "
                + "untouched; only the number of units spawned changes."
        );
        Assert.That(RosterRules.Validate(roster, catalog.units).IsValid, Is.True);
        for (int slot = 0; slot < TutorialSession.UnitsPerTeam; slot++)
        {
            Assert.That(
                roster[slot],
                Is.EqualTo(TutorialSession.TutorialUnitIndex),
                "Every slot the tutorial actually spawns must hold the taught unit."
            );
        }
    }

    [Test]
    public void SpawnLayoutFieldsOneUnitPerTeamOnOppositeSides()
    {
        TutorialSession.Begin();

        Assert.That(GameLoop.UnitsPerTeamThisMatch, Is.EqualTo(TutorialSession.UnitsPerTeam));

        var layout = TutorialSession.CreateSpawnLayout();
        Assert.That(layout.Count, Is.EqualTo(GameLoop.TeamCount));
        Assert.That(
            layout.All(positions => positions.Length == TutorialSession.UnitsPerTeam),
            Is.True
        );
        Assert.That(layout[GameLoop.HostTeamIndex][0], Is.EqualTo(TutorialSession.PlayerSpawn));
        Assert.That(layout[GameLoop.OpponentTeamIndex][0], Is.EqualTo(TutorialSession.EnemySpawn));

        TutorialSession.End();
        Assert.That(
            GameLoop.UnitsPerTeamThisMatch,
            Is.EqualTo(RosterRules.UnitsPerPlayer),
            "Ending the sandbox must hand full crews back to ordinary matches."
        );
    }

    [Test]
    public void MatchOptionsAreEliminationAgainstAiWithoutFog()
    {
        MatchOptions options = TutorialSession.BuildMatchOptions();

        Assert.That(options.gameMode, Is.EqualTo(GameMode.Elimination));
        Assert.That(options.IsBotMatch, Is.True, "The sandbox runs as a single-client host match.");
        Assert.That(
            options.fogOfWar,
            Is.False,
            "Hiding the opponent would hide the lesson; fog is left for real matches."
        );
    }

    [Test]
    public void CoachingCopyReadsAsPlainInstructions()
    {
        string[] instructions =
        {
            TutorialDirector.DrawRoutePrompt,
            TutorialDirector.MoveCloserPrompt,
            TutorialDirector.LockInPrompt,
            TutorialDirector.UseAbilityPrompt,
            TutorialDirector.PickTargetPrompt,
            TutorialDirector.DodgeNowPrompt,
        };
        string[] lessons =
        {
            TutorialDirector.AutoShootLesson,
            TutorialDirector.EnemyDodgedLesson,
            TutorialDirector.ClosingLesson,
        };
        string[] copy = instructions.Concat(lessons).ToArray();

        foreach (string line in copy)
        {
            Assert.That(line, Is.Not.Empty);
            Assert.That(
                line,
                Does.Not.Contain("—").And.Not.Contain("–"),
                "Tutorial copy is written without dashes for asides."
            );
            Assert.That(line.Trim(), Is.EqualTo(line));
        }

        Assert.That(copy.Distinct().Count(), Is.EqualTo(copy.Length));
        Assert.That(
            instructions.Intersect(lessons),
            Is.Empty,
            "A line is either the instruction on the dock or a lesson on the card, never both."
        );

        // The card is named twice and only twice: where the student is asked to press it, and in
        // the closing line that generalises the dock to a full crew. It used to be named once,
        // because an ability was reached by clicking the unit a second time and the card was a
        // detail that could wait for real matches. Pressing the card is now the way an ability is
        // ordered, so it is the lesson rather than the footnote.
        Assert.That(TutorialDirector.UseAbilityPrompt, Does.Contain("card"));
        Assert.That(TutorialDirector.ClosingLesson, Does.Contain("card"));
        Assert.That(
            copy.Count(line => line.Contains("card")),
            Is.EqualTo(2),
            "Only the ability prompt and the closing lesson should talk about ability cards."
        );
    }

    [Test]
    public void TutorialEndsOnItsOwnResultRatherThanAVictory()
    {
        MatchResult result = MatchResult.ForWinner(
            GameLoop.HostTeamIndex,
            MatchResultReason.TutorialComplete
        );

        Assert.That(result.IsValid, Is.True);
        Assert.That(
            result.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo("Tutorial complete."),
            "The sandbox closes because the lessons ran out, so it must not claim a win."
        );
        Assert.That(
            result.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo(result.GetStatusForTeam(GameLoop.HostTeamIndex)),
            "The result reads the same from either seat because nobody actually won."
        );
    }
}
