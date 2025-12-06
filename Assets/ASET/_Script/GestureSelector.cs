using UnityEngine;
using TMPro;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Threading;

public class GestureSelector : MonoBehaviour
{
    [Header("Gesture Selection Settings")]
    [Tooltip("手势持续多久才确认选择 (秒)")]
    public float selectionTimeThreshold = 2.0f;

    [Header("UI 引用")]
    public TextMeshProUGUI feedbackText;
    public Image cardModeImage;
    public Image randomModeImage;
    public ParticleSystem cardModeParticles;
    public ParticleSystem randomModeParticles;

    [Header("Config Panel UI")]
    [Tooltip("密码面板的 GameObject 引用")]
    public GameObject passwordPanel;
    [Tooltip("配置面板的 GameObject 引用")]
    public GameObject configPanel;

    [Header("Visual Effects Settings")]
    public float baseScale = 1.0f;
    public float maxScale = 1.15f;
    [Tooltip("视觉缩放的平滑速度")]
    public float scaleLerpSpeed = 10f;
    public Color cardModeGlowColor = Color.blue;
    public Color randomModeGlowColor = Color.red;
    public float maxEmissionRate = 50f;

    [Header("Gesture Detection Parameters")]
    [Tooltip("最小隔离距离：一个手指尖到其他指尖的平均距离必须超过此阈值，才算作隔离。")]
    public float isolationDistanceThreshold = 0.08f;
    [Tooltip("五个指尖的 Landmarker 索引")]
    private static readonly int[] FINGER_TIPS = { 4, 8, 12, 16, 20 };

    [Header("关联组件")]
    public Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner handLandmarkerRunner;

    // 内部状态
    private const string DefaultFeedbackText = "Waiting for hand detection...";
    private int _currentDetectedFingers = 0;
    private float _timeGestureStarted = 0.0f;
    private bool _selectionConfirmed = false;

    private float _cardModeTargetScale = 1.0f;
    private float _randomModeTargetScale = 1.0f;

    private SynchronizationContext _synchronizationContext;

    // **********************************************
    // ★★★ 核心修改：将引用检查移到 Awake() 中 ★★★
    // **********************************************
    void Awake()
    {
        // 捕获同步上下文
        _synchronizationContext = SynchronizationContext.Current;

        // 检查必要引用：文字 + Runner + 两个 Image
        if (feedbackText == null || handLandmarkerRunner == null ||
            cardModeImage == null || randomModeImage == null)
        {
            // 打印错误并禁用脚本
            Debug.LogError("GestureSelector: 必要引用未绑定（feedbackText / runner / 两个 Image）。禁用脚本。请检查 Inspector 绑定！");
            enabled = false;
        }
    }

    void Start()
    {
        // 如果 Awake 中发现引用缺失，则直接退出，不执行 Start 逻辑
        if (!enabled)
        {
            return;
        }

        // 粒子系统是“可选”的，没填就跳过相关设置
        if (cardModeParticles != null)
        {
            SetParticleShapeToRect(cardModeParticles, cardModeImage.rectTransform);
            SetParticleEmission(cardModeParticles, 0f);
        }
        if (randomModeParticles != null)
        {
            SetParticleShapeToRect(randomModeParticles, randomModeImage.rectTransform);
            SetParticleEmission(randomModeParticles, 0f);
        }

        // 初始化 Image
        cardModeImage.rectTransform.localScale = Vector3.one * baseScale;
        randomModeImage.rectTransform.localScale = Vector3.one * baseScale;
        cardModeImage.color = Color.white;
        randomModeImage.color = Color.white;

        // 订阅识别结果事件
        handLandmarkerRunner.OnHandLandmarksOutput.AddListener(OnHandLandmarksDetected);
        feedbackText.text = DefaultFeedbackText;
    }
    // **********************************************

    void OnDestroy()
    {
        if (handLandmarkerRunner != null)
        {
            handLandmarkerRunner.OnHandLandmarksOutput.RemoveListener(OnHandLandmarksDetected);
        }
    }

