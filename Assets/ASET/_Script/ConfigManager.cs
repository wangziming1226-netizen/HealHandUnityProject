using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

public class ConfigManager : MonoBehaviour
{
    // ===================================
    // 1. 音频设置
    // ===================================
    [Header("Audio Settings")]
    [SerializeField] private AudioSource sceneAudioSource;
    [SerializeField] private AudioClip voiceClip;
    [SerializeField] private float loopInterval = 5.0f;

    private float audioTimer = 0f;
    private bool isAudioPausedByUI = false;

    // ===================================
    // 2. 基础 UI 引用 (密码 & 主面板)
    // ===================================
    [Header("Main UI References")]
    [Tooltip("密码输入面板")]
    public GameObject passwordPanel;
    public TMP_InputField passwordInputField;

    [Tooltip("配置设置面板 (灰色主面板)")]
    public GameObject configPanel;

    [Header("Numpad Display")]
    public TextMeshProUGUI passwordDisplay;
    public int maxPasswordLength = 8;

    // ===================================
    // 3. 动态配置生成设置
    // ===================================
    [Header("Config Generation")]
    [Tooltip("生成的配置项放在这个父物体下 (ScrollView Content)")]
    public Transform configListContainer;

    [Tooltip("配置项预制件 (挂载 ConfigItemUI)")]
    public GameObject configItemPrefab;

    private List<ConfigItemUI> _spawnedItems = new List<ConfigItemUI>();

    // ===================================
    // 4. 文件浏览器设置
    // ===================================
    [Header("File Browser Settings")]
    [Tooltip("文件选择面板 (默认隐藏)")]
    public GameObject fileBrowserPanel;

    [Tooltip("文件列表容器 (ScrollView Content)")]
    public Transform fileListContainer;

    [Tooltip("文件按钮预制件 (需包含 Button 和 TextMeshProUGUI)")]
    public GameObject fileButtonPrefab;

    [Header("Buttons (Auto Bind)")]
    [Tooltip("左侧的 Load 按钮")]
    public Button openFileBrowserButton;

    [Tooltip("文件面板右上角的 关闭 按钮")]
    public Button closeFileBrowserButton;

    // 数据文件夹路径: Assets/ASET/data
    private string ExternalDataPath
    {
        get
        {
#if UNITY_EDITOR
            // 电脑编辑器里：为了方便你调试，依然存在 Assets 文件夹下
            return Path.Combine(Application.dataPath, "ASET/data");
#else
        // iOS/Android 真机里：存入沙盒文档目录 (可读可写)
        // 路径类似: /var/mobile/Containers/Data/Application/xxxx/Documents/SavedData
        return Path.Combine(Application.persistentDataPath, "SavedData"); 
#endif
        }
    }

    // ===================================
    // 5. 【新增】数据可视化引用
    // ===================================
    [Header("Data Visualization")]
    [Tooltip("拖入挂载了 ChartPanelController 的面板物体")]
    public ChartPanelController chartController;

    // ===================================
    // 6. 核心数据
    // ===================================
    [Header("Configuration Data")]
    public string configFileName = "game_settings.json";
    private string _defaultSavePath;
    private const string DefaultPassword = "999";

    [HideInInspector]
    public GameConfig CurrentConfig;

    // ===================================
    // Unity 生命周期
    // ===================================

    void Awake()
    {
        _defaultSavePath = Path.Combine(Application.persistentDataPath, configFileName);

        LoadConfig(); // 加载默认配置

        // 音频初始化
        if (sceneAudioSource == null)
        {
            sceneAudioSource = GetComponent<AudioSource>();
            if (sceneAudioSource == null) sceneAudioSource = gameObject.AddComponent<AudioSource>();
        }

        sceneAudioSource.clip = voiceClip;
        sceneAudioSource.playOnAwake = false;
        sceneAudioSource.loop = false;
        sceneAudioSource.spatialBlend = 0.0f;
        audioTimer = loopInterval;
    }

    void Start()
    {
        // 自动绑定 UI 按钮事件
        if (openFileBrowserButton != null)
        {
            openFileBrowserButton.onClick.RemoveAllListeners();
            openFileBrowserButton.onClick.AddListener(OpenFileBrowser);
        }

        if (closeFileBrowserButton != null)
        {
            closeFileBrowserButton.onClick.RemoveAllListeners();
            closeFileBrowserButton.onClick.AddListener(CloseFileBrowser);
        }
    }

