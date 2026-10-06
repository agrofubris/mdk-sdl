// The enhanced look's post-processing, over the lit scene: ambient occlusion from the camera's
// depth (fading in the haze), then a little glow (the scene's blurred mips, screen-blended).
//
//   scene (mips) --+--------------- colour x occlusion --+- screen -> frame
//   camera depth --' occlusion                   glow ---'
//
// Occlusion (Alchemy): points on a disc in the surface's plane around the pixel, looked up in the
// depth buffer; what rises above the plane nearby occludes.
//
//          q (depth buffer)
//          o
//         /  v: occludes by max(v.n, 0) / v.v
//   -----o---------  plane through p, normal n
//        p

#include "bindings.hlsli"

#define TAU 6.28318530718
#define GOLDEN_ANGLE 2.39996323
#define GAMMA 2.2
#define SAMPLES 12
#define FAR 1.0

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOut vs_main(uint id : SV_VertexID)
{
    // (-1,-1), (3,-1), (-1,3): covers the screen.
    float2 clip = float2(id == 1 ? 3.0 : -1.0, id == 2 ? 3.0 : -1.0);
    VertexOut output;
    output.position = float4(clip, 0.0, 1.0);
    output.uv = float2(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5);
    return output;
}

COMBINED_SAMPLER(0) Texture2D<float4> scene_texture : register(t0, space2);
COMBINED_SAMPLER(0) SamplerState scene_sampler : register(s0, space2);
COMBINED_SAMPLER(1) Texture2D<float> depth_texture : register(t1, space2);
COMBINED_SAMPLER(1) SamplerState depth_sampler : register(s1, space2);

cbuffer PostUniforms : register(b0, space3)
{
    // Clip space to the point relative to the camera (world axes), and back.
    float4x4 clip_to_view;
    float4x4 view_to_clip;
    // xy: the frame's size in pixels; zw: one pixel in uv.
    float4 screen;
    // x: radius (units); y: strength; z: the haze's density per unit.
    float4 occlusion;
    // x: bloom (all colours); y: threshold (bright colours); z: intensity; w: the blurred mips.
    float4 glow;
};

float3 to_linear(float3 c)
{
    return pow(max(c, 0.0), GAMMA);
}

float3 to_srgb(float3 c)
{
    return pow(max(c, 0.0), 1.0 / GAMMA);
}

float depth_at(float2 uv)
{
    int2 pixel = clamp(int2(uv * screen.xy), int2(0, 0), int2(screen.xy) - 1);
    return depth_texture.Load(int3(pixel, 0));
}

float3 point_at(float2 uv)
{
    float4 p = mul(clip_to_view, float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth_at(uv), 1.0));
    return p.xyz / p.w;
}

// The nearer of two differences (no normal across a depth edge).
float3 nearer(float3 a, float3 b)
{
    return dot(a, a) < dot(b, b) ? a : b;
}

float ambient_occlusion(float2 uv, float3 p, float2 pixel)
{
    float3 dx = nearer(point_at(uv + float2(screen.z, 0.0)) - p, p - point_at(uv - float2(screen.z, 0.0)));
    float3 dy = nearer(point_at(uv + float2(0.0, screen.w)) - p, p - point_at(uv - float2(0.0, screen.w)));
    float3 n = normalize(cross(dx, dy));
    if (dot(n, p) > 0.0)
    {
        n = -n;
    }

    // A disc in the plane, turned per pixel (interleaved gradient noise) to spread the samples.
    float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    float3 t = normalize(cross(n, abs(n.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0)));
    float3 b = cross(n, t);
    float radius = occlusion.x;
    float bias = 0.02 * radius;
    float epsilon = 0.01 * radius * radius;
    float sum = 0.0;
    [loop] for (int i = 0; i < SAMPLES; i++)
    {
        float angle = noise * TAU + i * GOLDEN_ANGLE;
        float3 s = p + (t * cos(angle) + b * sin(angle)) * (radius * (i + noise) / SAMPLES);
        float4 clip = mul(view_to_clip, float4(s, 1.0));
        if (clip.w <= 0.0)
        {
            continue;
        }

        float2 at = float2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
        if (any(at < 0.0) || any(at > 1.0) || depth_at(at) >= FAR)
        {
            continue;
        }

        float3 v = point_at(at) - p;
        float vv = dot(v, v);
        float near = saturate(1.0 - vv / (4.0 * radius * radius));
        sum += max(dot(v, n) - bias, 0.0) / (vv + epsilon) * radius * near;
    }

    return saturate(1.0 - occlusion.y * sum / SAMPLES);
}

float4 ps_main(VertexOut input) : SV_Target
{
    float2 uv = input.uv;
    float3 colour = to_linear(scene_texture.SampleLevel(scene_sampler, uv, 0.0).rgb);

    // The sky (nothing in the depth buffer) has no occlusion.
    if (depth_at(uv) < FAR)
    {
        float3 p = point_at(uv);
        float clear = exp(-occlusion.z * length(p));
        colour *= lerp(1.0, ambient_occlusion(uv, p, input.position.xy), clear);
    }

    float3 blurred = to_linear((scene_texture.SampleLevel(scene_sampler, uv, glow.w).rgb
        + scene_texture.SampleLevel(scene_sampler, uv, glow.w + 2.0).rgb) * 0.5);
    float3 shine = saturate((blurred * glow.x + max(blurred - glow.y, 0.0)) * glow.z);
    colour = 1.0 - (1.0 - colour) * (1.0 - shine);
    return float4(to_srgb(colour), 1.0);
}
