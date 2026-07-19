using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

[TestFixture]
[Category("RelaySession")]
public class RelaySessionEditModeTests
{
    [Test]
    public void AuthenticationProfile_UsesLaunchNameAndMeetsSdkContract()
    {
        string playerOne = RelayManager.BuildAuthenticationProfile(
            new[] { "Unity", "-name", "Player 1" }
        );
        string playerTwo = RelayManager.BuildAuthenticationProfile(
            new[] { "Unity", "-name", "Player 2" }
        );
        string longName = RelayManager.BuildAuthenticationProfile(
            new[] { "Unity", "-name", "A very long virtual player name with punctuation!" }
        );

        Assert.That(playerOne, Is.Not.EqualTo(playerTwo));
        Assert.That(playerTwo, Does.StartWith("bp-player-2-"));
        Assert.That(longName.Length, Is.LessThanOrEqualTo(30));
        Assert.That(longName, Does.Match("^[a-zA-Z0-9_-]+$"));
    }

    [Test]
    public void EditorRelay_UsesFirewallTolerantSecureWebSockets()
    {
        PropertyInfo connectionType = typeof(RelayManager).GetProperty(
            "RelayConnectionType",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        PropertyInfo usesWebSockets = typeof(RelayManager).GetProperty(
            "RelayUsesWebSockets",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.That(connectionType?.GetValue(null), Is.EqualTo("wss"));
        Assert.That(usesWebSockets?.GetValue(null), Is.True);
    }

    [Test]
    public void LaunchName_ParsesUnityCommandLine()
    {
        Assert.That(
            RelayManager.GetLaunchName(new[] { "Unity", "-name", "Player 2" }),
            Is.EqualTo("Player 2")
        );
        Assert.That(
            RelayManager.GetLaunchName(new[] { "Unity", "-name=SmokeClient" }),
            Is.EqualTo("SmokeClient")
        );
        Assert.That(RelayManager.GetLaunchName(new[] { "Unity" }), Is.EqualTo("MainEditor"));
    }

    [TestCase(" 6b c d f g ", "6BCDFG")]
    [TestCase("6789bcdfghjk", "6789BCDFGHJK")]
    public void JoinCode_NormalizesSupportedRelayCodes(string value, string expected)
    {
        Assert.That(RelayManager.TryNormalizeJoinCode(value, out string normalized), Is.True);
        Assert.That(normalized, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("6789B")]
    [TestCase("6789BCDFGHJKLM")]
    [TestCase("6789BA")]
    [TestCase("6789B-")]
    public void JoinCode_RejectsMalformedValues(string value)
    {
        Assert.That(RelayManager.TryNormalizeJoinCode(value, out string normalized), Is.False);
        Assert.That(normalized, Is.Null);
    }

    [Test]
    public void DirectivePath_FollowsMppmLibraryRedirect()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BattlePlanPathTest");
        string cloneRoot = Path.Combine(projectRoot, "Library", "VP", "mppm-player2");
        string path = DevMppmAutoJoin.ResolveDirectivePath(
            new[] { "Unity", "-projectPath", cloneRoot, "-library-redirect", "../.." },
            Path.Combine(cloneRoot, "Assets")
        );

        Assert.That(
            path,
            Is.EqualTo(Path.Combine(projectRoot, "Library", "BattlePlanMppmSession.json"))
        );
    }

    [Test]
    public void DirectivePath_UsesProjectLibraryForVirtualPlayerFolder()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BattlePlanPathTest");
        string path = DevMppmAutoJoin.ResolveDirectivePath(
            new[]
            {
                "Unity",
                "-projectPath",
                projectRoot,
                "-library-redirect",
                Path.Combine("Library", "VP", "mppm-player2"),
            },
            Path.Combine(projectRoot, "Assets")
        );

        Assert.That(
            path,
            Is.EqualTo(Path.Combine(projectRoot, "Library", "BattlePlanMppmSession.json"))
        );
    }

    [Test]
    public void DirectiveValidation_AcceptsCurrentLocalAndRelaySessionsOnly()
    {
        DevMppmSessionDirective local = NewDirective("local", string.Empty);
        DevMppmSessionDirective relay = NewDirective("relay", "6789BC");
        DevMppmSessionDirective stale = NewDirective("local", string.Empty);
        stale.createdUtcTicks = DateTime.UtcNow.AddMinutes(-20).Ticks;
        DevMppmSessionDirective malformedRelay = NewDirective("relay", "ABCDEF");

        Assert.That(DevMppmAutoJoin.IsValidDirective(local), Is.True);
        Assert.That(DevMppmAutoJoin.IsValidDirective(relay), Is.True);
        Assert.That(DevMppmAutoJoin.IsValidDirective(stale), Is.False);
        Assert.That(DevMppmAutoJoin.IsValidDirective(malformedRelay), Is.False);
    }

    [Test]
    public void DirectiveSessionGate_RejectsStaleAndConsumedNonces()
    {
        long sessionStart = DateTime.UtcNow.Ticks;
        DevMppmSessionDirective directive = NewDirective("local", string.Empty);
        directive.createdUtcTicks = sessionStart - TimeSpan.FromSeconds(6).Ticks;

        Assert.That(DevMppmAutoJoin.ShouldAcceptDirective(directive, null, sessionStart), Is.False);

        directive.createdUtcTicks = sessionStart - TimeSpan.FromSeconds(4).Ticks;
        Assert.That(DevMppmAutoJoin.ShouldAcceptDirective(directive, null, sessionStart), Is.True);
        Assert.That(
            DevMppmAutoJoin.ShouldAcceptDirective(directive, directive.nonce, sessionStart),
            Is.False
        );
    }

    [Test]
    public void DirectivePublish_AtomicallyReplacesExistingFile()
    {
        string directory = NewTemporaryDirectory();
        string path = Path.Combine(directory, "directive.json");
        try
        {
            DevMppmSessionDirective first = NewDirective("local", string.Empty);
            DevMppmSessionDirective second = NewDirective("relay", "6789BC");

            DevMppmAutoJoin.WriteDirectiveAtomically(path, first);
            DevMppmAutoJoin.WriteDirectiveAtomically(path, second);

            Assert.That(
                DevMppmAutoJoin.TryReadDirective(path, out DevMppmSessionDirective read),
                Is.True
            );
            Assert.That(read.nonce, Is.EqualTo(second.nonce));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ConditionalClear_DoesNotDeleteReplacementDirective()
    {
        string directory = NewTemporaryDirectory();
        string path = Path.Combine(directory, "directive.json");
        try
        {
            DevMppmSessionDirective replacement = NewDirective("local", string.Empty);
            DevMppmAutoJoin.WriteDirectiveAtomically(path, replacement);

            DevMppmAutoJoin.ClearDirectiveIfCurrent(path, Guid.NewGuid().ToString("N"));

            Assert.That(
                DevMppmAutoJoin.TryReadDirective(path, out DevMppmSessionDirective read),
                Is.True
            );
            Assert.That(read.nonce, Is.EqualTo(replacement.nonce));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void DirectiveRead_IsShareTolerantAndRejectsGarbage()
    {
        string directory = NewTemporaryDirectory();
        string path = Path.Combine(directory, "directive.json");
        try
        {
            DevMppmSessionDirective directive = NewDirective("local", string.Empty);
            DevMppmAutoJoin.WriteDirectiveAtomically(path, directive);
            using (
                FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                )
            )
            {
                Assert.That(DevMppmAutoJoin.TryReadDirective(path, out _), Is.True);
            }

            File.WriteAllText(path, "{not-json");
            Assert.That(DevMppmAutoJoin.TryReadDirective(path, out _), Is.False);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"BattlePlanRelayTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static DevMppmSessionDirective NewDirective(string mode, string relayJoinCode)
    {
        return new DevMppmSessionDirective
        {
            version = 1,
            mode = mode,
            nonce = Guid.NewGuid().ToString("N"),
            relayJoinCode = relayJoinCode,
            createdUtcTicks = DateTime.UtcNow.Ticks,
        };
    }
}
