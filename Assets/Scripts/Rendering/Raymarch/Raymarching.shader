Shader "Fluid/Raymarching"
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
                float3 viewVector : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
                o.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));
                return o;
            }

            Texture3D<float4> DensityMap;
            SamplerState linearClampSampler;

            const float indexOfRefraction;
            const int numRefractions;
            const float3 extinctionCoeff;

            const float3 testParams;
            const float3 boundsSize;
            const float volumeValueOffset;
            const float densityMultiplier;
            const float viewMarchStepSize;
            const float lightStepSize;
            static const float TinyNudge = 0.01;

            // Test-environment settings
            const float3 dirToSun;
            const float4 tileCol1;
            const float4 tileCol2;
            const float4 tileCol3;
            const float4 tileCol4;
            const float3 tileColVariation;
            const float tileScale;
            const float tileDarkOffset;

            const float4x4 cubeLocalToWorld;
            const float4x4 cubeWorldToLocal;
            const float3 floorPos;
            const float3 floorSize;
            
            static const float3 cubeCol = float3(0.95, 0.3, 0.35);
            static const float iorAir = 1;

            struct HitInfo
            {
                bool didHit;
                bool isInside;
                float dst;
                float3 hitPoint;
                float3 normal;
            };

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

            // Random value in normal distribution (with mean=0 and sd=1)
            float RandomValueNormalDistribution(inout uint state)
            {
                const float PI = 3.1415926;
                // Thanks to https://stackoverflow.com/a/6178290
                float theta = 2 * PI * RandomUNorm(state);
                float rho = sqrt(-2 * log(RandomUNorm(state)));
                return rho * cos(theta);
            }

            // Calculate a random direction
            float3 RandomDirection(inout uint state)
            {
                // Thanks to https://math.stackexchange.com/a/1585996
                float x = RandomValueNormalDistribution(state);
                float y = RandomValueNormalDistribution(state);
                float z = RandomValueNormalDistribution(state);
                return normalize(float3(x, y, z));
            }

            float2 RandomPointInCircle(inout uint rngState)
            {
                const float PI = 3.1415926;
                float angle = RandomUNorm(rngState) * 2 * PI;
                float2 pointOnCircle = float2(cos(angle), sin(angle));
                return pointOnCircle * sqrt(RandomUNorm(rngState));
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

            // PCG (permuted congruential generator). Thanks to:
            // www.pcg-random.org and www.shadertoy.com/view/XlGcRh
            uint NextRandom(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result;
            }

            float RandomValue(inout uint state)
            {
                return NextRandom(state) / 4294967295.0; // 2^32 - 1
            }

            float RandomSNorm(inout uint state)
            {
                return RandomValue(state) * 2 - 1;
            }

            float RandomSNorm3(inout uint state)
            {
                float a = RandomValue(state) * 2 - 1;
                float b = RandomValue(state) * 2 - 1;
                float c = RandomValue(state) * 2 - 1;
                return float3(a, b, c);
            }

            // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero
            float2 RayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 rayDir)
            {
                float3 invRayDir = 1 / rayDir;
                // Adapted from: http://jcgt.org/published/0007/03/04/
                float3 t0 = (boundsMin - rayOrigin) * invRayDir;
                float3 t1 = (boundsMax - rayOrigin) * invRayDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));

                // CASE 1: ray intersects box from outside (0 <= dstA <= dstB)
                // dstA is dst to nearest intersection, dstB dst to far intersection

                // CASE 2: ray intersects box from inside (dstA < 0 < dstB)
                // dstA is the dst to intersection behind the ray, dstB is dst to forward intersection

                // CASE 3: ray misses box (dstA > dstB)

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            float SampleDensity(float3 pos)
            {
                float3 uvw = (pos + boundsSize * 0.5) / boundsSize;

                const float epsilon = 0.0001;
                bool isEdge = any(uvw >= 1 - epsilon || uvw <= epsilon);
                if (isEdge) return -volumeValueOffset;

                return DensityMap.SampleLevel(linearClampSampler, uvw, 0).r - volumeValueOffset;
            }


            float CalculateDensityAlongRay(float3 rayPos, float3 rayDir, float stepSize)
            {
                // Test for non-normalize ray and return 0 in that case.
                // This happens when refract direction is calculated, but ray is totally reflected
                if (dot(rayDir, rayDir) < 0.9) return 0;

                float2 boundsDstInfo = RayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, rayPos, rayDir);
                float dstToBounds = boundsDstInfo[0];
                float dstThroughBounds = boundsDstInfo[1];
                if (dstThroughBounds <= 0) return 0;

                float dstTravelled = 0;
                float opticalDepth = 0;
                float nudge = stepSize * 0.5;
                float3 entryPoint = rayPos + rayDir * (dstToBounds + nudge);
                dstThroughBounds -= (nudge + TinyNudge);

                while (dstTravelled < dstThroughBounds)
                {
                    rayPos = entryPoint + rayDir * dstTravelled;
                    float density = SampleDensity(rayPos) * densityMultiplier * stepSize;
                    if (density > 0)
                    {
                        opticalDepth += density;
                    }
                    dstTravelled += stepSize;
                }

                return opticalDepth;
            }

            float CalculateDensityAlongRay(float3 rayPos, float3 rayDir)
            {
                return CalculateDensityAlongRay(rayPos, rayDir, lightStepSize);
            }

            float3 CalculateClosestFaceNormal(float3 boxSize, float3 p)
            {
                float3 halfSize = boxSize * 0.5;
                float3 o = (halfSize - abs(p));
                return (o.x < o.y && o.x < o.z) ? float3(sign(p.x), 0, 0) : (o.y < o.z) ? float3(0, sign(p.y), 0) : float3(0, 0, sign(p.z));
            }

            struct LightResponse
            {
                float3 reflectDir;
                float3 refractDir;
                float reflectWeight;
                float refractWeight;
            };

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

            float3 CalculateNormal(float3 pos)
            {
                float3 uvw = (pos + boundsSize * 0.5) / boundsSize;

                const float s = 0.1;
                float3 offsetX = float3(1, 0, 0) * s;
                float3 offsetY = float3(0, 1, 0) * s;
                float3 offsetZ = float3(0, 0, 1) * s;

                float dx = SampleDensity(pos - offsetX) - SampleDensity(pos + offsetX);
                float dy = SampleDensity(pos - offsetY) - SampleDensity(pos + offsetY);
                float dz = SampleDensity(pos - offsetZ) - SampleDensity(pos + offsetZ);

                float3 volumeNormal = normalize(float3(dx, dy, dz));

                // Smoothly flatten normals out at boundary edges
                float3 o = boundsSize / 2 - abs(pos);
                float faceWeight = min(o.x, min(o.y, o.z));
                float3 faceNormal = CalculateClosestFaceNormal(boundsSize, pos);
                const float smoothDst = 0.3;
                const float smoothPow = 5;
                faceWeight = (1 - smoothstep(0, smoothDst, faceWeight)) * (1 - pow(saturate(volumeNormal.y), smoothPow));

                return normalize(volumeNormal * (1 - faceWeight) + faceNormal * (faceWeight));
            }


            struct SurfaceInfo
            {
                float3 pos;
                float3 normal;
                float densityAlongRay;
                bool foundSurface;
            };

            bool IsInsideFluid(float3 pos)
            {
                float2 boundsDstInfo = RayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, pos, float3(0, 0, 1));
                return (boundsDstInfo.x <= 0 && boundsDstInfo.y > 0) && SampleDensity(pos) > 0;
            }

            SurfaceInfo FindNextSurface(float3 origin, float3 rayDir, bool findNextFluidEntryPoint, uint rngState, float rngWeight, float maxDst)
            {
                SurfaceInfo info = (SurfaceInfo)0;
                if (dot(rayDir, rayDir) < 0.5) return info;

                float2 boundsDstInfo = RayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, origin, rayDir);
                float r = (RandomValue(rngState) - 0.5) * viewMarchStepSize * 0.4 * 1;
                bool hasExittedFluid = !IsInsideFluid(origin);
                origin = origin + rayDir * (boundsDstInfo.x + r);

                float stepSize = viewMarchStepSize;
                bool hasEnteredFluid = false;
                float3 lastPosInFluid = origin;

                float dstToTest = boundsDstInfo[1] - (TinyNudge) * 2;

                for (float dst = 0; dst < dstToTest; dst += stepSize)
                {
                    bool isLastStep = dst + stepSize >= dstToTest;
                    float3 samplePos = origin + rayDir * dst;
                    float thickness = SampleDensity(samplePos) * densityMultiplier * stepSize;
                    bool insideFluid = thickness > 0;
                    if (insideFluid)
                    {
                        hasEnteredFluid = true;
                        lastPosInFluid = samplePos;
                        if (dst <= maxDst)
                        {
                            info.densityAlongRay += thickness;
                        }
                    }

                    if (!insideFluid) hasExittedFluid = true;

                    bool found;
                    if (findNextFluidEntryPoint) found = insideFluid && hasExittedFluid;
                    else found = hasEnteredFluid && (!insideFluid || isLastStep);

                    if (found)
                    {
                        info.pos = lastPosInFluid;
                        info.foundSurface = true;
                        break;
                    }
                }

                return info;
            }

            HitInfo RayBox(float3 rayPos, float3 rayDir, float3 centre, float3 size)
            {
                HitInfo hitInfo = RayUnitBox((rayPos - centre) / size, rayDir / size);
                hitInfo.hitPoint = hitInfo.hitPoint * size + centre;
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPos);
                return hitInfo;
            }

            HitInfo RayBoxWithMatrix(float3 rayPos, float3 rayDir, float4x4 localToWorld, float4x4 worldToLocal)
            {
                float3 posLocal = mul(worldToLocal, float4(rayPos, 1));
                float3 dirLocal = mul(worldToLocal, float4(rayDir, 0));
                HitInfo hitInfo = RayUnitBox(posLocal, dirLocal);
                hitInfo.normal = normalize(mul(localToWorld, float4(hitInfo.normal, 0)));
                hitInfo.hitPoint = mul(localToWorld, float4(hitInfo.hitPoint, 1));
                if (hitInfo.didHit) hitInfo.dst = length(hitInfo.hitPoint - rayPos);
                return hitInfo;
            }

            float Modulo(float x, float y)
            {
                return (x - y * floor(x / y));
            }

            uint HashInt2(int2 v)
            {
                return v.x * 5023 + v.y * 96456;
            }

            float3 Transmittance(float thickness)
            {
                return exp(-thickness * extinctionCoeff);
            }

            float3 SampleSky(float3 dir)
            {
                const float3 colGround = float3(0.35, 0.3, 0.35) * 0.53;
                const float3 colSkyHorizon = float3(1, 1, 1);
                const float3 colSkyZenith = float3(0.08, 0.37, 0.73);

                float sun = pow(max(0, dot(dir, dirToSun)), 500) * 1;
                float skyGradientT = pow(smoothstep(0, 0.4, dir.y), 0.35);
                float groundToSkyT = smoothstep(-0.01, 0, dir.y);
                float3 skyGradient = lerp(colSkyHorizon, colSkyZenith, skyGradientT);

                return lerp(colGround, skyGradient, groundToSkyT) + sun * (groundToSkyT >= 1);
            }

            float3 SampleEnvironment(float3 pos, float3 dir)
            {
                HitInfo floorInfo = RayBox(pos, dir, floorPos, floorSize);
                HitInfo cubeInfo = RayBoxWithMatrix(pos, dir, cubeLocalToWorld, cubeWorldToLocal);

                if (cubeInfo.didHit && cubeInfo.dst < floorInfo.dst)
                {
                    return saturate(dot(cubeInfo.normal, dirToSun) * 0.5 + 0.5) * cubeCol;
                }
                else if (floorInfo.didHit)
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

                    float3 shadowMap = Transmittance(CalculateDensityAlongRay(floorInfo.hitPoint, _WorldSpaceLightPos0, lightStepSize * 2) * 2);
                    bool inShadow = RayBoxWithMatrix(floorInfo.hitPoint, dirToSun, cubeLocalToWorld, cubeWorldToLocal).didHit;
                    if (inShadow) shadowMap *= 0.2;
                    return tileCol * shadowMap;
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

            float3 Light(float3 pos, float3 dir)
            {
                return SampleEnvironmentAA(pos, dir);
            }

            float3 RayMarchFluid(float2 uv, float stepSize)
            {
                uint rngState = (uint)(uv.x * 1243 + uv.y * 96456);
                float3 localViewVector = mul(unity_CameraInvProjection, float4(uv * 2 - 1, 0, -1));
                float3 rayDir = normalize(mul(unity_CameraToWorld, float4(localViewVector, 0)));
                float3 rayPos = _WorldSpaceCameraPos.xyz;
                bool travellingThroughFluid = IsInsideFluid(rayPos);

                float3 transmittance = 1;
                float3 light = 0;

                for (int i = 0; i < numRefractions; i++)
                {
                    float densityStepSize = lightStepSize * (i + 1); // increase step size with each iteration
                    bool searchForNextFluidEntryPoint = !travellingThroughFluid;

                    HitInfo cubeHit = RayBoxWithMatrix(rayPos, rayDir, cubeLocalToWorld, cubeWorldToLocal);
                    SurfaceInfo surfaceInfo = FindNextSurface(rayPos, rayDir, searchForNextFluidEntryPoint, rngState, i == 0 ? 1 : 0, cubeHit.dst);
                    bool useCubeHit = cubeHit.didHit && cubeHit.dst < length(surfaceInfo.pos - rayPos);
                    if (!surfaceInfo.foundSurface) break;

                    transmittance *= Transmittance(surfaceInfo.densityAlongRay);

                    // Hit test cube
                    if (useCubeHit)
                    {
                        if (travellingThroughFluid)
                        {
                            transmittance *= Transmittance(CalculateDensityAlongRay(cubeHit.hitPoint, cubeHit.normal, densityStepSize));
                        }
                        light += Light(rayPos, rayDir) * transmittance;
                        transmittance = 0;
                        break;
                    }

                    // If light hits the floor it will be scattered in all directions (in hemisphere)
                    // Not sure how to handle this in real-time, so just break out of loop here
                    if (surfaceInfo.pos.y < -boundsSize.y / 2 + 0.05)
                    {
                        break;
                    }

                    float3 normal = CalculateNormal(surfaceInfo.pos);
                    if (dot(normal, rayDir) > 0) normal = -normal;

                    // Indicies of refraction
                    float iorA = travellingThroughFluid ? indexOfRefraction : iorAir;
                    float iorB =travellingThroughFluid ? iorAir : indexOfRefraction;

                    // Calculate reflection and refraction, and choose which path to follow
                    LightResponse lightResponse = CalculateReflectionAndRefraction(rayDir, normal, iorA, iorB);
                    float densityAlongRefractRay = CalculateDensityAlongRay(surfaceInfo.pos, lightResponse.refractDir, densityStepSize);
                    float densityAlongReflectRay = CalculateDensityAlongRay(surfaceInfo.pos, lightResponse.reflectDir, densityStepSize);
                    bool traceRefractedRay = densityAlongRefractRay * lightResponse.refractWeight > densityAlongReflectRay * lightResponse.reflectWeight;
                    travellingThroughFluid = traceRefractedRay != travellingThroughFluid;

                    // Approximate less interesting path
                    if (traceRefractedRay) light += Light(surfaceInfo.pos, lightResponse.reflectDir) * transmittance * Transmittance(densityAlongReflectRay) * lightResponse.reflectWeight;
                    else light += Light(surfaceInfo.pos, lightResponse.refractDir) * transmittance * Transmittance(densityAlongRefractRay) * lightResponse.refractWeight;

                    // Set up ray for more interesting path
                    rayPos = surfaceInfo.pos;
                    rayDir = traceRefractedRay ? lightResponse.refractDir : lightResponse.reflectDir;
                    transmittance *= (traceRefractedRay ? lightResponse.refractWeight : lightResponse.reflectWeight);
                }

                // Approximate remaining path
                float densityRemainder = CalculateDensityAlongRay(rayPos, rayDir, lightStepSize);
                light += Light(rayPos, rayDir) * transmittance * Transmittance(densityRemainder);

                return light;
            }


            float4 frag(v2f i) : SV_Target
            {
                return float4(RayMarchFluid(i.uv, viewMarchStepSize), 1);
            }
            
            ENDCG
        }
    }
}