    void Update()
    {
        HandleAudioLoop();
    }

    // ===================================
    // 音频循环逻辑
    // ===================================
    private void HandleAudioLoop()
    {
        if (sceneAudioSource == null || voiceClip == null) return;

        // 检查是否有任意面板打开 (包括 Chart 面板)
        bool isChartOpen = chartController != null && chartController.gameObject.activeInHierarchy;
        bool panelsAreOpen =
            (passwordPanel != null && passwordPanel.activeInHierarchy) ||
            (configPanel != null && configPanel.activeInHierarchy) ||
            (fileBrowserPanel != null && fileBrowserPanel.activeInHierarchy) ||
            isChartOpen;

        if (panelsAreOpen)
        {
            if (sceneAudioSource.isPlaying)
            {
                sceneAudioSource.Pause();
                isAudioPausedByUI = true;
            }
            return;
        }
        else
        {
            if (isAudioPausedByUI)
            {
                sceneAudioSource.UnPause();
                isAudioPausedByUI = false;
            }
        }

        audioTimer += Time.deltaTime;
        if (audioTimer >= loopInterval)
        {
            audioTimer = 0f;
            sceneAudioSource.Play();
        }
    }

    // ===================================
    // 密码面板逻辑
    // ===================================
    public void OpenPasswordPanel()
    {
        if (passwordPanel != null)
        {
            passwordPanel.SetActive(true);
            if (configPanel != null) configPanel.SetActive(false);
            if (fileBrowserPanel != null) fileBrowserPanel.SetActive(false);
            if (chartController != null) chartController.CloseChart();

            if (passwordInputField != null) passwordInputField.text = "";
            UpdatePasswordDisplay();
        }
    }

    public void ClosePasswordPanel()
    {
        if (passwordPanel != null) passwordPanel.SetActive(false);
    }

    public void VerifyPasswordAndOpenConfig()
    {
        if (passwordInputField == null) return;

        if (passwordInputField.text == DefaultPassword)
        {
            ClosePasswordPanel();
            if (configPanel != null)
            {
                configPanel.SetActive(true);
                PopulateConfigInputs(); // 生成 UI
            }
        }
        else
        {
            passwordInputField.text = "";
            UpdatePasswordDisplay();
        }
    }

    public void CloseConfigPanel()
    {
        if (configPanel != null) configPanel.SetActive(false);
    }

    public void NumpadInput(int digit)
    {
        if (passwordInputField == null) return;
        if (passwordInputField.text.Length >= maxPasswordLength) return;
        passwordInputField.text += digit.ToString();
        UpdatePasswordDisplay();
    }

    public void NumpadBackspace()
    {
        if (passwordInputField == null) return;
        if (passwordInputField.text.Length > 0)
            passwordInputField.text = passwordInputField.text.Substring(0, passwordInputField.text.Length - 1);
        UpdatePasswordDisplay();
    }

    private void UpdatePasswordDisplay()
    {
        if (passwordInputField != null && passwordDisplay != null)
        {
            passwordDisplay.text = new string('*', passwordInputField.text.Length);
        }
    }

    // ===================================
    // 存储与加载逻辑 (GameConfig)
    // ===================================
    private void LoadConfig()
    {
        if (File.Exists(_defaultSavePath))
        {
            try
            {
                string json = File.ReadAllText(_defaultSavePath);
                CurrentConfig = JsonUtility.FromJson<GameConfig>(json);
            }
            catch (System.Exception)
            {
                CurrentConfig = GameConfig.GetDefaultConfig();
                SaveConfigInternal();
            }
        }
        else
        {
            CurrentConfig = GameConfig.GetDefaultConfig();
            SaveConfigInternal();
        }
    }

    public void SaveConfig()
    {
        UpdateConfigFromInputs();
        SaveConfigInternal();
        CloseConfigPanel();
    }

    private void SaveConfigInternal()
    {
        string json = JsonUtility.ToJson(CurrentConfig, true);
        try
        {
            File.WriteAllText(_defaultSavePath, json);
            Debug.Log($"Config saved to: {_defaultSavePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save config: {e}");
        }
    }

