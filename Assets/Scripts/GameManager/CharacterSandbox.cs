#if UNITY_EDITOR
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Launcher for the character sandbox: a human-playable, one-a-side match between the character
/// being tested and a stationary target dummy, on a real board, spawned against a real wall.
///
/// Everything about the match's shape lives in <see cref="SandboxSession"/> and is read while the
/// board is being built, so nothing here has to unpick a normal match afterwards — no surplus units
/// to eliminate, no invisible bootstrap round, no dev mode to switch off mid-flight. This file only
/// starts and stops the session; the Editor window under <c>Battle Plan > Character Sandbox</c> is
/// the screen that chooses what to test (<c>Editor/CharacterSandboxWindow.cs</c> — it lives in the
/// Editor assembly, which is why the menu item is over there rather than here).
///
/// The chosen indices are kept in <see cref="UnityEditor.SessionState"/> rather than in statics
/// because entering Play mode reloads the domain and wipes those, and the launch has to survive
/// that hop.
/// </summary>
public static class CharacterSandbox
{
    private const string AutoRunKey = "BattlePlan.CharacterSandbox.AutoRun";
    private const string TestUnitKey = "BattlePlan.CharacterSandbox.TestUnit";
    private const string DummyUnitKey = "BattlePlan.CharacterSandbox.DummyUnit";
    private const string DummyFightsBackKey = "BattlePlan.CharacterSandbox.DummyFightsBack";

    /// <summary>The bot crew's default: a plain mid-range rifle Soldier, no surprises.</summary>
    public const int DefaultDummyUnitIndex = 4;

    /// <summary>
    /// Starts (or restarts) the sandbox with the given crews. Safe to call while a match — including
    /// a previous sandbox run — is already going; that match is torn down first.
    /// </summary>
    public static void Launch(int testUnitIndex, int dummyUnitIndex, bool dummyFightsBack)
    {
        UnityEditor.SessionState.SetInt(TestUnitKey, testUnitIndex);
        UnityEditor.SessionState.SetInt(DummyUnitKey, dummyUnitIndex);
        UnityEditor.SessionState.SetBool(DummyFightsBackKey, dummyFightsBack);

        if (Application.isPlaying)
        {
            BeginSessionAndLoadJoinScene();
            return;
        }

        UnityEditor.SessionState.SetBool(AutoRunKey, true);
        UnityEditor.EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRunAfterEnterPlay()
    {
        // Only the main editor runs the sandbox; there is no MPPM clone involved (it is a solo bot
        // match, exactly like the tutorial's).
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        // AfterSceneLoad fires on every scene load, so the request is consumed once.
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;
        UnityEditor.SessionState.EraseBool(AutoRunKey);
        BeginSessionAndLoadJoinScene();
    }

    private static void BeginSessionAndLoadJoinScene()
    {
        if (SandboxLaunchRunner.Instance != null)
            Object.Destroy(SandboxLaunchRunner.Instance.gameObject);

        GameObject runner = new("SandboxLaunchRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<SandboxLaunchRunner>();
    }

    /// <summary>Whether a sandbox match is live right now, for the window's own status line.</summary>
    public static bool IsRunning => Application.isPlaying && SandboxSession.IsActive;

    internal static void BeginSessionFromStoredSelection()
    {
        SandboxSession.Begin(
            UnityEditor.SessionState.GetInt(TestUnitKey, 0),
            UnityEditor.SessionState.GetInt(DummyUnitKey, DefaultDummyUnitIndex)
        );
        SandboxSession.DummyFightsBack = UnityEditor.SessionState.GetBool(
            DummyFightsBackKey,
            false
        );
    }
}

/// <summary>
/// Carries out one sandbox launch. This needs a coroutine rather than a plain method because
/// restarting has to shut the previous match down FIRST and <see cref="NetworkManager.Shutdown"/> is
/// asynchronous — it takes a frame or more to stop listening. Loading the join scene before that
/// finishes makes its <c>CreateMatch</c> throw "A network session is already running", leaving the
/// sandbox stranded on the join screen with no board.
/// </summary>
internal sealed class SandboxLaunchRunner : MonoBehaviour
{
    private const float ShutdownTimeoutSeconds = 10f;

    public static SandboxLaunchRunner Instance { get; private set; }

    private void Awake() => Instance = this;

    private System.Collections.IEnumerator Start()
    {
        // Ends any previous session before the new one begins, so its crew size and dummy freeze
        // cannot bleed into this launch.
        SandboxSession.End();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (networkManager.IsServer)
                NetworkHelper.CleanupAllNetworkObjects();
            networkManager.Shutdown();
            MatchOptions.Reset();
            GameLoop.ResetMatchState();

            float deadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
            while (
                NetworkManager.Singleton != null
                && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
            )
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogWarning(
                        "[Sandbox] Previous match did not shut down in time; launching anyway."
                    );
                    break;
                }
                yield return null;
            }
        }

        CharacterSandbox.BeginSessionFromStoredSelection();

        // The session needs the JoinGame scene's NetworkManager, so it routes through there and
        // creates its own loopback host on arrival instead of showing the lobby — see
        // JoinGameUIController.TryStartSandboxMatch.
        MatchOptions.SetCurrent(SandboxSession.BuildMatchOptions());
        SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);

        Instance = null;
        Destroy(gameObject);
    }
}
#endif
