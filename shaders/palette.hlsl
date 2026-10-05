// Paletted surfaces in the original look: palette indices (R8) looked up in a 256x1 palette,
// nearest texels, index 0 transparent. Flat-coloured surfaces skip the lookup.
// SDL_GPU register spaces: vertex uniforms space1, fragment resources space2, fragment uniforms space3.

cbuffer VertexUniforms : register(b0, space1)
{
    float4x4 view_projection;
};

struct VertexIn
{
    float3 position : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOut vs_main(VertexIn input)
{
    VertexOut output;
    output.position = mul(view_projection, float4(input.position, 1.0));
    output.uv = input.uv;
    return output;
}

Texture2D<float> index_texture : register(t0, space2);
SamplerState index_sampler : register(s0, space2);
Texture2D<float4> palette_texture : register(t1, space2);
SamplerState palette_sampler : register(s1, space2);

cbuffer FragmentUniforms : register(b0, space3)
{
    float4 colour;
    int textured;
    // Animated textures: frames stacked vertically.
    int frame_count;
    int frame;
    int unused;
};

float4 ps_main(VertexOut input) : SV_Target
{
    if (textured == 0)
    {
        return colour;
    }

    float2 uv = input.uv;
    if (frame_count > 1)
    {
        uv.y = (frac(uv.y) + frame) / frame_count;
    }

    int index = (int)round(index_texture.Sample(index_sampler, uv) * 255.0);
    if (index == 0)
    {
        discard;
    }

    return float4(palette_texture.Load(int3(index, 0, 0)).rgb, 1.0) * colour;
}
