Shader "Hidden/BilateralFilter2D"
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
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float worldRadius;
            int maxScreenSpaceRadius;
            float strength;
            float diffStrength;
            float3 smoothMask;

            float CalculateGaussianWeight2D(int x, int y, float sigma)
            {
                float c = 2 * sigma * sigma;
                return exp(-(x * x + y * y) / c);
            }

            float CalculateScreenSpaceRadius(float3 viewPoint, float worldRadius, int imageWidth)
            {
                float clipW = viewPoint.z;
                float proj = UNITY_MATRIX_P._m00;
                float pxPerMeter = (imageWidth * proj) / (2 * clipW);
                return abs(pxPerMeter * worldRadius);
            }


            float4 ViewPos(float2 uv, float depth)
            {
                float3 origin = 0;
                float3 viewVector = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
                float3 dir = normalize(viewVector);
                return float4(origin + dir * depth, depth);
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 centreSample = tex2D(_MainTex, i.uv);

                // Calculate screen space radius
                float depth = centreSample.a;
                float3 viewPos = ViewPos(i.uv, depth);
                int radius = round(CalculateScreenSpaceRadius(viewPos, worldRadius, _MainTex_TexelSize.z));
                radius = min(maxScreenSpaceRadius, radius);

                float sigma = max(0.0000001, radius * strength);

                float4 sum = 0;
                float wSum = 0;

                for (int dx = -radius; dx <= radius; dx ++)
                {
                    for (int dy = -radius; dy <= radius; dy ++)
                    {
                        float2 uv2 = i.uv + float2(dx, dy) * _MainTex_TexelSize.xy;
                        float4 sample = tex2Dlod(_MainTex, float4(uv2, 0, 0));

                        float w = CalculateGaussianWeight2D(dx, dy, sigma);

                        float centreDiff = centreSample.a - sample.a;
                        float diffWeight = exp(-centreDiff * centreDiff * diffStrength);

                        float sampleWeight = w * diffWeight;
                        sum += sample * sampleWeight;
                        wSum += sampleWeight;
                    }
                }
               
                if (wSum > 0)
                {
                    sum /= wSum;
                }

                 return float4(lerp(centreSample.rgb, sum.rgb, smoothMask), depth);
            }

            ENDCG
        }
    }
}
