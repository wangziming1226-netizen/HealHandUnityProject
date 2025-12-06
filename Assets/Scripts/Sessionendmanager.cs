using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// 管理训练结束界面，显示总结和提供重启/退出选项
/// </summary>
public class SessionEndManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject endPanel;              // 结束界面面板
    public TMP_Text summaryText;             // 总结文字
    public TMP_Text titleText;               // 标题（可选）
    public Button restartButton;             // 重新开始按钮
    public Button quitButton;                // 退出按钮（可选）

    [Header("References")]
    public SessionLogger logger;             // 日志记录器
    public BlockStateManager stateManager;   // 状态管理器（可选）

    [Header("Settings")]
    public bool autoExportLog = true;        // 结束时自动导出日志

    private float sessionStartTime;
    private int totalCardsCompleted;

    void Start()
    {
        // 初始隐藏结束面板
        if (endPanel)
            endPanel.SetActive(false);

        // 记录开始时间
        sessionStartTime = Time.time;

        // 绑定按钮事件
        if (restartButton)
            restartButton.onClick.AddListener(RestartSession);

        if (quitButton)
            quitButton.onClick.AddListener(QuitApplication);

        // 自动查找引用
        if (!logger)
            logger = FindFirstObjectByType<SessionLogger>();

        if (!stateManager)
            stateManager = FindFirstObjectByType<BlockStateManager>();
    }

    /// <summary>
    /// 显示结束界面
    /// </summary>
    /// <param name="totalCards">完成的卡片总数</param>
    /// <param name="reason">结束原因</param>
    public void ShowEndScreen(int totalCards, string reason = "Training Complete")
    {
        if (!endPanel)
        {
            Debug.LogWarning("[SessionEnd] End panel is not assigned!");
            return;
        }

        totalCardsCompleted = totalCards;
        float sessionDuration = Time.time - sessionStartTime;

        // 激活结束面板
        endPanel.SetActive(true);

        // 设置标题
        if (titleText)
            titleText.text = reason;

        // 设置总结文字
        if (summaryText)
        {
            summaryText.text = GenerateSummary(totalCards, sessionDuration);
        }

        // 自动导出日志
        if (autoExportLog && logger)
        {
            logger.ExportCsvNow();
        }

        // 暂停游戏（可选）
        // Time.timeScale = 0f;

        Debug.Log($"[SessionEnd] Training session ended. Cards: {totalCards}, Duration: {FormatTime(sessionDuration)}");
    }

    /// <summary>
    /// 生成总结文字
    /// </summary>
    string GenerateSummary(int cards, float duration)
    {
        string summary = $"<size=48><b>Training Complete!</b></size>\n\n";
        summary += $"<size=36>Cards Completed: <b>{cards}</b></size>\n";
        summary += $"<size=36>Total Time: <b>{FormatTime(duration)}</b></size>\n\n";
        
        // 简单的鼓励语
        if (cards >= 10)
            summary += "<size=32><color=#4CAF50>Excellent work! 🎉</color></size>";
        else if (cards >= 5)
            summary += "<size=32><color=#8BC34A>Great job! 👍</color></size>";
        else
            summary += "<size=32><color=#FFC107>Good start! 💪</color></size>";

        return summary;
    }

    /// <summary>
    /// 格式化时间显示
    /// </summary>
    string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60);
        int secs = Mathf.FloorToInt(seconds % 60);
        return $"{minutes:00}:{secs:00}";
    }

    /// <summary>
    /// 重新开始训练
    /// </summary>
    public void RestartSession()
    {
        Debug.Log("[SessionEnd] Restarting session...");
        
        // 恢复时间缩放（如果之前暂停了）
        Time.timeScale = 1f;

        // 重新加载当前场景
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// 退出应用
    /// </summary>
    public void QuitApplication()
    {
        Debug.Log("[SessionEnd] Quitting application...");

        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    #if UNITY_EDITOR
    void OnValidate()
    {
        // 清除 HideFlags，方便调试
        if (endPanel) endPanel.hideFlags = HideFlags.None;
        if (summaryText) summaryText.hideFlags = HideFlags.None;
        if (titleText) titleText.hideFlags = HideFlags.None;
        if (restartButton) restartButton.hideFlags = HideFlags.None;
        if (quitButton) quitButton.hideFlags = HideFlags.None;
    }
    #endif
}