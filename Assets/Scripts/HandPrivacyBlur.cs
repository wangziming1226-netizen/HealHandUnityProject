using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class HandPrivacyBlur : MonoBehaviour
{
    [Header("Hand Source")]
    public HandGestureRecognizer recognizer;

    [Tooltip("是否用手部关键点作为圆心；不勾则固定在屏幕中心")]
    public bool useHandCenter = false;

    [Header("Blur Settings")]
    [Range(0.5f, 8f)] public float blurRadius  = 4f;     // 和 Shader 里的 _BlurRadius 对应
    [Range(0.05f, 0.5f)] public float focusRadius = 0.25f; // 和 _FocusRadius 对应
    [Range(0.005f, 0.3f)] public float focusEdge  = 0.08f; // 和 _EdgeWidth 对应

    [Tooltip("当使用手部中心时，把圆心在竖直方向整体上/下移（UV 坐标，正数向上）")]
    [Range(-0.5f, 0.5f)] public float centerYOffset = 0.1f;

    RawImage _img;
    Material _matInstance;

    // === 这些名字要和 Shader 里的一模一样 ===
    static readonly int ID_BlurRadius  = Shader.PropertyToID("_BlurRadius");
    static readonly int ID_FocusCenter = Shader.PropertyToID("_FocusCenter");
    static readonly int ID_FocusRadius = Shader.PropertyToID("_FocusRadius");
    static readonly int ID_EdgeWidth   = Shader.PropertyToID("_EdgeWidth");

    void Awake()
    {
        _img = GetComponent<RawImage>();

        // 克隆一份材质，避免影响别的 UI
        if (_img.material != null)
        {
            _matInstance = Instantiate(_img.material);
        }
        else
        {
            _matInstance = new Material(Shader.Find("UI/HandPrivacyBlur"));
        }

        _img.material = _matInstance;
    }

    void OnDestroy()
    {
        if (_matInstance != null)
        {
            Destroy(_matInstance);
        }
    }

    void Update()
    {
        if (_matInstance == null) return;

        // 把 Inspector 里的参数实时塞进 Shader
        _matInstance.SetFloat(ID_BlurRadius,  blurRadius);
        _matInstance.SetFloat(ID_FocusRadius, focusRadius);
        _matInstance.SetFloat(ID_EdgeWidth,   focusEdge);

        // === 计算圆心位置（UV 坐标 0~1） ===
        Vector2 centerUV = new Vector2(0.5f, 0.5f); // 默认屏幕中心

        if (useHandCenter && recognizer != null)
        {
            var pts = recognizer.LastPoints;
            if (pts != null && pts.Length > 0)
            {
                // 简单取所有关键点的平均作为手中心
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < pts.Length; i++)
                    sum += pts[i];

                centerUV = sum / pts.Length;
            }
        }

        // 整体在竖直方向偏移一点，让圆更靠近脸
        centerUV.y = Mathf.Clamp01(centerUV.y + centerYOffset);

        _matInstance.SetVector(ID_FocusCenter,
            new Vector4(centerUV.x, centerUV.y, 0f, 0f));
    }
}
