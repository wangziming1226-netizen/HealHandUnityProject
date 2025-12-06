using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine.SceneManagement;
using Mediapipe.Tasks.Vision.HandLandmarker;
using System.IO;
using Mediapipe.Unity.Sample;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class SerializableGestureData
{
    public string GestureId;
    public List<Vector3> Landmarks;
    public string ScreenshotFileName;
}

public class GestureCaptureManager : MonoBehaviour
{
    public Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner handLandmarkerRunner;

    [Header("UI References")]
    public TextMeshProUGUI gestureNameText;
    public Button captureButton;
    public Button returnButton;
    [Tooltip("包含实时摄像头画面的父对象 (例如 Container Panel)")]
    public GameObject liveFeedContainer;

    [Header("Confirmation Panel")]
    [Tooltip("用于显示冻结帧的 RawImage，现在也是 Save/Retry 按钮的父对象")]
    public RawImage freezeFrameImage;
    public Button saveButton;
    public Button retryButton;
    public Transform visualizationParent;
    public LineRenderer lineRendererTemplate;

    [Header("Bone Points Settings")]
    [Tooltip("用于表示骨架点的 GameObject 模板 (例如一个小的球体或方块)")]
    public GameObject bonePointTemplate;
    public float bonePointSize = 0.08f;
    public Color bonePointColor = Color.yellow;

    [Header("Scene Configuration")]
    public string listSceneName = "GestureListScene";

    // 注意：在真机上我们会忽略这个路径，使用沙盒路径
    [Header("Data Storage")]
    public string recordSubPath = "/ASET/record";

    // ===========================================
    // 【核心修改】跨平台存储路径逻辑
    // ===========================================
    private string SaveFolderPath
    {
        get
        {
#if UNITY_EDITOR
            // 电脑编辑器：存到 Assets/ASET/record (方便调试)
            // 确保 recordSubPath 以 / 开头或处理路径拼接
            string subPath = recordSubPath.StartsWith("/") ? recordSubPath : "/" + recordSubPath;
            return Application.dataPath + subPath;
#else
            // iOS/Android 真机：存到沙盒文档目录 (可读可写)
            return Path.Combine(Application.persistentDataPath, "SavedRecordings");
#endif
        }
    }

    // --- 私有变量 ---
    private string _targetGestureId;
    private List<Mediapipe.Tasks.Components.Containers.NormalizedLandmarks> _lastDetectedLandmarksList;
    private Mediapipe.Unity.ImageSource imageSource;

    private volatile bool _isHandDetected = false;
    private volatile bool _newDataAvailable = false;

    private string _currentScreenshotFileName;
    private List<Vector3> _currentCapturedVectorLandmarks;
    private Mediapipe.Tasks.Components.Containers.NormalizedLandmarks _currentCapturedLandmarks;
    private Texture2D _staticFreezeTexture;

    // Z 轴深度比例因子
    private const float Z_DEPTH_SCALE = -10f;
    private const float CANVAS_PLANE_DISTANCE = 100f;

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
        if (gestureNameText == null || handLandmarkerRunner == null || saveButton == null || retryButton == null ||
            freezeFrameImage == null || visualizationParent == null || lineRendererTemplate == null ||
            liveFeedContainer == null || bonePointTemplate == null)
        {
            Debug.LogError("GestureCaptureManager: Core references are not bound! Check bonePointTemplate.");
            enabled = false;
            return;
        }

        imageSource = ImageSourceProvider.ImageSource;
        if (imageSource == null) Debug.LogError("ImageSource is null!");

        if (GestureRecordingManager.Instance != null)
        {
            _targetGestureId = GestureRecordingManager.Instance.TargetGestureId;
        }
        else
        {
            Debug.LogError("GestureRecordingManager.Instance is null. Please run from GestureListScene.");
            _targetGestureId = "ERROR_ID_NOT_FOUND";
        }

        handLandmarkerRunner.OnHandLandmarksOutput.AddListener(OnHandLandmarksDetected);
        captureButton.onClick.AddListener(OnCaptureButtonClicked);
        returnButton.onClick.AddListener(ReturnToGestureListScene);
        saveButton.onClick.AddListener(OnSaveButtonClicked);
        retryButton.onClick.AddListener(OnRetryButtonClicked);

