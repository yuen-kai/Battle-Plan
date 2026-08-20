using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Batch-mode entry points for Tools/build-release.sh. Each method builds one platform to
/// Builds/&lt;ProductFolder&gt;/, using the scenes already configured in Build Settings.
/// </summary>
public static class BuildScript
{
    const string ProductFolder = "BattlePlan1";

    static string[] Scenes =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    static string BuildsRoot =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Version1");

    public static void BuildMac() =>
        Build(BuildTarget.StandaloneOSX, Path.Combine(BuildsRoot, ProductFolder + ".app"));

    public static void BuildWindows() =>
        Build(BuildTarget.StandaloneWindows64,
            Path.Combine(BuildsRoot, ProductFolder + "Windows", PlayerSettings.productName + ".exe"));

    public static void BuildLinux() =>
        Build(BuildTarget.StandaloneLinux64,
            Path.Combine(BuildsRoot, ProductFolder + "Linux", PlayerSettings.productName + ".x86_64"));

    public static void BuildWebGL() =>
        Build(BuildTarget.WebGL, Path.Combine(BuildsRoot, ProductFolder + "Web"));

    static void Build(BuildTarget target, string locationPathName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(locationPathName) ?? BuildsRoot);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = locationPathName,
            target = target,
            options = BuildOptions.None,
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                $"{target} build did not succeed: {report.summary.result} " +
                $"({report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings)");
        }

        Debug.Log($"[BuildScript] {target} build succeeded -> {locationPathName}");
    }
}
