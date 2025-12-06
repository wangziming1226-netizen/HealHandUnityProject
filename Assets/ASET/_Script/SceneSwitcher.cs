using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 负责处理所有场景跳转逻辑，使用单例模式确保跨场景持久化。
/// 挂载到初始场景中的一个全局管理器对象上。
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    // 单例模式的静态引用
    public static SceneSwitcher Instance { get; private set; }

    void Awake()
    {
        // 检查实例是否已存在
        if (Instance == null)
        {
            // 如果不存在，将当前对象设置为实例
            Instance = this;

            // 关键：防止此对象在加载新场景时被销毁
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 如果场景中已经存在一个实例，则销毁当前的这个，以保证唯一性
            Destroy(gameObject);
        }
    }

    // ===========================================
    // 跳转方法
    // ===========================================

    /// <summary>
    /// 绑定到 Record 按钮，跳转到手势列表界面。
    /// </summary>
    public void LoadGestureListScene()
    {
        SceneManager.LoadScene("GestureListScene");
        Debug.Log("SceneSwitcher: 正在加载手势列表场景 (GestureListScene)...");
    }

    /// <summary>
    /// 绑定到手势列表界面的录制按钮，跳转到 MediaPipe 捕获界面。
    /// </summary>
    public void LoadGestureCaptureScene()
    {
        SceneManager.LoadScene("GestureCaptureScene");
        Debug.Log("SceneSwitcher: 正在加载手势录制场景 (GestureCaptureScene)...");
    }

    // ===========================================
    // 返回方法
    // ===========================================

    /// <summary>
    /// 绑定到 GestureListScene 中的 “Return” 或 “Back” 按钮，返回主界面。
    /// </summary>
    public void ReturnToInitialScene()
    {
        // **请将 "YourInitialConfigSceneName" 替换为您实际的初始配置场景名称**
        SceneManager.LoadScene("YourInitialConfigSceneName");
        Debug.Log("SceneSwitcher: 正在返回初始配置场景...");
    }

    /// <summary>
    /// 在 GestureCaptureScene 完成记录或取消后，返回到手势列表界面。
    /// </summary>
    public void ReturnToGestureListScene()
    {
        SceneManager.LoadScene("GestureListScene");
        Debug.Log("SceneSwitcher: 正在返回手势列表场景...");
    }
}