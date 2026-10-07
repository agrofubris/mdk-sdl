using System.Drawing;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>What an inset shows: the frame's scene again through its own camera, or only what was
/// drawn between <see cref="Renderer.BeginInset"/> and <see cref="Renderer.EndInset"/>.</summary>
public enum InsetContent { Scene, Own }

/// <summary>Whether an inset is drawn under the 2D canvas or over it.</summary>
public enum InsetLayer { UnderCanvas, OverCanvas }

/// <summary>Insets: 3D views in rectangles of the canvas (sniper mode's round cameras and clip).
/// <code>
///   scene ──► insets under the canvas ──► canvas ──► insets over the canvas
///             (depth cleared, scissored)              (depth cleared, scissored)
/// </code></summary>
public sealed unsafe partial class Renderer
{
    private sealed class Inset
    {
        public View View;
        public RectangleF Area;
        public InsetContent Content;
        public InsetLayer Layer;
        public readonly List<DrawCommand> Commands = [];
    }

    private readonly List<Inset> _insets = [];
    /// <summary>Insets of earlier frames, reused.</summary>
    private readonly Stack<Inset> _spareInsets = new();
    private Inset? _open;

    /// <summary>Where draws go: the open inset's own list, or the scene's.</summary>
    private List<DrawCommand> Queue => _open?.Commands ?? _commands;

    /// <summary>Starts an inset in <paramref name="area"/> (canvas units) seen through
    /// <paramref name="view"/>; with <see cref="InsetContent.Own"/>, draws until
    /// <see cref="EndInset"/> go into it.</summary>
    public void BeginInset(View view, RectangleF area, InsetContent content, InsetLayer layer)
    {
        _open = _spareInsets.Count > 0 ? _spareInsets.Pop() : new Inset();
        (_open.View, _open.Area, _open.Content, _open.Layer) = (view, area, content, layer);
        _open.Commands.Clear();
        _insets.Add(_open);
    }

    public void EndInset() => _open = null;

    private void ClearInsets()
    {
        foreach (var inset in _insets)
        {
            _spareInsets.Push(inset);
        }

        _insets.Clear();
        _open = null;
    }

    private void RenderInsets(SDL_GPUCommandBuffer* commands)
    {
        RenderInsets(commands, InsetLayer.UnderCanvas);
        RenderCanvas(commands);
        RenderInsets(commands, InsetLayer.OverCanvas);

        ClearInsets();
    }

    private void RenderInsets(SDL_GPUCommandBuffer* commands, InsetLayer layer)
    {
        foreach (var inset in _insets)
        {
            if (inset.Layer == layer)
            {
                RenderInset(commands, inset);
            }
        }
    }

    private void RenderInset(SDL_GPUCommandBuffer* commands, Inset inset)
    {
        var pass = BeginOver(commands);
        var scale = _targetHeight / CanvasHeight;
        var viewport = new SDL_GPUViewport
        {
            x = inset.Area.X * scale,
            y = inset.Area.Y * scale,
            w = inset.Area.Width * scale,
            h = inset.Area.Height * scale,
            min_depth = 0f,
            max_depth = 1f,
        };
        var scissor = new SDL_Rect
        {
            x = (int)viewport.x,
            y = (int)viewport.y,
            w = (int)MathF.Ceiling(viewport.w),
            h = (int)MathF.Ceiling(viewport.h),
        };
        SDL_SetGPUViewport(pass, &viewport);
        SDL_SetGPUScissor(pass, &scissor);

        var scene = inset.Content == InsetContent.Scene;
        if (scene && Panorama != null)
        {
            DrawSky(commands, pass, inset.View, Panorama);
        }

        var source = scene ? _commands : inset.Commands;
        foreach (var order in Passes)
        {
            if (order != Pass.Overlay)
            {
                DrawPass(commands, pass, source, order, inset.View);
            }
        }

        SDL_EndGPURenderPass(pass);
    }

    private void RenderCanvas(SDL_GPUCommandBuffer* commands)
    {
        var pass = BeginOver(commands);
        DrawPass(commands, pass, _commands, Pass.Overlay, default);

        SDL_EndGPURenderPass(pass);
    }

    /// <summary>A render pass over the frame drawn so far, its depth cleared.</summary>
    private SDL_GPURenderPass* BeginOver(SDL_GPUCommandBuffer* commands)
    {
        var colourTarget = new SDL_GPUColorTargetInfo
        {
            texture = _target,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            texture = _depth,
            clear_depth = 1f,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        return SDL_BeginGPURenderPass(commands, &colourTarget, 1, &depthTarget);
    }
}
