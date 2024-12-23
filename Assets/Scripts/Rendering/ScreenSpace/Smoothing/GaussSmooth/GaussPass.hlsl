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

sampler2D _MainTex;
float4 _MainTex_TexelSize;
float radius;
int useWorldSpaceRadius;
int maxScreenSpaceRadius;
float strength;
float3 smoothMask;

float Calculate1DGaussianKernel(int x, float sigma)
{
    float c = 2 * sigma * sigma;
    return exp(-x * x / c);
}

// Calculate the number of pixels covered by a world-space radius at given dst from camera
float CalculateScreenSpaceRadius(float worldRadius, float depth, int imageWidth)
{
    // Thanks to x.com/FreyaHolmer/status/1820157167682388210
    float widthScale = UNITY_MATRIX_P._m00; // smaller values correspond to higher fov (objects appear smaller)
    float pxPerMeter = (imageWidth * widthScale) / (2 * depth);
    return abs(pxPerMeter) * worldRadius;
}


float4 ViewPos(float2 uv, float depth)
{
    float3 origin = 0;
    float3 viewVector = mul(unity_CameraInvProjection, float4(uv.xy * 2 - 1, 0, -1));
    float3 dir = normalize(viewVector);
    return float4(origin + dir * depth, depth);
}

float4 CalculateBlur1D(float2 uv, float2 dir)
{
    float4 original = tex2D(_MainTex, uv);
    float depth = original.a;

    float4 sum = 0;
    float wSum = 0;
    float2 texelDelta = _MainTex_TexelSize.xy * dir;

    float3 viewPos = ViewPos(uv, depth);

    int radiusInt = ceil(radius);
    float sigma = max(0.0000001, radius / (6 * max(0.01, strength)));

    if (useWorldSpaceRadius == 1)
    {
        float radiusFloat = CalculateScreenSpaceRadius(radius, depth, _MainTex_TexelSize.z);
        if (radiusInt <= 1 && radius > 0) radius = 2;
        radiusInt = min(maxScreenSpaceRadius, radius);
        float fR = max(0, radius - radiusFloat); // use fractional part of radius in sigma calc to avoid harsh boundaries where radius integer changes
        sigma = max(0.0000001, (radius - fR) / (6 * max(0.001, strength)));
    }


    for (int x = -radiusInt; x <= radiusInt; x++)
    {
        float2 sampleUV = uv + texelDelta * x;
        float4 sample = tex2Dlod(_MainTex, float4(sampleUV, 0, 0));
        float depth = sample.a;

        if (depth < 10000)
        {
            float w = Calculate1DGaussianKernel(x, sigma);
            sum += sample * w;
            wSum += w;
        }
    }

    float3 blurResult = (sum / wSum).rgb;

    return float4(lerp(original.rgb, blurResult, smoothMask), depth);
}
