Shader "Fluid/SmoothThickPrepare"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(v.vertex.xyz, 1.0));
                o.uv = v.uv;
                return o;
            }

            sampler2D Depth;
            sampler2D Thick;

            float4 frag (v2f i) : SV_Target
            {
                float depth = tex2D(Depth, i.uv).r;
                float thick = tex2D(Thick, i.uv).r;

                return float4(depth, thick, thick, depth);
            }
            ENDCG
        }
    }
}
