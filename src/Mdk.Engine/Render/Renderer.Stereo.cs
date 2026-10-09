using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>How a frame holds the two eyes of a stereo view; <see cref="Renderer.StereoMode"/>.</summary>
public enum Stereo
{
    /// <summary>One view: no stereo.</summary>
    Off,
    /// <summary>Side by side: the left eye's image in the left half of the frame (3D TVs).</summary>
    Sbs,
    /// <summary>Full side by side: each eye's half with its own aspect, seen as it is (VR viewers,
    /// PC glasses).</summary>
    SbsFull,
    /// <summary>Full side by side with the halves exchanged, for crossed free viewing (the left eye's
    /// image in the right half).</summary>
    CrossView,
    /// <summary>Row-interleaved: even rows the left eye (row-interleaved displays, shutter glasses).</summary>
    Interlaced,
    /// <summary>Row-interleaved the other way: even rows the right eye.</summary>
    InterlacedReversed,
    /// <summary>Top and bottom (over-under): the left eye's image in the top half.</summary>
    Tab,
    /// <summary>Top and bottom with the halves exchanged: the right eye's image in the top half.</summary>
    TabReversed,
    /// <summary>A phone in a VR viewer: full side by side, each half bent against the lens
    /// (barrel), the HUD and the menus kept to the middle (<see cref="StereoModes.CanvasZoom"/>).</summary>
    Vr,
}

/// <summary>The stereo modes and their names (the options page, the console, --stereo).</summary>
public static class StereoModes
{
    /// <summary>The modes' names, by value (the options page's).</summary>
    public static readonly string[] Names =
        ["Off", "Side by side (half)", "Side by side (full)", "Cross view", "Interlaced", "Interlaced reverse", "Top and bottom", "Top and bottom rev.", "VR viewer (phone)"];

    /// <summary>The most the eyes go apart and the farthest their images converge (the sliders).</summary>
    public const float MaxSeparation = 2f;
    public const float MinConvergence = 1f;
    public const float MaxConvergence = 4000f;

    /// <summary>The mode of a name: the page's, or a short one (off, sbs, sbsfull, crossview, int, intr).</summary>
    public static bool TryParse(string name, out Stereo stereo)
    {
        switch (name.Trim().ToLowerInvariant().Replace("_", "").Replace(" ", ""))
        {
            case "off":
                stereo = Stereo.Off;
                return true;
            case "sbs" or "sidebyside":
                stereo = Stereo.Sbs;
                return true;
            case "sbsfull" or "sidebysidefull":
                stereo = Stereo.SbsFull;
                return true;
            case "cross" or "crossview":
                stereo = Stereo.CrossView;
                return true;
            case "int" or "interlaced":
                stereo = Stereo.Interlaced;
                return true;
            case "intr" or "interlacedreversed" or "interlacedreverse":
                stereo = Stereo.InterlacedReversed;
                return true;
            case "tab" or "topandbottom" or "overunder":
                stereo = Stereo.Tab;
                return true;
            case "tabr" or "tabreversed" or "topandbottomreversed" or "topandbottomreverse" or "overunderreversed":
                stereo = Stereo.TabReversed;
                return true;
            case "vr" or "vrviewer" or "vrviewerphone":
                stereo = Stereo.Vr;
                return true;
            default:
                stereo = Stereo.Off;
                return false;
        }
    }

    /// <summary>An eye's camera aspect in a frame of <paramref name="aspect"/>: full side by side
    /// halves it (16:9 → 8:9 an eye); the others keep it (the TV stretches half side by side back).</summary>
    public static float EyeAspect(Stereo stereo, float aspect) =>
        stereo is Stereo.SbsFull or Stereo.CrossView or Stereo.Vr ? aspect / 2f : aspect;

    /// <summary>The VR viewer's canvas: the share of an eye's view the HUD and the menus take.</summary>
    private const float VrCanvas = 0.7f;

    /// <summary>The canvas' size in an eye's view: smaller in the VR viewer, whose lenses blur the edges.</summary>
    public static float CanvasZoom(Stereo stereo) => stereo == Stereo.Vr ? VrCanvas : 1f;
}

