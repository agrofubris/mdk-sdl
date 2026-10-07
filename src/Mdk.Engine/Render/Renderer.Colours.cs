using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>The enhanced look's colour textures: each index texture seen through a palette,
/// expanded to RGBA8 with all its mips (<see cref="ColourMips"/>), one array layer per frame, and
/// sampled trilinear and anisotropic. Made before the frame that first needs them; made again when
/// their indices or palette change (bullet holes, fading palettes). The original look and the canvas
/// keep the index textures.
/// <code>
///   index texture ─┐  (CPU copies)
///   palette ───────┴─► Expand ─► Chain ─► 2D array (layer = frame) + mips ─► enhanced.hlsl t3
/// </code></summary>
public sealed unsafe partial class Renderer
{
    private const float MaxAnisotropy = 16f;

    private readonly record struct ColourKey(int Texture, int Palette, int Frames);

    /// <summary>A texture's size and pixels as last uploaded.</summary>
    private sealed record Pixels(int Width, int Height, byte[] Data);

    private readonly Dictionary<int, Pixels> _indexPixels = [];
    private readonly Dictionary<int, byte[]> _palettePixels = [];
    private readonly Dictionary<ColourKey, IntPtr> _colours = [];
    private SDL_GPUSampler* _colourSampler;
    /// <summary>Bound where a draw has no colour texture (flat colours, the canvas).</summary>
    private SDL_GPUTexture* _blankColours;