    void Update()
    {
        // 缩放插值
        if (cardModeImage != null)
        {
            cardModeImage.rectTransform.localScale = Vector3.Lerp(
                cardModeImage.rectTransform.localScale,
                Vector3.one * _cardModeTargetScale,
                Time.deltaTime * scaleLerpSpeed);
        }

        if (randomModeImage != null)
        {
            randomModeImage.rectTransform.localScale = Vector3.Lerp(
                randomModeImage.rectTransform.localScale,
                Vector3.one * _randomModeTargetScale,
                Time.deltaTime * scaleLerpSpeed);
        }

        // 粒子根据缩放进度调整（如果有的话）
        float maxDeltaScale = maxScale - baseScale;
        if (cardModeParticles != null && cardModeImage != null)
        {
            float cardProgress = Mathf.Clamp01(
                (cardModeImage.rectTransform.localScale.x - baseScale) / maxDeltaScale);
            UpdateParticlesVisuals(cardModeParticles, cardProgress);
        }
        if (randomModeParticles != null && randomModeImage != null)
        {
            float randomProgress = Mathf.Clamp01(
                (randomModeImage.rectTransform.localScale.x - baseScale) / maxDeltaScale);
            UpdateParticlesVisuals(randomModeParticles, randomProgress);
        }
    }

