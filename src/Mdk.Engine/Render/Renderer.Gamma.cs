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

    /// <summary>The frame after the gamma, the frame's size; made when the gamma isn't 1.</summary>
    private SDL_GPUTexture* _graded;

    /// <summary>The frame shown and saved: the target, or the graded one.</summary>
    private SDL_GPUTexture* _shown;

    /// <summary>The frame through the gamma into <see cref="_graded"/> (at 1: the target as it is).</summary>
    private void ApplyGamma(SDL_GPUCommandBuffer* commands)
    {
        _shown = _target;
        if (Gamma == DefaultGamma)
        {
            return;
        }

        if (_graded == null)
        {
            _graded = CreateTexture(ColourFormat, _targetWidth, _targetHeight,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        }

        var uniforms = new GammaUniforms { Inverse = new Vector4(1f / Gamma, 0f, 0f, 0f) };
        var sampler = new SDL_GPUTextureSamplerBinding { texture = _target, sampler = _mipSampler };
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
        _shown = _graded;
    }

    private void ReleaseGraded()
    {
        if (_graded == null)
        {
            return;
        }

        SDL_ReleaseGPUTexture(_device, _graded);
        _graded = null;
    }
}
