using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

/// <summary>
/// 使用2D图片显示目标手势的视觉引导
/// 简单、高效、易于实现
/// </summary>
public class GestureGuideVisual : MonoBehaviour
{
    [Header("Hand Gesture Images")]
    [Tooltip("张手图片")]
    public Sprite openHandSprite;
    
    [Tooltip("握拳图片")]
    public Sprite fistSprite;
    
    [Tooltip("OK手势图片")]
    public Sprite okSprite;

    [Header("UI Components")]
    public Image handImage;              // 显示手势的Image组件
    public Image successGlow;            // 成功时的绿色光晕
    public TMP_Text instructionText;     // 指示文字
    public GameObject guidePanel;        // 整个引导面板

    [Header("Visual Settings")]
    [Tooltip("默认颜色（白色半透明）")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.8f);
    
    [Tooltip("成功时的颜色（绿色）")]
    public Color successColor = new Color(0.3f, 1f, 0.3f, 1f);
    
    [Tooltip("呼吸动画的速度")]
    [Range(0.5f, 2f)]
    public float pulseSpeed = 1f;
    
    [Tooltip("呼吸动画的缩放幅度")]
    [Range(1.0f, 1.3f)]
    public float pulseScale = 1.1f;

    private Coroutine pulseCoroutine;

    void Start()
    {
        HideGuide();
    }

    /// <summary>
    /// 显示目标手势的引导
    /// </summary>
    /// <param name="gesture">手势类型: "open", "fist", "ok"</param>
    public void ShowGuide(string gesture)
    {
        if (!guidePanel) return;
        
        guidePanel.SetActive(true);
        
        // 重置颜色
        if (handImage)
            handImage.color = normalColor;
        
        if (successGlow)
            successGlow.gameObject.SetActive(false);

        // 根据手势类型设置图片
        Sprite targetSprite = null;
        string instruction = "";

        switch (gesture.ToLower())
        {
            case "open":
                targetSprite = openHandSprite;
                instruction = "Open your hand like this";
                break;
            
            case "fist":
                targetSprite = fistSprite;
                instruction = "Make a fist like this";
                break;
            
            case "ok":
                targetSprite = okSprite;
                instruction = "Make OK gesture like this";
                break;
            
            default:
                Debug.LogWarning($"Unknown gesture: {gesture}");
                return;
        }

        if (handImage && targetSprite)
        {
            handImage.sprite = targetSprite;
            handImage.gameObject.SetActive(true);
        }

        if (instructionText)
            instructionText.text = instruction;

        // 开始呼吸动画
        StartPulse();
    }

    /// <summary>
    /// 显示成功反馈（变绿色 + 光晕）
    /// </summary>
    public void ShowSuccess()
    {
        if (handImage)
            handImage.color = successColor;

        if (successGlow)
        {
            successGlow.gameObject.SetActive(true);
            StartCoroutine(GlowEffect());
        }

        if (instructionText)
            instructionText.text = "Perfect! ✓";
    }

    /// <summary>
    /// 隐藏引导
    /// </summary>
    public void HideGuide()
    {
        if (guidePanel)
            guidePanel.SetActive(false);
        
        StopPulse();
    }

    /// <summary>
    /// 开始呼吸动画（缩放效果）
    /// </summary>
    void StartPulse()
    {
        StopPulse();
        if (handImage)
            pulseCoroutine = StartCoroutine(PulseAnimation());
    }

    /// <summary>
    /// 停止呼吸动画
    /// </summary>
    void StopPulse()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }

        if (handImage)
            handImage.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// 呼吸动画协程
    /// </summary>
    IEnumerator PulseAnimation()
    {
        float cycleDuration = 1f / pulseSpeed;
        
        while (handImage && handImage.gameObject.activeSelf)
        {
            // 放大阶段
            float elapsed = 0f;
            while (elapsed < cycleDuration / 2)
            {
                float t = elapsed / (cycleDuration / 2);
                float scale = Mathf.Lerp(1f, pulseScale, t);
                handImage.transform.localScale = Vector3.one * scale;
                
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 缩小阶段
            elapsed = 0f;
            while (elapsed < cycleDuration / 2)
            {
                float t = elapsed / (cycleDuration / 2);
                float scale = Mathf.Lerp(pulseScale, 1f, t);
                handImage.transform.localScale = Vector3.one * scale;
                
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }

    /// <summary>
    /// 成功光晕效果
    /// </summary>
    IEnumerator GlowEffect()
    {
        if (!successGlow) yield break;

        float duration = 0.5f;
        float elapsed = 0f;

        Color startColor = successGlow.color;
        startColor.a = 0f;
        successGlow.color = startColor;

        // 淡入
        while (elapsed < duration)
        {
            float alpha = Mathf.Lerp(0f, 0.5f, elapsed / duration);
            Color c = successGlow.color;
            c.a = alpha;
            successGlow.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.3f);

        // 淡出
        elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = Mathf.Lerp(0.5f, 0f, elapsed / duration);
            Color c = successGlow.color;
            c.a = alpha;
            successGlow.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        successGlow.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 编辑器中清除HideFlags，方便调试
        if (handImage) handImage.hideFlags = HideFlags.None;
        if (successGlow) successGlow.hideFlags = HideFlags.None;
        if (instructionText) instructionText.hideFlags = HideFlags.None;
        if (guidePanel) guidePanel.hideFlags = HideFlags.None;
    }
#endif
}