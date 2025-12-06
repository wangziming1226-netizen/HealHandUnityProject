using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class ChartPanelController : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI statsText;
    public RectTransform graphContainer;
    public GameObject panelRoot;

    [Header("Prefabs")]
    public GameObject circlePrefab;
    public GameObject linePrefab;
    public GameObject labelPrefab;

    [Header("Settings")]
    public Color lineColor = Color.green;
    // public Color pointColor = Color.white; // 【修改1】注释掉：不再由脚本控制点颜色，改由Prefab控制
    public float lineThickness = 3f;

    private List<GameObject> _spawnedObjects = new List<GameObject>();

    public void ShowChart(SessionData data)
    {
        panelRoot.SetActive(true);
        ClearGraph();

        if (data == null || data.Rounds == null || data.Rounds.Count == 0)
        {
            if (statsText) statsText.text = "No data available.";
            return;
        }

        UpdateStats(data);
        DrawGraph(data.Rounds);
    }

    public void CloseChart()
    {
        panelRoot.SetActive(false);
    }

    private void UpdateStats(SessionData data)
    {
        if (statsText == null) return;

        float avgScore = data.Rounds.Average(r => r.FinalScore);
        float avgTime = data.Rounds.Average(r => r.TimeTaken);
        int totalRounds = data.Rounds.Count;

        statsText.text = $"<b>Session Date:</b> {data.SessionStartTime}\n" +
                         $"<b>Total Rounds:</b> {totalRounds}   " +
                         $"<b>Avg Score:</b> {avgScore:F1}   " +
                         $"<b>Avg Time:</b> {avgTime:F2}s";
    }

    private void DrawGraph(List<RoundData> rounds)
    {
        float width = graphContainer.rect.width;
        float height = graphContainer.rect.height;

        float maxDiff = rounds.Max(r => r.NextDifficulty);
        float yMax = Mathf.Max(10f, maxDiff + 2);
        float yMin = 0f;

        float xStep = width / (rounds.Count + 1);
        Vector2? lastPos = null;

        for (int i = 0; i < rounds.Count; i++)
        {
            float xPos = (i + 1) * xStep - (width / 2f);
            float normalizedValue = (rounds[i].NextDifficulty - yMin) / (yMax - yMin);
            float yPos = normalizedValue * height - (height / 2f);

            Vector2 currentPos = new Vector2(xPos, yPos);

            if (lastPos.HasValue)
            {
                CreateDotConnection(lastPos.Value, currentPos);
            }

            CreateCircle(currentPos);
            CreateLabel(rounds[i].GestureName, xPos, yPos + 30f);

            lastPos = currentPos;
        }
    }

    private void CreateCircle(Vector2 anchoredPosition)
    {
        GameObject circle = Instantiate(circlePrefab, graphContainer);
        circle.transform.localScale = Vector3.one;

        RectTransform rect = circle.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPosition;

        // 【修改2】注释掉：不再强行修改大小，使用 Prefab 默认大小
        // rect.sizeDelta = new Vector2(15, 15); 

        var img = circle.GetComponent<Image>();
        // 【修改3】注释掉：不再强行修改颜色，使用 Prefab 默认颜色
        // if(img) img.color = pointColor;

        circle.transform.SetAsLastSibling();
        _spawnedObjects.Add(circle);
    }

    private void CreateDotConnection(Vector2 dotA, Vector2 dotB)
    {
        GameObject line = Instantiate(linePrefab, graphContainer);
        line.transform.localScale = Vector3.one;

        var img = line.GetComponent<Image>();
        if (img)
        {
            img.color = lineColor; // 线条颜色还是建议由代码控制，方便统一
            img.raycastTarget = false;
        }

        RectTransform rect = line.GetComponent<RectTransform>();
        Vector2 direction = dotB - dotA;
        float distance = direction.magnitude;

        rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = dotA;
        rect.sizeDelta = new Vector2(distance, lineThickness); // 线条长度必须由代码算

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        rect.localRotation = Quaternion.Euler(0, 0, angle);

        line.transform.SetAsFirstSibling();
        _spawnedObjects.Add(line);
    }

    private void CreateLabel(string text, float xPos, float yPos)
    {
        GameObject labelObj = Instantiate(labelPrefab, graphContainer);
        labelObj.transform.localScale = Vector3.one;

        RectTransform rect = labelObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(xPos, yPos);

        TextMeshProUGUI txt = labelObj.GetComponent<TextMeshProUGUI>();
        if (txt)
        {
            txt.text = text;
            txt.alignment = TextAlignmentOptions.Center;

            // 【修改4】注释掉：不再强行修改字号和颜色，使用 Prefab 默认设置
            // txt.fontSize = 24; 
            // txt.color = Color.white; 
        }

        _spawnedObjects.Add(labelObj);
    }

    private void ClearGraph()
    {
        foreach (var obj in _spawnedObjects)
        {
            if (obj != null) Destroy(obj);
        }
        _spawnedObjects.Clear();
    }
}