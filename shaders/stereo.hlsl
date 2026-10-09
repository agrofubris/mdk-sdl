// The stereo composite (Renderer.Stereo.cs): the two eyes' frames (drawn whole, each through its
// own camera) into one frame:
//
//   side by side:   the left eye in the left half, the right eye in the right half (3D TVs, VR)
//   interlaced:     even rows one eye, odd rows the other (row-interleaved displays, shutter glasses)
//   top and bottom: the left eye in the top half, the right eye in the bottom half (over-under)
//
// mode.y exchanges the eyes: crossed free viewing (crossview), or the other halves/rows (the
// reversed interlaced and top and bottom; displays whose rows or halves start with the right eye).

#include "bindings.hlsli"

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

COMBINED_SAMPLER(0) Texture2D<float4> left_texture : register(t0, space2);
COMBINED_SAMPLER(0) SamplerState left_sampler : register(s0, space2);
COMBINED_SAMPLER(1) Texture2D<float4> right_texture : register(t1, space2);
COMBINED_SAMPLER(1) SamplerState right_sampler : register(s1, space2);

cbuffer StereoUniforms : register(b0, space3)
{
    // x: 0 side by side, 1 interlaced, 2 top and bottom; y: 1 the eyes exchanged.
    float4 mode;
};

float4 ps_main(VertexOut input) : SV_Target
{
    bool swapped = mode.y > 0.5;
    if (mode.x < 0.5)
    {
        // Each half gets an eye whole.
        float2 uv = float2(frac(input.uv.x * 2.0), input.uv.y);
        bool left_eye = (input.uv.x < 0.5) != swapped;
        return left_eye ? left_texture.SampleLevel(left_sampler, uv, 0.0)
                        : right_texture.SampleLevel(right_sampler, uv, 0.0);
    }

    if (mode.x < 1.5)
    {
        // Even rows one eye (the odd ones the other).
        bool even = ((int)input.position.y & 1) == 0;
        bool left_eye = even != swapped;
        return left_eye ? left_texture.SampleLevel(left_sampler, input.uv, 0.0)
                        : right_texture.SampleLevel(right_sampler, input.uv, 0.0);
    }

    // Top and bottom: each half gets an eye whole.
    float2 uv = float2(input.uv.x, frac(input.uv.y * 2.0));
    bool left_eye = (input.uv.y < 0.5) != swapped;
    return left_eye ? left_texture.SampleLevel(left_sampler, uv, 0.0)
                    : right_texture.SampleLevel(right_sampler, uv, 0.0);
}
