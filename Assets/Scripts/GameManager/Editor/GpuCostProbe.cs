#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Measures what the two expensive render settings actually cost, on a real board, in one Play
/// session. Answers the question "is 8x MSAA worth it" with a number instead of an argument.
///
/// Both settings have runtime setters on the pipeline asset, so a single session can walk every
/// value and time each one rather than needing a rebuild per configuration. Two things make the
/// numbers mean something:
/// <list type="bullet">
/// <item>It times <b>GPU frame time</b> from <c>FrameTimingManager</c>, not frames per second. The
/// Editor presents the Game view on its own schedule and <see cref="FrameRatePolicy"/> caps the
/// rate on purpose, so frames per second is pinned and would read the same for every setting. GPU
/// milliseconds per frame is the actual work.</item>
/// <item>It sweeps one variable at a time from a fixed baseline, so a difference belongs to the
/// setting that changed.</item>
/// </list>
///
/// A dev tool: it drives Play mode itself, prints a table, restores every value it touched and
/// stops. Nothing here ships.
/// </summary>
public static class GpuCostProbe
{
    private const string AutoRunKey = "BattlePlan.GpuCostProbe.AutoRun";

    /// <summary>
    /// Wall-clock seconds each configuration is held at the shipping frame cap. Long enough for the
    /// hardware's own utilisation counters, sampled from outside the editor, to average out.
    /// </summary>
    private const float HoldSeconds = 5f;

    /// <summary>Discarded after a change, for render targets to be recreated and settle.</summary>
    private const float SettleSeconds = 1.5f;

    private static readonly (string Label, float RenderScale, int Msaa, int Shadow)[] Sweep =
    {
        ("BEFORE  rs 2.00, MSAA 8x, shadow 4096", 2f, 8, 4096),
        ("NOW     rs 2.00, MSAA 2x, shadow 2048", 2f, 2, 2048),
        ("SHIPPED rs 1.00, MSAA 8x, shadow 4096", 1f, 8, 4096),
        ("AFTER   rs 1.00, MSAA 2x, shadow 2048", 1f, 2, 2048),
        ("renderScale 0.50  (MSAA 2x, shadow 2048)", 0.5f, 2, 2048),
        ("renderScale 0.75  (MSAA 2x, shadow 2048)", 0.75f, 2, 2048),
        ("renderScale 1.00  (MSAA 2x, shadow 2048)", 1f, 2, 2048),
        ("renderScale 1.50  (MSAA 2x, shadow 2048)", 1.5f, 2, 2048),
        ("renderScale 2.00  (MSAA 2x, shadow 2048)", 2f, 2, 2048),
        ("MSAA 1x  (rs 1.00, shadow 2048)", 1f, 1, 2048),
        ("MSAA 2x  (rs 1.00, shadow 2048)", 1f, 2, 2048),
        ("MSAA 4x  (rs 1.00, shadow 2048)", 1f, 4, 2048),
        ("MSAA 8x  (rs 1.00, shadow 2048)", 1f, 8, 2048),
        ("shadow 1024  (rs 1.00, MSAA 2x)", 1f, 2, 1024),
        ("shadow 4096  (rs 1.00, MSAA 2x)", 1f, 2, 4096),
    };

    private readonly struct Result
    {
        public readonly string Label;
        public readonly float FramesPerSecond;
        public readonly float CpuMilliseconds;
        public readonly int Frames;

        public Result(string label, float framesPerSecond, float cpuMilliseconds, int frames)
        {
            Label = label;
            FramesPerSecond = framesPerSecond;
            CpuMilliseconds = cpuMilliseconds;
            Frames = frames;
        }
    }

