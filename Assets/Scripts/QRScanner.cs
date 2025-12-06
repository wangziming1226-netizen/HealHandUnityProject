using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using ZXing;
using ZXing.Common;

[DisallowMultipleComponent]
public class QRScanner : MonoBehaviour
{
    [Header("UI")]
    public RawImage preview;          // 建议直接拖 Annotatable Screen 的 RawImage
    public AspectRatioFitter fitter;
    public TMP_Text statusText;

    [Header("Scan")]
    [Range(0.1f, 1f)] public float scanInterval = 0.3f;
    public bool openUrlIfLink = true;
    public bool autoRestartOnTap = true;
    public AudioClip beep;

    [Header("Events")]
    public UnityEvent<string> onDecoded;

    [Header("Camera Source (from Mediapipe)")]
    [Tooltip("拖 Annotatable Screen 上的 CameraTextureTap")]
    public CameraTextureTap cameraTap;    // ✅ 不再自己开 WebCam，只从这里拿纹理

    BarcodeReaderGeneric reader;
    bool  scanning = true;
    float nextScan;

    // CPU 侧中转纹理（用于把 RenderTexture / 外部纹理拷到可读 Texture2D）
    Texture2D scratchTex;
    byte[]    rgbaBuffer;

    [Serializable]
    public class CardConfig
    {
        public string card_id, gesture, difficulty;
        public float hold_secs;
    }

