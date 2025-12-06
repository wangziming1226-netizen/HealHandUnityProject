#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 强制把 Build 按钮恢复为 Unity 默认构建流程：
/// 1) 清掉任何第三方注册的 BuildPlayerHandler；
/// 2) 由我们注册一个仅调用 BuildPipeline.BuildPlayer 的 handler；
/// 3) 打印更清晰的 BuildReport，便于排错。
/// 仅在 Editor 下生效。
/// </summary>
[InitializeOnLoad]
internal static class ForceDefaultBuildHandler
{
    // 防止被重复安装
    private static bool s_installed;

    static ForceDefaultBuildHandler()
    {
        // 等所有包的 InitializeOnLoad 跑完，再由我们“最后接管”
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        if (s_installed) return;
        s_installed = true;

        try
        {
            // 1) 清空已注册的 handler（包括第三方）
            BuildPlayerWindow.RegisterBuildPlayerHandler(null);

            // 2) 仅调用 Unity 的默认构建管线（BuildPipeline.BuildPlayer）
            BuildPlayerWindow.RegisterBuildPlayerHandler((BuildPlayerOptions opts) =>
            {
                try
                {
                    // 直接用 BuildPipeline（不同版本 API 行为更一致）
                    BuildReport report = BuildPipeline.BuildPlayer(opts);
                    LogBuildReport(report);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    throw;
                }
            });

            Debug.Log("[ForceDefaultBuildHandler] Build handler is forced to Unity default (BuildPipeline.BuildPlayer).");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            // 兜底：至少保证没有第三方 handler
            try { BuildPlayerWindow.RegisterBuildPlayerHandler(null); } catch { }
        }
    }

    // 打印更清晰的构建结果与错误
    private static void LogBuildReport(BuildReport report)
    {
        if (report == null)
        {
            Debug.LogWarning("[BuildReport] null report (Build succeeded without report or an exception occurred earlier).");
            return;
        }

        var sum = report.summary;
        Debug.Log($"[BuildReport] result={sum.result}  platform={sum.platform}  " +
                  $"size={(sum.totalSize / 1048576f):0.0}MB  warnings={sum.totalWarnings}  errors={sum.totalErrors}");

        if (sum.result == BuildResult.Failed)
        {
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    {
                        Debug.LogError($"[BuildError] {step.name}: {msg.content}");
                    }
                }
            }
        }
    }
}
#endif
