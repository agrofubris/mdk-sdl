using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Mdk.Engine.Platform;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>A corner of a triangle: position (MDK coordinates, Z up) and texture coordinates
/// (0-1 over one frame).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector3 position, Vector2 uv)
{
    public Vector3 Position = position;
    public Vector2 Uv = uv;
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
    /// <summary>The 2D canvas over the scene (HUD, menus): blended, no depth.</summary>
    Overlay,
}

/// <summary>A surface: an index texture through a palette, a flat colour, or a mirror showing the
/// panorama <paramref name="RowShift"/> rows lower or higher.</summary>
public readonly record struct Material(int Texture, int Palette, Vector4 Colour, int FrameCount, Pass Pass, float RowShift = 0f)
{
    public const int None = -1;

    public static Material Flat(Vector4 colour, Pass pass) => new(None, None, colour, 1, pass);

    public static Material Mirror(float rowShift) => new(None, None, Vector4.One, 1, Pass.Mirror, rowShift);
}

/// <summary>The level's panorama (index texture and palette ids): <paramref name="WrapWidth"/>
/// columns cover 360 degrees, <paramref name="HorizonRow"/> at eye level; the screen above and below
/// it takes palette colours <paramref name="TopColour"/> and <paramref name="BottomColour"/>.</summary>
public sealed record Panorama(int Sky, int MirrorSky, int Palette, float WrapWidth, float HorizonRow, float Offset,
    float Height, int TopColour, int BottomColour);

