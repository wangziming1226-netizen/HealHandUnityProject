using UnityEngine;
using TMPro;

public class BlockStateManager : MonoBehaviour
{
    [Header("References")]
    public GestureJudge judge;
    public QRScanner qrScanner;
    public HandGestureRecognizer recognizer;    // 需要暴露 LastPoints
    public MonoBehaviour handRunner;            // 可选：Hand Landmarker Runner
    public TMP_Text statusText;
    public TMP_Text countDownText;
    public SessionLogger logger;                // 可选

    [Header("Session End")]
    public int totalCardsTarget = 10;           // 总共要完成多少张卡（0 = 无限制）
    public SessionEndManager endManager;        // 结束管理器

    [Header("Flow")]
    public int   cardsPerBlock = 3;             // 每完成多少张卡触发一次状态检测
    public float thumbsHold    = 0.6f;          // 拇指手势需要保持的秒数
    public float restSeconds   = 300f;          // 休息 5 分钟

    [Header("Orientation Tuning")]
    public bool  invertThumbY = true;           // 你的坐标是纹理坐标时通常要勾上
    [Range(0f, 1f)] public float thumbStraightMin = 0.10f; // 拇指"伸直"最低值（越小越宽松）
    [Range(0f, 1f)] public float minVectorLen   = 0.03f;   // 拇指方向向量最小长度（太短判不出）
    [Range(5f, 45f)] public float angleToleranceDeg = 25f; // 基础角度容差

    [Header("Other Fingers (optional)")]
    public bool  needOtherFingersCurled = false; // 是否强制四指要弯曲
    [Range(0f, 1f)] public float fourFingersCurlMin = 0.16f; // 四指弯曲阈值（需要时才生效）

    [Header("State Check Timing")]
    [Tooltip("进入拇指判定后，先空窗多少秒再开始检测（避免沿用上一帧的手势）")]
    public float stateCheckWarmup = 0.7f;       // 冷却 / 预备时间（秒）
    float warmupLeft = 0f;                      // 当前这一轮的剩余预备时间

    [Header("Debug")]
    public bool showDebug = false;

    enum Mode  { Training, StateCheck, Rest, Stopped }
    enum Thumb { Unknown, Up, Down, Side }
    
    Mode  mode = Mode.Training;
    int   doneInBlock = 0;
    int   totalCompleted = 0;      // 总完成数
    float holdTimer   = 0f;
    float restLeft    = 0f;
    float sessionStartTime;        // 会话开始时间
    
    // 🔍 提供给其它脚本查询当前是否处于正常训练阶段
    public bool IsTrainingMode => mode == Mode.Training;
    

    void Awake()
    {
        if (!judge)      judge      = FindFirstObjectByType<GestureJudge>(FindObjectsInactive.Include);
        if (!qrScanner)  qrScanner  = FindFirstObjectByType<QRScanner>(FindObjectsInactive.Include);
        if (!recognizer) recognizer = FindFirstObjectByType<HandGestureRecognizer>(FindObjectsInactive.Include);
        if (!logger)     logger     = FindFirstObjectByType<SessionLogger>(FindObjectsInactive.Include);
        if (!endManager) endManager = FindFirstObjectByType<SessionEndManager>(FindObjectsInactive.Include);

        if (countDownText) countDownText.gameObject.SetActive(false);
        if (statusText)    statusText.text = "Ready…";

        sessionStartTime = Time.time;  // 记录开始时间
    }

    /// <summary>
    /// 在 GestureJudge.OnSuccess 里再额外调用一次
    /// </summary>
    public void OnTaskSuccess()
    {
        if (mode != Mode.Training) return;

        totalCompleted++;  // 累计总完成数
        doneInBlock++;

        // 检查是否达到总目标
        if (totalCardsTarget > 0 && totalCompleted >= totalCardsTarget)
        {
            EndSession("All Cards Completed!");
            return;
        }

        if (doneInBlock >= cardsPerBlock)
            StartStateCheck();
    }

    void StartStateCheck()
    {
        mode = Mode.StateCheck;
        doneInBlock = 0;
        holdTimer = 0f;
        warmupLeft = stateCheckWarmup;   // ⭐ 每次进入 StateCheck 先走预备时间

        if (judge) { judge.enabled = false; judge.requireScan = false; }
        if (qrScanner) qrScanner.enabled = false; // 暂停扫码
        if (statusText) statusText.text = "State Check: show thumb (UP=continue / DOWN=stop / SIDE=rest)";

        if (recognizer) recognizer.enableGate = false; // 放开门控以读取关键点
        logger?.MarkStateCheck("begin");
    }

