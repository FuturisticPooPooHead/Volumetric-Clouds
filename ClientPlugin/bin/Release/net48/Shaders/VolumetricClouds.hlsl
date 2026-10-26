// ClientPlugin/Shaders/VolumetricClouds.hlsl
// SM 5.0 Volumetric Planetary Cloud Raymarching Pixel Shader for Space Engineers 1 (VRage.Render11)

#include <Postprocess/PostprocessBase.hlsli>
#include <Frame.hlsli>

// =================================================================================================
// Constant Buffers & Resource Bindings
// =================================================================================================

cbuffer CloudConstants : register(b8)
{
    float4 CenterInnerRadius;  // xyz = Camera-relative planet center (world space), w = Inner radius
    float4 PoleOuterRadius;    // xyz = Planet normalized rotation pole axis, w = Outer radius
    float4 WindOffsetLow;      // xy = Low cloud layer UV offset, zw = Wind direction & speed
    float4 WindOffsetMidHigh;  // xy = Mid cloud layer UV offset, zw = High cloud layer UV offset
    float4 CoverageLayers;     // x = Low coverage %, y = Mid coverage %, z = High coverage %, w = Global density multiplier
    float4 SunDirection;       // xyz = Normalized vector toward sun, w = Direct sunlight intensity
    float4 AmbientColor;       // rgb = Ground & atmospheric ambient tint, w = Precipitation / darkness factor
    float4 StepQuality;        // x = Primary step count (e.g. 24..96), y = Shadow step count (e.g. 4..8), z = Dither strength, w = Reserved / LoD
};

// Texture Resources
Texture3D<float4> CloudNoiseTex       : register(t20); // r = Perlin-Worley base, gba = High-freq Worley detail
Texture2D<float4> WeatherMapTex       : register(t21); // r = Low cover, g = Mid cover, b = High cover, a = Precipitation
Texture2D<float>  DepthTex            : register(t22); // Resolved GBuffer depth buffer

// Samplers
SamplerState LinearWrapSampler        : register(s0);
SamplerState LinearClampSampler       : register(s1);
SamplerComparisonState ShadowSampler  : register(s2);

// =================================================================================================
// Constants & Helper Functions
// =================================================================================================

static const float PI = 3.14159265359f;
static const float INV_FOUR_PI = 0.0795774715459f;
static const float DENSITY_EPSILON = 0.001f;

// Screen-space interleaved gradient noise for sub-step temporal / spatial jitter
float InterleavedGradientNoise(float2 screenPos)
{
    float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    return frac(magic.z * frac(dot(screenPos, magic.xy)));
}

// Ray-Sphere intersection returning nearest (t0) and farthest (t1) distances along the ray
bool RaySphere(float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius, out float t0, out float t1)
{
    float3 oc = rayOrigin - sphereCenter;
    float b = dot(oc, rayDir);
    float c = dot(oc, oc) - (sphereRadius * sphereRadius);
    float discriminant = b * b - c;

    if (discriminant < 0.0f)
    {
        t0 = -1.0f;
        t1 = -1.0f;
        return false;
    }

    float sqrtD = sqrt(discriminant);
    t0 = -b - sqrtD;
    t1 = -b + sqrtD;
    return true;
}

// Computes the dual spherical shell boundary intervals [tMin, tMax]
bool ComputeCloudShellInterval(
    float3 rayOrigin,
    float3 rayDir,
    float3 center,
    float rInner,
    float rOuter,
    out float tNear,
    out float tFar)
{
    float tOuter0, tOuter1;
    if (!RaySphere(rayOrigin, rayDir, center, rOuter, tOuter0, tOuter1) || tOuter1 < 0.0f)
    {
        tNear = 0.0f;
        tFar = 0.0f;
        return false;
    }

    float tInner0, tInner1;
    bool hitInner = RaySphere(rayOrigin, rayDir, center, rInner, tInner0, tInner1) && tInner1 > 0.0f;

    // Clamp behind-camera starts to 0
    tOuter0 = max(tOuter0, 0.0f);

    if (!hitInner || tInner1 < 0.0f)
    {
        // Ray does not intersect the inner void; it passes through outer shell only
        tNear = tOuter0;
        tFar = tOuter1;
        return tFar > tNear;
    }

    // Ray intersects inner sphere
    if (tOuter0 < tInner0)
    {
        // Camera is outside or inside upper shell looking towards the planet ground
        tNear = tOuter0;
        tFar = max(0.0f, tInner0);
    }
    else
    {
        // Camera is beneath inner atmosphere or piercing upward
        tNear = max(0.0f, tInner1);
        tFar = tOuter1;
    }

    return tFar > tNear;
}

