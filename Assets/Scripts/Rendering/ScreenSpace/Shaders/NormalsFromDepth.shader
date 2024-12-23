Shader "Fluid/NormalsFromDepth"
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
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4x4 _CameraInvViewMatrix;
            int useSmoothedDepth;

            float4 ViewPos(float2 uv)
            {
                float4 depthInfo = tex2D(_MainTex, uv);
                float depth = useSmoothedDepth ? depthInfo.r : depthInfo.a;

                float3 origin = 0;

                float3 viewVector = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
                float3 dir = normalize(viewVector);
                return float4(origin + dir * depth, depth);
            }


            float4 frag (v2f i) : SV_Target
            {
                float4 posCentre = ViewPos(i.uv);
                if (posCentre.a > 10000)
                {
                    return 0;
                }

                float3 origin = _WorldSpaceCameraPos;
                float2 o = _MainTex_TexelSize.xy;

                float3 ddx = ViewPos(i.uv + float2(o.x, 0)) - posCentre;
                float3 ddx2 = posCentre - ViewPos(i.uv + float2(-o.x, 0));
                if (abs(ddx2.z) < abs(ddx.z))
                {
                    ddx = ddx2;
                }
                
                float3 ddy = ViewPos(i.uv + float2(0, o.y)) - posCentre;
                float3 ddy2 = posCentre - ViewPos(i.uv + float2(0,-o.y));
                if (abs(ddy2.z) < abs(ddy.z)) {
                    ddy = ddy2;
                }
                
                float3 viewNormal = normalize(cross(ddy,ddx));
                float3 worldNormal = mul(unity_CameraToWorld, float4(viewNormal, 0));

                return float4(worldNormal, 1);
            }
            ENDCG
        }
    }
}
