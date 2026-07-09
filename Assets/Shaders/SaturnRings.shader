Shader "Custom/SaturnRings"
{
    Properties
    {
        _MainTex ("Ring Map Texture (1D/2D)", 2D) = "white" {}
        _InnerRadius ("Inner Radius Cutoff", Range(0.0, 1.0)) = 0.3
        _OuterRadius ("Outer Radius Cutoff", Range(0.0, 1.0)) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _InnerRadius;
            float _OuterRadius;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Shift UV center from (0.5, 0.5) to (0, 0)
                o.uv = v.uv - 0.5;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calculate distance from center
                float dist = length(i.uv) * 2.0; 

                // Clip pixels outside the inner/outer ring boundaries
                if (dist < _InnerRadius || dist > _OuterRadius)
                {
                    discard;
                }

                // Map distance to a 0-1 UV range
                float t = (dist - _InnerRadius) / (_OuterRadius - _InnerRadius);
                
                // Sample the ring texture using the calculated radius
                fixed4 col = tex2D(_MainTex, float2(t, 0.5));
                return col;
            }
            ENDCG
        }
    }
}