// Dual-Lobe Henyey-Greenstein Phase Function (Silver lining forward lobe + flank back-scattering)
float HenyeyGreenstein(float cosAngle, float g)
{
    float g2 = g * g;
    return INV_FOUR_PI * ((1.0f - g2) / pow(max(1.0f + g2 - 2.0f * g * cosAngle, 0.0001f), 1.5f));
}

float DualLobePhase(float cosAngle)
{
    float forwardLobe = HenyeyGreenstein(cosAngle, 0.75f);
    float backLobe    = HenyeyGreenstein(cosAngle, -0.25f);
    return lerp(backLobe, forwardLobe, 0.7f);
}

// Powder sugar multiple-scattering empirical approximation
float PowderSugarEffect(float opticalDepth)
{
    return 1.0f - exp(-2.0f * opticalDepth);
}

// Height-dependent cloud density gradient
float ComputeHeightGradient(float normalizedHeight, float lowCover, float midCover, float highCover)
{
    // Layer 1: Low Stratus / Fog (0.0 to 0.3)
    float stratus = smoothstep(0.0f, 0.1f, normalizedHeight) * (1.0f - smoothstep(0.2f, 0.35f, normalizedHeight)) * lowCover;
    
    // Layer 2: Mid Cumulus (0.2 to 0.7)
    float cumulus = smoothstep(0.18f, 0.35f, normalizedHeight) * (1.0f - smoothstep(0.6f, 0.75f, normalizedHeight)) * midCover;
    
    // Layer 3: High Cirrus (0.65 to 1.0)
    float cirrus  = smoothstep(0.65f, 0.8f, normalizedHeight) * (1.0f - smoothstep(0.95f, 1.0f, normalizedHeight)) * highCover;

    return max(stratus, max(cumulus, cirrus));
}

// Maps spherical 3D position to equirectangular UV for weather sampling
float2 PositionToEquirectangularUV(float3 p, float3 center, float3 pole)
{
    float3 relPos = normalize(p - center);
    float phi = asin(clamp(dot(relPos, pole), -1.0f, 1.0f));
    
    // Construct local tangent basis
    float3 arbitrary = abs(pole.y) < 0.999f ? float3(0.0f, 1.0f, 0.0f) : float3(1.0f, 0.0f, 0.0f);
    float3 east = normalize(cross(pole, arbitrary));
    float3 north = cross(east, pole);

    float lambda = atan2(dot(relPos, east), dot(relPos, north));
    float u = (lambda / (2.0f * PI)) + 0.5f;
    float v = (phi / PI) + 0.5f;
    return float2(u, v);
}

// Samples volumetric cloud density at camera-relative position p
float SampleCloudDensity(float3 p, float3 center, float rInner, float rOuter, float lod)
{
    float distFromCenter = length(p - center);
    if (distFromCenter < rInner || distFromCenter > rOuter)
        return 0.0f;

    float normHeight = (distFromCenter - rInner) / max(rOuter - rInner, 1.0f);

    float2 weatherUV = PositionToEquirectangularUV(p, center, PoleOuterRadius.xyz);
    float4 weather = WeatherMapTex.SampleLevel(LinearWrapSampler, weatherUV + WindOffsetLow.xy, 0);

    float heightFactor = ComputeHeightGradient(
        normHeight,
        CoverageLayers.x * weather.r,
        CoverageLayers.y * weather.g,
        CoverageLayers.z * weather.b
    );

    if (heightFactor <= 0.01f)
        return 0.0f;

    // 3D Noise Coordinates animated with altitude-dependent wind shear
    float3 sampleCoords = (p - center) * 0.00015f;
    sampleCoords.xy += WindOffsetLow.xy * (1.0f - normHeight * 0.5f);

    float4 noiseSample = CloudNoiseTex.SampleLevel(LinearWrapSampler, sampleCoords, lod);

    // Perlin-Worley base shape
    float baseDensity = noiseSample.r;
    
    // High-frequency detail erosion via Worley GBA channels
    float highFreqDetail = dot(noiseSample.gba, float3(0.625f, 0.25f, 0.125f));
    float erodedDensity = baseDensity - (1.0f - highFreqDetail) * (1.0f - normHeight) * 0.35f;

    float cloudCoverageThreshold = 1.0f - (CoverageLayers.w * heightFactor);
    float density = smoothstep(cloudCoverageThreshold, cloudCoverageThreshold + 0.2f, erodedDensity);

    return density * CoverageLayers.w;
}

// Light march towards sun to compute optical shadow depth
float MarchShadowRay(float3 samplePos, float3 center, float rInner, float rOuter, float stepSize, int stepCount)
{
    float3 lightDir = SunDirection.xyz;
    float totalOpticalDepth = 0.0f;
    float3 curPos = samplePos + lightDir * (stepSize * 0.5f);

    for (int i = 0; i < stepCount; ++i)
    {
        float d = SampleCloudDensity(curPos, center, rInner, rOuter, 1.5f);
        totalOpticalDepth += d * stepSize;
        curPos += lightDir * stepSize;
    }

    return totalOpticalDepth;
}

