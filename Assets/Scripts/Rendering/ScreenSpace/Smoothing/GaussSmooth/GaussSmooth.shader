Shader "Hidden/GaussSmooth"
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
            #pragma fragment frag
            #pragma vertex vert

            #include "UnityCG.cginc"
            #include "GaussPass.hlsl"
            
           
            float4 frag (v2f i) : SV_Target
            {
                return CalculateBlur1D(i.uv, float2(1, 0));
            }

            ENDCG
        }
         Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma vertex vert

            #include "UnityCG.cginc"
            #include "GaussPass.hlsl"
           
            float4 frag (v2f i) : SV_Target
            {
                return CalculateBlur1D(i.uv, float2(0, 1));
            }

            ENDCG
        }
      
    }
}
