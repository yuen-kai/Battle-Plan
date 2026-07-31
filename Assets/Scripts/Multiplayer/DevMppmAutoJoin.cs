#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum DevMppmSessionMode
{
    Local,
    Relay,
}

[Serializable]
public sealed class DevMppmSessionDirective
{
    public int version;
    public string mode;
    public string nonce;
    public string relayJoinCode;
    public long createdUtcTicks;
}

/// <summary>
/// Explicit, project-scoped coordination for two-player MPPM development sessions. Player 2 does
/// nothing until the main editor publishes a directive for the current Play session.
/// </summary>
public static class DevMppmAutoJoin
{
    public const string LoopbackAddress = "127.0.0.1";
    public const ushort LoopbackPort = 7777;

    private const int DirectiveVersion = 1;
    private const string DirectiveFileName = "BattlePlanMppmSession.json";
    private const double MaximumDirectiveAgeMinutes = 15d;
    private const double SessionStartToleranceSeconds = 5d;
    private static bool runnerCreated;
    private static long playSessionStartedUtcTicks;

    public static bool IsMainEditorPlayer => Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
    public static long PlaySessionStartedUtcTicks => playSessionStartedUtcTicks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        runnerCreated = false;
        playSessionStartedUtcTicks = DateTime.UtcNow.Ticks;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (IsMainEditorPlayer)
        {
            ClearSession();
            return;
        }

        if (runnerCreated || !IsPlayerTwo(Environment.GetCommandLineArgs()))
            return;

