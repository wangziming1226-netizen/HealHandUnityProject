using System;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class HandGestureRecognizer : MonoBehaviour
{
    [Header("UI（可选）")]
    public TMP_Text statusText;   // 仍然保留，但默认不再自动写字

    [Header("回调事件")]
    public UnityEvent OnOpenHand;
    public UnityEvent OnFist;
    public UnityEvent OnOK;

    [Header("阈值（可按实际调）")]
    [Range(0f, 1f)] public float okTipDist = 0.08f;
    [Range(0f, 1f)] public float fistAvgCurl = 0.18f;
    [Range(0f, 1f)] public float openAvgSpread = 0.22f;

    [Header("显示")]
    // 是否显示“原始识别字幕”（Open/Fist/OK）。默认关闭，避免和任务提示打架
    public bool showRawLabel = false;

    [Header("任务门控（Gate）")]
    // 当启用且设置了 gateGesture 时，只有该手势才算“识别到”
    public bool   enableGate   = true;
    public Gesture gateGesture = Gesture.Unknown; // 由扫码脚本设置

    [Header("稳定性设置")]
    [Tooltip("同一个手势需要连续多少帧才算真的识别到")]
    [Range(1, 20)] public int requiredStableFrames = 5;

    [Tooltip("关键点平滑系数，越小越平滑（0.2~0.4 比较合适）")]
    [Range(0f, 1f)] public float smoothFactor = 0.3f;

    // 原始关键点 & 平滑后的关键点
    Vector2[] _rawPoints;
    Vector2[] _smoothedPoints;

    // 稳定手势检测
    Gesture _lastFrameGesture   = Gesture.Unknown;
    int     _stableFrameCount   = 0;

    public enum Gesture { Unknown, OK, Fist, Open }

    /// <summary>对外暴露的“当前稳定手势”</summary>
    public Gesture CurrentGesture { get; private set; } = Gesture.Unknown;

    /// <summary>供其它脚本（例如 BlockStateManager）读取的最后一帧关键点（已平滑）</summary>
    public Vector2[] LastPoints => _smoothedPoints ?? _rawPoints;

    void Awake()
    {
        if (statusText) statusText.text = "Ready...";
    }

    /// <summary>外部（如 QRToGestureLinker）调用，用预设一键设置三个阈值。</summary>
    public void ApplyPreset(GesturePreset p)
    {
        okTipDist     = p.okTipDist;
        fistAvgCurl   = p.fistAvgCurl;
        openAvgSpread = p.openAvgSpread;
    }

    /// <summary>外部设置门控目标手势（如从二维码里读到）</summary>
    public void GateTo(string g)
    {
        gateGesture = FromString(g);
        enableGate  = true;
    }

    public void ClearGate()
    {
        gateGesture = Gesture.Unknown;
        enableGate  = false;
    }

    Gesture FromString(string s)
    {
        if (string.IsNullOrEmpty(s)) return Gesture.Unknown;
        switch (s.ToLowerInvariant())
        {
            case "ok":   return Gesture.OK;
            case "fist": return Gesture.Fist;
            case "open": return Gesture.Open;
            default:     return Gesture.Unknown;
        }
    }

    /// <summary>
    /// Binder 每帧喂进来的 2D 关键点（0~1）
    /// </summary>
    public void Feed(Vector2[] points)
    {
        _rawPoints = points;

        // 1）没有手，直接清空状态
        if (points == null || points.Length < 21)
        {
            CurrentGesture   = Gesture.Unknown;
            _stableFrameCount = 0;
            _lastFrameGesture = Gesture.Unknown;

            Write(showRawLabel ? "No hand" : null);
            return;
        }

        // 2）做关键点平滑（低通滤波）
        if (_smoothedPoints == null || _smoothedPoints.Length != points.Length)
        {
            _smoothedPoints = (Vector2[])points.Clone();
        }
        else
        {
            for (int i = 0; i < points.Length; i++)
            {
                _smoothedPoints[i] = Vector2.Lerp(_smoothedPoints[i], points[i], smoothFactor);
            }
        }

        // 3）基于平滑后的点做一次“原始分类”
        var rawGesture = Classify(_smoothedPoints);

        // 4）门控：锁定目标手势时，非目标一律当 Unknown（等于忽略）
        if (enableGate && gateGesture != Gesture.Unknown && rawGesture != gateGesture)
        {
            rawGesture = Gesture.Unknown;
        }

        // 5）连续帧稳定判定
        if (rawGesture == _lastFrameGesture)
        {
            _stableFrameCount++;
        }
        else
        {
            _stableFrameCount = 1;
            _lastFrameGesture = rawGesture;
        }

        Gesture stableGesture = Gesture.Unknown;
        if (_stableFrameCount >= requiredStableFrames)
        {
            stableGesture = rawGesture;
        }

        CurrentGesture = stableGesture;

        // 6）根据“稳定后的手势”触发事件和可选字幕
        switch (stableGesture)
        {
            case Gesture.OK:
                Write(showRawLabel ? "OK" : null);
                OnOK?.Invoke();
                break;

            case Gesture.Fist:
                Write(showRawLabel ? "Fist" : null);
                OnFist?.Invoke();
                break;

            case Gesture.Open:
                Write(showRawLabel ? "Open hand" : null);
                OnOpenHand?.Invoke();
                break;

            default:
                Write(showRawLabel ? "…" : null);
                break;
        }
    }

    // ================== 手势分类核心 ==================

    Gesture Classify(Vector2[] lm)
    {
        const int WRIST = 0;
        const int THUMB_TIP = 4, INDEX_TIP = 8, MIDDLE_TIP = 12, RING_TIP = 16, PINKY_TIP = 20;
        const int INDEX_PIP = 6,  MIDDLE_PIP = 10, RING_PIP = 14, PINKY_PIP = 18;
        const int INDEX_MCP = 5,  MIDDLE_MCP = 9,  RING_MCP = 13, PINKY_MCP = 17;

        float thumbIndex = Dist(lm[THUMB_TIP], lm[INDEX_TIP]);

        float indexCurl  = Curl(lm[INDEX_TIP],  lm[INDEX_PIP],  lm[INDEX_MCP]);
        float middleCurl = Curl(lm[MIDDLE_TIP], lm[MIDDLE_PIP], lm[MIDDLE_MCP]);
        float ringCurl   = Curl(lm[RING_TIP],   lm[RING_PIP],   lm[RING_MCP]);
        float pinkyCurl  = Curl(lm[PINKY_TIP],  lm[PINKY_PIP],  lm[PINKY_MCP]);
        float avgCurl    = (indexCurl + middleCurl + ringCurl + pinkyCurl) / 4f;

        float spread =
            (Dist(lm[INDEX_TIP],  lm[WRIST]) +
             Dist(lm[MIDDLE_TIP], lm[WRIST]) +
             Dist(lm[RING_TIP],   lm[WRIST]) +
             Dist(lm[PINKY_TIP],  lm[WRIST])) / 4f;

        // OK：拇指与食指尖距离小 + 其它手指基本伸直
        if (thumbIndex < okTipDist && avgCurl < 0.12f)
            return Gesture.OK;

        // Fist：四指弯曲很大
        if (avgCurl > fistAvgCurl)
            return Gesture.Fist;

        // Open：四指张开且不太弯
        if (spread > openAvgSpread && avgCurl < 0.15f)
            return Gesture.Open;

        return Gesture.Unknown;
    }

    float Dist(Vector2 a, Vector2 b) => (a - b).magnitude;

    float Curl(Vector2 tip, Vector2 pip, Vector2 mcp)
    {
        float a = Dist(tip, mcp);
        float b = Dist(pip, mcp) + 1e-5f;
        return 1f - Mathf.Clamp01(a / b);
    }

    void Write(string s)
    {
        if (!statusText) return;
        if (string.IsNullOrEmpty(s))
        {
            // 当 showRawLabel = false 时，传 null 不改 UI
            return;
        }
        statusText.text = s;
    }
}