    void StartRest()
    {
        mode = Mode.Rest;
        restLeft = restSeconds;

        if (handRunner)   handRunner.enabled = false;
        if (statusText)   statusText.text = "Please rest for 5 minutes…";
        if (countDownText) countDownText.gameObject.SetActive(true);

        if (judge) judge.enabled = false;
        if (qrScanner) qrScanner.enabled = false;

        logger?.MarkStateCheck("thumb_side");
    }

    void StopAll()
    {
        mode = Mode.Stopped;

        if (statusText) statusText.text = "Training stopped (thumb down)";
        if (handRunner) handRunner.enabled = false;
        if (judge) { judge.enabled = false; judge.requireScan = false; }
        if (qrScanner) qrScanner.enabled = false;
        if (countDownText) countDownText.gameObject.SetActive(false);

        logger?.MarkStateCheck("thumb_down");

        // 显示结束界面
        EndSession("Training Stopped by User");
    }

    void ResumeTraining()
    {
        mode = Mode.Training;
        holdTimer = 0f;
        warmupLeft = 0f;

        if (statusText) statusText.text = "Ready…";
        if (handRunner) handRunner.enabled = true;

        if (judge) { judge.enabled = true; judge.requireScan = true; } // 下一轮必须扫码 Arm
        if (qrScanner) { qrScanner.enabled = true; qrScanner.RestartScan(); } // 真正恢复扫描
        if (recognizer) recognizer.enableGate = true;

        if (countDownText) countDownText.gameObject.SetActive(false);
        logger?.MarkStateCheck("thumb_up");
    }

    /// <summary>
    /// 结束会话并显示总结
    /// </summary>
    void EndSession(string reason = "Training Complete")
    {
        mode = Mode.Stopped;

        // 禁用所有组件
        if (handRunner) handRunner.enabled = false;
        if (judge) judge.enabled = false;
        if (qrScanner) qrScanner.enabled = false;
        if (recognizer) recognizer.enabled = false;

        // 显示结束界面
        if (endManager)
        {
            endManager.ShowEndScreen(totalCompleted, reason);
        }
        else
        {
            Debug.LogWarning("[BlockStateManager] SessionEndManager not found!");
            if (statusText)
                statusText.text = $"Session Complete! Cards: {totalCompleted}";
        }

        Debug.Log($"[BlockStateManager] Session ended. Total cards: {totalCompleted}, Reason: {reason}");
    }

    void Update()
    {
        switch (mode)
        {
            case Mode.StateCheck: TickStateCheck(); break;
            case Mode.Rest:       TickRest();       break;
        }
    }

    void TickRest()
    {
        restLeft -= Time.unscaledDeltaTime;
        if (countDownText)
        {
            int m = Mathf.Max(0, Mathf.FloorToInt(restLeft / 60));
            int s = Mathf.Max(0, Mathf.FloorToInt(restLeft % 60));
            countDownText.text = $"Rest {m:00}:{s:00}";
        }
        if (restLeft <= 0f) ResumeTraining();
    }

    void TickStateCheck()
    {
        // ① 先走冷却 / 预备期：这段时间内完全不判定手势，避免沿用上一帧的结果
        if (warmupLeft > 0f)
        {
            warmupLeft -= Time.deltaTime;
            holdTimer = 0f;   // 冷却期内也不累积 hold

            if (showDebug && statusText)
            {
                statusText.text =
                    $"State Check… get ready ({warmupLeft:2.0}s)";
            }
            return;
        }

        // ② 冷却结束后，再开始读取当前帧的拇指方向
        var lm = recognizer ? recognizer.LastPoints : null;
        Thumb t = ClassifyThumb(lm, out float angleDeg, out float mag, out float avgCurl, out float straight);

        if (showDebug && statusText)
        {
            statusText.text =
                $"State Check…  t={t}  hold={holdTimer:0.00}/{thumbsHold:0.00}\n" +
                $"angle={angleDeg:0.0}°  mag={mag:0.000}  curl4={avgCurl:0.000}  thumbStraight={straight:0.000}";
        }

        if (t == Thumb.Unknown)
        {
            holdTimer = 0f;
            return;
        }

        holdTimer += Time.deltaTime;
        if (holdTimer >= thumbsHold)
        {
            switch (t)
            {
                case Thumb.Up:   logger?.MarkStateCheck("thumb_up");   ResumeTraining(); break;
                case Thumb.Down: logger?.MarkStateCheck("thumb_down"); StopAll();        break;
                case Thumb.Side: logger?.MarkStateCheck("thumb_side"); StartRest();      break;
            }
        }
    }

