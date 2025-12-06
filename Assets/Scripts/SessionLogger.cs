using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class SessionLogger : MonoBehaviour
{
    [Header("References")]
    public QRToGestureLinker linker;
    public GestureJudge judge;

    [Header("Options")]
    public bool   autosaveEachSuccess = true;
    public string filePrefix = "handrehab";

    [Serializable]
    public class Row
    {
        public string time;
        public string card_id;
        public string gesture;
        public string difficulty;
        public float  hold_secs;
        public string device;
        public string platform;
        public string note; // 可用于 state_check 等扩展写入
    }

    readonly List<Row> rows = new();

    void Awake()
    {
#if UNITY_EDITOR
        ClearFlags(this); ClearFlags(linker); ClearFlags(judge);
        static void ClearFlags(UnityEngine.Object o){ if (o) o.hideFlags = HideFlags.None; }
#endif
        if (!linker) linker = FindFirstObjectByType<QRToGestureLinker>(FindObjectsInactive.Include);
        if (!judge)  judge  = FindFirstObjectByType<GestureJudge>(FindObjectsInactive.Include);
    }

    public void MarkSuccess()
    {
        rows.Add(new Row {
            time       = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            card_id    = linker ? (linker.lastCardId ?? "") : "",
            gesture    = linker ? (linker.targetGesture ?? judge?.targetGesture ?? "") : (judge?.targetGesture ?? ""),
            difficulty = linker ? (linker.lastDifficulty ?? "") : "",
            hold_secs  = judge  ? Mathf.Max(0, judge.requiredHold) : 0f,
            device     = SystemInfo.deviceModel,
            platform   = Application.platform.ToString(),
            note       = ""
        });

        Debug.Log($"[SessionLogger] +1 success (total {rows.Count})");
        if (autosaveEachSuccess) ExportCsvNow();
    }

    // 可供状态检测时写 “thumb_up/thumb_down/thumb_side”
    public void MarkStateCheck(string result)
    {
        rows.Add(new Row {
            time       = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            card_id    = linker ? (linker.lastCardId ?? "") : "",
            gesture    = "state_check",
            difficulty = linker ? (linker.lastDifficulty ?? "") : "",
            hold_secs  = 0f,
            device     = SystemInfo.deviceModel,
            platform   = Application.platform.ToString(),
            note       = result ?? ""
        });
        Debug.Log($"[SessionLogger] state_check = {result}");
        if (autosaveEachSuccess) ExportCsvNow();
    }

    public void ExportCsvNow()
    {
        if (rows.Count == 0) { Debug.LogWarning("[SessionLogger] No rows to export."); return; }

        var sb = new StringBuilder();
        sb.AppendLine("time,card_id,gesture,difficulty,hold_secs,device,platform,note");
        foreach (var r in rows)
        {
            string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", " ");
            sb.AppendLine($"{Esc(r.time)},{Esc(r.card_id)},{Esc(r.gesture)},{Esc(r.difficulty)},{r.hold_secs},{Esc(r.device)},{Esc(r.platform)},{Esc(r.note)}");
        }

        string dir  = Application.persistentDataPath;
        string name = $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[SessionLogger] CSV exported:\n{path}");
    }
}