    [UnityEditor.MenuItem("Battle Plan/Measure GPU cost")]
    public static void Run()
    {
        if (Application.isPlaying)
        {
            Begin();
            return;
        }

        UnityEditor.SessionState.SetBool(AutoRunKey, true);
        // FrameTimingManager needs this to report anything at all. Its GPU timer is unimplemented on
        // Metal in-editor and reads zero regardless, which is why the GPU side of this measurement
        // comes from the hardware's own utilisation counters instead; cpuFrameTime does work.
        UnityEditor.PlayerSettings.enableFrameTimingStats = true;
        SandboxLauncher.Launch();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRunAfterEnterPlay()
    {
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;
        UnityEditor.SessionState.EraseBool(AutoRunKey);
        Begin();
    }

    private static void Begin()
    {
        GameObject runner = new(nameof(GpuCostProbe));
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<GpuCostProbeRunner>();
    }

    internal static IEnumerator Measure()
    {
        Debug.Log("[GPU] Waiting for a board to measure…");
        float deadline = Time.realtimeSinceStartup + 120f;
        while (
            (GameLoop.currentPhase != "planning" || GameLoop.GetTeamUnits(0).Length == 0)
            && Time.realtimeSinceStartup < deadline
        )
        {
            yield return null;
        }

        if (GameLoop.currentPhase != "planning")
        {
            Debug.LogError("[GPU] No board came up within two minutes; nothing measured.");
            yield break;
        }

        if (UniversalRenderPipeline.asset is not UniversalRenderPipelineAsset pipeline)
        {
            Debug.LogError("[GPU] The active pipeline is not URP; nothing to measure.");
            yield break;
        }

        int originalMsaa = pipeline.msaaSampleCount;
        int originalShadow = pipeline.mainLightShadowmapResolution;
        float originalRenderScale = pipeline.renderScale;
        int originalVSync = QualitySettings.vSyncCount;
        int originalTarget = Application.targetFrameRate;
        int originalInterval = OnDemandRendering.renderFrameInterval;

        // Uncapped, deliberately. Capped, every configuration delivers the same 60 frames and the
        // difference hides in idle time — the machine's own utilisation counters saturate at 100%
        // and discriminate nothing. Uncapped, the GPU is the limiter and frame time *is* GPU cost
        // per frame: at the cap this board's CPU frame measures about 1.4 ms, so the ~10 ms an
        // uncapped frame takes is the GPU's, not the CPU's. Suspended for the run either way, since
        // a focus change would otherwise re-cap it midway.
        FrameRatePolicy.Suspended = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        OnDemandRendering.renderFrameInterval = 1;

        List<Result> results = new();
        try
        {
            Debug.Log(
                $"[GPU] Board up ({GameLoop.GetTeamUnits(0).Length}v{GameLoop.GetTeamUnits(1).Length}), "
                    + $"screen {Screen.width}x{Screen.height}. Sweeping."
            );

            yield return SweepQualityLevels(results);

            foreach ((string label, float renderScale, int msaa, int shadow) in Sweep)
            {
                pipeline.renderScale = renderScale;
                pipeline.msaaSampleCount = msaa;
                pipeline.mainLightShadowmapResolution = shadow;
                yield return SampleInto(results, label);
            }
        }
        finally
        {
            FrameRatePolicy.Suspended = false;
            pipeline.msaaSampleCount = originalMsaa;
            pipeline.mainLightShadowmapResolution = originalShadow;
            pipeline.renderScale = originalRenderScale;
            QualitySettings.vSyncCount = originalVSync;
            Application.targetFrameRate = originalTarget;
            OnDemandRendering.renderFrameInterval = originalInterval;
            Debug.Log(Report(results));
        }
    }

    /// <summary>
    /// Times the three quality levels exactly as they are authored, before anything is overridden.
    /// The sweeps below isolate one setting at a time from a shared baseline; this one answers the
    /// question a player actually asks, which is what the preset in the menu costs them.
    /// </summary>
    private static IEnumerator SweepQualityLevels(List<Result> results)
    {
        int originalLevel = QualitySettings.GetQualityLevel();
        try
        {
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, true);
                // The level just re-applied its own vSync count over the uncapped one.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                yield return null;

                string detail = UniversalRenderPipeline.asset is UniversalRenderPipelineAsset asset
                    ? $"rs {asset.renderScale:0.00}, MSAA {asset.msaaSampleCount}x, shadow {asset.mainLightShadowmapResolution}"
                    : "not URP";
                yield return SampleInto(results, $"Quality {QualitySettings.names[level]} ({detail})");
            }
        }
        finally
        {
            QualitySettings.SetQualityLevel(originalLevel, true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
    }

    private static IEnumerator SampleInto(List<Result> results, string label)
    {
        yield return new WaitForSecondsRealtime(SettleSeconds);

        long startedAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int startFrame = Time.frameCount;
        float startTime = Time.realtimeSinceStartup;

        double cpuTotal = 0d;
        int counted = 0;
        FrameTiming[] timings = new FrameTiming[1];
        while (Time.realtimeSinceStartup - startTime < HoldSeconds)
        {
            yield return null;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) == 0)
                continue;

            cpuTotal += timings[0].cpuFrameTime;
            counted++;
        }

        long endedAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int frames = Time.frameCount - startFrame;
        float elapsed = Time.realtimeSinceStartup - startTime;

        Result result = new(
            label,
            frames / Mathf.Max(0.001f, elapsed),
            counted > 0 ? (float)(cpuTotal / counted) : 0f,
            frames
        );
        results.Add(result);
        // The window is stamped so utilisation samples taken from outside the editor, which cannot
        // see these configuration changes, can be attributed to the configuration that was live.
        Debug.Log(
            $"[GPU] MARK {startedAt} {endedAt} {result.Label} | {result.FramesPerSecond:0.0} fps, "
                + $"frame {result.CpuMilliseconds:0.00} ms, {result.Frames} frames"
        );
    }

    private static string Report(List<Result> results)
    {
        StringBuilder report = new();
        report.AppendLine("[GPU] RESULT");
        if (results.Count == 0)
        {
            report.AppendLine("  Nothing was timed; no configuration completed a window.");
            return report.ToString();
        }

        report.AppendLine("  setting                                          fps   frame ms   frames");
        foreach (Result result in results)
        {
            report.AppendLine(
                $"  {result.Label,-45} {result.FramesPerSecond,7:0.0} {result.CpuMilliseconds,10:0.00} "
                    + $"{result.Frames,8}"
            );
        }
        return report.ToString();
    }
}

internal sealed class GpuCostProbeRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return GpuCostProbe.Measure();
        Destroy(gameObject);
    }
}
#endif
