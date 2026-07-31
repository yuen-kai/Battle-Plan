using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
[Category("OfflineTransport")]
public class OfflineTransportEditModeTests
{
    private GameObject host;
    private NetworkManager networkManager;
    private UnityTransport unityTransport;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("OfflineTransportTests");
        networkManager = host.AddComponent<NetworkManager>();
        unityTransport = host.AddComponent<UnityTransport>();
        networkManager.NetworkConfig = new NetworkConfig { NetworkTransport = unityTransport };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(host);
    }

    private OfflineTransport Transport =>
        networkManager.NetworkConfig.NetworkTransport as OfflineTransport;

    /// The whole fix rests on this: a browser cannot listen, but a solo match only needs
    /// StartServer() to succeed so the rest of GameLoop's IsServer logic can run.
    [Test]
    public void StartServer_SucceedsWithoutBindingASocket()
    {
        OfflineTransport.Configure(networkManager);

        Assert.That(Transport.StartServer(), Is.True);
    }

    [Test]
    public void StartClient_FailsBecauseThereIsNothingToConnectTo()
    {
        OfflineTransport.Configure(networkManager);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("OfflineTransport"));

        Assert.That(Transport.StartClient(), Is.False);
    }

    /// A solo session never queues anything for the wire, so polling must stay quiet
    /// rather than synthesising connect events NGO already handles in-process.
    [Test]
    public void PollEvent_ReportsNothing()
    {
        OfflineTransport.Configure(networkManager);

        NetworkEvent polled = Transport.PollEvent(out _, out var payload, out _);

        Assert.That(polled, Is.EqualTo(NetworkEvent.Nothing));
        Assert.That(payload.Count, Is.Zero);
    }

    [Test]
    public void Configure_SelectsTheSocketlessTransport()
    {
        OfflineTransport.Configure(networkManager);

        Assert.That(networkManager.NetworkConfig.NetworkTransport, Is.InstanceOf<OfflineTransport>());
    }

    [Test]
    public void Configure_ReusesTheSameComponentWhenCalledTwice()
    {
        OfflineTransport.Configure(networkManager);
        NetworkTransport first = networkManager.NetworkConfig.NetworkTransport;
        OfflineTransport.Configure(networkManager);

        Assert.That(networkManager.NetworkConfig.NetworkTransport, Is.SameAs(first));
        Assert.That(host.GetComponents<OfflineTransport>().Length, Is.EqualTo(1));
    }

    [Test]
    public void Restore_ReturnsNetworkedPlayToUnityTransport()
    {
        OfflineTransport.Configure(networkManager);
        OfflineTransport.Restore(networkManager);

        Assert.That(networkManager.NetworkConfig.NetworkTransport, Is.SameAs(unityTransport));
    }

    [Test]
    public void Restore_LeavesUnityTransportUntouched()
    {
        OfflineTransport.Restore(networkManager);

        Assert.That(networkManager.NetworkConfig.NetworkTransport, Is.SameAs(unityTransport));
    }

    [Test]
    public void Restore_ToleratesAMissingNetworkManager()
    {
        Assert.DoesNotThrow(() => OfflineTransport.Restore(null));
    }

    /// The tutorial reaches the same host path as "play against AI", which is why both
    /// broke together on web.
    [Test]
    public void TutorialAndAiMatches_BothRequestTheSoloPath()
    {
        Assert.That(TutorialSession.BuildMatchOptions().IsBotMatch, Is.True);
        Assert.That(
            new MatchOptions { opponentType = OpponentType.AI }.Sanitized().IsBotMatch,
            Is.True
        );
        Assert.That(
            new MatchOptions { opponentType = OpponentType.Player }.Sanitized().IsBotMatch,
            Is.False
        );
    }
}