    private void OnHandLandmarksDetected(HandLandmarkerResult result)
    {
        if (SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ => ProcessHandLandmarks(result), null);
        }
        else
        {
            ProcessHandLandmarks(result);
        }
    }

    private void ProcessHandLandmarks(HandLandmarkerResult result)
    {
        if (_selectionConfirmed) return;

        if (passwordPanel != null && passwordPanel.activeInHierarchy)
        {
            feedbackText.text = "Please input PIN code";
            ResetGestureState();
            return;
        }
        if (configPanel != null && configPanel.activeInHierarchy)
        {
            feedbackText.text = "Config";
            ResetGestureState();
            return;
        }

        int detectedFingers = 0;
        if (result.handLandmarks != null && result.handLandmarks.Count > 0)
        {
            var handLandmarks = result.handLandmarks[0];

            bool isFiveFingersUp = CountFingersUp(handLandmarks) == 5;
            bool isOneIsolated = IsOneFingerIsolated(handLandmarks);

            if (isFiveFingersUp) detectedFingers = 5;
            else if (isOneIsolated) detectedFingers = 1;
        }

        if (detectedFingers != 0)
        {
            if (detectedFingers != _currentDetectedFingers)
            {
                ResetTargetScale(_currentDetectedFingers);
                _currentDetectedFingers = detectedFingers;
                _timeGestureStarted = Time.time;
                UpdateFeedbackText(detectedFingers);
            }

            SetTargetScale(_currentDetectedFingers, maxScale);

            float elapsed = Time.time - _timeGestureStarted;
            if (elapsed >= selectionTimeThreshold)
            {
                ConfirmModeSelection(_currentDetectedFingers);
            }
        }
        else
        {
            if (_currentDetectedFingers != 0)
            {
                SetTargetScale(_currentDetectedFingers, baseScale);
            }
            _currentDetectedFingers = 0;
            _timeGestureStarted = 0.0f;
            UpdateFeedbackText(0);
        }
    }

    private int CountFingersUp(NormalizedLandmarks landmarks)
    {
        if (landmarks.landmarks.Count == 0) return 0;

        int count = 0;
        if (landmarks.landmarks[4].y < landmarks.landmarks[2].y) count++;

        int[] tipIds = { 8, 12, 16, 20 };
        int[] mcpIds = { 5, 9, 13, 17 };

        for (int i = 0; i < tipIds.Length; i++)
        {
            if (landmarks.landmarks[tipIds[i]].y < landmarks.landmarks[mcpIds[i]].y)
                count++;
        }
        return count;
    }

    private bool IsOneFingerIsolated(NormalizedLandmarks landmarks)
    {
        if (landmarks.landmarks.Count < 21) return false;

        foreach (int currentTipId in FINGER_TIPS)
        {
            Vector3 currentTipPos = new Vector3(
                landmarks.landmarks[currentTipId].x,
                landmarks.landmarks[currentTipId].y,
                landmarks.landmarks[currentTipId].z);

            float sumDistance = 0f;
            int otherFingersCount = 0;

            foreach (int otherTipId in FINGER_TIPS)
            {
                if (currentTipId == otherTipId) continue;

                Vector3 otherTipPos = new Vector3(
                    landmarks.landmarks[otherTipId].x,
                    landmarks.landmarks[otherTipId].y,
                    landmarks.landmarks[otherTipId].z);

                sumDistance += Vector3.Distance(currentTipPos, otherTipPos);
                otherFingersCount++;
            }

            if (otherFingersCount > 0)
            {
                float averageDistance = sumDistance / otherFingersCount;
                if (averageDistance > isolationDistanceThreshold)
                {
                    int mcpIndex = GetMcpIndexForTip(currentTipId);
                    if (mcpIndex != -1)
                    {
                        if (landmarks.landmarks[currentTipId].y <
                            landmarks.landmarks[mcpIndex].y)
                            return true;
                    }
                    else return true;
                }
            }
        }
        return false;
    }

    private int GetMcpIndexForTip(int tipId)
    {
        switch (tipId)
        {
            case 4: return 2;
            case 8: return 5;
            case 12: return 9;
            case 16: return 13;
            case 20: return 17;
            default: return -1;
        }
    }

    private void ResetGestureState()
    {
        if (_currentDetectedFingers != 0)
        {
            SetTargetScale(_currentDetectedFingers, baseScale);
            _currentDetectedFingers = 0;
            _timeGestureStarted = 0.0f;
        }
    }

    private void UpdateFeedbackText(int fingersUp)
    {
        Color displayColor;
        if (fingersUp == 1)
        {
            feedbackText.text = "Hand Recognized: ONE finger (Card Mode)";
            displayColor = cardModeGlowColor;
        }
        else if (fingersUp == 5)
        {
            feedbackText.text = "Hand Recognized: FIVE fingers (Random Mode)";
            displayColor = randomModeGlowColor;
        }
        else
        {
            feedbackText.text = DefaultFeedbackText;
            displayColor = Color.white;
        }
        feedbackText.color = displayColor;
    }

    private void UpdateParticlesVisuals(ParticleSystem ps, float progress)
    {
        if (ps == null) return;

        if (progress > 0.01f)
        {
            float currentEmissionRate = Mathf.Lerp(0f, maxEmissionRate, progress);
            SetParticleEmission(ps, currentEmissionRate);
        }
        else
        {
            SetParticleEmission(ps, 0f);
        }
    }

    private void SetTargetScale(int fingersUp, float targetScale)
    {
        if (fingersUp == 1) _cardModeTargetScale = targetScale;
        else if (fingersUp == 5) _randomModeTargetScale = targetScale;
    }

    private void ResetTargetScale(int fingersUp)
    {
        if (fingersUp == 1) _cardModeTargetScale = baseScale;
        else if (fingersUp == 5) _randomModeTargetScale = baseScale;
    }

    private void SetParticleEmission(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var emission = ps.emission;
        emission.rateOverTime = rate;
    }

    public void SetParticleShapeToRect(ParticleSystem ps, RectTransform rectTransform)
    {
        if (ps == null || rectTransform == null) return;

        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        float width = Vector3.Distance(corners[1], corners[2]);
        float height = Vector3.Distance(corners[2], corners[3]);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;

        const float overflowMargin = 0.05f;
        ps.transform.localScale = Vector3.one;
        shape.scale = new Vector3(width + overflowMargin,
                                  height + overflowMargin,
                                  0.01f);
    }

    private void ConfirmModeSelection(int fingersUp)
    {
        if (_selectionConfirmed) return;
        _selectionConfirmed = true;

        if (fingersUp == 1)
        {
            feedbackText.text = "MODE SELECTED: CARD MODE! Loading...";
            feedbackText.color = Color.green;
            StartCoroutine(LoadSceneWithDelay("CardModeScene", 1.0f));
        }
        else if (fingersUp == 5)
        {
            feedbackText.text = "MODE SELECTED: RANDOM MODE! Loading...";
            feedbackText.color = Color.green;
            StartCoroutine(LoadSceneWithDelay("RandomModeScene", 1.0f));
        }
    }

    private System.Collections.IEnumerator LoadSceneWithDelay(string sceneName, float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(sceneName);
    }
}