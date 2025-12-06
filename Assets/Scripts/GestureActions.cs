using UnityEngine;
using TMPro;
using System.Collections;

public class GestureActions : MonoBehaviour
{
    [Header("要控制的对象/组件")]
    public GameObject qrScannerRoot;   // 扫码整体物体（或摄像头/面板）
    public AudioSource sfxOK;          // OK 提示音（可选）
    public AudioSource sfxFist;        // Fist 提示音（可选）
    public TMP_Text toast;             // 临时提示文字（可选）

    [Header("效果参数")]
    public float cooldown = 0.6f;      // 事件冷却，防止频繁触发
    public float toastSecs = 0.8f;     // 提示停留时长

    float _last;

    bool Ready() => Time.time - _last >= cooldown;

    public void OnOpenHand()
    {
        if (!Ready()) return;
        _last = Time.time;

        if (qrScannerRoot) qrScannerRoot.SetActive(true);
        if (toast) StartCoroutine(ShowToast("Scanner ON"));
    }

    public void OnFist()
    {
        if (!Ready()) return;
        _last = Time.time;

        if (qrScannerRoot) qrScannerRoot.SetActive(false);
        if (sfxFist) sfxFist.Play();
        if (toast) StartCoroutine(ShowToast("Scanner OFF"));
    }

    public void OnOK()
    {
        if (!Ready()) return;
        _last = Time.time;

        if (sfxOK) sfxOK.Play();
        if (toast) StartCoroutine(ShowToast("OK!"));
        // TODO: 在这里做“确认/下一步”的业务，例如提交、截图、跳场景等
    }

    IEnumerator ShowToast(string msg)
    {
        toast.gameObject.SetActive(true);
        toast.text = msg;
        yield return new WaitForSeconds(toastSecs);
        toast.gameObject.SetActive(false);
    }
}
