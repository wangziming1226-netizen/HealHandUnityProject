#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

internal static class SafeBuildMenu
{
    [MenuItem("Tools/Build/iOS (Safe)")]
    static void BuildiOSSafe()
    {
        // 读取 Build Settings 中已勾选的场景
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[SafeBuild] No scenes are checked in Build Settings.");
            return;
        }

        var opts = new BuildPlayerOptions
        {
            target = BuildTarget.iOS,
            targetGroup = BuildTargetGroup.iOS,
            locationPathName = "Builds/iOS", // 自行调整
            scenes = scenes,
            options = BuildOptions.None
        };

        // 取消任何自定义 Build 拦截
        BuildPlayerWindow.RegisterBuildPlayerHandler(null);

        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[SafeBuild] Build finished: {report.summary.result}, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");

        if (report.summary.result == BuildResult.Failed)
        {
            foreach (var s in report.steps)
            {
                foreach (var m in s.messages)
                {
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.LogError($"[BuildError] {s.name}: {m.content}");
                }
            }

            // 打开 Editor.log 所在位置（按平台拼出常见路径）
            var logPath = GetEditorLogPathCompat();
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                EditorUtility.RevealInFinder(logPath);
            }
            else
            {
                EditorUtility.DisplayDialog("SafeBuild",
                    "Build 失败，但未能定位 Editor.log 的默认路径。\n请在 Console 窗口右上角的菜单中打开日志。",
                    "OK");
            }
        }
    }

    /// <summary>
    /// 返回 Unity Editor 日志的“常见路径”，避免使用不存在的新 API。
    /// </summary>
    static string GetEditorLogPathCompat()
    {
#if UNITY_EDITOR_OSX
        // macOS: ~/Library/Logs/Unity/Editor.log
        return Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
            "Library", "Logs", "Unity", "Editor.log");
#elif UNITY_EDITOR_WIN
        // Windows: %LOCALAPPDATA%\Unity\Editor\Editor.log
        return Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log");
#elif UNITY_EDITOR_LINUX
        // Linux: ~/.config/unity3d/Editor.log
        return Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
            ".config", "unity3d", "Editor.log");
#else
        return string.Empty;
#endif
    }
}
#endif