    // ===================================
    // 动态 UI 生成 (反射)
    // ===================================
    private void PopulateConfigInputs()
    {
        foreach (Transform child in configListContainer) Destroy(child.gameObject);
        _spawnedItems.Clear();

        if (CurrentConfig == null) return;

        FieldInfo[] fields = typeof(GameConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            GameObject newItemObj = Instantiate(configItemPrefab, configListContainer);
            newItemObj.transform.localScale = Vector3.one;

            ConfigItemUI itemUI = newItemObj.GetComponent<ConfigItemUI>();
            if (itemUI != null)
            {
                itemUI.Setup(field, CurrentConfig);
                _spawnedItems.Add(itemUI);
            }
        }
    }

    private void UpdateConfigFromInputs()
    {
        foreach (var item in _spawnedItems) item.ApplyValue();
    }

    // ===================================
    // 文件浏览器逻辑
    // ===================================

    public void OpenFileBrowser()
    {
        if (fileBrowserPanel != null)
        {
            fileBrowserPanel.SetActive(true);
            RefreshFileList();
        }
    }

    public void CloseFileBrowser()
    {
        if (fileBrowserPanel != null)
        {
            fileBrowserPanel.SetActive(false);
        }
    }

    private void RefreshFileList()
    {
        foreach (Transform child in fileListContainer) Destroy(child.gameObject);

        string folderPath = ExternalDataPath;
        if (!Directory.Exists(folderPath))
        {
            try { Directory.CreateDirectory(folderPath); }
            catch { return; }
        }

        string[] files = Directory.GetFiles(folderPath, "*.json");
        if (files.Length == 0) return;

        foreach (string filePath in files)
        {
            GameObject btnObj = Instantiate(fileButtonPrefab, fileListContainer);
            btnObj.transform.localScale = Vector3.one;

            string fileName = Path.GetFileName(filePath);
            TextMeshProUGUI txt = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = fileName;

            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                string pathRef = filePath;
                btn.onClick.AddListener(() => OnFileSelected(pathRef));
            }
        }
    }

    /// <summary>
    /// 【核心修改】点击文件后的分流处理：是配置还是数据？
    /// </summary>
    private void OnFileSelected(string fullPath)
    {
        Debug.Log($"Selected file: {fullPath}");

        try
        {
            string json = File.ReadAllText(fullPath);

            // 1. 尝试检测是否为 Session 数据 (用于画图)
            if (json.Contains("SessionStartTime") && json.Contains("Rounds"))
            {
                SessionData sessionData = JsonUtility.FromJson<SessionData>(json);
                if (sessionData != null)
                {
                    OpenChartPanel(sessionData); // 如果是数据，打开图表
                    return;
                }
            }

            // 2. 尝试检测是否为 GameConfig (用于设置)
            GameConfig newConfig = JsonUtility.FromJson<GameConfig>(json);
            if (newConfig != null)
            {
                CurrentConfig = newConfig;
                PopulateConfigInputs(); // 刷新设置 UI
                CloseFileBrowser();     // 仅在加载设置时才关闭列表
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"文件解析失败: {e.Message}");
        }
    }

    // ===================================
    // 【新增】图表交互逻辑
    // ===================================

    private void OpenChartPanel(SessionData data)
    {
        // 1. 暂时隐藏文件列表
        if (fileBrowserPanel != null) fileBrowserPanel.SetActive(false);

        // 2. 显示图表
        if (chartController != null)
        {
            chartController.ShowChart(data);

            // 3. 绑定图表背景点击事件 (点击任意位置返回列表)
            // 获取或添加 Button 组件到 ChartPanel 的根物体上
            Button bgButton = chartController.GetComponent<Button>();
            if (bgButton == null) bgButton = chartController.gameObject.AddComponent<Button>();

            // 设置 Transition 为 None 防止闪烁
            bgButton.transition = Selectable.Transition.None;

            bgButton.onClick.RemoveAllListeners();
            bgButton.onClick.AddListener(CloseChartAndShowList);
        }
    }

    /// <summary>
    /// 关闭图表并重新显示文件列表
    /// </summary>
    private void CloseChartAndShowList()
    {
        if (chartController != null) chartController.CloseChart();

        OpenFileBrowser(); // 重新打开文件列表供用户选择其他文件
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (gameObject != null && (gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0)
        {
            gameObject.hideFlags &= ~HideFlags.DontSaveInEditor;
        }
    }
#endif
}