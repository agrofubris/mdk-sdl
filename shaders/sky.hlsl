// The sky: the original blits a panorama as a 2D backdrop. It scrolls horizontally with the yaw
// (wrap_width pixels for 360 degrees) and vertically with the pitch, horizon_row at eye level; above
// and below it the screen is filled with top and bottom colours. Drawn as one screen-filling
// triangle before the level, without depth.

#include "panorama.hlsli"

cbuffer VertexUniforms : register(b0, space1)
{
    // Clip space to a world direction (the camera's rotation and projection, inverted).
    float4x4 clip_to_direction;
};

struct VertexOut
{
    float4 position : SV_Position;
    float3 direction : TEXCOORD0;
};

VertexOut vs_main(uint id : SV_VertexID)
{
    // (-1,-1), (3,-1), (-1,3): covers the screen.
    float2 clip = float2(id == 1 ? 3.0 : -1.0, id == 2 ? 3.0 : -1.0);
    VertexOut output;
    output.position = float4(clip, 0.0, 1.0);
    float4 direction = mul(clip_to_direction, float4(clip, 1.0, 1.0));
    output.direction = direction.xyz / direction.w;
    return output;
}

float4 ps_main(VertexOut input) : SV_Target
{
    float2 pixel = panorama_pixel(normalize(input.direction));
    int index;
    if (pixel.y < 0.0)
    {
        index = top_colour;
    }
    else if (pixel.y >= panorama_height)
    {
        index = bottom_colour;
    }
    else
    {
        index = panorama_index(int(pixel.x), int(pixel.y));
    }

    return float4(palette_texture.Load(int3(index, 0, 0)).rgb, 1.0);
}