// =================================================================================================
// Pixel Shader Entry Point
// =================================================================================================

float4 __pixel_shader(PostprocessVertex input) : SV_Target0
{
    float2 uv = screen_to_uv(input.position.xy);

    // Reconstruct camera-relative view ray
    float3 screenRay = compute_screen_ray(uv);
    float3 rayDir = normalize(mul(screenRay, (float3x3)get_view_to_world_matrix()));
    float3 rayOrigin = float3(0.0f, 0.0f, 0.0f); // Camera position is local (0,0,0) in camera-relative frame

    float3 planetCenter = CenterInnerRadius.xyz;
    float rInner = CenterInnerRadius.w;
    float rOuter = PoleOuterRadius.w;

    // Check intersection with planetary atmosphere cloud shell
    float tNear, tFar;
    if (!ComputeCloudShellInterval(rayOrigin, rayDir, planetCenter, rInner, rOuter, tNear, tFar))
    {
        return float4(0.0f, 0.0f, 0.0f, 0.0f);
    }

    // Hardware scene depth occlusion
    float hwDepth = DepthTex.SampleLevel(LinearClampSampler, uv, 0).r;
    float3 sceneWorldPos = ReconstructWorldPosition(hwDepth, uv);
    float sceneDistance = length(sceneWorldPos); // Distance from camera

    // Clip cloud march interval against solid foreground objects (terrain, grids, cockpit)
    if (sceneDistance < tNear)
    {
        return float4(0.0f, 0.0f, 0.0f, 0.0f);
    }
    tFar = min(tFar, sceneDistance);

    if (tFar <= tNear)
    {
        return float4(0.0f, 0.0f, 0.0f, 0.0f);
    }

    // Dithering & step configuration
    int primarySteps = (int)clamp(StepQuality.x, 16.0f, 128.0f);
    int shadowSteps  = (int)clamp(StepQuality.y, 2.0f, 16.0f);
    float ditherJitter = InterleavedGradientNoise(input.position.xy) * StepQuality.z;

    float totalMarchDistance = tFar - tNear;
    float stepSize = totalMarchDistance / (float)primarySteps;

    float tCurrent = tNear + (stepSize * ditherJitter);
    float cosAngle = dot(rayDir, SunDirection.xyz);
    float phaseValue = DualLobePhase(cosAngle);

    float transmittance = 1.0f;
    float3 accumulatedColor = float3(0.0f, 0.0f, 0.0f);

    float3 sunLight = SunDirection.www;
    float3 ambientLight = AmbientColor.rgb;
    float absorptionCoeff = 0.08f;
    float scatteringCoeff = 0.15f;
    float extinctionCoeff = absorptionCoeff + scatteringCoeff;

    float shadowStepSize = (rOuter - rInner) / (float)shadowSteps * 0.4f;

    // Primary volumetric marching loop
    for (int stepIdx = 0; stepIdx < primarySteps; ++stepIdx)
    {
        if (tCurrent >= tFar || transmittance < 0.01f)
            break;

        float3 samplePos = rayOrigin + rayDir * tCurrent;
        float density = SampleCloudDensity(samplePos, planetCenter, rInner, rOuter, 0.0f);

        if (density > DENSITY_EPSILON)
        {
            // March towards sun for self-shadowing and light attenuation
            float shadowOpticalDepth = MarchShadowRay(samplePos, planetCenter, rInner, rOuter, shadowStepSize, shadowSteps);
            float lightTransmittance = exp(-shadowOpticalDepth * extinctionCoeff);

            // Powder sugar effect illuminates dense multi-scattered pockets
            float powder = PowderSugarEffect(density * stepSize * 0.2f);
            
            // Direct sunlight + indirect atmospheric bounce
            float3 directLight = sunLight * lightTransmittance * phaseValue * powder;
            float3 incomingLight = directLight + ambientLight;

            // Beer-Lambert transmittance law
            float sampleExtinction = exp(-density * extinctionCoeff * stepSize);
            float3 stepScattering = incomingLight * (density * scatteringCoeff);
            
            // Integrated segment accumulation
            accumulatedColor += transmittance * (stepScattering - stepScattering * sampleExtinction) / max(density * extinctionCoeff, 0.00001f);
            transmittance *= sampleExtinction;
        }

        tCurrent += stepSize;
    }

    float cloudAlpha = 1.0f - transmittance;
    return float4(accumulatedColor, cloudAlpha);
}
