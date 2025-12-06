using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把挂在的 RawImage 做左右/上下镜像，用来修正前置摄像头的“方向感”
/// 挂在：Mediapipe 那块显示手部画面的 RawImage 上
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class MirrorRawImage : MonoBehaviour
{
    [Header("Mirror Settings")]
    public bool mirrorHorizontally = true;   // 左右翻转
    public bool mirrorVertically   = false;  // 上下翻转（一般不用）

    RectTransform _rt;
    Vector3 _originalScale;
    bool _initialized = false;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _originalScale = _rt.localScale;
        _initialized = true;
    }

    void OnEnable()
    {
        ApplyMirror();
    }

#if UNITY_EDITOR
    // 在 Inspector 改勾选时也能立刻看到效果
    void OnValidate()
    {
        if (!_rt)
            _rt = GetComponent<RectTransform>();

        if (!_initialized && _rt != null)
            _originalScale = _rt.localScale;

        ApplyMirror();
    }
#endif

    void ApplyMirror()
    {
        if (_rt == null) return;

        // 确保我们是基于“绝对值”来翻转，避免连翻几次越来越乱
        var sx = Mathf.Abs(_originalScale.x) * (mirrorHorizontally ? -1f : 1f);
        var sy = Mathf.Abs(_originalScale.y) * (mirrorVertically   ? -1f : 1f);

        _rt.localScale = new Vector3(sx, sy, _originalScale.z);
    }
}
