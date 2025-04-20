Shader "Custom/MiddleCutoffShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FadeStart ("Fade Start", Range(0, 1)) = 0.4 // Start of the middle section to disappear
        _FadeEnd ("Fade End", Range(0, 1)) = 0.6 // End of the middle section to disappear
        _FadeProgress ("Fade Progress", Range(0, 1)) = 0.0 // Controls how much of the middle is faded
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
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
            float _FadeStart;
            float _FadeEnd;
            float _FadeProgress;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Calculate the dynamic cutoff range for the middle section
                float fadePositionStart = _FadeStart;
                float fadePositionEnd = lerp(_FadeStart, _FadeEnd, _FadeProgress);

                // Discard pixels within the fading middle section
                if (i.uv.y >= fadePositionStart && i.uv.y <= fadePositionEnd)
                {
                    discard;
                }

                return col;
            }
            ENDCG
        }
    }
}
