Shader "Hidden/BilateralFilter1D"
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
            #include "BilateralPass.hlsl"

            float4 frag (v2f i) : SV_Target
            {
                return CalculateBlur1D(i.uv, float2(1, 0));
            }

            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "BilateralPass.hlsl"

            float4 frag (v2f i) : SV_Target
            {
                return CalculateBlur1D(i.uv, float2(0, 1));
            }

            ENDCG
        }
    }
}
