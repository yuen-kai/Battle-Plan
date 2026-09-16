using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The red number a crit earns, and the one bit of plumbing that gets the crit to the peer drawing
/// it. Both halves are local and visual, so both are reachable without a live session.
/// </summary>
[TestFixture]
[Category("CritDamageNumber")]
public class CritDamageNumberEditModeTests
{
    private readonly List<GameObject> spawnedObjects = new();
    private GameObject networkManagerHost;

    [SetUp]
    public void SetUp()
    {
        // Health.TakeDamage is IsServer-gated, and the crit announcement it sends reads
        // NetworkManager before it does anything. A never-started instance answers both: the gate
        // can be forced open, and an RPC on a manager that is not listening returns without
        // sending, which is what keeps the announcement out of the way of these assertions.
        networkManagerHost = new GameObject("CritDamageNumberEditModeTestsNetworkManager");
        NetworkManager manager = networkManagerHost.AddComponent<NetworkManager>();
        SetSingleton(manager);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject spawned in spawnedObjects)
        {
            if (spawned != null)
                Object.DestroyImmediate(spawned);
        }
        spawnedObjects.Clear();

        SetSingleton(null);
        Object.DestroyImmediate(networkManagerHost);
    }

    private static void SetSingleton(NetworkManager manager)
    {
        PropertyInfo property = typeof(NetworkManager).GetProperty(
            "Singleton",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null, "Missing NetworkManager.Singleton property.");
        property.SetValue(null, manager);
    }

    private Health CreateServerHealth()
    {
        GameObject unit = new("CritDamageNumberTarget");
        spawnedObjects.Add(unit);

        Health health = unit.AddComponent<Health>();
        PropertyInfo isServer = typeof(NetworkBehaviour).GetProperty(
            "IsServer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(isServer, Is.Not.Null, "Missing NetworkBehaviour.IsServer property.");
        isServer.SetValue(health, true);
        return health;
    }

    private static void Announce(Health health)
    {
        MethodInfo method = typeof(Health).GetMethod(
            "AnnounceCrit",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(method, Is.Not.Null, "Missing Health.AnnounceCrit.");
        method.Invoke(health, null);
    }

    private static bool TakeAnnouncement(Health health)
    {
        MethodInfo method = typeof(Health).GetMethod(
            "TakeCritAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(method, Is.Not.Null, "Missing Health.TakeCritAnnouncement.");
        return (bool)method.Invoke(health, null);
    }

    /// <summary>
    /// The server marks itself instead of waiting on its own RPC loopback, because a host draws
    /// its number synchronously inside the health write. If this ever stops holding, every crit a
    /// host deals reads gold on the host and red only on the remote peer.
    /// </summary>
    [Test]
    public void AnnouncingACritMarksTheServerItself()
    {
        Health health = CreateServerHealth();

        Assert.That(
            TakeAnnouncement(health),
            Is.False,
            "A unit that has taken nothing has no crit pending."
        );

        Announce(health);

        Assert.That(TakeAnnouncement(health), Is.True);
    }

    [Test]
    public void OneAnnouncementColoursExactlyOneNumber()
    {
        Health health = CreateServerHealth();
        Announce(health);

        Assert.That(TakeAnnouncement(health), Is.True);
        Assert.That(
            TakeAnnouncement(health),
            Is.False,
            "The crit belongs to the hit that earned it; the next number is an ordinary one."
        );
    }

    // === The look itself ===

    private static object LookFor(DamageTone tone, bool crit)
    {
        MethodInfo method = typeof(DamagePopupLabel).GetMethod(
            "LookFor",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null, "Missing DamagePopupLabel.LookFor.");
        return method.Invoke(null, new object[] { tone, crit });
    }

    private static T Read<T>(object look, string field)
    {
        FieldInfo info = look.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(info, Is.Not.Null, $"Missing Tone.{field}.");
        return (T)info.GetValue(look);
    }

    [TestCase(DamageTone.Normal)]
    [TestCase(DamageTone.Heavy)]
    [TestCase(DamageTone.Critical)]
    public void ACritIsRedWhereEveryOtherNumberIsGold(DamageTone tone)
    {
        Color gold = Read<Color>(LookFor(tone, false), "FillBottom");
        Color crit = Read<Color>(LookFor(tone, true), "FillBottom");

        Assert.That(
            gold.g,
            Is.GreaterThan(0.75f),
            "The ordinary ramp carries magnitude in a single hue family, and that family is gold."
        );
        Assert.That(
            crit.g,
            Is.LessThan(0.4f),
            "A crit has to leave that family outright, or it reads as one more step of the ramp."
        );
        Assert.That(crit.r, Is.GreaterThan(crit.g).And.GreaterThan(crit.b));
    }

    /// <summary>
    /// Cap height is how the number says how much: the crit hue replaces the ramp's colour, never
    /// its size. A crit for eleven stays small and a crit for ninety stays large.
    /// </summary>
    [TestCase(DamageTone.Normal)]
    [TestCase(DamageTone.Heavy)]
    [TestCase(DamageTone.Critical)]
    public void ACritKeepsTheSizeItsDamageEarned(DamageTone tone)
    {
        Assert.That(
            Read<float>(LookFor(tone, true), "CapCells"),
            Is.EqualTo(Read<float>(LookFor(tone, false), "CapCells")).Within(0.0001f)
        );
        Assert.That(
            Read<float>(LookFor(tone, true), "RiseCells"),
            Is.EqualTo(Read<float>(LookFor(tone, false), "RiseCells")).Within(0.0001f)
        );
    }

    [Test]
    public void EveryCritShakesEvenTheOnesTheGoldRampWouldWhisper()
    {
        Assert.That(
            Read<float>(LookFor(DamageTone.Normal, false), "ShakeDegrees"),
            Is.EqualTo(0f),
            "Only a kill shakes on the ordinary ramp."
        );
        Assert.That(
            Read<float>(LookFor(DamageTone.Normal, true), "ShakeDegrees"),
            Is.GreaterThan(0f),
            "A crit for a small number has no size cue left, so the shake is the one it gets."
        );
    }

    /// <summary>
    /// Red cannot be as bright as the gold it replaces, and this board's own deck is brighter than
    /// most fills. The drive is what buys that luminance back through the tonemapper's shoulder, so
    /// a crit must never be driven softer than the ramp it left.
    /// </summary>
    [Test]
    public void ACritIsDrivenHarderThanTheBrightestGoldNumber()
    {
        Assert.That(
            Read<float>(LookFor(DamageTone.Normal, true), "Drive"),
            Is.GreaterThan(Read<float>(LookFor(DamageTone.Critical, false), "Drive"))
        );
    }
}