/// <summary>The camera: its view-projection, the inverse of its rotation and projection (clip space
/// to world directions, for the sky), and its position.</summary>
public readonly record struct View(Matrix4x4 ViewProjection, Matrix4x4 ClipToDirection, Vector3 Position);

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
        public float Unused;
        public Vector4 Camera;
    }

    /// <summary>Whether a pipeline reads vertex buffers and the depth buffer (the sky does neither).</summary>
    private enum Geometry { Mesh, Screen }

    private readonly record struct DrawCommand(int Mesh, int First, int Count, Material Material, int Frame, Matrix4x4 World);

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
    private readonly SDL_GPUTextureFormat _depthFormat;
    private readonly Dictionary<Pass, IntPtr> _pipelines = [];
    private readonly SDL_GPUGraphicsPipeline* _skyPipeline;
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
        _device = SDL_CreateGPUDevice(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, false, (byte*)null);
        Check(_device != null, "SDL_CreateGPUDevice");
        Check(SDL_ClaimWindowForGPUDevice(_device, window.Handle), "SDL_ClaimWindowForGPUDevice");

        var depthUsage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET;
        _depthFormat = SDL_GPUTextureSupportsFormat(_device, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, depthUsage)
            ? SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT
            : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D24_UNORM;

        _repeatSampler = CreateSampler(SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT);
        _clampSampler = CreateSampler(SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE);

        foreach (var pass in Enum.GetValues<Pass>())
        {
            var program = pass == Pass.Mirror ? "mirror" : "palette";
            _pipelines[pass] = (IntPtr)CreatePipeline(program, pass, Geometry.Mesh);
        }

        _skyPipeline = CreatePipeline("sky", Pass.DoubleSided, Geometry.Screen);
        _canvasMesh = CreateDynamicMesh(CanvasQuads * QuadVertices);

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

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException($"{what}: {SDL_GetError()}");
        }
    }

    private SDL_GPUSampler* CreateSampler(SDL_GPUSamplerAddressMode mode)
    {
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
            address_mode_u = mode,
            address_mode_v = mode,
            address_mode_w = mode,
        };
        return SDL_CreateGPUSampler(_device, &info);
    }

    private SDL_GPUShader* LoadShader(string name, SDL_GPUShaderStage stage, uint samplers)
    {
        using var stream = typeof(Renderer).Assembly.GetManifestResourceStream("shaders/" + name)
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
                format = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL,
                stage = stage,
                num_samplers = samplers,
                num_uniform_buffers = 1,
            };
            var shader = SDL_CreateGPUShader(_device, &info);
            Check(shader != null, $"SDL_CreateGPUShader {name}");
            return shader;
        }
    }

    private SDL_GPUGraphicsPipeline* CreatePipeline(string program, Pass pass, Geometry geometry)
    {
        var vertexShader = LoadShader(program + ".vs.dxil", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0);
        var fragmentShader = LoadShader(program + ".ps.dxil", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2);
        var bufferDescription = new SDL_GPUVertexBufferDescription
        {
            slot = 0,
            pitch = (uint)sizeof(Vertex),
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
        };
        var attributes = stackalloc SDL_GPUVertexAttribute[2];
        attributes[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attributes[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = (uint)sizeof(Vector3) };

        var blended = pass is Pass.Blended or Pass.Overlay;
        var colourTarget = new SDL_GPUColorTargetDescription
        {
            format = ColourFormat,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = blended,
                src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
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
                num_vertex_attributes = geometry == Geometry.Mesh ? 2u : 0u,
            },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = pass == Pass.Solid && geometry == Geometry.Mesh ? SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK : SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                // MDK's front faces wind clockwise on screen.
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_CLOCKWISE,
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
                enable_depth_test = geometry == Geometry.Mesh && pass != Pass.Overlay,
                enable_depth_write = geometry == Geometry.Mesh && !blended,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &colourTarget,
                num_color_targets = 1,
                depth_stencil_format = _depthFormat,
                has_depth_stencil_target = true,
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
        return _textures.Count - 1;
    }

    /// <summary>New pixels for a texture of the same size (a video's frame, a fading palette).</summary>
    public void UpdateTexture(int texture, int width, int height, byte[] pixels) =>
        Upload((SDL_GPUTexture*)_textures[texture], (uint)width, (uint)height, pixels);

    /// <summary>A 256-colour palette, RGBA8.</summary>
    public int CreatePalette(byte[] rgba) => CreateRgbaTexture(PaletteSize, 1, rgba);

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

    /// <summary>Draws the queued batches (opaque first, then blended) and shows them. With
    /// <paramref name="screenshot"/>, also saves the frame as a BMP.</summary>
    public void Present(View view, Vector4 clearColour, string? screenshot = null)
    {
        var commands = SDL_AcquireGPUCommandBuffer(_device);
        SDL_GPUTexture* swapchain;
        uint width, height;
        if (!SDL_WaitAndAcquireGPUSwapchainTexture(commands, _window.Handle, &swapchain, &width, &height) || swapchain == null)
        {
            SDL_SubmitGPUCommandBuffer(commands);
            _commands.Clear();
            ClearInsets();
            return;
        }

        EnsureTargets(width, height);
        QueueCanvas();
        UploadDynamic(commands);
        RenderScene(commands, view, clearColour);
        Blit(commands, swapchain, width, height);
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

    public float AspectRatio => _targetHeight == 0 ? 4f / 3f : (float)_targetWidth / _targetHeight;

    private void RenderScene(SDL_GPUCommandBuffer* commands, View view, Vector4 clearColour)
    {
        var colourTarget = new SDL_GPUColorTargetInfo
        {
            texture = _target,
            clear_color = new SDL_FColor { r = clearColour.X, g = clearColour.Y, b = clearColour.Z, a = clearColour.W },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
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

        var pass = SDL_BeginGPURenderPass(commands, &colourTarget, 1, &depthTarget);
        if (Panorama != null)
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
        if (!canvasNow)
        {
            RenderInsets(commands);
        }
    }

    /// <summary>The panorama behind everything: one screen-filling triangle.</summary>
    private void DrawSky(SDL_GPUCommandBuffer* commands, SDL_GPURenderPass* pass, View view, Panorama panorama)
    {
        SDL_BindGPUGraphicsPipeline(pass, _skyPipeline);
        var clipToDirection = view.ClipToDirection;
        SDL_PushGPUVertexUniformData(commands, 0, (IntPtr)(&clipToDirection), (uint)sizeof(Matrix4x4));
        BindPanorama(commands, pass, panorama, panorama.Sky, 0f, view.Position);
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
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
            Camera = new Vector4(camera, 1f),
        };
        SDL_PushGPUFragmentUniformData(commands, 0, (IntPtr)(&uniforms), (uint)sizeof(PanoramaUniforms));
    }

    private void DrawOne(SDL_GPUCommandBuffer* commands, SDL_GPURenderPass* pass, DrawCommand command, View view)
    {
        var material = command.Material;
        SDL_BindGPUGraphicsPipeline(pass, (SDL_GPUGraphicsPipeline*)_pipelines[material.Pass]);

        var binding = new SDL_GPUBufferBinding { buffer = (SDL_GPUBuffer*)_buffers[command.Mesh] };
        SDL_BindGPUVertexBuffers(pass, 0, &binding, 1);

        // The canvas is already in clip space; the rest goes through the camera.
        var transform = material.Pass == Pass.Overlay ? command.World : command.World * view.ViewProjection;
        SDL_PushGPUVertexUniformData(commands, 0, (IntPtr)(&transform), (uint)sizeof(Matrix4x4));

        // Mirrors show nothing without a panorama.
        if (material.Pass == Pass.Mirror)
        {
            if (Panorama == null)
            {
                return;
            }

            BindPanorama(commands, pass, Panorama, Panorama.MirrorSky, material.RowShift, view.Position);
            SDL_DrawGPUPrimitives(pass, (uint)command.Count, 1, (uint)command.First, 0);
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
        SDL_DrawGPUPrimitives(pass, (uint)command.Count, 1, (uint)command.First, 0);
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
        if (_target != null && width == _targetWidth && height == _targetHeight)
        {
            return;
        }

        if (_target != null)
        {
            SDL_ReleaseGPUTexture(_device, _target);
            SDL_ReleaseGPUTexture(_device, _depth);
        }

        _target = CreateTexture(ColourFormat, width, height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _depth = CreateTexture(_depthFormat, width, height, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);
        _targetWidth = width;
        _targetHeight = height;
    }

    private SDL_GPUTexture* CreateTexture(SDL_GPUTextureFormat format, uint width, uint height, SDL_GPUTextureUsageFlags usage)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = format,
            usage = usage,
            width = width,
            height = height,
            layer_count_or_depth = 1,
            num_levels = 1,
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

        SDL_ReleaseGPUGraphicsPipeline(_device, _skyPipeline);

        if (_target != null)
        {
            SDL_ReleaseGPUTexture(_device, _target);
            SDL_ReleaseGPUTexture(_device, _depth);
        }

        SDL_ReleaseGPUSampler(_device, _repeatSampler);
        SDL_ReleaseGPUSampler(_device, _clampSampler);
        SDL_ReleaseWindowFromGPUDevice(_device, _window.Handle);
        SDL_DestroyGPUDevice(_device);
    }
}
