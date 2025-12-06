using UnityEngine;
using Mediapipe.Unity.Sample.HandLandmarkDetection;

/// <summary>
/// CardModeScene初始化器 - 确保干净的摄像头环境
/// 添加到CardModeScene中，执行顺序要早于其他脚本
/// </summary>
[DefaultExecutionOrder(-100)]
public class CardModeSceneInitializer : MonoBehaviour
{
    [Header("Debug")]
    public bool showDebugInfo = true;

    void Awake()
    {
        Debug.Log("========== CardModeScene Initializer ==========");
        
        // 1. 查找所有 HandLandmarkerRunner
        var allRunners = FindObjectsOfType<HandLandmarkerRunner>(true);
        Debug.Log($"Found {allRunners.Length} HandLandmarkerRunner instances");
        
        int disabledCount = 0;
        foreach (var runner in allRunners)
        {
            // 检查是否来自main场景（DontDestroyOnLoad的对象）
            if (runner.gameObject.scene.name != gameObject.scene.name)
            {
                Debug.Log($"Disabling old HandLandmarkerRunner from scene: {runner.gameObject.scene.name}");
                runner.enabled = false;
                disabledCount++;
                
                // 可选：完全销毁
                // Destroy(runner.gameObject);
            }
            else
            {
                Debug.Log($"Keeping HandLandmarkerRunner in current scene: {runner.gameObject.name}");
            }
        }
        
        Debug.Log($"Disabled {disabledCount} old HandLandmarkerRunner instances");
        
        // 2. 检查可用摄像头
        Debug.Log($"Available cameras: {WebCamTexture.devices.Length}");
        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            var cam = WebCamTexture.devices[i];
            Debug.Log($"  [{i}] {cam.name}, Front={cam.isFrontFacing}");
        }
        
        // 3. 查找QRScanner并确认设置
        var qrScanner = FindFirstObjectByType<QRScanner>();
        if (qrScanner != null)
        {
            Debug.Log($"QRScanner found: {qrScanner.gameObject.name}");
            
            // 使用反射检查Camera Preference
            var type = qrScanner.GetType();
            var field = type.GetField("cameraPreference");
            if (field != null)
            {
                var value = field.GetValue(qrScanner);
                Debug.Log($"  Camera Preference: {value}");
                
                // 如果不是Front，强制设置
                if (value.ToString() != "Front")
                {
                    Debug.LogWarning($"  ⚠️ Camera Preference is {value}, forcing to Front!");
                    field.SetValue(qrScanner, 1); // 1 = Front
                }
            }
        }
        else
        {
            Debug.LogWarning("QRScanner not found in scene!");
        }
        
        Debug.Log("========================================");
    }
}