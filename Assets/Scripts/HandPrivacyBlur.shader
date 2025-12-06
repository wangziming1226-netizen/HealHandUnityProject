Shader "UI/HandPrivacyBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BlurRadius ("Blur Radius", Float) = 4.0
        _FocusCenter ("Focus Center", Vector) = (0.5, 0.5, 0, 0)
        _FocusRadius ("Focus Radius", Float) = 0.20
        _FocusEdge ("Focus Edge (unused)", Float) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                float4 color: COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _BlurRadius;
            float4 _FocusCenter;
            float _FocusRadius;
            float _FocusEdge; // 现在不怎么用，先保留接口

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // 原图
                fixed4 sharp = tex2D(_MainTex, uv);

                // 重度模糊（sampling 更密）
                float2 texel = _MainTex_TexelSize.xy * _BlurRadius;
                fixed4 blur  = 0;
                blur += tex2D(_MainTex, uv + texel * float2(-2,-2));
                blur += tex2D(_MainTex, uv + texel * float2(-1,-2));
                blur += tex2D(_MainTex, uv + texel * float2( 0,-2));
                blur += tex2D(_MainTex, uv + texel * float2( 1,-2));
                blur += tex2D(_MainTex, uv + texel * float2( 2,-2));

                blur += tex2D(_MainTex, uv + texel * float2(-2,-1));
                blur += tex2D(_MainTex, uv + texel * float2(-1,-1));
                blur += tex2D(_MainTex, uv + texel * float2( 0,-1));
                blur += tex2D(_MainTex, uv + texel * float2( 1,-1));
                blur += tex2D(_MainTex, uv + texel * float2( 2,-1));

                blur += tex2D(_MainTex, uv + texel * float2(-2, 0));
                blur += tex2D(_MainTex, uv + texel * float2(-1, 0));
                blur += tex2D(_MainTex, uv);
                blur += tex2D(_MainTex, uv + texel * float2( 1, 0));
                blur += tex2D(_MainTex, uv + texel * float2( 2, 0));

                blur += tex2D(_MainTex, uv + texel * float2(-2, 1));
                blur += tex2D(_MainTex, uv + texel * float2(-1, 1));
                blur += tex2D(_MainTex, uv + texel * float2( 0, 1));
                blur += tex2D(_MainTex, uv + texel * float2( 1, 1));
                blur += tex2D(_MainTex, uv + texel * float2( 2, 1));

                blur += tex2D(_MainTex, uv + texel * float2(-2, 2));
                blur += tex2D(_MainTex, uv + texel * float2(-1, 2));
                blur += tex2D(_MainTex, uv + texel * float2( 0, 2));
                blur += tex2D(_MainTex, uv + texel * float2( 1, 2));
                blur += tex2D(_MainTex, uv + texel * float2( 2, 2));

                blur /= 25.0;

                // 计算与聚焦中心的距离
                float2 center = _FocusCenter.xy;
                float d = distance(uv, center);

                // 硬切：半径以内完全清晰，半径以外完全模糊
                float t = step(_FocusRadius, d); // d < R → t=0（清晰），d >= R → t=1（模糊）

                fixed4 col = lerp(sharp, blur, t) * i.color;
                return col;
            }
            ENDCG
        }
    }
}
