using UnityEngine;
using UnityEngine.Events;

public class GestureJudge : MonoBehaviour
{
    [Header("Sources")]
    public HandGestureRecognizer recognizer;

    [Header("Task")]
    [Tooltip("目标手势：open / fist / ok（小写）")]
    public string targetGesture = "open";   // ok / fist / open
    [Tooltip("需要保持的秒数")]
    public float requiredHold = 1.0f;

    [Header("Flow")]
    [Tooltip("是否必须经过二维码 Arm() 之后才开始判定")]
    public bool requireScan = true;

    [Header("Events")]
    public UnityEvent onSuccess;            // 成功回调
    public UnityEvent<float> onProgress;    // 进度 0~1

    // 运行时状态
    float timer;
    bool  armed;

    // 把字符串目标转换成枚举，方便跟识别器对齐
    HandGestureRecognizer.Gesture targetEnum = HandGestureRecognizer.Gesture.Open;

    void Awake()
    {
        if (!recognizer)
            recognizer = FindFirstObjectByType<HandGestureRecognizer>(FindObjectsInactive.Include);

        targetEnum = StringToEnum(targetGesture);
    }

    // 供 QRToGestureLinker 调用：武装本轮任务
    public void Arm(string gesture, float holdSeconds)
    {
        targetGesture = (gesture ?? "open").ToLowerInvariant();
        targetEnum    = StringToEnum(targetGesture);

        requiredHold = Mathf.Max(0.1f, holdSeconds);
        timer        = 0f;
        armed        = true;

        onProgress?.Invoke(0f);
    }

    // 旧接口：现在留空实现，防止 Inspector 里原有绑定报错
    public void OnOpen() { /* legacy, now unused */ }
    public void OnFist() { /* legacy, now unused */ }
    public void OnOK()   { /* legacy, now unused */ }

    void LateUpdate()
    {
        if (!recognizer) return;

        // 需要扫码但还没“武装”，直接等待
        if (requireScan && !armed)
        {
            timer = 0f;
            onProgress?.Invoke(0f);
            return;
        }

        // 使用识别器已经“稳定”后的结果
        var g = recognizer.CurrentGesture;

        // 同目标手势就累计，Unknown 就保持原样，其他手势清零
        if (g == targetEnum)
        {
            timer += Time.deltaTime;
            float p = Mathf.Clamp01(timer / Mathf.Max(0.0001f, requiredHold));
            onProgress?.Invoke(p);

            if (timer >= requiredHold)
            {
                onSuccess?.Invoke();
                timer = 0f;
                onProgress?.Invoke(0f);

                // 成功一次后如果需要扫码，则解除武装，等待下一次扫码
                if (requireScan) armed = false;
            }
        }
        else if (g == HandGestureRecognizer.Gesture.Unknown)
        {
            // Unknown：不加不减，让计时保留（防止抖一下就清零）
        }
        else
        {
            // 识别成了别的稳定手势（fist / ok / open 但不是我们要的），清零
            timer = 0f;
            onProgress?.Invoke(0f);
        }
    }

    HandGestureRecognizer.Gesture StringToEnum(string s)
    {
        switch ((s ?? "").ToLowerInvariant())
        {
            case "ok":   return HandGestureRecognizer.Gesture.OK;
            case "fist": return HandGestureRecognizer.Gesture.Fist;
            case "open": return HandGestureRecognizer.Gesture.Open;
            default:     return HandGestureRecognizer.Gesture.Unknown;
        }
    }
}
