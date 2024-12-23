Shader "Fluid/FluidRender"
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Textures
            sampler2D _MainTex;
            sampler2D Normals;
            sampler2D Comp;
            sampler2D ShadowMap;
            
            const float3 extinctionCoefficients;
            const float3 dirToSun;
            const float3 boundsSize;
            const float refractionMultiplier;

            // Test environment settings
            const float4 tileCol1;
            const float4 tileCol2;
            const float4 tileCol3;
            const float4 tileCol4;
            const float3 tileColVariation;
            const float tileScale;
            const float tileDarkOffset;
            const float sunIntensity;
            const float sunInvSize;
            const float4x4 shadowVP;
            const float3 floorPos;
            const float3 floorSize;


            // Debug values
            float3 testParams;
            int debugDisplayMode;
            float depthDisplayScale;
            float thicknessDisplayScale;
            StructuredBuffer<uint> foamCountBuffer;
            uint foamMax;

            
            struct HitInfo
            {
                bool didHit;
                bool isInside;
                float dst;
                float3 hitPoint;
                float3 normal;
            };

            
            struct LightResponse
            {
                float3 reflectDir;
                float3 refractDir;
                float reflectWeight;
                float refractWeight;
            };


            float3 WorldViewDir(float2 uv)
            {
                float3 viewVector = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
                return normalize(mul(unity_CameraToWorld, viewVector));
            }

            // Test intersection of ray with unit box centered at origin
            HitInfo RayUnitBox(float3 pos, float3 dir)
            {
                const float3 boxMin = -1;
                const float3 boxMax = 1;
                float3 invDir = 1 / dir;

                // Thanks to https://tavianator.com/2011/ray_box.html
                float3 tMin = (boxMin - pos) * invDir;
                float3 tMax = (boxMax - pos) * invDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);

                // Set hit info
                HitInfo hitInfo = (HitInfo)0;
                hitInfo.dst = 1.#INF;
                hitInfo.didHit = tFar >= tNear && tFar > 0;
                hitInfo.isInside = tFar > tNear && tNear <= 0;

                if (hitInfo.didHit)
                {
                    float hitDst = hitInfo.isInside ? tFar : tNear;
                    float3 hitPos = pos + dir * hitDst;

                    hitInfo.dst = hitDst;
                    hitInfo.hitPoint = hitPos;

                    // Calculate normal
                    float3 o = (1 - abs(hitPos));
                    float3 absNormal = (o.x < o.y && o.x < o.z) ? float3(1, 0, 0) : (o.y < o.z) ? float3(0, 1, 0) : float3(0, 0, 1);
                    hitInfo.normal = absNormal * sign(hitPos) * (hitInfo.isInside ? -1 : 1);
                }

                return hitInfo;
            }

            HitInfo RayBox(float3 rayPos, float3 rayDir, float3 centre, float3 size)
            {
                HitInfo hitInfo = RayUnitBox((rayPos - centre) / size, rayDir / size);
                hitInfo.hitPoint = hitInfo.hitPoint * size + centre;
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPos);
                return hitInfo;
            }

            float3 CalculateClosestFaceNormal(float3 boxSize, float3 p)
            {
                float3 halfSize = boxSize * 0.5;
                float3 o = (halfSize - abs(p));
                return (o.x < o.y && o.x < o.z) ? float3(sign(p.x), 0, 0) : (o.y < o.z) ? float3(0, sign(p.y), 0) : float3(0, 0, sign(p.z));
            }

            float4 SmoothEdgeNormals(float3 normal, float3 pos, float3 boxSize)
            {
                // Smoothly flatten normals out at boundary edges
                float3 o = boxSize / 2 - abs(pos);
                float faceWeight = max(0, min(o.x, o.z));
                float3 faceNormal = CalculateClosestFaceNormal(boxSize, pos);
                const float smoothDst = 0.01;
                const float smoothPow = 5;
                //faceWeight = (1 - smoothstep(0, smoothDst, faceWeight)) * (1 - pow(saturate(normal.y), smoothPow));
                float cornerWeight = 1 - saturate(abs(o.x - o.z) * 6);
                faceWeight = 1 - smoothstep(0, smoothDst, faceWeight);
                faceWeight *= (1 - cornerWeight);

                return float4(normalize(normal * (1 - faceWeight) + faceNormal * (faceWeight)), faceWeight);
            }


            float Modulo(float x, float y)
            {
                return (x - y * floor(x / y));
            }
            

            float3 RGBToHSV(float3 rgb)
            {
                // Thanks to http://lolengine.net/blog/2013/07/27/rgb-to-hsv-in-glsl
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = rgb.g < rgb.b ? float4(rgb.bg, K.wz) : float4(rgb.gb, K.xy);
                float4 q = rgb.r < p.x ? float4(p.xyw, rgb.r) : float4(rgb.r, p.yzx);

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HSVToRGB(float3 hsv)
            {
                // Thanks to http://lolengine.net/blog/2013/07/27/rgb-to-hsv-in-glsl
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(hsv.xxx + K.xyz) * 6.0 - K.www);
                return hsv.z * lerp(K.xxx, saturate(p - K.xxx), hsv.y);
            }

            float3 TweakHSV(float3 colRGB, float hueShift, float satShift, float valShift)
            {
                float3 hsv = RGBToHSV(colRGB);
                return saturate(HSVToRGB(hsv + float3(hueShift, satShift, valShift)));
            }

            float3 TweakHSV(float3 colRGB, float3 shift)
            {
                float3 hsv = RGBToHSV(colRGB);
                return saturate(HSVToRGB(hsv + shift));
            }

            uint NextRandom(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            uint RngSeedUintFromUV(float2 uv)
            {
                return (uint)(uv.x * 5023 + uv.y * 96456);
            }

            // PCG (permuted congruential generator). Thanks to:
            // www.pcg-random.org and www.shadertoy.com/view/XlGcRh
            uint NextRandomUint(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            float RandomUNorm(inout uint state)
            {
                return NextRandomUint(state) / 4294967295.0; // 2^32 - 1
            }

            float RandomSNorm(inout uint state)
            {
                return RandomUNorm(state) * 2 - 1;
            }

            float RandomSNorm3(inout uint state)
            {
                return float3(RandomSNorm(state), RandomSNorm(state), RandomSNorm(state));
            }


            uint HashInt2(int2 v)
            {
                return v.x * 5023 + v.y * 96456;
            }

            float3 SampleSky(float3 dir)
            {
                const float3 colGround = float3(0.35, 0.3, 0.35) * 0.53;
                const float3 colSkyHorizon = float3(1, 1, 1);
                const float3 colSkyZenith = float3(0.08, 0.37, 0.73);


                float sun = pow(max(0, dot(dir, dirToSun)), sunInvSize) * sunIntensity;
                float skyGradientT = pow(smoothstep(0, 0.4, dir.y), 0.35);
                float groundToSkyT = smoothstep(-0.01, 0, dir.y);
                float3 skyGradient = lerp(colSkyHorizon, colSkyZenith, skyGradientT);

                return lerp(colGround, skyGradient, groundToSkyT) + sun * (groundToSkyT >= 1);
            }

            float3 SampleEnvironment(float3 pos, float3 dir)
            {
                HitInfo floorInfo = RayBox(pos, dir, floorPos, floorSize);

                if (floorInfo.didHit)
                {
                    // Choose tileCol based on quadrant
                    float3 tileCol = floorInfo.hitPoint.x < 0 ? tileCol1 : tileCol2;
                    if (floorInfo.hitPoint.z < 0) tileCol = floorInfo.hitPoint.x < 0 ? tileCol3 : tileCol4;

                    // If tile is a dark tile, then darken it
                    int2 tileCoord = floor(floorInfo.hitPoint.xz * tileScale);
                    bool isDarkTile = Modulo(tileCoord.x, 2) == Modulo(tileCoord.y, 2);
                    tileCol = TweakHSV(tileCol, float3(0, 0, tileDarkOffset * isDarkTile));

                    // Vary hue/sat/val randomly
                    uint rngState = HashInt2(tileCoord);
                    float3 randomVariation = RandomSNorm3(rngState) * tileColVariation * 0.1;
                    tileCol = TweakHSV(tileCol, randomVariation);

                    float4 shadowClip = mul(shadowVP, float4(floorInfo.hitPoint, 1));
                    shadowClip /= shadowClip.w;
                    float2 shadowUV = shadowClip.xy * 0.5 + 0.5;
                    float shadowEdgeWeight = shadowUV.x >= 0 && shadowUV.x <= 1 && shadowUV.y >= 0 && shadowUV.y <= 1;
                    float3 shadow = tex2D(ShadowMap, shadowUV).r * shadowEdgeWeight;
                    shadow = exp(-shadow * 1 * extinctionCoefficients);

                    float ambientLight = 0.17;
                    shadow = shadow * (1 - ambientLight) + ambientLight;

                    return tileCol * shadow;
                }

                return SampleSky(dir);
            }

            // Crude anti-aliasing
            float3 SampleEnvironmentAA(float3 pos, float3 dir)
            {
                float3 right = unity_CameraToWorld._m00_m10_m20;
                float3 up = unity_CameraToWorld._m01_m11_m21;
                float aa = 0.01;

                float3 sum = 0;
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        float3 jitteredFocusPoint = (pos + dir) + (right * ox + up * oy) * 0.7 / _ScreenParams.x;
                        float3 jDir = normalize(jitteredFocusPoint - pos);
                        sum += SampleEnvironment(pos, jDir);
                    }
                }

                return sum / 9;
            }


            // Calculate the number of pixels covered by a world-space radius at given dst from camera
            float CalculateScreenSpaceRadius(float worldRadius, float depth, int imageWidth)
            {
                // Thanks to x.com/FreyaHolmer/status/1820157167682388210
                float widthScale = UNITY_MATRIX_P._m00; // smaller values correspond to higher fov (objects appear smaller)
                float pxPerMeter = (imageWidth * widthScale) / (2 * depth);
                return abs(pxPerMeter) * worldRadius;
            }

            // Calculate the proportion of light that is reflected at the boundary between two media (via the fresnel equations)
            // Note: the amount of light refracted can be calculated as 1 minus this value
            float CalculateReflectance(float3 inDir, float3 normal, float iorA, float iorB)
            {
                float refractRatio = iorA / iorB;
                float cosAngleIn = -dot(inDir, normal);
                float sinSqrAngleOfRefraction = refractRatio * refractRatio * (1 - cosAngleIn * cosAngleIn);
                if (sinSqrAngleOfRefraction >= 1) return 1; // Ray is fully reflected, no refraction occurs

                float cosAngleOfRefraction = sqrt(1 - sinSqrAngleOfRefraction);
                // Perpendicular polarization
                float rPerpendicular = (iorA * cosAngleIn - iorB * cosAngleOfRefraction) / (iorA * cosAngleIn + iorB * cosAngleOfRefraction);
                rPerpendicular *= rPerpendicular;
                // Parallel polarization
                float rParallel = (iorB * cosAngleIn - iorA * cosAngleOfRefraction) / (iorB * cosAngleIn + iorA * cosAngleOfRefraction);
                rParallel *= rParallel;

                // Return the average of the perpendicular and parallel polarizations
                return (rPerpendicular + rParallel) / 2;
            }


            float3 Refract(float3 inDir, float3 normal, float iorA, float iorB)
            {
                float refractRatio = iorA / iorB;
                float cosAngleIn = -dot(inDir, normal);
                float sinSqrAngleOfRefraction = refractRatio * refractRatio * (1 - cosAngleIn * cosAngleIn);
                if (sinSqrAngleOfRefraction > 1) return 0; // Ray is fully reflected, no refraction occurs

                float3 refractDir = refractRatio * inDir + (refractRatio * cosAngleIn - sqrt(1 - sinSqrAngleOfRefraction)) * normal;
                return refractDir;
            }

            float3 Reflect(float3 inDir, float3 normal)
            {
                return inDir - 2 * dot(inDir, normal) * normal;
            }


            LightResponse CalculateReflectionAndRefraction(float3 inDir, float3 normal, float iorA, float iorB)
            {
                LightResponse result;

                result.reflectWeight = CalculateReflectance(inDir, normal, iorA, iorB);
                result.refractWeight = 1 - result.reflectWeight;

                result.reflectDir = Reflect(inDir, normal);
                result.refractDir = Refract(inDir, normal, iorA, iorB);

                return result;
            }
            
            float4 DebugModeDisplay(float depthSmooth, float depth, float thicknessSmooth, float thickness, float3 normal)
            {
                float3 col = 0;

                switch (debugDisplayMode)
                {
                case 1:
                    col = depth / depthDisplayScale;
                    break;
                case 2:
                    col = depthSmooth / depthDisplayScale;
                    break;
                case 3:
                    if (dot(normal, normal) == 0) col = 0;
                    float3 normalDisplay = normal * 0.5 + 0.5;
                    col = pow(normalDisplay, 2.2), 1;
                    break;
                case 4:
                    col = thickness / thicknessDisplayScale;
                    break;
                case 5:
                    col = thicknessSmooth / thicknessDisplayScale;
                    break;
                default:
                    col = float3(1, 0, 1);
                    break;
                }

                return float4(col, 1);
            }

            float4 frag(v2f i) : SV_Target
            {
                if (i.uv.y < 0.005)
                {
                    return i.uv.x < (foamCountBuffer[0] / (float)foamMax);
                }

                // ---- Read data from texture ----
                float3 normal = tex2D(Normals, i.uv).xyz;

                float4 packedData = tex2D(Comp, i.uv);
                float depthSmooth = packedData.r;
                float thickness = packedData.g;
                float thickness_hard = packedData.b;
                float depth_hard = packedData.a;

                float4 bg = tex2D(_MainTex, float2(i.uv.x, i.uv.y));
                float foam = bg.r;
                float foamDepth = bg.b;

                // ---- Get test-environment colour (and early exit if view ray misses fluid) ----
                float3 viewDirWorld = WorldViewDir(i.uv);
                float3 world = SampleEnvironmentAA(_WorldSpaceCameraPos, viewDirWorld);
                if (depthSmooth > 1000) return float4(world, 1) * (1 - foam) + foam;

                // ---- Calculate fluid hit point and smooth out normals along edges of bounding box ----
                float3 hitPos = _WorldSpaceCameraPos.xyz + viewDirWorld * depthSmooth;
                float3 smoothEdgeNormal = SmoothEdgeNormals(normal, hitPos, boundsSize).xyz;
                normal = normalize(normal + smoothEdgeNormal * 6 * max(0, dot(normal, smoothEdgeNormal.xyz)));

                // ---- Debug display mode ----
                if (debugDisplayMode != 0)
                {
                    return DebugModeDisplay(depthSmooth, depth_hard, thickness, thickness_hard, normal);
                }

                // ---- Calculate shading ----
                const float ambientLight = 0.3;
                float shading = dot(normal, dirToSun) * 0.5 + 0.5;
                shading = shading * (1 - ambientLight) + ambientLight;

                // ---- Calculate reflection and refraction ----
                LightResponse lightResponse = CalculateReflectionAndRefraction(viewDirWorld, normal, 1, 1.33);
                float3 reflectDir = reflect(viewDirWorld, normal);

                float3 exitPos = hitPos + lightResponse.refractDir * thickness * refractionMultiplier;
                // Clamp to ensure doesn't go below floor
                exitPos += lightResponse.refractDir * max(0, floorPos.y + floorSize.y - exitPos.y) / lightResponse.refractDir.y;
           
                // Colour
                float3 transmission = exp(-thickness * extinctionCoefficients);
                float3 reflectCol = SampleEnvironmentAA(hitPos, lightResponse.reflectDir);
                float3 refractCol = SampleEnvironmentAA(exitPos, viewDirWorld);
                refractCol = refractCol * (1 - foam) + foam;
                refractCol *= transmission;

                // If foam is in front of the fluid, overwrite the reflected col with the foam col
                if (foamDepth < depthSmooth)
                {
                    reflectCol = reflectCol * (1 - foam) + foam;
                }

                // Blend between reflected and refracted col
                float3 col = lerp(reflectCol, refractCol, lightResponse.refractWeight);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}