using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using System.Linq;

/// <summary>
/// 管理手势列表 UI，处理手势回放（加载截图和骨架），并启动录制场景。
/// </summary>
public class GestureListManager : MonoBehaviour
{
    [Header("UI References")]
    public Transform contentParent;
    public GameObject gestureItemPrefab;
    public Button returnButton;

    [Header("Playback Overlay")]
    public GameObject playbackOverlayPanel;
    public TextMeshProUGUI playbackStatusText;
    public Transform visualizationParent;
    [Tooltip("用于在回放界面显示截图的 Image 组件")]
    public Image playbackScreenshotImage;
    [Tooltip("用于绘制骨架连线的 LineRenderer 模板")]
    public LineRenderer lineRendererTemplate;

    [Header("Bone Points Settings (Playback)")]
    [Tooltip("用于表示骨架点的 GameObject 模板 (与 CaptureScene 相同)")]
    public GameObject bonePointTemplate;
    public float bonePointSize = 0.08f;
    public Color bonePointColor = Color.yellow; // 颜色应由预制件材质设置

    [Header("Scene Configuration")]
    public string initialSceneName = "main";
    public string captureSceneName = "GestureCaptureScene";

    [Header("Data Storage Configuration")]
    [Tooltip("截图存储的子路径，对应 Application.dataPath/ASET/record")]
    public string recordSubPath = "/ASET/record";

    [Header("Initial Gesture Data")]
    public List<GestureData> initialGestures;

    // 默认不清理数据
    [Header("Debug Settings")]
    [Tooltip("【重要】如果为 True，将在启动时删除所有在 'record' 文件夹中的已保存手势。")]
    public bool clearRecordFolderOnStart = false; // 默认不清理


    // 【核心参数】Z 轴深度参数，与 GestureCaptureManager 保持一致
    private const float Z_DEPTH_SCALE = -10f;
    private const float CANVAS_PLANE_DISTANCE = 100f;


    // (CONNECTIONS 保持不变)
    private static readonly int[,] CONNECTIONS = new int[,]
    {
        {0, 1}, {1, 2}, {2, 3}, {3, 4}, // Thumb
        {0, 5}, {5, 6}, {6, 7}, {7, 8}, // Index
        {9, 10}, {10, 11}, {11, 12}, // Middle
        {0, 13}, {13, 14}, {14, 15}, {15, 16}, // Ring
        {0, 17}, {17, 18}, {18, 19}, {19, 20}, // Pinky
        {5, 9}, {9, 13}, {13, 17} // Palm
    };

    void Start()
    {
        // (Start 函数保持不变)
        if (contentParent == null || gestureItemPrefab == null || playbackOverlayPanel == null ||
            lineRendererTemplate == null || playbackScreenshotImage == null || bonePointTemplate == null)
        {
            Debug.LogError("GestureListManager: Core references are missing! Check ... or bonePointTemplate.");
            enabled = false;
            return;
        }

        SetupReturnButton();
        playbackOverlayPanel.SetActive(false);
        LoadAndDisplayGestures();
    }

    void Update()
    {
        // (Update 函数保持不变)
        if (playbackOverlayPanel.activeInHierarchy && Input.GetMouseButtonDown(0))
        {
            HidePlayback();
        }
    }

    // ===========================================
    // Scene Navigation & UI Binding 
    // ===========================================

    private void LoadCaptureScene()
    {
        SceneManager.LoadScene(captureSceneName);
    }

    private void ReturnToInitialScene()
    {
        SceneManager.LoadScene(initialSceneName);
    }

    private void SetupReturnButton()
    {
        if (returnButton != null)
        {
            returnButton.onClick.AddListener(ReturnToInitialScene);
        }
    }

    private void LoadAndDisplayGestures()
    {
        if (clearRecordFolderOnStart)
        {
            ClearAllRecordData();
        }

        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        List<GestureData> allGestures = initialGestures;
        for (int i = 0; i < allGestures.Count; i++)
        {
            GestureData data = allGestures[i];
            GameObject itemGO = Instantiate(gestureItemPrefab, contentParent);
            SetupGestureItem(itemGO, data);
        }
    }

    private void SetupGestureItem(GameObject itemGO, GestureData data)
    {
        // (SetupGestureItem 函数保持不变)
        TextMeshProUGUI nameText = itemGO.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = data.displayName;
        }
        Image iconImage = itemGO.transform.Find("IconImage")?.GetComponent<Image>();
        if (iconImage != null && data.iconSprite != null)
        {
            iconImage.sprite = data.iconSprite;
        }
        Button recordButton = itemGO.transform.Find("ControlsPanel/RecordButton")?.GetComponent<Button>();
        Button playButton = itemGO.transform.Find("ControlsPanel/PlayButton")?.GetComponent<Button>();

