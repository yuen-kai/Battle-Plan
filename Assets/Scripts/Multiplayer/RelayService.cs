using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public sealed class RelayManager : MonoBehaviour
{
    private const int MinimumJoinCodeLength = 6;
    private const int MaximumJoinCodeLength = 12;
    private const string JoinCodeAlphabet = "6789BCDFGHJKLMNPQRTW";

    private static readonly object servicesInitializationLock = new();
    private static Task servicesInitializationTask;
    private static string servicesAuthenticationProfile;

    private int transportPreparationVersion;

    public static RelayManager Instance { get; private set; }
    public string PreparedJoinCode { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Allocates a Relay host and prepares Unity Transport. The caller remains responsible for
    /// starting NGO after confirming that its UI operation is still current.
    /// </summary>
    public async Task<string> PrepareHostAsync(
        int maxConnections,
        CancellationToken cancellationToken = default
    )
    {
        if (maxConnections < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxConnections),
                "Relay requires at least one remote connection."
            );

        int preparationVersion = BeginTransportPreparation();
        UnityTransport transport = RequireAvailableTransport();
        await EnsureServicesReadyAsync(cancellationToken);
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);
        joinCode = NormalizeJoinCodeOrThrow(joinCode);

        CommitRelayTransport(
            preparationVersion,
            transport,
            AllocationUtils.ToRelayServerData(allocation, RelayConnectionType),
            cancellationToken
        );
        PreparedJoinCode = joinCode;
        return joinCode;
    }

    /// <summary>
    /// Joins a Relay allocation and prepares Unity Transport. The caller remains responsible for
    /// starting NGO after confirming that its UI operation is still current.
    /// </summary>
    public async Task PrepareClientAsync(
        string joinCode,
        CancellationToken cancellationToken = default
    )
    {
        joinCode = NormalizeJoinCodeOrThrow(joinCode);
        int preparationVersion = BeginTransportPreparation();
        UnityTransport transport = RequireAvailableTransport();
        await EnsureServicesReadyAsync(cancellationToken);
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);

        CommitRelayTransport(
            preparationVersion,
            transport,
            AllocationUtils.ToRelayServerData(allocation, RelayConnectionType),
            cancellationToken
        );
    }

    /// <summary>
    /// Invalidates in-flight Relay work immediately. SDK requests already on the wire may still
    /// finish, but they can no longer reconfigure the transport.
    /// </summary>
    public void CancelPendingPreparation()
    {
        Interlocked.Increment(ref transportPreparationVersion);
        PreparedJoinCode = null;
    }

    public static bool TryNormalizeJoinCode(string value, out string normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        StringBuilder builder = new(MaximumJoinCodeLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
                continue;

            char upper = char.ToUpperInvariant(character);
            if (JoinCodeAlphabet.IndexOf(upper) < 0)
                return false;
            if (builder.Length == MaximumJoinCodeLength)
                return false;

            builder.Append(upper);
        }

        if (builder.Length < MinimumJoinCodeLength || builder.Length > MaximumJoinCodeLength)
        {
            return false;
        }

        normalized = builder.ToString();
        return true;
    }

    public static string NormalizeJoinCodeOrThrow(string value)
    {
        if (TryNormalizeJoinCode(value, out string normalized))
            return normalized;

        throw new ArgumentException(
            "Relay codes contain 6 to 12 characters from 6789BCDFGHJKLMNPQRTW.",
            nameof(value)
        );
    }

    public static string GetLaunchName(string[] arguments)
    {
        if (arguments != null)
        {
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (
                    string.Equals(argument, "-name", StringComparison.OrdinalIgnoreCase)
                    && index + 1 < arguments.Length
                    && !string.IsNullOrWhiteSpace(arguments[index + 1])
                )
                {
                    return arguments[index + 1].Trim();
                }

                const string namePrefix = "-name=";
                if (
                    argument != null
                    && argument.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
                    && argument.Length > namePrefix.Length
                )
                {
                    return argument.Substring(namePrefix.Length).Trim();
                }
            }
        }

        return "MainEditor";
    }

    public static string BuildAuthenticationProfile(string[] arguments)
    {
        string launchName = GetLaunchName(arguments);
        StringBuilder slug = new(17);
        bool previousWasSeparator = false;

        foreach (char character in launchName)
        {
            bool isAsciiLetter =
                (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
            bool isAsciiDigit = character >= '0' && character <= '9';
            if (isAsciiLetter || isAsciiDigit)
            {
                if (slug.Length == 17)
                    break;
                slug.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator && slug.Length > 0 && slug.Length < 17)
            {
                slug.Append('-');
                previousWasSeparator = true;
            }
        }

        while (slug.Length > 0 && slug[slug.Length - 1] == '-')
            slug.Length--;
        if (slug.Length == 0)
            slug.Append("player");

        return $"bp-{slug}-{StableHash(launchName):x8}";
    }

    private static string RelayConnectionType
    {
        get
        {
#if UNITY_WEBGL || UNITY_EDITOR
            return "wss";
#else
            return "dtls";
#endif
        }
    }

    private static bool RelayUsesWebSockets
    {
        get
        {
#if UNITY_WEBGL || UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    private int BeginTransportPreparation()
    {
        PreparedJoinCode = null;
        return Interlocked.Increment(ref transportPreparationVersion);
    }

    private void EnsurePreparationIsCurrent(
        int preparationVersion,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this == null || preparationVersion != Volatile.Read(ref transportPreparationVersion))
        {
            throw new OperationCanceledException("Relay preparation was superseded.");
        }
    }

    private static UnityTransport RequireAvailableTransport()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            throw new InvalidOperationException("NetworkManager is not available.");
        if (
            networkManager.ShutdownInProgress
            || networkManager.IsListening
            || networkManager.IsClient
            || networkManager.IsServer
        )
            throw new InvalidOperationException(
                "Relay transport cannot be changed while NGO is running."
            );

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
            throw new InvalidOperationException("UnityTransport is not available.");

        // A previous solo match may have swapped the socketless transport in.
        OfflineTransport.Restore(networkManager);
        return transport;
    }

    private void CommitRelayTransport(
        int preparationVersion,
        UnityTransport expectedTransport,
        Unity.Networking.Transport.Relay.RelayServerData relayServerData,
        CancellationToken cancellationToken
    )
    {
        EnsurePreparationIsCurrent(preparationVersion, cancellationToken);
        UnityTransport currentTransport = RequireAvailableTransport();
        if (currentTransport != expectedTransport)
            throw new OperationCanceledException(
                "The active network transport changed during Relay preparation."
            );

        ConfigureRelayTransport(currentTransport, relayServerData);
    }

    private static void ConfigureRelayTransport(
        UnityTransport transport,
        Unity.Networking.Transport.Relay.RelayServerData relayServerData
    )
    {
        transport.SetRelayServerData(relayServerData);
        transport.UseWebSockets = RelayUsesWebSockets;
    }

    private static async Task EnsureServicesReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string profile = BuildAuthenticationProfile(Environment.GetCommandLineArgs());
        Task initializationTask;

        lock (servicesInitializationLock)
        {
            if (
                servicesInitializationTask == null
                || servicesInitializationTask.IsCanceled
                || servicesInitializationTask.IsFaulted
            )
            {
                servicesAuthenticationProfile = profile;
                servicesInitializationTask = InitializeServicesAndAuthenticationAsync(profile);
            }
            else if (
                !string.Equals(servicesAuthenticationProfile, profile, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Unity Gaming Services was initialized for a different player profile."
                );
            }

            initializationTask = servicesInitializationTask;
        }

        await initializationTask;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task InitializeServicesAndAuthenticationAsync(string profile)
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions options = new InitializationOptions().SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }

        IAuthenticationService authentication = AuthenticationService.Instance;
        if (!string.Equals(authentication.Profile, profile, StringComparison.Ordinal))
        {
            if (authentication.IsSignedIn)
                authentication.SignOut();
            authentication.SwitchProfile(profile);
        }

        if (!authentication.IsSignedIn)
            await authentication.SignInAnonymouslyAsync();
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        uint hash = offset;
        foreach (char character in value ?? string.Empty)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }
}
