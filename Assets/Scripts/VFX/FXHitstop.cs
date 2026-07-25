using System.Collections;
using UnityEngine;

/// <summary>
/// The freeze frame on a lethal hit (ArtDirection §9.3, §9.4).
///
/// Time.timeScale is a global that this project already writes from two other places:
/// GameLoop sets it at the top of every round and back to 1 when the match finishes, and
/// DevInput.SetSpeed drives the automated-test fast-forward through it (risk item 2). So this
/// helper does three things and it is not optional that it does all three:
///
///   1. It refuses to run at all while GameLoop.devMode is on, because a hitstop inside a
///      six-times-speed E2E run desynchronises the suite.
///   2. It captures whatever timeScale was in force and restores that value, not 1.
///   3. It only restores if nothing else has moved timeScale in the meantime, so a round
///      boundary or a match end that lands mid-freeze wins instead of being stomped.
///
/// Purely local and presentation-only: nothing here is replicated, and a peer that skips the
/// hitstop sees the same simulation as one that runs it.
/// </summary>
public sealed class FXHitstop : MonoBehaviour
{
    private static FXHitstop instance;
    private static bool running;

    private float capturedTimeScale = 1f;
    private float appliedTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        running = false;
    }

    /// <summary>True while a freeze is in force. Exposed for dev tooling and tests.</summary>
    public static bool IsRunning => running;

    /// <summary>
    /// Freezes for <paramref name="seconds"/> of real time at <paramref name="timeScale"/>.
    /// Silently does nothing in dev mode or if a freeze is already running.
    /// </summary>
    public static void Request(float seconds, float timeScale)
    {
        if (GameLoop.devMode || running || seconds <= 0f)
            return;

        Ensure().StartCoroutine(instance.Freeze(seconds, Mathf.Clamp(timeScale, 0.01f, 1f)));
    }

    private static FXHitstop Ensure()
    {
        if (instance == null)
        {
            GameObject host = new("FXHitstop");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<FXHitstop>();
        }
        return instance;
    }

    private IEnumerator Freeze(float seconds, float timeScale)
    {
        running = true;
        capturedTimeScale = Time.timeScale;
        appliedTimeScale = timeScale;
        Time.timeScale = timeScale;

        // Real time, because scaled time is exactly what we just slowed down.
        yield return new WaitForSecondsRealtime(seconds);

        Restore();
        running = false;
    }

    private void Restore()
    {
        if (!running)
            return;

        // Someone authoritative (round start, match end, DevInput) moved timeScale while we were
        // frozen. Their value is the current truth; leave it alone.
        if (Mathf.Approximately(Time.timeScale, appliedTimeScale))
            Time.timeScale = capturedTimeScale;
    }

    private void OnDisable()
    {
        Restore();
        running = false;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
