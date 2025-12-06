using UnityEngine;

/// <summary>
/// 兼容 Mediapipe 反射查找的“外部纹理源”
/// 同时暴露属性 + 方法，最大概率被识别：InputTexture / Texture / SetInputTexture / SetInput
/// </summary>
public class ExternalTextureSourceCompat : MonoBehaviour
{
    [SerializeField] private Texture _inputTexture;

    // 给 Mediapipe 反射用的属性名（常见写法）
    public Texture InputTexture
    {
        get => _inputTexture;
        set => _inputTexture = value;
    }

    // 有些实现会用 Texture 这个名字
    public Texture Texture => _inputTexture;

    // 给 Mediapipe 反射用的方法名（常见写法）
    public void SetInputTexture(Texture tex) => _inputTexture = tex;

    // 兜底别名
    public void SetInput(Texture tex) => SetInputTexture(tex);
}