    // —— 角度法：更稳健地区分 Up / Down / Side —— 
    Thumb ClassifyThumb(Vector2[] lm, out float angleDeg, out float mag, out float avgCurl, out float thumbStraight)
    {
        angleDeg = 0f; mag = 0f; avgCurl = 0f; thumbStraight = 0f;

        if (lm == null || lm.Length < 21) return Thumb.Unknown;

        // 索引
        const int THUMB_TIP=4, THUMB_IP=3, THUMB_MCP=2;
        const int INDEX_TIP=8,  INDEX_PIP=6,  INDEX_MCP=5;
        const int MIDDLE_TIP=12, MIDDLE_PIP=10, MIDDLE_MCP=9;
        const int RING_TIP=16,   RING_PIP=14,   RING_MCP=13;
        const int PINKY_TIP=20,  PINKY_PIP=18,  PINKY_MCP=17;

        // 指弯曲度
        float Curl(Vector2 tip, Vector2 pip, Vector2 mcp)
        {
            float a = (tip - mcp).magnitude;
            float b = (pip - mcp).magnitude + 1e-5f;
            return 1f - Mathf.Clamp01(a / b);
        }

        float c1 = Curl(lm[INDEX_TIP],  lm[INDEX_PIP],  lm[INDEX_MCP]);
        float c2 = Curl(lm[MIDDLE_TIP], lm[MIDDLE_PIP], lm[MIDDLE_MCP]);
        float c3 = Curl(lm[RING_TIP],   lm[RING_PIP],   lm[RING_MCP]);
        float c4 = Curl(lm[PINKY_TIP],  lm[PINKY_PIP],  lm[PINKY_MCP]);
        avgCurl  = (c1 + c2 + c3 + c4) / 4f;

        if (needOtherFingersCurled && avgCurl < fourFingersCurlMin)
            return Thumb.Unknown;

        // 拇指伸直程度（tip-mcp 相对 ip-mcp）
        thumbStraight = 1f - Curl(lm[THUMB_TIP], lm[THUMB_IP], lm[THUMB_MCP]);
        if (thumbStraight < thumbStraightMin)
            return Thumb.Unknown;

        // 拇指方向向量（tip - ip）
        Vector2 v = (lm[THUMB_TIP] - lm[THUMB_IP]);
        if (invertThumbY) v.y = -v.y;   // 纹理坐标时翻转 Y
        mag = v.magnitude;
        if (mag < minVectorLen) return Thumb.Unknown;

        // 角度（-180..180），右为 0°，上为 +90°，下为 -90°
        angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

        // ⭐ 分开设置容差：竖直宽一点，水平窄一点
        float vertTol  = Mathf.Clamp(angleToleranceDeg, 5f, 60f);
        float horizTol = vertTol * 0.6f;

        // 归一到 [-180, 180]
        float NormAngle(float a)
        {
            while (a >  180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);

        // ---- 先判竖直（Up / Down），且要求"竖直分量占优势" ----
        float upDelta   = Mathf.Abs(NormAngle(angleDeg - 90f));
        float downDelta = Mathf.Abs(NormAngle(angleDeg + 90f));

        if (ay >= ax && upDelta <= vertTol)
            return Thumb.Up;

        if (ay >= ax && downDelta <= vertTol)
            return Thumb.Down;

        // ---- 再判水平（Side），且要求"水平分量占优势" ----
        float rightDelta = Mathf.Abs(NormAngle(angleDeg - 0f));
        float leftDelta1 = Mathf.Abs(NormAngle(angleDeg - 180f));
        float leftDelta2 = Mathf.Abs(NormAngle(angleDeg + 180f));
        float sideDelta  = Mathf.Min(rightDelta, Mathf.Min(leftDelta1, leftDelta2));

        if (ax > ay && sideDelta <= horizTol)
            return Thumb.Side;

        return Thumb.Unknown;
    }
}
