using System;

[Serializable]
public struct GesturePreset
{
    public float okTipDist;      // OK 指尖距离阈值
    public float fistAvgCurl;    // 拳头平均弯曲阈值
    public float openAvgSpread;  // 张手平均张开阈值
}
