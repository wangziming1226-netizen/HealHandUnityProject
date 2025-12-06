using UnityEngine;
using System; // 必须引入，为了 [Serializable]

// 确保该类可以被 Unity 的 Inspector 序列化显示
[Serializable]
public class GestureData
{
    public string gestureId;     // 唯一ID，例如: "fist", "palm"
    public string displayName;   // 显示给用户的名称，例如: "握拳"
    public Sprite iconSprite;    // 手势的图标图片资源
    public bool isRecorded = false; // 标记该手势是否已经录制过数据
}