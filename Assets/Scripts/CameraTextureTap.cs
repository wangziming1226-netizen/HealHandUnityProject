using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class CameraTextureTap : MonoBehaviour
{
    RawImage _img;

    // 给外部用的只读属性：当前屏幕上的那张纹理
    public Texture CurrentTexture => _img != null ? _img.texture : null;

    void Awake()
    {
        _img = GetComponent<RawImage>();
    }
}
