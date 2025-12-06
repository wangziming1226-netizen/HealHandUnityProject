using UnityEngine;

/// <summary>
/// 这是一个“数据信使”类 (Singleton)。
/// 它的唯一职责是在场景切换时“存活”下来，并携带数据。
/// </summary>
public class GestureRecordingManager : MonoBehaviour
{
    // 1. 静态实例 (单例模式)
    public static GestureRecordingManager Instance { get; private set; }

    // 2. 这是我们要在场景间传递的数据
    public string TargetGestureId { get; set; }

    void Awake()
    {
        // 3. 标准的单例模式实现
        if (Instance == null)
        {
            // 如果这是第一个实例，将其设置为静态实例
            Instance = this;

            // 4. 【核心】告诉 Unity 在加载新场景时不要销毁这个 GameObject
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 如果一个实例已经存在 (例如返回主场景时)
            // 销毁这个重复的 GameObject
            Destroy(gameObject);
        }
    }
}