/// <summary>The frame's stereo state: the settings (<see cref="StereoMode"/>, the eyes' separation
/// and convergence), and the eye targets, each holding one eye's whole frame (scene, insets, canvas).
/// <code>
///   scene through the left eye ──► eye 0 ─┐
///   scene through the right eye ─► eye 1 ─┴─ "stereo" ─► frame ─► swapchain, or the screenshot
/// </code></summary>
public sealed unsafe partial class Renderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct StereoUniforms
    {
        /// <summary>x: 0 side by side, 1 interlaced, 2 top and bottom, 3 the VR viewer; y: 1 the eyes
        /// exchanged (crossview, the reversed interlaced and top and bottom); z: an eye's aspect.</summary>
        public Vector4 Mode;
    }

    private const uint StereoEyes = 2;

    /// <summary>The eyes of a stereo frame, the frame's size (the same as the frame's targets).</summary>
    private readonly SDL_GPUTexture*[] _eyes = new SDL_GPUTexture*[StereoEyes];

    /// <summary>Where the scene draws: the frame's target, or the eye being rendered.</summary>
    private SDL_GPUTexture* _frame;

    /// <summary>The eye being drawn (−1 the left, +1 the right, 0: no stereo; the insets).</summary>
    private float _eyeSide;

    /// <summary>How the frame holds the two eyes; <see cref="Stereo.Off"/> without stereo.</summary>
    public Stereo StereoMode { get; set; }

    /// <summary>The eyes' distance apart (MDK units) and the distance their images converge at
    /// (the same point on the screen in both; 0 or less: parallel rays, converging at infinity).</summary>
    public float StereoSeparation { get; set; } = 0.25f;
    public float StereoConvergence { get; set; } = 10f;

    /// <summary>The eye targets of the frame's size, made (or freed) when stereo is switched.</summary>
    private void EnsureEyes()
    {
        if ((StereoMode != Stereo.Off) == (_eyes[0] != null))
        {
            return;
        }

        ReleaseEyes();
        if (StereoMode == Stereo.Off)
        {
            return;
        }

        for (var eye = 0; eye < _eyes.Length; eye++)
        {
            _eyes[eye] = CreateTexture(ColourFormat, _targetWidth, _targetHeight,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        }
    }

    private void ReleaseEyes()
    {
        for (var eye = 0; eye < _eyes.Length; eye++)
        {
            if (_eyes[eye] != null)
            {
                SDL_ReleaseGPUTexture(_device, _eyes[eye]);
                _eyes[eye] = null;
            }
        }
    }

    /// <summary>Draws one eye's whole frame through its view into its target.</summary>
    private void RenderEye(SDL_GPUCommandBuffer* commands, View view, Vector4 clearColour, int eye, float side)
    {
        _frame = _eyes[eye];
        _eyeSide = side;
        RenderScene(commands, view.Eye(side, StereoSeparation, StereoConvergence), clearColour);
    }

    /// <summary>The two eyes into the frame: side by side or top and bottom (an eye a half;
    /// crossview and the reversed top and bottom: exchanged), or interlaced (an eye the even rows;
    /// reversed: the odd ones).</summary>
    private void CompositeStereo(SDL_GPUCommandBuffer* commands)
    {
        var layout = StereoMode switch
        {
            Stereo.Interlaced or Stereo.InterlacedReversed => 1f,
            Stereo.Tab or Stereo.TabReversed => 2f,
            Stereo.Vr => 3f,
            _ => 0f,
        };
        var swapped = StereoMode is Stereo.CrossView or Stereo.InterlacedReversed or Stereo.TabReversed;
        var uniforms = new StereoUniforms { Mode = new Vector4(layout, swapped ? 1f : 0f, AspectRatio, 0f) };

        var samplers = stackalloc SDL_GPUTextureSamplerBinding[(int)StereoEyes];
        samplers[0] = new SDL_GPUTextureSamplerBinding { texture = _eyes[0], sampler = _mipSampler };
        samplers[1] = new SDL_GPUTextureSamplerBinding { texture = _eyes[1], sampler = _mipSampler };

        var colourTarget = new SDL_GPUColorTargetInfo
        {
            texture = _target,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(commands, &colourTarget, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PipelineKey("stereo", Pass.DoubleSided, Primitive.Triangles, Geometry.Post, 1, Output.Colour)));
        SDL_BindGPUFragmentSamplers(pass, 0, samplers, StereoEyes);
        SDL_PushGPUFragmentUniformData(commands, 0, (IntPtr)(&uniforms), (uint)sizeof(StereoUniforms));
        DrawPrimitives(pass, ScreenTriangle, 0, Primitive.Triangles);
        SDL_EndGPURenderPass(pass);
    }
}
