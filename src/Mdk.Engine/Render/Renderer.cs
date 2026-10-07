using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Mdk.Engine.Platform;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>A corner of a triangle: position (MDK coordinates, Z up), texture coordinates (0-1
/// over one frame) and a colour (RGBA8, R in the low byte; white unless given) multiplying the
/// material's, interpolated across the triangle (Gouraud).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector3 position, Vector2 uv)
{
    public const uint White = uint.MaxValue;
    private const int GreenShift = 8;
    private const int BlueShift = 16;
    private const int AlphaShift = 24;

    public Vector3 Position = position;
    public Vector2 Uv = uv;
    public uint Colour = White;

    public Vertex(Vector3 position, Vector2 uv, uint colour) : this(position, uv) => Colour = colour;

    /// <summary>A colour packed for <see cref="Colour"/>.</summary>
    public static uint Rgba(byte r, byte g, byte b, byte a) =>
        r | (uint)g << GreenShift | (uint)b << BlueShift | (uint)a << AlphaShift;
}

/// <summary>How a batch is drawn.</summary>
public enum Pass
{
    /// <summary>Opaque, back faces culled (arenas).</summary>
    Solid,
    /// <summary>Opaque, both faces (models).</summary>
    DoubleSided,
    /// <summary>The panorama where the sky behind would be, both faces (mirrors).</summary>
    Mirror,
    /// <summary>Blended by alpha, both faces, after the opaque ones (glass).</summary>
    Blended,
    /// <summary>Opaque, both faces, over the scene without depth (the fall's explosions).</summary>
    OnTop,
    /// <summary>The 2D canvas over the scene (HUD, menus): blended, no depth.</summary>
    Overlay,
}

/// <summary>How a blended pass meets what's behind: mixed by alpha, or added (scaled by alpha).</summary>
public enum Blend { Alpha, Add }

/// <summary>What a mesh's vertices make: triangles (three each) or lines (two each, one pixel wide).</summary>
public enum Primitive { Triangles, Lines }

/// <summary>A surface: an index texture through a palette, a flat colour, or a mirror showing the
/// panorama <paramref name="RowShift"/> rows lower or higher; <paramref name="Shading"/> picks the
/// original look or the enhanced one; <paramref name="Blend"/> applies to the blended passes;
/// <paramref name="DepthLayer"/> draws it that many steps nearer in depth (a poster over its wall).</summary>
public readonly record struct Material(int Texture, int Palette, Vector4 Colour, int FrameCount, Pass Pass, float RowShift = 0f,
    Shading Shading = Shading.Original, Blend Blend = Blend.Alpha, int DepthLayer = 0)
{
    public const int None = -1;

    public static Material Flat(Vector4 colour, Pass pass) => new(None, None, colour, 1, pass);

    public static Material Mirror(float rowShift) => new(None, None, Vector4.One, 1, Pass.Mirror, rowShift);
}

/// <summary>The level's panorama (index texture and palette ids): <paramref name="WrapWidth"/>
/// columns cover 360 degrees, <paramref name="HorizonRow"/> at eye level; the screen above and below
/// it takes palette colours <paramref name="TopColour"/> and <paramref name="BottomColour"/>.</summary>
public sealed record Panorama(int Sky, int MirrorSky, int Palette, float WrapWidth, float HorizonRow, float Offset,
    float Height, int TopColour, int BottomColour, Sampling Sampling = Sampling.Nearest);

/// <summary>What is behind the scene (the scripts' sky modes, 0x574304): the panorama, the clear
/// colour, or the last frame kept.</summary>
public enum Backdrop { Sky, Clear, Keep }

/// <summary>The camera: its view-projection, the inverse of its rotation and projection (clip space
/// to world directions, for the sky), and its position.</summary>
public readonly record struct View(Matrix4x4 ViewProjection, Matrix4x4 ClipToDirection, Vector3 Position);

/// <summary>What the last frame drew: its GPU draw calls and triangles (lines count none).</summary>
public readonly record struct RenderStats(int DrawCalls, int Triangles);

/// <summary>Draws paletted triangle meshes with SDL_GPU.
/// <code>
///  game ──► Renderer ──► offscreen colour + depth ──blit──► swapchain
///              │                    └─download──► screenshot (BMP)
///              └ meshes, index textures, palettes (GPU resources by id)
/// </code></summary>
public sealed unsafe partial class Renderer : IDisposable
{
    private const SDL_GPUTextureFormat ColourFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
    private const int BytesPerPixel = 4;
    private const int PaletteSize = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct FragmentUniforms
    {
        public Vector4 Colour;
        public int Textured;
        public int FrameCount;
        public int Frame;
        public int Unused;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PanoramaUniforms
    {
        public float WrapWidth;
        public float HorizonRow;
        public float Offset;
        public float Height;
        public int TopColour;
        public int BottomColour;
        public float RowShift;
        public Sampling Sampling;
        public Vector4 Camera;
    }

    /// <summary>Whether a pipeline reads vertex buffers and the depth buffer (the sky does neither;
    /// the enhanced look's post-processing has no depth buffer).</summary>
    private enum Geometry { Mesh, Screen, Post }

    /// <summary>What a pipeline writes: colour (and depth), or depth alone: the camera's (the enhanced
    /// look's ambient occlusion) or the sun's (its shadows, biased).</summary>
    private enum Output { Colour, Depth, Shadow }

    /// <summary>A vertex's position, texture coordinates and colour.</summary>
    private const uint VertexAttributes = 3;

    private readonly record struct PipelineKey(string Program, Pass Pass, Primitive Primitive, Geometry Geometry, uint Samples, Output Output,
        Blend Blend = Blend.Alpha);

    /// <summary>A shader format and the extension of its embedded programs (shaders/palette.vs.dxil).</summary>
    private readonly record struct ShaderFormat(SDL_GPUShaderFormat Format, string Extension);

    /// <summary>The formats in the order tried: Direct3D 12, Metal, Vulkan. The build embeds those its
    /// tools could compile (Mdk.Engine.csproj).</summary>
    private static readonly ShaderFormat[] ShaderFormats =
    [
        new(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, ".dxil"),
        new(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, ".msl"),
        new(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, ".spv"),
    ];

    /// <summary>The program whose resource tells whether a format is embedded.</summary>
    private const string ShaderProbe = "shaders/palette.vs";

    private readonly record struct DrawCommand(int Mesh, int First, int Count, Material Material, int Frame, Matrix4x4 World,
        Primitive Primitive = Primitive.Triangles);

    /// <summary>The 2D canvas: <see cref="CanvasHeight"/> units high like the original's view, as
    /// wide as the window's aspect makes it; quads collected each frame into one dynamic mesh.</summary>
    public const float CanvasHeight = 360f;
    private const int CanvasQuads = 4096;
    private const int QuadVertices = 6;
    private readonly int _canvasMesh;
    private readonly List<Vertex> _canvasVertices = [];
    private readonly List<(int First, int Count, Material Material)> _canvasCommands = [];

    private readonly Window _window;
    private readonly SDL_GPUDevice* _device;
    private readonly ShaderFormat _shaderFormat;
    private readonly SDL_GPUTextureFormat _depthFormat;
    private readonly Dictionary<PipelineKey, IntPtr> _pipelines = [];
    private readonly SDL_GPUSampler* _repeatSampler;
    private readonly SDL_GPUSampler* _clampSampler;
    private readonly List<IntPtr> _textures = [];
    private readonly List<IntPtr> _buffers = [];
    private readonly List<DrawCommand> _commands = [];
    /// <summary>Dynamic meshes: their upload buffer and the bytes to copy before the next frame.</summary>
    private readonly Dictionary<int, (IntPtr Transfer, uint Pending)> _dynamic = [];

    private SDL_GPUTexture* _target;
    private SDL_GPUTexture* _depth;
    private uint _targetWidth;
    private uint _targetHeight;

    public Renderer(Window window)
    {
        _window = window;
        _device = CreateDevice(out _shaderFormat);
        Check(_device != null, "SDL_CreateGPUDevice");
        Check(SDL_ClaimWindowForGPUDevice(_device, window.Handle), "SDL_ClaimWindowForGPUDevice");

        var depthUsage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET;
        _depthFormat = SDL_GPUTextureSupportsFormat(_device, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, depthUsage)
            ? SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT
            : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D24_UNORM;

        _repeatSampler = CreateSampler(SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT);
        _clampSampler = CreateSampler(SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE);

        // The enhanced look samples its depth buffers.
        var sampledDepth = depthUsage | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
        _sampledDepthFormat = SDL_GPUTextureSupportsFormat(_device, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sampledDepth)
            ? SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT
            : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM;
        _mipSampler = CreateSampler(SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE, Sampling.Linear);

        _canvasMesh = CreateDynamicMesh(CanvasQuads * QuadVertices);
        CreateColourSampler();

        // Texture 0 is bound for flat colours, so it must outlive every scope (Release).
        CreateRgbaTexture(1, 1, new byte[BytesPerPixel]);
    }

    /// <summary>A point of the window (in its coordinates) on the canvas.</summary>
    public Vector2 CanvasPoint(float x, float y)
    {
        int width, height;
        SDL_GetWindowSize(_window.Handle, &width, &height);
        return height <= 0 ? Vector2.Zero : new Vector2(x, y) * (CanvasHeight / height);
    }

    /// <summary>The textures and meshes created so far; <see cref="Release"/> frees those created after.</summary>
    public readonly record struct Scope(int Textures, int Buffers);

    public Scope Mark() => new(_textures.Count, _buffers.Count);

    /// <summary>Frees the textures and meshes created after <paramref name="scope"/> (a screen of
    /// the game ends): their ids are reused.</summary>
    public void Release(Scope scope)
    {
        SDL_WaitForGPUIdle(_device);
        ReleaseColoursFrom(scope.Textures);
        for (var i = _textures.Count - 1; i >= scope.Textures; i--)
        {
            SDL_ReleaseGPUTexture(_device, (SDL_GPUTexture*)_textures[i]);
            _textures.RemoveAt(i);
        }

        for (var i = _buffers.Count - 1; i >= scope.Buffers; i--)
        {
            SDL_ReleaseGPUBuffer(_device, (SDL_GPUBuffer*)_buffers[i]);
            if (_dynamic.Remove(i, out var dynamic))
            {
                SDL_ReleaseGPUTransferBuffer(_device, (SDL_GPUTransferBuffer*)dynamic.Transfer);
            }

            _buffers.RemoveAt(i);
        }

        Panorama = null;
        Backdrop = Backdrop.Sky;
        Lighting = null;
    }

    /// <summary>The canvas's width for the window's aspect.</summary>
    public float CanvasWidth => CanvasHeight * AspectRatio;

    /// <summary>Draws part of an index texture (<paramref name="source"/> in its pixels) on the canvas,
    /// tinted (alpha fades it); palette index 0 stays transparent.</summary>
    public void DrawImage(int texture, int palette, Vector2 textureSize, RectangleF source, RectangleF target, Vector4 tint)
    {
        var uv0 = new Vector2(source.Left, source.Top) / textureSize;
        var uv1 = new Vector2(source.Right, source.Bottom) / textureSize;
        AddQuad(target, uv0, uv1, new Material(texture, palette, tint, 1, Pass.Overlay));
    }

    /// <summary>Fills a rectangle of the canvas with a colour (alpha blends it).</summary>
    public void FillRect(RectangleF target, Vector4 colour) =>
        AddQuad(target, Vector2.Zero, Vector2.Zero, Material.Flat(colour, Pass.Overlay));

    /// <summary>Adds a colour, scaled by its alpha, to a rectangle of the canvas (clamped at white).</summary>
    public void AddRect(RectangleF target, Vector4 colour) =>
        AddQuad(target, Vector2.Zero, Vector2.Zero, Material.Flat(colour, Pass.Overlay) with { Blend = Blend.Add });

    /// <summary>A rectangle's outline, <paramref name="width"/> units thick, inside it.</summary>
    public void FrameRect(RectangleF target, float width, Vector4 colour)
    {
        FillRect(new RectangleF(target.Left, target.Top, target.Width, width), colour);
        FillRect(new RectangleF(target.Left, target.Bottom - width, target.Width, width), colour);
        FillRect(new RectangleF(target.Left, target.Top + width, width, target.Height - 2 * width), colour);
        FillRect(new RectangleF(target.Right - width, target.Top + width, width, target.Height - 2 * width), colour);
    }

    private void AddQuad(RectangleF target, Vector2 uv0, Vector2 uv1, Material material)
    {
        if (_canvasVertices.Count >= CanvasQuads * QuadVertices)
        {
            return;
        }

        var first = _canvasVertices.Count;
        Vertex Corner(float x, float y, float u, float v) => new(new Vector3(x, y, 0f), new Vector2(u, v));
        _canvasVertices.Add(Corner(target.Left, target.Top, uv0.X, uv0.Y));
        _canvasVertices.Add(Corner(target.Right, target.Top, uv1.X, uv0.Y));
        _canvasVertices.Add(Corner(target.Right, target.Bottom, uv1.X, uv1.Y));
        _canvasVertices.Add(Corner(target.Left, target.Top, uv0.X, uv0.Y));
        _canvasVertices.Add(Corner(target.Right, target.Bottom, uv1.X, uv1.Y));
        _canvasVertices.Add(Corner(target.Left, target.Bottom, uv0.X, uv1.Y));

        // Neighbouring quads of one material draw together.
        if (_canvasCommands.Count > 0 && _canvasCommands[^1].Material == material)
        {
            var last = _canvasCommands[^1];
            _canvasCommands[^1] = (last.First, last.Count + QuadVertices, material);
            return;
        }

        _canvasCommands.Add((first, QuadVertices, material));
    }

    /// <summary>The sky and mirrors' panorama; without one the screen is cleared to a colour.</summary>
    public Panorama? Panorama { get; set; }

    /// <summary>What the scene is drawn over; the sky needs a <see cref="Panorama"/>.</summary>
    public Backdrop Backdrop { get; set; }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException($"{what}: {SDL_GetError()}");
        }
    }

    /// <summary>A device for the first embedded shader format a GPU driver takes (SDL_GPU_DRIVER can
    /// choose the driver, e.g. vulkan on Windows), or null.</summary>
    private static SDL_GPUDevice* CreateDevice(out ShaderFormat format)
    {
        var embedded = typeof(Renderer).Assembly.GetManifestResourceNames();
        foreach (var candidate in ShaderFormats)
        {
            if (!embedded.Contains(ShaderProbe + candidate.Extension))
            {
                continue;
            }

            var device = SDL_CreateGPUDevice(candidate.Format, false, (byte*)null);
            if (device == null)
            {
                continue;
            }

            format = candidate;
            return device;
        }

        format = default;
        return null;
    }

    private SDL_GPUSampler* CreateSampler(SDL_GPUSamplerAddressMode mode, Sampling sampling = Sampling.Nearest)
    {
        var linear = sampling == Sampling.Linear;
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = linear ? SDL_GPUFilter.SDL_GPU_FILTER_LINEAR : SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            mag_filter = linear ? SDL_GPUFilter.SDL_GPU_FILTER_LINEAR : SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            mipmap_mode = linear ? SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR : SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
            address_mode_u = mode,
            address_mode_v = mode,
            address_mode_w = mode,
            max_lod = linear ? AllMips : 0f,
        };
        return SDL_CreateGPUSampler(_device, &info);
    }

    private SDL_GPUShader* LoadShader(string name, SDL_GPUShaderStage stage, uint samplers)
    {
        using var stream = typeof(Renderer).Assembly.GetManifestResourceStream("shaders/" + name + _shaderFormat.Extension)
            ?? throw new InvalidOperationException($"Shader {name} not embedded: {string.Join(", ", typeof(Renderer).Assembly.GetManifestResourceNames())}");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        var entry = stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vs_main"u8 : "ps_main"u8;
        fixed (byte* codePtr = code)
        fixed (byte* entryPtr = entry)
        {
            var info = new SDL_GPUShaderCreateInfo
            {
                code = codePtr,
                code_size = (nuint)code.Length,
                entrypoint = entryPtr,
                format = _shaderFormat.Format,
                stage = stage,
                num_samplers = samplers,
                num_uniform_buffers = 1,
            };
            var shader = SDL_CreateGPUShader(_device, &info);
            Check(shader != null, $"SDL_CreateGPUShader {name}");
            return shader;
        }
    }

    /// <summary>A pipeline, created the first time it's asked for.</summary>
    private SDL_GPUGraphicsPipeline* Pipeline(PipelineKey key)
    {
        if (!_pipelines.TryGetValue(key, out var pipeline))
        {
            pipeline = _pipelines[key] = (IntPtr)CreatePipeline(key);
        }

        return (SDL_GPUGraphicsPipeline*)pipeline;
    }

    private SDL_GPUGraphicsPipeline* CreatePipeline(PipelineKey key)
    {
        var (program, pass, primitive, geometry, samples, output, blend) = key;
        var vertexShader = LoadShader(program + ".vs", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0);
        var fragmentShader = LoadShader(program + ".ps", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, SamplerCount(program));
        var bufferDescription = new SDL_GPUVertexBufferDescription
        {
            slot = 0,
            pitch = (uint)sizeof(Vertex),
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
        };
        var attributes = stackalloc SDL_GPUVertexAttribute[(int)VertexAttributes];
        attributes[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attributes[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = (uint)sizeof(Vector3) };
        attributes[2] = new SDL_GPUVertexAttribute { location = 2, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offset = (uint)(sizeof(Vector3) + sizeof(Vector2)) };

        var blended = pass is Pass.Blended or Pass.Overlay;
        var colourTarget = new SDL_GPUColorTargetDescription
        {
            format = ColourFormat,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = blended,
                src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                dst_color_blendfactor = blend == Blend.Add ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            },
        };

        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vertexShader,
            fragment_shader = fragmentShader,
            vertex_input_state = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &bufferDescription,
                num_vertex_buffers = geometry == Geometry.Mesh ? 1u : 0u,
                vertex_attributes = attributes,
                num_vertex_attributes = geometry == Geometry.Mesh ? VertexAttributes : 0u,
            },
            primitive_type = primitive == Primitive.Lines ? SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST : SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = pass == Pass.Solid && geometry == Geometry.Mesh && output != Output.Shadow ? SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK : SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                // MDK's front faces wind clockwise on screen.
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_CLOCKWISE,
                enable_depth_bias = output == Output.Shadow,
                depth_bias_slope_factor = output == Output.Shadow ? ShadowSlopeBias : 0f,
            },
            multisample_state = new SDL_GPUMultisampleState { sample_count = SampleCount(samples) },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
                enable_depth_test = geometry == Geometry.Mesh && pass is not (Pass.Overlay or Pass.OnTop),
                enable_depth_write = geometry == Geometry.Mesh && !blended && pass != Pass.OnTop,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &colourTarget,
                num_color_targets = output == Output.Colour ? 1u : 0u,
                depth_stencil_format = output == Output.Colour ? _depthFormat : _sampledDepthFormat,
                has_depth_stencil_target = geometry != Geometry.Post,
            },
        };
        var pipeline = SDL_CreateGPUGraphicsPipeline(_device, &info);
        Check(pipeline != null, $"SDL_CreateGPUGraphicsPipeline {program} {pass}");
        SDL_ReleaseGPUShader(_device, vertexShader);
        SDL_ReleaseGPUShader(_device, fragmentShader);
        return pipeline;
    }

    /// <summary>An 8-bit index texture; frames of animated ones stacked vertically.</summary>
    public int CreateIndexTexture(int width, int height, byte[] indices)
    {
        var texture = CreateTexture(SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM, (uint)width, (uint)height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        Upload(texture, (uint)width, (uint)height, indices);
        _textures.Add((IntPtr)texture);
        KeepIndices(_textures.Count - 1, width, height, indices);
        return _textures.Count - 1;
    }

    /// <summary>New pixels for a texture of the same size (a video's frame, a fading palette).</summary>
    public void UpdateTexture(int texture, int width, int height, byte[] pixels)
    {
        Upload((SDL_GPUTexture*)_textures[texture], (uint)width, (uint)height, pixels);
        Changed(texture, width, height, pixels);
    }

    /// <summary>A 256-colour palette, RGBA8.</summary>
    public int CreatePalette(byte[] rgba)
    {
        var palette = CreateRgbaTexture(PaletteSize, 1, rgba);
        KeepPalette(palette, rgba);
        return palette;
    }

    public int CreateRgbaTexture(int width, int height, byte[] rgba)
    {
        var texture = CreateTexture(ColourFormat, (uint)width, (uint)height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        Upload(texture, (uint)width, (uint)height, rgba);
        _textures.Add((IntPtr)texture);
        return _textures.Count - 1;
    }

    /// <summary>A vertex buffer of triangles (three vertices each).</summary>
    public int CreateMesh(Vertex[] vertices)
    {
        var size = (uint)(vertices.Length * sizeof(Vertex));
        var bufferInfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = size };
        var buffer = SDL_CreateGPUBuffer(_device, &bufferInfo);
        Check(buffer != null, "SDL_CreateGPUBuffer");

        var transfer = CreateTransfer(size, SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD);
        var mapped = SDL_MapGPUTransferBuffer(_device, transfer, false);
        MemoryMarshal.AsBytes(vertices.AsSpan()).CopyTo(new Span<byte>((void*)mapped, (int)size));
        SDL_UnmapGPUTransferBuffer(_device, transfer);

        var commands = SDL_AcquireGPUCommandBuffer(_device);
        var copy = SDL_BeginGPUCopyPass(commands);
        var source = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer };
        var destination = new SDL_GPUBufferRegion { buffer = buffer, size = size };
        SDL_UploadToGPUBuffer(copy, &source, &destination, false);
        SDL_EndGPUCopyPass(copy);
        SDL_SubmitGPUCommandBuffer(commands);
        SDL_ReleaseGPUTransferBuffer(_device, transfer);

        _buffers.Add((IntPtr)buffer);
        return _buffers.Count - 1;
    }

    /// <summary>A vertex buffer of up to <paramref name="capacity"/> vertices, rewritten with
    /// <see cref="UpdateMesh"/> (sprites).</summary>
    public int CreateDynamicMesh(int capacity)
    {
        var size = (uint)(capacity * sizeof(Vertex));
        var bufferInfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = size };
        var buffer = SDL_CreateGPUBuffer(_device, &bufferInfo);
        Check(buffer != null, "SDL_CreateGPUBuffer");
        _buffers.Add((IntPtr)buffer);
        var id = _buffers.Count - 1;
        _dynamic[id] = ((IntPtr)CreateTransfer(size, SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD), 0);
        return id;
    }

    /// <summary>New vertices for a dynamic mesh, uploaded before the next frame is drawn.</summary>
    public void UpdateMesh(int mesh, ReadOnlySpan<Vertex> vertices)
    {
        var (transfer, _) = _dynamic[mesh];
        var bytes = MemoryMarshal.AsBytes(vertices);
        var mapped = SDL_MapGPUTransferBuffer(_device, (SDL_GPUTransferBuffer*)transfer, true);
        bytes.CopyTo(new Span<byte>((void*)mapped, bytes.Length));
        SDL_UnmapGPUTransferBuffer(_device, (SDL_GPUTransferBuffer*)transfer);
        _dynamic[mesh] = (transfer, (uint)bytes.Length);
    }

    /// <summary>The canvas's quads become draws in screen space, after the scene.</summary>
    private void QueueCanvas()
    {
        if (_canvasVertices.Count > 0)
        {
            UpdateMesh(_canvasMesh, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_canvasVertices));
        }

        var screen = Matrix4x4.CreateOrthographicOffCenter(0f, CanvasWidth, CanvasHeight, 0f, 0f, 1f);
        foreach (var (first, count, material) in _canvasCommands)
        {
            _commands.Add(new DrawCommand(_canvasMesh, first, count, material, 0, screen));
        }

        _canvasVertices.Clear();
        _canvasCommands.Clear();
    }

    /// <summary>Copies the pending dynamic meshes into their buffers.</summary>
    private void UploadDynamic(SDL_GPUCommandBuffer* commands)
    {
        var pending = _dynamic.Where(d => d.Value.Pending > 0).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var copy = SDL_BeginGPUCopyPass(commands);
        foreach (var (mesh, (transfer, size)) in pending)
        {
            var source = new SDL_GPUTransferBufferLocation { transfer_buffer = (SDL_GPUTransferBuffer*)transfer };
            var destination = new SDL_GPUBufferRegion { buffer = (SDL_GPUBuffer*)_buffers[mesh], size = size };
            SDL_UploadToGPUBuffer(copy, &source, &destination, true);
            _dynamic[mesh] = (transfer, 0);
        }

        SDL_EndGPUCopyPass(copy);
    }

    /// <summary>Queues triangles of a mesh for the next frame; <paramref name="frame"/> picks an animated texture's frame.</summary>
    public void Draw(int mesh, int firstVertex, int vertexCount, Material material, int frame = 0) =>
        Queue.Add(new DrawCommand(mesh, firstVertex, vertexCount, material, frame, Matrix4x4.Identity));

    /// <summary>Queues triangles of a mesh placed in the world by <paramref name="world"/> (models).</summary>
    public void Draw(int mesh, int firstVertex, int vertexCount, Material material, int frame, Matrix4x4 world) =>
        Queue.Add(new DrawCommand(mesh, firstVertex, vertexCount, material, frame, world));

    /// <summary>Queues lines of a mesh (two vertices each) in a material's colour (not a mirror).</summary>
    public void DrawLines(int mesh, int firstVertex, int vertexCount, Material material) =>
        Queue.Add(new DrawCommand(mesh, firstVertex, vertexCount, material, 0, Matrix4x4.Identity, Primitive.Lines));

    /// <summary>Draws the queued batches (opaque first, then blended) and shows them. With
    /// <paramref name="screenshot"/>, also saves the frame as a BMP.</summary>
    public void Present(View view, Vector4 clearColour, string? screenshot = null)
    {
        Overlay?.Invoke();
        var commands = SDL_AcquireGPUCommandBuffer(_device);
        SDL_GPUTexture* swapchain = null;
        uint width = HiddenWidth, height = HiddenHeight;

        // A hidden window has no swapchain to show: the frame stays in the offscreen target.
        var shown = _window.Visibility == Visibility.Shown;
        if (shown && (!SDL_WaitAndAcquireGPUSwapchainTexture(commands, _window.Handle, &swapchain, &width, &height) || swapchain == null))
        {
            SDL_SubmitGPUCommandBuffer(commands);
            _commands.Clear();
            ClearInsets();
            return;
        }

        EnsureTargets(width, height);
        QueueCanvas();
        UploadDynamic(commands);
        PrepareColours(commands);
        (_drawCalls, _triangles) = (0, 0);
        RenderScene(commands, view, clearColour);
        Stats = new RenderStats(_drawCalls, _triangles);
        if (swapchain != null)
        {
            Blit(commands, swapchain, width, height);
        }
        if (screenshot != null)
        {
            Save(commands, screenshot);
        }
        else
        {
            SDL_SubmitGPUCommandBuffer(commands);
        }

        _commands.Clear();
    }

    /// <summary>Draws on the canvas over every frame, whoever presents it, just before (the
    /// developer tools).</summary>
    public Action? Overlay { get; set; }

    /// <summary>The last presented frame's draws (the debug overlay).</summary>
    public RenderStats Stats { get; private set; }

    /// <summary>The sky and the post pass draw one screen-filling triangle.</summary>
    private const int ScreenTriangle = 3;
    private const int TriangleVertices = 3;
    private int _drawCalls;
    private int _triangles;

    /// <summary>Draws vertices of the bound buffer, counted in <see cref="Stats"/>.</summary>
    private void DrawPrimitives(SDL_GPURenderPass* pass, int count, int first, Primitive primitive)
    {
        _drawCalls++;
        _triangles += primitive == Primitive.Triangles ? count / TriangleVertices : 0;
        SDL_DrawGPUPrimitives(pass, (uint)count, 1, (uint)first, 0);
    }

    /// <summary>The offscreen frame of a hidden window (the shown one's default size).</summary>
    private const uint HiddenWidth = 1280;
    private const uint HiddenHeight = 960;

    public float AspectRatio => _targetHeight == 0 ? 4f / 3f : (float)_targetWidth / _targetHeight;

    private void RenderScene(SDL_GPUCommandBuffer* commands, View view, Vector4 clearColour)
    {
        if (Lighting is { } lighting)
        {
            RenderEnhanced(commands, view, clearColour, lighting);
            return;
        }

        var colourTarget = SceneColour(_target, clearColour);
        var depthTarget = SceneDepth();
        var pass = SDL_BeginGPURenderPass(commands, &colourTarget, 1, &depthTarget);
        _passSamples = _samples;
        if (Panorama != null && Backdrop == Backdrop.Sky)
        {
            DrawSky(commands, pass, view, Panorama);
        }

        // With insets, the canvas waits for them (Renderer.Insets.cs).
        var canvasNow = _insets.Count == 0;
        foreach (var order in Enum.GetValues<Pass>())
        {
            foreach (var command in _commands.Where(c => c.Material.Pass == order && (canvasNow || order != Pass.Overlay)))
            {
                DrawOne(commands, pass, command, view);
            }
        }

        SDL_EndGPURenderPass(pass);
        _passSamples = 1;
        if (!canvasNow)
        {
            RenderInsets(commands);
        }
    }

    /// <summary>The panorama behind everything: one screen-filling triangle.</summary>
    private void DrawSky(SDL_GPUCommandBuffer* commands, SDL_GPURenderPass* pass, View view, Panorama panorama)
    {
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PipelineKey("sky", Pass.DoubleSided, Primitive.Triangles, Geometry.Screen, _passSamples, Output.Colour)));
        var clipToDirection = view.ClipToDirection;
        SDL_PushGPUVertexUniformData(commands, 0, (IntPtr)(&clipToDirection), (uint)sizeof(Matrix4x4));
        BindPanorama(commands, pass, panorama, panorama.Sky, 0f, view.Position);
        DrawPrimitives(pass, ScreenTriangle, 0, Primitive.Triangles);
    }

    private void BindPanorama(SDL_GPUCommandBuffer* commands, SDL_GPURenderPass* pass, Panorama panorama, int texture, float rowShift, Vector3 camera)
    {
        var samplers = stackalloc SDL_GPUTextureSamplerBinding[2];
        samplers[0] = new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)_textures[texture], sampler = _clampSampler };
        samplers[1] = new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)_textures[panorama.Palette], sampler = _clampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, samplers, 2);

        var uniforms = new PanoramaUniforms
        {
            WrapWidth = panorama.WrapWidth,
            HorizonRow = panorama.HorizonRow,
            Offset = panorama.Offset,
            Height = panorama.Height,
            TopColour = panorama.TopColour,
            BottomColour = panorama.BottomColour,
            RowShift = rowShift,
            Sampling = panorama.Sampling,
            Camera = new Vector4(camera, 1f),
        };
        SDL_PushGPUFragmentUniformData(commands, 0, (IntPtr)(&uniforms), (uint)sizeof(PanoramaUniforms));
    }

    private void DrawOne(SDL_GPUCommandBuffer* commands, SDL_GPURenderPass* pass, DrawCommand command, View view)
    {
        var material = command.Material;
        var mode = ModeOf(command);
        var program = material.Pass == Pass.Mirror ? "mirror" : mode != null ? "enhanced" : "palette";
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PipelineKey(program, material.Pass, command.Primitive, Geometry.Mesh, _passSamples, Output.Colour, material.Blend)));

        var binding = new SDL_GPUBufferBinding { buffer = (SDL_GPUBuffer*)_buffers[command.Mesh] };
        SDL_BindGPUVertexBuffers(pass, 0, &binding, 1);

        // The canvas is already in clip space; the rest goes through the camera.
        // Layers and lines are drawn nearer than the surfaces they lie on.
        var nearer = command.World * DepthPull.Of(view.Position, command.Primitive, material.DepthLayer);
        var transform = material.Pass == Pass.Overlay ? command.World : nearer * view.ViewProjection;
        if (mode is { } enhanced)
        {
            DrawEnhanced(commands, pass, command, transform, enhanced, view);
            return;
        }

        SDL_PushGPUVertexUniformData(commands, 0, (IntPtr)(&transform), (uint)sizeof(Matrix4x4));

        // Mirrors show nothing without a panorama.
        if (material.Pass == Pass.Mirror)
        {
            if (Panorama == null)
            {
                return;
            }

            BindPanorama(commands, pass, Panorama, Panorama.MirrorSky, material.RowShift, view.Position);
            DrawPrimitives(pass, command.Count, command.First, command.Primitive);
            return;
        }

        // Flat colours still bind a texture pair: any will do.
        var textured = material.Texture != Material.None;
        var samplers = stackalloc SDL_GPUTextureSamplerBinding[2];
        samplers[0] = new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)_textures[textured ? material.Texture : 0], sampler = _repeatSampler };
        samplers[1] = new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)_textures[textured ? material.Palette : 0], sampler = _clampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, samplers, 2);

        var uniforms = new FragmentUniforms
        {
            Colour = material.Colour,
            Textured = textured ? 1 : 0,
            FrameCount = material.FrameCount,
            Frame = command.Frame,
        };
        SDL_PushGPUFragmentUniformData(commands, 0, (IntPtr)(&uniforms), (uint)sizeof(FragmentUniforms));
        DrawPrimitives(pass, command.Count, command.First, command.Primitive);
    }

    private void Blit(SDL_GPUCommandBuffer* commands, SDL_GPUTexture* swapchain, uint width, uint height)
    {
        var blit = new SDL_GPUBlitInfo
        {
            source = new SDL_GPUBlitRegion { texture = _target, w = _targetWidth, h = _targetHeight },
            destination = new SDL_GPUBlitRegion { texture = swapchain, w = width, h = height },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
        };
        SDL_BlitGPUTexture(commands, &blit);
    }

    /// <summary>Downloads the frame and writes it as a 32-bit BMP.</summary>
    private void Save(SDL_GPUCommandBuffer* commands, string path)
    {
        var size = _targetWidth * _targetHeight * BytesPerPixel;
        var transfer = CreateTransfer(size, SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD);
        var copy = SDL_BeginGPUCopyPass(commands);
        var source = new SDL_GPUTextureRegion { texture = _target, w = _targetWidth, h = _targetHeight, d = 1 };
        var destination = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer };
        SDL_DownloadFromGPUTexture(copy, &source, &destination);
        SDL_EndGPUCopyPass(copy);
        var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(commands);
        SDL_WaitForGPUFences(_device, true, &fence, 1);
        SDL_ReleaseGPUFence(_device, fence);

        var mapped = SDL_MapGPUTransferBuffer(_device, transfer, false);
        var rgba = new ReadOnlySpan<byte>((void*)mapped, (int)size).ToArray();
        SDL_UnmapGPUTransferBuffer(_device, transfer);
        SDL_ReleaseGPUTransferBuffer(_device, transfer);
        Bmp.Write(path, (int)_targetWidth, (int)_targetHeight, rgba);
    }

    private void EnsureTargets(uint width, uint height)
    {
        var samples = SupportedSamples();
        if (_target != null && width == _targetWidth && height == _targetHeight && samples == _samples)
        {
            return;
        }

        if (_target != null)
        {
            SDL_ReleaseGPUTexture(_device, _target);
            SDL_ReleaseGPUTexture(_device, _depth);
        }

        ReleaseSizedTargets();
        _target = CreateTexture(ColourFormat, width, height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _depth = CreateTexture(_depthFormat, width, height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);
        _targetWidth = width;
        _targetHeight = height;
        _samples = samples;
        CreateMultisampled(width, height);
    }

    private SDL_GPUTexture* CreateTexture(SDL_GPUTextureFormat format, uint width, uint height, SDL_GPUTextureUsageFlags usage,
        uint levels = 1, uint samples = 1)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = format,
            usage = usage,
            width = width,
            height = height,
            layer_count_or_depth = 1,
            num_levels = levels,
            sample_count = SampleCount(samples),
        };
        var texture = SDL_CreateGPUTexture(_device, &info);
        Check(texture != null, "SDL_CreateGPUTexture");
        return texture;
    }

    private SDL_GPUTransferBuffer* CreateTransfer(uint size, SDL_GPUTransferBufferUsage usage)
    {
        var info = new SDL_GPUTransferBufferCreateInfo { usage = usage, size = size };
        return SDL_CreateGPUTransferBuffer(_device, &info);
    }

    private void Upload(SDL_GPUTexture* texture, uint width, uint height, byte[] pixels)
    {
        var transfer = CreateTransfer((uint)pixels.Length, SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD);
        var mapped = SDL_MapGPUTransferBuffer(_device, transfer, false);
        pixels.CopyTo(new Span<byte>((void*)mapped, pixels.Length));
        SDL_UnmapGPUTransferBuffer(_device, transfer);

        var commands = SDL_AcquireGPUCommandBuffer(_device);
        var copy = SDL_BeginGPUCopyPass(commands);
        var source = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, pixels_per_row = width, rows_per_layer = height };
        var destination = new SDL_GPUTextureRegion { texture = texture, w = width, h = height, d = 1 };
        SDL_UploadToGPUTexture(copy, &source, &destination, false);
        SDL_EndGPUCopyPass(copy);
        SDL_SubmitGPUCommandBuffer(commands);
        SDL_ReleaseGPUTransferBuffer(_device, transfer);
    }

    public void Dispose()
    {
        SDL_WaitForGPUIdle(_device);
        foreach (var texture in _textures)
        {
            SDL_ReleaseGPUTexture(_device, (SDL_GPUTexture*)texture);
        }

        foreach (var buffer in _buffers)
        {
            SDL_ReleaseGPUBuffer(_device, (SDL_GPUBuffer*)buffer);
        }

        foreach (var (transfer, _) in _dynamic.Values)
        {
            SDL_ReleaseGPUTransferBuffer(_device, (SDL_GPUTransferBuffer*)transfer);
        }

        foreach (var pipeline in _pipelines.Values)
        {
            SDL_ReleaseGPUGraphicsPipeline(_device, (SDL_GPUGraphicsPipeline*)pipeline);
        }

        if (_target != null)
        {
            SDL_ReleaseGPUTexture(_device, _target);
            SDL_ReleaseGPUTexture(_device, _depth);
        }

        ReleaseSizedTargets();
        ReleaseShadowMap();
        SDL_ReleaseGPUSampler(_device, _repeatSampler);
        SDL_ReleaseGPUSampler(_device, _clampSampler);
        SDL_ReleaseGPUSampler(_device, _mipSampler);
        DisposeColours();
        SDL_ReleaseWindowFromGPUDevice(_device, _window.Handle);
        SDL_DestroyGPUDevice(_device);
    }
}