        runnerCreated = true;
        DevMppmAutoJoinRunner runner = new GameObject(
            nameof(DevMppmAutoJoinRunner)
        ).AddComponent<DevMppmAutoJoinRunner>();
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
    }

    public static void EnableLocalAutoJoin()
    {
        RequireMainEditor();
        Publish(DevMppmSessionMode.Local, null);
    }

    /// <summary>
    /// Publishes a one-shot Relay smoke directive. Normal Relay hosts are unaffected unless this
    /// API is called explicitly after their host has started.
    /// </summary>
    public static void PublishRelaySmoke(string joinCode)
    {
        RequireMainEditor();
        Publish(DevMppmSessionMode.Relay, RelayManager.NormalizeJoinCodeOrThrow(joinCode));
    }

    public static bool PublishPreparedRelaySmoke()
    {
        RelayManager relayManager = RelayManager.Instance;
        if (relayManager == null || string.IsNullOrEmpty(relayManager.PreparedJoinCode))
            return false;

        PublishRelaySmoke(relayManager.PreparedJoinCode);
        return true;
    }

    public static void DisableAutoJoin()
    {
        ClearSession();
    }

    public static void ConfigureLoopbackTransport(NetworkManager networkManager)
    {
        if (networkManager == null)
            throw new ArgumentNullException(nameof(networkManager));
        if (
            networkManager.ShutdownInProgress
            || networkManager.IsListening
            || networkManager.IsClient
            || networkManager.IsServer
        )
            throw new InvalidOperationException(
                "Direct transport cannot be changed while NGO is running."
            );

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
            throw new InvalidOperationException("UnityTransport is not available.");

        // A previous solo match may have swapped the socketless transport in.
        OfflineTransport.Restore(networkManager);
        transport.SetConnectionData(LoopbackAddress, LoopbackPort, LoopbackAddress);
        transport.UseWebSockets = false;
    }

    public static bool IsPlayerTwo(string[] arguments)
    {
        string launchName = RelayManager.GetLaunchName(arguments);
        string compactName = launchName.Replace(" ", string.Empty);
        return string.Equals(compactName, "Player2", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDirectivePath(string[] arguments, string dataPath)
    {
        string projectPath = GetCommandLineValue(arguments, "-projectPath");
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            if (string.IsNullOrWhiteSpace(dataPath))
                throw new ArgumentException("A project data path is required.", nameof(dataPath));
            projectPath = Directory.GetParent(dataPath)?.FullName;
        }

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new InvalidOperationException("Could not resolve the Unity project path.");

        string libraryRedirect = GetCommandLineValue(arguments, "-library-redirect");
        string libraryPath = string.IsNullOrWhiteSpace(libraryRedirect)
            ? Path.Combine(projectPath, "Library")
            : Path.GetFullPath(Path.Combine(projectPath, libraryRedirect));
        string sharedMppmLibrary =
            FindMppmSharedLibrary(projectPath) ?? FindMppmSharedLibrary(libraryPath);
        return Path.Combine(sharedMppmLibrary ?? libraryPath, DirectiveFileName);
    }

    public static bool TryReadDirective(out DevMppmSessionDirective directive)
    {
        return TryReadDirective(DirectivePath, out directive);
    }

    public static bool TryReadDirective(string path, out DevMppmSessionDirective directive)
    {
        directive = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using StreamReader reader = new(stream);
            string json = reader.ReadToEnd();
            DevMppmSessionDirective parsed = JsonUtility.FromJson<DevMppmSessionDirective>(json);
            if (!IsValidDirective(parsed))
                return false;

            directive = parsed;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool ShouldAcceptDirective(
        DevMppmSessionDirective directive,
        string lastConsumedNonce,
        long sessionStartUtcTicks
    )
    {
        if (
            !IsValidDirective(directive)
            || string.Equals(directive.nonce, lastConsumedNonce, StringComparison.Ordinal)
        )
        {
            return false;
        }

        long earliestAcceptedTicks = Math.Max(
            DateTime.MinValue.Ticks,
            sessionStartUtcTicks - TimeSpan.FromSeconds(SessionStartToleranceSeconds).Ticks
        );
        return directive.createdUtcTicks >= earliestAcceptedTicks;
    }

    public static bool IsValidDirective(DevMppmSessionDirective directive)
    {
        if (
            directive == null
            || directive.version != DirectiveVersion
            || string.IsNullOrWhiteSpace(directive.nonce)
            || directive.createdUtcTicks <= 0
        )
        {
            return false;
        }

        DateTime createdUtc;
        try
        {
            createdUtc = new DateTime(directive.createdUtcTicks, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        TimeSpan age = DateTime.UtcNow - createdUtc;
        if (
            age < TimeSpan.FromSeconds(-5)
            || age > TimeSpan.FromMinutes(MaximumDirectiveAgeMinutes)
        )
            return false;

        if (string.Equals(directive.mode, "local", StringComparison.Ordinal))
            return string.IsNullOrEmpty(directive.relayJoinCode);
        if (!string.Equals(directive.mode, "relay", StringComparison.Ordinal))
            return false;

        return RelayManager.TryNormalizeJoinCode(directive.relayJoinCode, out string normalized)
            && string.Equals(directive.relayJoinCode, normalized, StringComparison.Ordinal);
    }

    public static void ClearSession()
    {
        string path = DirectivePath;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException exception)
        {
            Debug.LogWarning($"[DevMppmAutoJoin] Could not clear directive: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.LogWarning($"[DevMppmAutoJoin] Could not clear directive: {exception.Message}");
        }
    }

    public static bool IsDirectiveCurrent(string nonce)
    {
        return TryReadDirective(out DevMppmSessionDirective directive)
            && string.Equals(directive.nonce, nonce, StringComparison.Ordinal);
    }

    public static void ClearDirectiveIfCurrent(string nonce)
    {
        ClearDirectiveIfCurrent(DirectivePath, nonce);
    }

    public static void ClearDirectiveIfCurrent(string path, string nonce)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(nonce))
            return;

        string claimedPath = $"{path}.{Guid.NewGuid():N}.consumed";
        bool claimExists = false;
        try
        {
            if (!File.Exists(path))
                return;

            File.Move(path, claimedPath);
            claimExists = true;
            bool nonceMatches =
                TryReadDirective(claimedPath, out DevMppmSessionDirective claimedDirective)
                && string.Equals(claimedDirective.nonce, nonce, StringComparison.Ordinal);
            if (!nonceMatches && !File.Exists(path))
            {
                File.Move(claimedPath, path);
                claimExists = false;
            }
        }
        catch (IOException)
        {
            // Another process replaced or consumed the directive. The next poll converges.
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.LogWarning($"[DevMppmAutoJoin] Could not consume directive: {exception.Message}");
        }
        finally
        {
            if (claimExists)
            {
                try
                {
                    if (File.Exists(claimedPath))
                        File.Delete(claimedPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string DirectivePath =>
        ResolveDirectivePath(Environment.GetCommandLineArgs(), Application.dataPath);

    private static void Publish(DevMppmSessionMode mode, string relayJoinCode)
    {
        DevMppmSessionDirective directive = new()
        {
            version = DirectiveVersion,
            mode = mode == DevMppmSessionMode.Local ? "local" : "relay",
            nonce = Guid.NewGuid().ToString("N"),
            relayJoinCode = relayJoinCode ?? string.Empty,
            createdUtcTicks = DateTime.UtcNow.Ticks,
        };

        WriteDirectiveAtomically(DirectivePath, directive);
        Debug.Log($"[DevMppmAutoJoin] Published {directive.mode} directive {directive.nonce}.");
    }

    public static void WriteDirectiveAtomically(string path, DevMppmSessionDirective directive)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A directive path is required.", nameof(path));
        if (!IsValidDirective(directive))
            throw new ArgumentException("The MPPM directive is invalid.", nameof(directive));

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(directive));
            ReplaceDirectiveFile(temporaryPath, path);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ReplaceDirectiveFile(string temporaryPath, string path)
    {
        IOException lastException = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
                return;
            }
            catch (IOException exception)
            {
                lastException = exception;
                if (!File.Exists(temporaryPath))
                    throw;
            }
        }

        throw new IOException("Could not atomically replace the MPPM directive.", lastException);
    }

    private static void RequireMainEditor()
    {
        if (!IsMainEditorPlayer)
            throw new InvalidOperationException(
                "Only the main MPPM editor can publish a development session."
            );
    }

    private static string GetCommandLineValue(string[] arguments, string key)
    {
        if (arguments == null)
            return null;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (
                string.Equals(argument, key, StringComparison.OrdinalIgnoreCase)
                && index + 1 < arguments.Length
            )
            {
                return arguments[index + 1];
            }

            string prefix = key + "=";
            if (argument != null && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument.Substring(prefix.Length);
            }
        }

        return null;
    }

    private static string FindMppmSharedLibrary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        DirectoryInfo child = null;
        DirectoryInfo current = new(Path.GetFullPath(path));
        while (current != null)
        {
            if (
                string.Equals(current.Name, "Library", StringComparison.OrdinalIgnoreCase)
                && child != null
                && string.Equals(child.Name, "VP", StringComparison.OrdinalIgnoreCase)
            )
            {
                return current.FullName;
            }

            child = current;
            current = current.Parent;
        }

        return null;
    }
}

public sealed class DevMppmAutoJoinRunner : MonoBehaviour
{
    private const float DirectivePollSeconds = 0.2f;
    private const float DirectAttemptSeconds = 12f;
    private const float ShutdownTimeoutSeconds = 3f;

    private IEnumerator Start()
    {
        string lastConsumedNonce = null;
        while (true)
        {
            DevMppmSessionDirective directive;
            while (
                !DevMppmAutoJoin.TryReadDirective(out directive)
                || !DevMppmAutoJoin.ShouldAcceptDirective(
                    directive,
                    lastConsumedNonce,
                    DevMppmAutoJoin.PlaySessionStartedUtcTicks
                )
            )
            {
                yield return new WaitForSecondsRealtime(DirectivePollSeconds);
            }

            Debug.Log(
                $"[DevMppmAutoJoin] Player 2 accepted {directive.mode} directive {directive.nonce}."
            );
            yield return EnsureJoinSceneLoaded(directive.nonce);
            if (!DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce))
                continue;

            if (string.Equals(directive.mode, "local", StringComparison.Ordinal))
                yield return RunLocalSession(directive);
            else
                yield return RunRelaySession(directive);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsServer)
            {
                Debug.LogError("[DevMppmAutoJoin] Player 2 unexpectedly became a server.");
                yield return StopClientAndRestoreDirect(networkManager);
                continue;
            }
            if (
                networkManager != null
                && networkManager.IsConnectedClient
                && DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce)
            )
            {
                lastConsumedNonce = directive.nonce;
                DevMppmAutoJoin.ClearDirectiveIfCurrent(directive.nonce);
                Debug.Log("[DevMppmAutoJoin] RESULT: Player 2 connected.");
                while (networkManager != null && networkManager.IsConnectedClient)
                    yield return null;
                if (networkManager != null)
                    yield return StopClientAndRestoreDirect(networkManager);
                continue;
            }

            if (networkManager != null)
                yield return StopClientAndRestoreDirect(networkManager);
        }
    }

    private static IEnumerator EnsureJoinSceneLoaded(string nonce)
    {
        if (
            !string.Equals(SceneManager.GetActiveScene().name, "JoinGame", StringComparison.Ordinal)
        )
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("JoinGame", LoadSceneMode.Single);
            while (load != null && !load.isDone && DevMppmAutoJoin.IsDirectiveCurrent(nonce))
            {
                yield return null;
            }
        }

        while (NetworkManager.Singleton == null && DevMppmAutoJoin.IsDirectiveCurrent(nonce))
        {
            yield return null;
        }
    }

    private static IEnumerator RunLocalSession(DevMppmSessionDirective directive)
    {
        while (DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce))
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                yield return null;
                continue;
            }
            if (networkManager.IsServer)
            {
                Debug.LogError("[DevMppmAutoJoin] Player 2 unexpectedly became a server.");
                yield break;
            }
            if (networkManager.IsConnectedClient)
                yield break;

            if (
                networkManager.ShutdownInProgress
                || networkManager.IsClient
                || networkManager.IsListening
            )
            {
                yield return StopClientAndRestoreDirect(networkManager);
                continue;
            }

            DevMppmAutoJoin.ConfigureLoopbackTransport(networkManager);
            ReconnectSession.RememberDirectClient();
            ReconnectSession.ApplyConnectionPayload(networkManager);
            Debug.Log("[DevMppmAutoJoin] Player 2 starting direct client.");
            if (!networkManager.StartClient())
            {
                yield return new WaitForSecondsRealtime(DirectivePollSeconds);
                continue;
            }

            float deadline = Time.realtimeSinceStartup + DirectAttemptSeconds;
            while (
                Time.realtimeSinceStartup < deadline
                && DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce)
                && networkManager != null
                && networkManager.IsClient
                && !networkManager.IsConnectedClient
            )
            {
                yield return null;
            }

            if (
                networkManager != null
                && !networkManager.IsServer
                && networkManager.IsConnectedClient
            )
                yield break;
            if (networkManager != null)
                yield return StopClientAndRestoreDirect(networkManager);
        }

        NetworkManager cancelledManager = NetworkManager.Singleton;
        if (cancelledManager != null)
            yield return StopClientAndRestoreDirect(cancelledManager);
        Debug.Log("[DevMppmAutoJoin] Direct session cancelled.");
    }

    private static IEnumerator RunRelaySession(DevMppmSessionDirective directive)
    {
        while (RelayManager.Instance == null && DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce))
        {
            yield return null;
        }

        RelayManager relayManager = RelayManager.Instance;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (relayManager == null || networkManager == null)
            yield break;
        if (networkManager.IsServer)
        {
            Debug.LogError("[DevMppmAutoJoin] Player 2 unexpectedly became a server.");
            yield return StopClientAndRestoreDirect(networkManager);
            yield break;
        }
        if (
            networkManager.ShutdownInProgress
            || networkManager.IsClient
            || networkManager.IsListening
        )
        {
            yield return StopClientAndRestoreDirect(networkManager);
        }
        if (
            networkManager == null
            || networkManager.ShutdownInProgress
            || networkManager.IsClient
            || networkManager.IsServer
            || networkManager.IsListening
        )
        {
            yield break;
        }

        using CancellationTokenSource cancellation = new();
        Task preparation = relayManager.PrepareClientAsync(
            directive.relayJoinCode,
            cancellation.Token
        );
        bool cancellationRequested = false;
        while (!preparation.IsCompleted)
        {
            if (!cancellationRequested && !DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce))
            {
                cancellationRequested = true;
                cancellation.Cancel();
                relayManager.CancelPendingPreparation();
            }
            yield return null;
        }

        if (preparation.IsCanceled || !DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce))
        {
            yield return StopClientAndRestoreDirect(networkManager);
            yield break;
        }
        if (preparation.IsFaulted)
        {
            Exception exception = preparation.Exception?.GetBaseException();
            Debug.LogError($"[DevMppmAutoJoin] Relay preparation failed: {exception?.Message}");
            DevMppmAutoJoin.ClearDirectiveIfCurrent(directive.nonce);
            yield return StopClientAndRestoreDirect(networkManager);
            yield break;
        }

        ReconnectSession.RememberRelayClient(directive.relayJoinCode);
        ReconnectSession.ApplyConnectionPayload(networkManager);
        Debug.Log("[DevMppmAutoJoin] Player 2 starting Relay client.");
        if (!networkManager.StartClient())
        {
            Debug.LogError("[DevMppmAutoJoin] Relay client could not start.");
            DevMppmAutoJoin.ClearDirectiveIfCurrent(directive.nonce);
            yield return StopClientAndRestoreDirect(networkManager);
            yield break;
        }

        while (
            networkManager != null
            && networkManager.IsClient
            && !networkManager.IsConnectedClient
            && DevMppmAutoJoin.IsDirectiveCurrent(directive.nonce)
        )
        {
            yield return null;
        }

        if (networkManager != null && !networkManager.IsServer && networkManager.IsConnectedClient)
            yield break;

        Debug.LogError("[DevMppmAutoJoin] Relay client disconnected before connecting.");
        DevMppmAutoJoin.ClearDirectiveIfCurrent(directive.nonce);
        if (networkManager != null)
            yield return StopClientAndRestoreDirect(networkManager);
    }

    private static IEnumerator StopClientAndRestoreDirect(NetworkManager networkManager)
    {
        if (networkManager == null)
            yield break;

        if (
            !networkManager.ShutdownInProgress
            && (networkManager.IsClient || networkManager.IsServer || networkManager.IsListening)
        )
            networkManager.Shutdown(true);

        float deadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
        while (
            networkManager != null
            && (
                networkManager.ShutdownInProgress
                || networkManager.IsClient
                || networkManager.IsServer
                || networkManager.IsListening
            )
            && Time.realtimeSinceStartup < deadline
        )
        {
            yield return null;
        }

        if (
            networkManager != null
            && !networkManager.ShutdownInProgress
            && !networkManager.IsClient
            && !networkManager.IsServer
            && !networkManager.IsListening
        )
        {
            DevMppmAutoJoin.ConfigureLoopbackTransport(networkManager);
        }
    }
}
#endif