    void Start()
    {
        Application.targetFrameRate = 60;

        // 没拖的话，尝试自动找一个
        if (!cameraTap)
            cameraTap = FindFirstObjectByType<CameraTextureTap>(FindObjectsInactive.Include);

        reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                TryHarder   = true,
                TryInverted = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
            }
        };

        if (fitter == null && preview != null)
            fitter = preview.GetComponent<AspectRatioFitter>();

        if (statusText) statusText.text = "Scan a QR card to start";
    }

    void Update()
    {
        // 还没找到视频源：尝试再找一次，然后直接返回
        if (!cameraTap)
        {
            cameraTap = FindFirstObjectByType<CameraTextureTap>(FindObjectsInactive.Include);
            if (!cameraTap)
            {
                if (statusText) statusText.text = "Waiting for camera…";
                return;
            }
        }

        Texture src = cameraTap.CurrentTexture;
        if (!src) return;

        // UI 适配比例（Mediapipe 那边已经处理好旋转/镜像了，这里只管宽高比）
        if (fitter)
        {
            float w = src.width;
            float h = src.height;
            if (w > 0 && h > 0)
                fitter.aspectRatio = w / h;
        }

        // 点击重启下一次扫码
        if (autoRestartOnTap && Input.GetMouseButtonDown(0))
            RestartScan();

        // 按固定时间间隔做一次解码
        if (scanning && Time.time >= nextScan)
        {
            nextScan = Time.time + scanInterval;
            TryDecodeFromTexture(src);
        }
    }

    // 从任意 Texture（Texture2D / RenderTexture / WebCamTexture）解码 QR
    void TryDecodeFromTexture(Texture src)
    {
        if (!src) return;

        int w = src.width;
        int h = src.height;
        if (w <= 0 || h <= 0) return;

        // 1) 先把源纹理拷成 CPU 可读的 RGBA32 Texture2D
        Texture2D cpuTex = EnsureScratchTexture(w, h);

        if (src is Texture2D tex2D)
        {
            // 源本身就是 Texture2D，直接拷贝像素
            var pixels = tex2D.GetPixels32();
            cpuTex.SetPixels32(pixels);
            cpuTex.Apply(false);
        }
        else if (src is RenderTexture rt)
        {
            // 源是 RenderTexture：从 GPU 拷一帧下来
            RenderTexture current = RenderTexture.active;
            RenderTexture.active = rt;
            cpuTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            cpuTex.Apply(false);
            RenderTexture.active = current;
        }
        else if (src is WebCamTexture wct)
        {
            // 理论上不会再走到这里，但以防万一
            var pixels = wct.GetPixels32();
            cpuTex.SetPixels32(pixels);
            cpuTex.Apply(false);
        }
        else
        {
            // 其它类型先不支持
            return;
        }

        // 2) 把 Texture2D 的像素转成 ZXing 需要的 RGBA byte[]
        var colors = cpuTex.GetPixels32();
        if (colors == null || colors.Length == 0) return;

        int need = colors.Length * 4;
        if (rgbaBuffer == null || rgbaBuffer.Length != need)
            rgbaBuffer = new byte[need];

        for (int i = 0, j = 0; i < colors.Length; i++)
        {
            Color32 c = colors[i];
            rgbaBuffer[j++] = c.r;
            rgbaBuffer[j++] = c.g;
            rgbaBuffer[j++] = c.b;
            rgbaBuffer[j++] = c.a;
        }

        var result = reader.Decode(
            rgbaBuffer,
            w,
            h,
            RGBLuminanceSource.BitmapFormat.RGBA32
        );

        if (result == null) return;

        // ✅ 扫码成功
        scanning = false;

        if (beep) AudioSource.PlayClipAtPoint(beep, Vector3.zero, 0.6f);
        Handheld.Vibrate();

        string txt = result.Text ?? string.Empty;

        // 把原始字符串发给后续逻辑（例如 QRToGestureLinker）
        onDecoded?.Invoke(txt);

        // 在 UI 上显示一条简短提示
        ShowDecodedSummary(txt);

        // 如果是链接，按设置打开浏览器
        if (openUrlIfLink && Uri.IsWellFormedUriString(txt, UriKind.Absolute))
        {
            Application.OpenURL(txt);
            return;
        }
    }

    Texture2D EnsureScratchTexture(int w, int h)
    {
        if (scratchTex == null || scratchTex.width != w || scratchTex.height != h)
        {
            if (scratchTex != null)
                Destroy(scratchTex);

            scratchTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            scratchTex.wrapMode = TextureWrapMode.Clamp;
            scratchTex.filterMode = FilterMode.Bilinear;
        }
        return scratchTex;
    }

    /// <summary>
    /// 把扫码结果转成 1–2 行的英文提示；优先解析我们自己的卡片 JSON。
    /// </summary>
    void ShowDecodedSummary(string txt)
    {
        if (!statusText) return;

        // 先尝试按我们的卡片 JSON 解析
        try
        {
            var cfg = JsonUtility.FromJson<CardConfig>(txt);
            if (!string.IsNullOrEmpty(cfg?.card_id))
            {
                string g = string.IsNullOrEmpty(cfg.gesture) ? "gesture" : cfg.gesture;
                statusText.text = $"Card {cfg.card_id}  •  {g}  •  {cfg.hold_secs:0.0} s";
                return;
            }
        }
        catch
        {
            // 不是我们格式的 JSON，就当普通字符串处理
        }

        // 再看看是不是 URL
        if (Uri.IsWellFormedUriString(txt, UriKind.Absolute))
        {
            statusText.text = "Opening link…";
        }
        else
        {
            statusText.text = "QR code scanned";
        }
    }

    public void RestartScan()
    {
        scanning = true;
        if (statusText) statusText.text = "Scan the next card";
    }

    void OnDestroy()
    {
        if (scratchTex != null)
            Destroy(scratchTex);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ClearHideFlags(this);
        ClearHideFlags(preview);
        ClearHideFlags(statusText);
        ClearHideFlags(fitter);
        ClearHideFlags(cameraTap);

        if (fitter == null && preview != null)
            fitter = preview.GetComponent<AspectRatioFitter>();
    }

    static void ClearHideFlags(UnityEngine.Object o)
    {
        if (o != null) o.hideFlags = HideFlags.None;
    }
#endif
}
