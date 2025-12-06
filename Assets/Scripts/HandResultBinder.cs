using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;

public class HandResultBinder : MonoBehaviour
{
    [Header("Source (Solution 上的 Runner)")]
    public HandLandmarkerRunner runner;

    [Header("Target (GestureRecognizer 上的脚本)")]
    public HandGestureRecognizer recognizer;

    // --- 主线程派发用 ---
    private readonly object _lockObj = new object();
    private Vector2[] _pendingPts;   // 等待在主线程喂给 recognizer 的结果
    private bool _hasPending;

    void OnEnable()
    {
        if (!runner) runner = FindFirstObjectByType<HandLandmarkerRunner>(FindObjectsInactive.Include);
        if (!recognizer) recognizer = FindFirstObjectByType<HandGestureRecognizer>(FindObjectsInactive.Include);

        if (runner != null) runner.OnResults += HandleResults;   // 回调（可能在子线程）
        else Debug.LogWarning("[Binder] 找不到 HandLandmarkerRunner（Solution 上的 Runner）");
    }

    void OnDisable()
    {
        if (runner != null) runner.OnResults -= HandleResults;
    }

    // ↓ 这个函数可能不在主线程里被调用！不要直接触碰任何 Unity 对象（Text、GameObject、isActiveAndEnabled 等）
    private void HandleResults(HandLandmarkerResult result)
    {
        // 只做“数据转换”，不要访问 Unity 组件
        var pts = BuildPoints(result);   // null 表示没有手

        lock (_lockObj)
        {
            _pendingPts = pts;   // 覆盖成最新一帧（防堆积）
            _hasPending = true;
        }
    }

    // 主线程：把 pending 的结果喂给 recognizer（这里才可以安全地改 UI）
    void Update()
    {
        if (!_hasPending) return;

        Vector2[] pts = null;
        lock (_lockObj)
        {
            if (_hasPending)
            {
                pts = _pendingPts;
                _pendingPts = null;
                _hasPending = false;
            }
        }

        if (!recognizer) return;
        // recognizer.Feed() 里可以改 TMP 文本、触发 UnityEvent 等
        recognizer.Feed(pts);
    }

    // 把 HandLandmarkerResult 解析成 Vector2[]（屏幕归一化 0-1）
    private Vector2[] BuildPoints(HandLandmarkerResult result)
    {
        if (ReferenceEquals(result, null)) return null;

        var allHands = TryGetLandmarks(result, out string debugNote);
        if (allHands == null || allHands.Count == 0 || allHands[0] == null || allHands[0].Count == 0)
        {
            if (!string.IsNullOrEmpty(debugNote))
                Debug.Log($"[Binder] 没拿到 landmarks。{debugNote}");
            return null;
        }

        var first = allHands[0];
        int n = Math.Min(21, first.Count);
        var pts = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            var lm = first[i];
            if (TryGetXY(lm, out float x, out float y))
                pts[i] = new Vector2(x, y);
            else
                pts[i] = Vector2.zero;
        }
        return pts;
    }

    // 兼容不同版本的 result，尽量把它还原成 IList<IList<NormalizedLandmark>>
    private IList<IList<NormalizedLandmark>> TryGetLandmarks(HandLandmarkerResult result, out string debugNote)
    {
        debugNote = null;
        object value = null;
        var t = result.GetType();

        string[] names = { "Landmarks", "landmarks", "HandLandmarks", "handLandmarks" };
        foreach (var name in names)
        {
            var p = t.GetProperty(name);
            if (p != null) { value = p.GetValue(result); break; }
            var f = t.GetField(name);
            if (f != null) { value = f.GetValue(result); break; }
        }

        if (value == null)
        {
            var props = t.GetProperties().Select(p => p.Name);
            var fields = t.GetFields().Select(f => f.Name);
            debugNote = $"可用属性: [{string.Join(", ", props)}]；可用字段: [{string.Join(", ", fields)}]";

            foreach (var p in t.GetProperties())
                if (TryUnwrap(p.GetValue(result), out var list)) return list;
            foreach (var f in t.GetFields())
                if (TryUnwrap(f.GetValue(result), out var list)) return list;

            return null;
        }

        if (TryUnwrap(value, out var unwrapped)) return unwrapped;

        debugNote = $"拿到的 {value.GetType().Name} 不是 landmarks 列表";
        return null;
    }

    // 尝试把各种包装类型拆成 IList<IList<NormalizedLandmark>>
    private bool TryUnwrap(object obj, out IList<IList<NormalizedLandmark>> result)
    {
        result = null;
        if (obj == null) return false;

        if (obj is IList<IList<NormalizedLandmark>> direct)
        {
            result = direct; return true;
        }

        if (obj is System.Collections.IList outer && outer.Count > 0)
        {
            var list = new List<IList<NormalizedLandmark>>();
            foreach (var item in outer)
            {
                if (item == null) continue;
                var it = item.GetType();

                var p = it.GetProperty("Landmark") ?? it.GetProperty("Landmarks") ??
                        it.GetProperty("landmark") ?? it.GetProperty("landmarks");
                var f = it.GetField("Landmark") ?? it.GetField("Landmarks") ??
                        it.GetField("landmark") ?? it.GetField("landmarks");

                object inner = null;
                if (p != null) inner = p.GetValue(item);
                else if (f != null) inner = f.GetValue(item);

                if (inner is IList<NormalizedLandmark> lmList)
                    list.Add(lmList);
                else if (inner is System.Collections.IList asIList && asIList.Count > 0 && asIList[0] is NormalizedLandmark)
                    list.Add(asIList.Cast<NormalizedLandmark>().ToList());
            }
            if (list.Count > 0) { result = list; return true; }
        }

        return false;
    }

    private bool TryGetXY(NormalizedLandmark lm, out float x, out float y)
    {
        x = y = 0f;
        if (lm == null) return false;

        var t = lm.GetType();

        var px = t.GetProperty("X") ?? t.GetProperty("x");
        var py = t.GetProperty("Y") ?? t.GetProperty("y");
        if (px != null && py != null)
        {
            x = Convert.ToSingle(px.GetValue(lm));
            y = Convert.ToSingle(py.GetValue(lm));
            return true;
        }

        var fx = t.GetField("X") ?? t.GetField("x");
        var fy = t.GetField("Y") ?? t.GetField("y");
        if (fx != null && fy != null)
        {
            x = Convert.ToSingle(fx.GetValue(lm));
            y = Convert.ToSingle(fy.GetValue(lm));
            return true;
        }

        return false;
    }
}
