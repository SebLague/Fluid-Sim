Shader "Instanced/Particle3D" {
	Properties {
		
	}
	SubShader {

		Tags {"Queue"="Geometry" }

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			StructuredBuffer<float3> Positions;
			StructuredBuffer<float3> Velocities;
			Texture2D<float4> ColourMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;

			float scale;
			float3 colour;

			float4x4 localToWorld;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float3 normal : NORMAL;
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				
				float3 centreWorld = Positions[instanceID];
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));
				v2f o;
				o.uv = v.texcoord;
				o.normal = v.normal;

				o.pos = UnityObjectToClipPos(objectVertPos);

				float speed = length(Velocities[instanceID]);
				float speedT = saturate(speed / velocityMax);
				float colT = speedT;
				o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0);
		
				return o;
			}

			float4 frag (v2f i) : SV_Target
			{
				float shading = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
				shading = (shading + 0.6) / 1.4;
				//return float4(i.normal * 0.5 + 0.5, 1);
				return float4(i.colour * shading, 1);
			}

			ENDCG
		}
	}
}