        if (recordButton != null)
        {
            recordButton.onClick.AddListener(() => OnRecordButtonClicked(data.gestureId));
        }

        bool isRecorded = CheckGestureRecorded(data.gestureId);

        if (playButton != null)
        {
            playButton.onClick.AddListener(() => OnPlayButtonClicked(data.gestureId, recordButton));
            playButton.gameObject.SetActive(isRecorded);
            if (isRecorded)
            {
                Image playButtonBackground = playButton.GetComponent<Image>();
                if (playButtonBackground != null)
                {
                    Color c = playButtonBackground.color;
                    c.a = 0f;
                    playButtonBackground.color = c;
                }
            }
        }
    }

    private void OnRecordButtonClicked(string gestureId)
    {
        // (OnRecordButtonClicked 函数保持不变)
        Debug.Log($"Preparing to record gesture: {gestureId}");
        if (GestureRecordingManager.Instance != null)
        {
            GestureRecordingManager.Instance.TargetGestureId = gestureId;
        }
        else
        {
            Debug.LogError("GestureRecordingManager.Instance is null! Make sure it exists in your list scene.");
            return;
        }
        LoadCaptureScene();
    }

    private void OnPlayButtonClicked(string gestureId, Button recordButton)
    {
        // (OnPlayButtonClicked 函数保持不变)
        Debug.Log($"Preparing to play back gesture: {gestureId}");

        if (!CheckGestureRecorded(gestureId))
        {
            Debug.LogWarning($"Playback failed: Data for {gestureId} not found. Play button is already disabled.");
            return;
        }

        if (LoadGestureData(gestureId, out SerializableGestureData data))
        {
            playbackOverlayPanel.SetActive(true);
            playbackStatusText.text = $"Playing back: {gestureId}. Click anywhere to return.";
            VisualizeLandmarks(data.Landmarks);
            if (!string.IsNullOrEmpty(data.ScreenshotFileName) && data.ScreenshotFileName != "N/A" && playbackScreenshotImage != null)
            {
                StartCoroutine(LoadImageFromFile(data.ScreenshotFileName));
            }
            else if (playbackScreenshotImage != null)
            {
                playbackScreenshotImage.sprite = null;
            }
        }
        else
        {
            Debug.LogWarning($"Playback failed: Data for {gestureId} file was found but load failed.");
        }
    }

    // ===========================================
    // Playback Logic & Utilities
    // ===========================================

    private void ClearAllRecordData()
    {
        // (ClearAllRecordData 函数保持不变)
        try
        {
            string path = Application.dataPath + recordSubPath;
            if (!Directory.Exists(path))
            {
                Debug.LogWarning($"[Debug] Record folder not found at: {path}");
                return;
            }

            Debug.LogWarning($"[Debug] Clearing record folder: {path}");

            string[] jsonFiles = Directory.GetFiles(path, "gesture_*.json");
            foreach (string filePath in jsonFiles)
            {
                File.Delete(filePath);
                Debug.Log($"[Debug] Deleted: {filePath}");
            }

            string[] pngFiles = Directory.GetFiles(path, "screenshot_*.png");
            foreach (string filePath in pngFiles)
            {
                File.Delete(filePath);
                Debug.Log($"[Debug] Deleted: {filePath}");
            }
            Debug.LogWarning("[Debug] Record folder cleared.");

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Debug] Error clearing record folder: {e.Message}");
        }
    }

    private void HidePlayback()
    {
        // (HidePlayback 函数保持不变)
        playbackOverlayPanel.SetActive(false);
        ClearVisualization();
        if (playbackScreenshotImage != null)
        {
            if (playbackScreenshotImage.sprite != null)
            {
                if (playbackScreenshotImage.sprite.texture != null)
                {
                    Destroy(playbackScreenshotImage.sprite.texture);
                }
                Destroy(playbackScreenshotImage.sprite);
            }
            playbackScreenshotImage.sprite = null;
        }
    }

    private void ClearVisualization()
    {
        // (ClearVisualization 函数保持不变)
        foreach (Transform child in visualizationParent)
        {
            Destroy(child.gameObject);
        }
    }

    private bool CheckGestureRecorded(string id)
    {
        // (CheckGestureRecorded 函数保持不变)
        // 只检查 Assets/ASET/record 路径
        string path = Path.Combine(Application.dataPath, recordSubPath.TrimStart('/'), $"gesture_{id}.json");
        if (File.Exists(path)) return true;

        return false;
    }

    private IEnumerator LoadImageFromFile(string fileName)
    {
        // (LoadImageFromFile 函数保持不变)
        string folderPath = Application.dataPath + recordSubPath;
        string path = Path.Combine(folderPath, fileName);
        string uri = "file:///" + path;
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(uri))
        {
            yield return uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Failed to load image from path {path}: {uwr.error}");
                playbackScreenshotImage.sprite = null;
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                Sprite sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f, 100f);
                playbackScreenshotImage.sprite = sprite;
            }
        }
    }

    private bool LoadGestureData(string id, out SerializableGestureData data)
    {
        // (LoadGestureData 函数保持不变)
        data = null;
        string assetPath = Path.Combine(Application.dataPath, recordSubPath.TrimStart('/'), $"gesture_{id}.json");

        if (File.Exists(assetPath))
        {
            try
            {
                string json = File.ReadAllText(assetPath);
                data = JsonUtility.FromJson<SerializableGestureData>(json);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load data from Asset path for {id}: {e}");
                return false;
            }
        }
        return false;
    }

    // ===========================================
    // 【核心修复】VisualizeLandmarks (已修复 Z 轴深度和尺寸)
    // ===========================================
    private void VisualizeLandmarks(List<Vector3> landmarks)
    {
        ClearVisualization();
        Camera cam = Camera.main;

        // 核心深度参数
        const float baseBoneZDistance = CANVAS_PLANE_DISTANCE - 1f; // 99f
        float lineThickness = 0.6f;
        float pointSize = 5.8f;

        // 1. 绘制线条
        for (int i = 0; i < CONNECTIONS.GetLength(0); i++)
        {
            int startIndex = CONNECTIONS[i, 0];
            int endIndex = CONNECTIONS[i, 1];
            if (startIndex >= landmarks.Count || endIndex >= landmarks.Count) continue;

            Vector3 startNormalized = landmarks[startIndex];
            Vector3 endNormalized = landmarks[endIndex];

            // 【关键修复】引入 Z 轴深度调整
            float startZ = baseBoneZDistance + (startNormalized.z * Z_DEPTH_SCALE);
            float endZ = baseBoneZDistance + (endNormalized.z * Z_DEPTH_SCALE);

            Vector3 startViewPort = new Vector3(startNormalized.x, 1f - startNormalized.y, startZ);
            Vector3 endViewPort = new Vector3(endNormalized.x, 1f - endNormalized.y, endZ);

            Vector3 startWorld = cam.ViewportToWorldPoint(startViewPort);
            Vector3 endWorld = cam.ViewportToWorldPoint(endViewPort);

            LineRenderer lrInstance = Instantiate(lineRendererTemplate, visualizationParent); // <<<--- BOLDED CODE START: 实例化 LineRenderer
            lrInstance.gameObject.SetActive(true);
            lrInstance.positionCount = 2;
            lrInstance.SetPosition(0, startWorld);
            lrInstance.SetPosition(1, endWorld);

            lrInstance.startWidth = lineThickness; // <<<--- BOLDED CODE START: 设置线条宽度
            lrInstance.endWidth = lineThickness; // <<<--- BOLDED CODE START: 设置线条宽度
            lrInstance.startColor = Color.cyan;
            lrInstance.endColor = Color.cyan;
        }

        // 2. 绘制点
        for (int i = 0; i < landmarks.Count; i++)
        {
            Vector3 pointNormalized = landmarks[i];

            // 【关键修复】引入 Z 轴深度调整
            float pointZ = baseBoneZDistance + (pointNormalized.z * Z_DEPTH_SCALE);

            Vector3 pointViewPort = new Vector3(pointNormalized.x, 1f - pointNormalized.y, pointZ);
            Vector3 pointWorld = cam.ViewportToWorldPoint(pointViewPort);

            GameObject pointInstance = Instantiate(bonePointTemplate, visualizationParent); // <<<--- BOLDED CODE START: 实例化 Point
            pointInstance.transform.position = pointWorld;
            pointInstance.transform.localScale = Vector3.one * pointSize; // <<<--- BOLDED CODE START: 设置点大小

            Renderer pointRenderer = pointInstance.GetComponent<Renderer>();
            if (pointRenderer != null && pointRenderer.material != null)
            {
                pointRenderer.material.color = bonePointColor; // <<<--- BOLDED CODE END: 设置点颜色
            }
        }
    }
}