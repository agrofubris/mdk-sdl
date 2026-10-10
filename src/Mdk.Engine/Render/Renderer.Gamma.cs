using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>The display's gamma: the finished frame (scene, HUD, menus; both eyes) brightened or
/// darkened before it's shown or saved. At 1 nothing is drawn: the frame shows as it is.
/// <code>
///   frame ─► "gamma" ─► graded frame ─► swapchain, or the screenshot
/// </code></summary>
public sealed unsafe partial class Renderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GammaUniforms
    {
        /// <summary>x: 1 / gamma.</summary>
        public Vector4 Inverse;
    }

    public const float MinGamma = 0.5f;
    public const float MaxGamma = 2f;
    public const float DefaultGamma = 1f;
    private const uint GammaSamplers = 1;

    /// <summary>Above 1 brighter (the darks lifted), below 1 darker.</summary>
    public float Gamma { get; set; } = DefaultGamma;

    /// <summary>The frame after the gamma, its source's size; made when the gamma isn't 1 and
    /// remade when the size changes.</summary>
    private SDL_GPUTexture* _graded;
    private uint _gradedWidth;
    private uint _gradedHeight;

    /// <summary>The frame shown and saved: the source given to <see cref="ApplyGamma"/>, or the
    /// graded one.</summary>
    private SDL_GPUTexture* _shownSource;
    private uint _shownWidth;
    private uint _shownHeight;

    /// <summary>The frame through the gamma into <see cref="_graded"/> (at 1: the source as it is);
    /// the source is the scene's frame, or the Leia pair the weave would take.</summary>
    private void ApplyGamma(SDL_GPUCommandBuffer* commands, SDL_GPUTexture* source, uint width, uint height)
    {
        _shownSource = source;
        _shownWidth = width;
        _shownHeight = height;
        if (Gamma == DefaultGamma)
        {
            return;
        }

        if (_graded == null || _gradedWidth != width || _gradedHeight != height)
        {
            ReleaseGraded();
            _graded = CreateTexture(ColourFormat, width, height,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
            (_gradedWidth, _gradedHeight) = (width, height);
        }

        var uniforms = new GammaUniforms { Inverse = new Vector4(1f / Gamma, 0f, 0f, 0f) };
        var sampler = new SDL_GPUTextureSamplerBinding { texture = source, sampler = _mipSampler };
        var colourTarget = new SDL_GPUColorTargetInfo
        {
            texture = _graded,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(commands, &colourTarget, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PipelineKey("gamma", Pass.DoubleSided, Primitive.Triangles, Geometry.Post, 1, Output.Colour)));
        SDL_BindGPUFragmentSamplers(pass, 0, &sampler, GammaSamplers);
        SDL_PushGPUFragmentUniformData(commands, 0, (IntPtr)(&uniforms), (uint)sizeof(GammaUniforms));
        DrawPrimitives(pass, ScreenTriangle, 0, Primitive.Triangles);
        SDL_EndGPURenderPass(pass);
        _shownSource = _graded;
    }

    private void ReleaseGraded()
    {
        if (_graded == null)
        {
            return;
        }

        SDL_ReleaseGPUTexture(_device, _graded);
        _graded = null;
        _gradedWidth = _gradedHeight = 0;
    }
}