    /// <summary>The trilinear, anisotropic sampler and the blank array.</summary>
    private void CreateColourSampler()
    {
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR,
            address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            max_lod = AllMips,
            enable_anisotropy = true,
            max_anisotropy = MaxAnisotropy,
        };
        _colourSampler = SDL_CreateGPUSampler(_device, &info);
        Check(_colourSampler != null, "SDL_CreateGPUSampler");
        _blankColours = CreateColours(1, 1, 1, 1);
        Upload(_blankColours, 1, 1, new byte[BytesPerPixel]);
    }

    /// <summary>Keeps what <see cref="ColourMips"/> needs of an index texture or a palette.</summary>
    private void KeepIndices(int texture, int width, int height, byte[] indices) =>
        _indexPixels[texture] = new Pixels(width, height, (byte[])indices.Clone());

    private void KeepPalette(int palette, byte[] rgba) => _palettePixels[palette] = (byte[])rgba.Clone();

    /// <summary>New pixels for a texture: its colour textures are made again.</summary>
    private void Changed(int texture, int width, int height, byte[] pixels)
    {
        if (_indexPixels.ContainsKey(texture))
        {
            KeepIndices(texture, width, height, pixels);
        }
        else if (_palettePixels.ContainsKey(texture))
        {
            KeepPalette(texture, pixels);
        }
        else
        {
            return;
        }

        ReleaseColours(k => k.Texture == texture || k.Palette == texture);
    }

    /// <summary>Frees the colour textures matching <paramref name="stale"/>.</summary>
    private void ReleaseColours(Func<ColourKey, bool> stale)
    {
        foreach (var key in _colours.Keys.Where(stale).ToList())
        {
            SDL_ReleaseGPUTexture(_device, (SDL_GPUTexture*)_colours[key]);
            _colours.Remove(key);
        }
    }

    /// <summary>Forgets the textures from <paramref name="first"/> on (a scope released).</summary>
    private void ReleaseColoursFrom(int first)
    {
        ReleaseColours(k => k.Texture >= first || k.Palette >= first);
        foreach (var id in _indexPixels.Keys.Where(id => id >= first).ToList())
        {
            _indexPixels.Remove(id);
        }

        foreach (var id in _palettePixels.Keys.Where(id => id >= first).ToList())
        {
            _palettePixels.Remove(id);
        }
    }

    private static ColourKey KeyOf(Material material) => new(material.Texture, material.Palette, Math.Max(material.FrameCount, 1));

    /// <summary>Makes the colour textures this frame's enhanced draws need, in one copy pass.</summary>
    private void PrepareColours(SDL_GPUCommandBuffer* commands)
    {
        var missing = _commands.Concat(_insets.SelectMany(i => i.Commands))
            .Where(c => c.Material.Texture != Material.None && ModeOf(c) is Mode.Lit or Mode.Sprite)
            .Select(c => KeyOf(c.Material))
            .Where(k => !_colours.ContainsKey(k) && _palettePixels.ContainsKey(k.Palette)
                && _indexPixels.TryGetValue(k.Texture, out var indices) && indices.Height % k.Frames == 0)
            .Distinct()
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var copy = SDL_BeginGPUCopyPass(commands);
        foreach (var key in missing)
        {
            _colours[key] = (IntPtr)UploadColours(copy, key);
        }

        SDL_EndGPUCopyPass(copy);
    }

    /// <summary>A colour texture: each frame expanded and its mips, uploaded in <paramref name="copy"/>.</summary>
    private SDL_GPUTexture* UploadColours(SDL_GPUCopyPass* copy, ColourKey key)
    {
        var indices = _indexPixels[key.Texture];
        var palette = _palettePixels[key.Palette];
        var (width, height) = (indices.Width, indices.Height / key.Frames);
        var frameSize = width * height;
        var levels = Enumerable.Range(0, key.Frames)
            .Select(f => ColourMips.Chain(ColourMips.Expand(indices.Data.AsSpan(f * frameSize, frameSize), palette), width, height))
            .ToList();
        var texture = CreateColours(width, height, key.Frames, levels[0].Count);

        var size = levels.Sum(chain => chain.Sum(level => level.Length));
        var transfer = CreateTransfer((uint)size, SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD);
        var mapped = new Span<byte>((void*)SDL_MapGPUTransferBuffer(_device, transfer, false), size);
        var offset = 0;
        foreach (var chain in levels)
        {
            foreach (var level in chain)
            {
                level.CopyTo(mapped[offset..]);
                offset += level.Length;
            }
        }

        SDL_UnmapGPUTransferBuffer(_device, transfer);

        offset = 0;
        for (var layer = 0; layer < key.Frames; layer++)
        {
            var (w, h) = (width, height);
            for (var mip = 0; mip < levels[layer].Count; mip++)
            {
                var source = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = (uint)offset, pixels_per_row = (uint)w, rows_per_layer = (uint)h };
                var destination = new SDL_GPUTextureRegion { texture = texture, mip_level = (uint)mip, layer = (uint)layer, w = (uint)w, h = (uint)h, d = 1 };
                SDL_UploadToGPUTexture(copy, &source, &destination, false);
                offset += levels[layer][mip].Length;
                (w, h) = (ColourMips.Half(w), ColourMips.Half(h));
            }
        }

        SDL_ReleaseGPUTransferBuffer(_device, transfer);
        return texture;
    }

    private SDL_GPUTexture* CreateColours(int width, int height, int layers, int levels)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D_ARRAY,
            format = ColourFormat,
            usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
            width = (uint)width,
            height = (uint)height,
            layer_count_or_depth = (uint)layers,
            num_levels = (uint)levels,
            sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
        };
        var texture = SDL_CreateGPUTexture(_device, &info);
        Check(texture != null, "SDL_CreateGPUTexture");
        return texture;
    }

    /// <summary>A draw's colour texture, or the blank one.</summary>
    private SDL_GPUTexture* ColoursOf(Material material) =>
        _colours.TryGetValue(KeyOf(material), out var texture) ? (SDL_GPUTexture*)texture : _blankColours;

    private void DisposeColours()
    {
        ReleaseColours(_ => true);
        SDL_ReleaseGPUTexture(_device, _blankColours);
        SDL_ReleaseGPUSampler(_device, _colourSampler);
    }
}