        freezeFrameImage.gameObject.SetActive(false);
        liveFeedContainer.SetActive(true);
        UpdateGestureNameText(false, false);
    }

    void Update()
    {
        if (_newDataAvailable)
        {
            _newDataAvailable = false;
            if (liveFeedContainer.activeInHierarchy)
            {
                if (_isHandDetected)
                {
                    UpdateGestureNameText(true, false);
                }
                else
                {
                    UpdateGestureNameText(false, false);
                }
            }
        }
    }

    void OnDestroy()
    {
        if (handLandmarkerRunner != null)
        {
            handLandmarkerRunner.OnHandLandmarksOutput.RemoveListener(OnHandLandmarksDetected);
        }

        if (_staticFreezeTexture != null)
        {
            Destroy(_staticFreezeTexture);
        }
    }

    private void OnHandLandmarksDetected(HandLandmarkerResult result)
    {
        if (result.handLandmarks != null && result.handLandmarks.Count > 0)
        {
            _lastDetectedLandmarksList = result.handLandmarks;
            _isHandDetected = true;
        }
        else
        {
            _lastDetectedLandmarksList = null;
            _isHandDetected = false;
        }
        _newDataAvailable = true;
    }

    private void OnCaptureButtonClicked()
    {
        if (!_isHandDetected || _lastDetectedLandmarksList == null || _lastDetectedLandmarksList.Count == 0)
        {
            Debug.LogWarning("No hand detected, cannot capture data!");
            UpdateGestureNameText(false, true);
            return;
        }

        handLandmarkerRunner.enabled = false;
        captureButton.interactable = false;
        returnButton.interactable = false;
        liveFeedContainer.SetActive(false);

        _currentCapturedLandmarks = _lastDetectedLandmarksList[0];
        _currentCapturedVectorLandmarks = _currentCapturedLandmarks.landmarks
            .Select(lm => new Vector3(lm.x, lm.y, lm.z))
            .ToList();

        Texture liveTexture = imageSource.GetCurrentTexture();
        if (liveTexture != null)
        {
            _staticFreezeTexture = ConvertToTexture2D(liveTexture);
            freezeFrameImage.texture = _staticFreezeTexture;
        }

        freezeFrameImage.gameObject.SetActive(true);
        VisualizeLandmarks(_currentCapturedVectorLandmarks);
        _currentScreenshotFileName = $"screenshot_{_targetGestureId}.png";
        UpdateGestureNameText(true, false, true);
    }

    private void OnSaveButtonClicked()
    {
        saveButton.interactable = false;
        retryButton.interactable = false;
        StartCoroutine(ProcessCaptureAndReturn());
    }

    private void OnRetryButtonClicked()
    {
        _currentCapturedLandmarks = default;
        _currentCapturedVectorLandmarks = null;
        _currentScreenshotFileName = null;

        if (_staticFreezeTexture != null)
        {
            Destroy(_staticFreezeTexture);
            _staticFreezeTexture = null;
        }

        freezeFrameImage.gameObject.SetActive(false);
        ClearVisualization();

        captureButton.interactable = true;
        returnButton.interactable = true;
        handLandmarkerRunner.enabled = true;
        liveFeedContainer.SetActive(true);

        UpdateGestureNameText(_isHandDetected, false);
    }

    private void ReturnToGestureListScene()
    {
        SceneManager.LoadScene(listSceneName);
    }

    private void SaveDataAndScreenshot(List<Vector3> landmarks, string id, string screenshotFileName, Texture2D textureToSave)
    {
        bool screenshotSuccess = SaveCameraFrame(screenshotFileName, textureToSave);
        if (!screenshotSuccess)
        {
            Debug.LogError("Camera frame capture failed, saving keypoints only.");
            screenshotFileName = "N/A";
        }
        string jsonData = SerializeVectorLandmarks(landmarks, id, screenshotFileName);
        SaveGestureData(jsonData, id);
        Debug.Log("Data and Screenshot save confirmed.");
    }

    // ===========================================
    // 文件保存逻辑 (已修改为支持 iOS)
    // ===========================================
    private Texture2D FlipTextureHorizontally(Texture2D original)
    {
        Color[] pixels = original.GetPixels();
        Color[] flippedPixels = new Color[pixels.Length];
        int width = original.width;
        int height = original.height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                flippedPixels[(y * width) + x] = pixels[(y * width) + (width - 1 - x)];
            }
        }
        Texture2D flippedTex = new Texture2D(width, height);
        flippedTex.SetPixels(flippedPixels);
        flippedTex.Apply();
        return flippedTex;
    }

    private bool SaveCameraFrame(string fileName, Texture2D tex2D)
    {
        if (tex2D == null)
        {
            Debug.LogError("Texture2D to save is null.");
            return false;
        }

        // 【修改】使用兼容 iOS 的路径
        string folderPath = SaveFolderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string path = Path.Combine(folderPath, fileName);
        Texture2D flippedTexture = FlipTextureHorizontally(tex2D);
        try
        {
            byte[] bytes = flippedTexture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            Debug.Log($"Camera frame saved: {path}");
            Destroy(flippedTexture);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save camera frame: {e}");
            if (flippedTexture != null)
            {
                Destroy(flippedTexture);
            }
            return false;
        }
    }

    private void SaveGestureData(string jsonData, string id)
    {
        // 【修改】使用兼容 iOS 的路径
        string folderPath = SaveFolderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string path = Path.Combine(folderPath, $"gesture_{id}.json");
        try
        {
            File.WriteAllText(path, jsonData);
            Debug.Log($"Gesture data saved: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save gesture data: {e}");
        }
    }

    private Texture2D ConvertToTexture2D(Texture source)
    {
        if (source == null) return null;
        Texture2D tex2D = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        tex2D.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
        tex2D.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return tex2D;
    }

    // ===========================================
    // 骨架绘制逻辑
    // ===========================================
    private void ClearVisualization()
    {
        foreach (Transform child in visualizationParent)
        {
            Destroy(child.gameObject);
        }
    }

    private void VisualizeLandmarks(List<Vector3> landmarks)
    {
        ClearVisualization();
        Camera cam = Camera.main;

        const float baseBoneZDistance = CANVAS_PLANE_DISTANCE - 1f;
        float lineThickness = 0.8f;

        // 1. 绘制线条
        for (int i = 0; i < CONNECTIONS.GetLength(0); i++)
        {
            int startIndex = CONNECTIONS[i, 0];
            int endIndex = CONNECTIONS[i, 1];
            if (startIndex >= landmarks.Count || endIndex >= landmarks.Count) continue;

            Vector3 startNormalized = landmarks[startIndex];
            Vector3 endNormalized = landmarks[endIndex];

            float startZ = baseBoneZDistance + (startNormalized.z * Z_DEPTH_SCALE);
            float endZ = baseBoneZDistance + (endNormalized.z * Z_DEPTH_SCALE);

            Vector3 startViewPort = new Vector3(startNormalized.x, 1f - startNormalized.y, startZ);
            Vector3 endViewPort = new Vector3(endNormalized.x, 1f - endNormalized.y, endZ);

            Vector3 startWorld = cam.ViewportToWorldPoint(startViewPort);
            Vector3 endWorld = cam.ViewportToWorldPoint(endViewPort);

            LineRenderer lrInstance = Instantiate(lineRendererTemplate, visualizationParent);
            lrInstance.gameObject.SetActive(true);
            lrInstance.positionCount = 2;
            lrInstance.SetPosition(0, startWorld);
            lrInstance.SetPosition(1, endWorld);

            lrInstance.startWidth = lineThickness;
            lrInstance.endWidth = lineThickness;
            lrInstance.startColor = Color.cyan;
            lrInstance.endColor = Color.cyan;
        }

        // 2. 绘制点
        for (int i = 0; i < landmarks.Count; i++)
        {
            Vector3 pointNormalized = landmarks[i];
            float pointZ = baseBoneZDistance + (pointNormalized.z * Z_DEPTH_SCALE);

            Vector3 pointViewPort = new Vector3(pointNormalized.x, 1f - pointNormalized.y, pointZ);
            Vector3 pointWorld = cam.ViewportToWorldPoint(pointViewPort);

            GameObject pointInstance = Instantiate(bonePointTemplate, visualizationParent);
            pointInstance.transform.position = pointWorld;
            pointInstance.transform.localScale = Vector3.one * bonePointSize;

            Renderer pointRenderer = pointInstance.GetComponent<Renderer>();
            if (pointRenderer != null && pointRenderer.material != null)
            {
                pointRenderer.material.color = bonePointColor;
            }
        }
    }

    private void UpdateGestureNameText(bool isHandDetected, bool isWarning, bool isSaving = false)
    {
        if (string.IsNullOrEmpty(_targetGestureId) || _targetGestureId == "ERROR_ID_NOT_FOUND")
        {
            gestureNameText.text = "Error: Target Gesture ID is missing!";
            gestureNameText.color = Color.red;
            return;
        }
        if (isSaving)
        {
            gestureNameText.text = $"Reviewing captured frame for: {_targetGestureId}";
            gestureNameText.color = Color.yellow;
        }
        else if (isWarning)
        {
            if (!_isHandDetected)
            {
                gestureNameText.text = $"!! WARNING !! Hand not detected. Show hand to record {_targetGestureId}";
            }
            else
            {
                gestureNameText.text = $"!! WARNING !! Click RECORD button ONLY when hand is steady.";
            }
            gestureNameText.color = Color.yellow;
        }
        else if (isHandDetected)
        {
            gestureNameText.text = $"Tracking: {_targetGestureId}";
            gestureNameText.color = Color.white;
        }
        else
        {
            gestureNameText.text = $"No hand detected. Ready to record: {_targetGestureId} (Show hand)";
            gestureNameText.color = Color.gray;
        }
    }

    private string SerializeVectorLandmarks(List<Vector3> landmarks, string id, string screenshotFileName)
    {
        var serializableData = new SerializableGestureData
        {
            GestureId = id,
            Landmarks = landmarks,
            ScreenshotFileName = screenshotFileName
        };
        return JsonUtility.ToJson(serializableData, true);
    }

    private IEnumerator ProcessCaptureAndReturn()
    {
        freezeFrameImage.gameObject.SetActive(false);
        UpdateGestureNameText(false, false, true);

        SaveDataAndScreenshot(_currentCapturedVectorLandmarks, _targetGestureId, _currentScreenshotFileName, _staticFreezeTexture);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
        yield return new WaitForSeconds(0.5f);

        ClearVisualization();
        if (_staticFreezeTexture != null)
        {
            Destroy(_staticFreezeTexture);
            _staticFreezeTexture = null;
        }

        ReturnToGestureListScene();
    }
}