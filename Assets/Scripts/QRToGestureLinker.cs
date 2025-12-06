using UnityEngine;
using TMPro;
using System.Collections;

public class QRToGestureLinker : MonoBehaviour
{
    public QRScanner scanner;
    public HandGestureRecognizer recognizer;
    public TMP_Text guide;
    
    [Header("Visual Guide")]
    public GestureGuideVisual visualGuide;  // 🆕 添加视觉引导引用
    
    [Header("State Manager (optional)")]
    public BlockStateManager stateManager;  // 🆕 用来判断当前是不是 Training
    

    // Difficulty presets
    public GesturePreset easy   = new GesturePreset { okTipDist = 0.10f, fistAvgCurl = 0.16f, openAvgSpread = 0.20f };
    public GesturePreset medium = new GesturePreset { okTipDist = 0.08f, fistAvgCurl = 0.18f, openAvgSpread = 0.22f };
    public GesturePreset hard   = new GesturePreset { okTipDist = 0.06f, fistAvgCurl = 0.20f, openAvgSpread = 0.24f };

    // Current task
    public string targetGesture = "open";
    public float  holdSecs = 1.0f;

    // For SessionLogger
    [HideInInspector] public string lastCardId = "";
    [HideInInspector] public string lastDifficulty = "";

    void Awake()
    {
        GuideIdle();
    }

    void OnEnable()
    {
        if (!scanner)      scanner      = FindFirstObjectByType<QRScanner>(FindObjectsInactive.Include);
        if (!recognizer)   recognizer   = FindFirstObjectByType<HandGestureRecognizer>(FindObjectsInactive.Include);
        if (!visualGuide)  visualGuide  = FindFirstObjectByType<GestureGuideVisual>(FindObjectsInactive.Include);
        if (!stateManager) stateManager = FindFirstObjectByType<BlockStateManager>(FindObjectsInactive.Include);
        
        if (scanner) scanner.onDecoded.AddListener(OnDecoded);
    }
    
    void OnDisable()
    {
        if (scanner) scanner.onDecoded.RemoveListener(OnDecoded);
    }

    public void OnDecoded(string payload)
    {
        // Parse card JSON
        QRScanner.CardConfig cfg = null;
        try { cfg = JsonUtility.FromJson<QRScanner.CardConfig>(payload); } catch {}

        if (cfg == null || string.IsNullOrEmpty(cfg.gesture))
        {
            if (guide) guide.text = "Scanned non-card content.";
            return;
        }

        // Save for logging
        lastCardId     = cfg.card_id ?? "";
        lastDifficulty = cfg.difficulty ?? "";

        targetGesture = (cfg.gesture ?? "open").ToLowerInvariant();
        holdSecs      = Mathf.Max(0.1f, cfg.hold_secs);

        // Apply difficulty preset
        switch ((cfg.difficulty ?? "medium").ToLowerInvariant())
        {
            case "easy":   recognizer?.ApplyPreset(easy);   break;
            case "hard":   recognizer?.ApplyPreset(hard);   break;
            default:       recognizer?.ApplyPreset(medium); break;
        }

        // Gate recognizer to the target gesture
        recognizer?.GateTo(targetGesture);

        // Arm judge (timer & success check)
        var judge = FindFirstObjectByType<GestureJudge>(FindObjectsInactive.Include);
        if (judge) judge.Arm(targetGesture, holdSecs);

        // 🆕 显示视觉引导
        if (visualGuide)
        {
            visualGuide.ShowGuide(targetGesture);
        }

        // Guide text (target summary)
        if (guide)
            guide.text = $"Target: {targetGesture.ToUpper()} · Hold {holdSecs:0.0}s · Difficulty: {lastDifficulty}";
    }

    /// <summary>
    /// Call this from GestureJudge.OnSuccess (UnityEvent) after success is confirmed.
    /// </summary>
    public void OnTaskSuccess()
    {
        // 🆕 显示成功反馈
        if (visualGuide)
            visualGuide.ShowSuccess();
        
        // 延迟隐藏，让病人看到成功反馈
        StartCoroutine(DelayedCleanup());
    }

    IEnumerator DelayedCleanup()
    {
        // 等待0.8秒，让病人看到绿色反馈
        yield return new WaitForSeconds(0.8f);
        
        // 隐藏视觉引导
        if (visualGuide)
            visualGuide.HideGuide();
        
        // 显示成功提示
        GuideSuccess();
        
        // Unlock recognizer
        if (recognizer) 
        {
            recognizer.ClearGate();
            recognizer.enableGate = true;  // 确保门控重新启用
        }
        
        // 🔧 是否允许重启扫码？—— 只在 Training 模式下允许
        bool allowRestart = true;
        if (stateManager != null)
        {
            if (!stateManager.IsTrainingMode)
            {
                allowRestart = false;
                Debug.Log("[QRLinker] Skip scanner restart (not in Training mode).");
            }
        }
        
        if (scanner && allowRestart)
        {
            scanner.enabled = true;        // 确保组件启用
            scanner.RestartScan();         // 重启扫描
            Debug.Log("[QRLinker] Scanner restarted");
        }
        else if (!scanner)
        {
            Debug.LogWarning("[QRLinker] Scanner reference is missing!");
        }
    }
    
    // ---------- Helper methods for UI text (callable from UnityEvents) ----------

    /// <summary>Show the generic idle hint when waiting for a card.</summary>
    public void GuideIdle()
    {
        if (guide) guide.text = "Scan a card → Do the gesture → Hold until bar fills";
    }

    /// <summary>Show a short hint when the timer is in progress.</summary>
    public void GuideHold()
    {
        if (guide) guide.text = "Hold...";
    }

    /// <summary>Show success message after finishing one card.</summary>
    public void GuideSuccess()
    {
        if (guide) guide.text = "Success! Scan next card.";
    }
}
