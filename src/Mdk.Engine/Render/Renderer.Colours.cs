using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>The enhanced look's colour textures: each index texture seen through a palette,
/// expanded to RGBA8 with all its mips (<see cref="ColourMips"/>), one array layer per frame, and
/// sampled trilinear and anisotropic. Made when the game prepares its materials (<see cref="Prepare"/>,
/// at load), else before the frame that first needs them; rewritten in place when their indices or
/// palette change (bullet holes, fading palettes). The original look and the canvas keep the index
/// textures.
/// <code>
///   index texture ─┐  (CPU copies)
///   palette ───────┴─► Expand ─► Chain (scratch) ─► staging ─► 2D array (layer = frame) + mips ─► enhanced.hlsl t3
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
    /// <summary>This frame's missing colour textures (reused).</summary>
    private readonly HashSet<ColourKey> _missing = [];
    private readonly List<ColourKey> _stale = [];
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

    /// <summary>Asks for a material's colour texture (the enhanced look's surfaces and sprites; nothing
    /// for the others): those of a level's load are all made in one copy pass before its first frame.</summary>
    public void Prepare(Material material)
    {
        if (!NeedsColours(material) || !CanExpand(KeyOf(material)) || _colours.ContainsKey(KeyOf(material)))
        {
            return;
        }

        // Made together in the next frame's copy pass, before it draws.
        _missing.Add(KeyOf(material));
    }

    /// <summary>Whether a material samples a colour texture (<see cref="ModeOf"/> Lit or Sprite).</summary>
    private static bool NeedsColours(Material material) =>
        material.Texture != Material.None && material.Shading != Shading.Original && material.Pass is not (Pass.Overlay or Pass.Mirror);

    /// <summary>Keeps what <see cref="ColourMips"/> needs of an index texture or a palette: the
    /// caller's array, not a copy (a level's textures are all kept: copies would double them; a
    /// changed texture comes through <see cref="Changed"/> with its array again).</summary>
    private void KeepIndices(int texture, int width, int height, byte[] indices)
    {
        if (_indexPixels.TryGetValue(texture, out var kept) && kept.Width == width && kept.Height == height && kept.Data == indices)
        {
            return;
        }

        _indexPixels[texture] = new Pixels(width, height, indices);
    }

    private void KeepPalette(int palette, byte[] rgba) => _palettePixels[palette] = rgba;

    /// <summary>New pixels for a texture: its colour textures are written again, in place.</summary>
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

        foreach (var (key, colours) in _colours)
        {
            if (key.Texture != texture && key.Palette != texture)
            {
                continue;
            }

            // A texture of another size is made again before the next frame that draws it.
            if (!CanExpand(key) || !SameSize(key, (SDL_GPUTexture*)colours))
            {
                _stale.Add(key);
                continue;
            }

            _batch.Add((key, colours));
        }

        if (_batch.Count > 0)
        {
            var commands = SDL_AcquireGPUCommandBuffer(_device);
            var copy = SDL_BeginGPUCopyPass(commands);
            UploadBatch(copy);
            SDL_EndGPUCopyPass(copy);
            SDL_SubmitGPUCommandBuffer(commands);
            _batch.Clear();
        }

        ReleaseStale();
    }

    /// <summary>Whether a colour texture still fits its index texture (the size it was made for).</summary>
    private bool SameSize(ColourKey key, SDL_GPUTexture* colours) =>
        _colourSizes.TryGetValue((IntPtr)colours, out var size) && size == SizeOf(key);

    private (int Width, int Height) SizeOf(ColourKey key)
    {
        var indices = _indexPixels[key.Texture];
        return (indices.Width, indices.Height / key.Frames);
    }

    private readonly Dictionary<IntPtr, (int Width, int Height)> _colourSizes = [];

    /// <summary>Frees the colour textures in <see cref="_stale"/>.</summary>
    private void ReleaseStale()
    {
        foreach (var key in _stale)
        {
            var texture = _colours[key];
            SDL_ReleaseGPUTexture(_device, (SDL_GPUTexture*)texture);
            _colourSizes.Remove(texture);
            _colours.Remove(key);
        }

        _stale.Clear();
    }

    /// <summary>Forgets the textures from <paramref name="first"/> on (a scope released).</summary>
    private void ReleaseColoursFrom(int first)
    {
        foreach (var key in _colours.Keys)
        {
            if (key.Texture >= first || key.Palette >= first)
            {
                _stale.Add(key);
            }
        }

        ReleaseStale();
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

    /// <summary>Whether the pixels to expand are known: the palette, and indices of whole frames.</summary>
    private bool CanExpand(ColourKey key) =>
        _palettePixels.ContainsKey(key.Palette) && _indexPixels.TryGetValue(key.Texture, out var indices) && indices.Height % key.Frames == 0;

    /// <summary>Makes the colour textures this frame's enhanced draws need and nobody prepared, in
    /// one copy pass.</summary>
    private void PrepareColours(SDL_GPUCommandBuffer* commands)
    {
        FindMissing(_commands);
        foreach (var inset in _insets)
        {
            FindMissing(inset.Commands);
        }

        if (_missing.Count == 0)
        {
            return;
        }

        foreach (var key in _missing)
        {
            _batch.Add((key, IntPtr.Zero));
        }

        var copy = SDL_BeginGPUCopyPass(commands);
        UploadBatch(copy);
        SDL_EndGPUCopyPass(copy);
        foreach (var (key, texture) in _batch)
        {
            _colours[key] = texture;
        }

        _batch.Clear();
        _missing.Clear();
    }

    private void FindMissing(List<DrawCommand> commands)
    {
        foreach (ref readonly var command in CollectionsMarshal.AsSpan(commands))
        {
            if (command.Material.Texture == Material.None || ModeOf(command) is not (Mode.Lit or Mode.Sprite))
            {
                continue;
            }

            var key = KeyOf(command.Material);
            if (!_colours.ContainsKey(key) && CanExpand(key))
            {
                _missing.Add(key);
            }
        }
    }

    /// <summary>The colour textures of a copy pass: each key and its texture (zero: a new one).</summary>
    private readonly List<(ColourKey Key, IntPtr Texture)> _batch = [];
    /// <summary>A colour texture's frames and mips on the CPU, grown to the largest (reused).</summary>
    private byte[] _chain = [];

    /// <summary>Bytes of a colour texture: every frame's mip chain.</summary>
    private int ColourSize(ColourKey key)
    {
        var (width, height) = SizeOf(key);
        return ColourMips.ChainSize(width, height) * key.Frames;
    }

    /// <summary>The <see cref="_batch"/>'s colour textures (new ones made): each frame expanded and
    /// its mips, all through one upload buffer, uploaded in <paramref name="copy"/>.</summary>
    private void UploadBatch(SDL_GPUCopyPass* copy)
    {
        var total = 0;
        foreach (var (key, _) in _batch)
        {
            total += ColourSize(key);
        }

        // Built in CPU memory (the upload buffer is slow to read back: each mip reads the last), in
        // chunks of the kept scratch, a chunk's textures on every core; then copied to the upload buffer.
        var mapped = MapStaging(total, out var transfer, out var temporary);
        var first = 0;
        var at = 0;
        while (first < _batch.Count)
        {
            var (last, size) = (first, 0);
            while (last < _batch.Count && (last == first || size + ColourSize(_batch[last].Key) <= ChainChunk))
            {
                size += ColourSize(_batch[last++].Key);
            }

            if (_chain.Length < size)
            {
                _chain = new byte[size];
            }

            ExpandChunk(first, last);
            _chain.AsSpan(0, size).CopyTo(mapped[at..]);
            (first, at) = (last, at + size);
        }

        SDL_UnmapGPUTransferBuffer(_device, transfer);
        var offset = 0;
        for (var i = 0; i < _batch.Count; i++)
        {
            var (key, texture) = _batch[i];
            if (texture == IntPtr.Zero)
            {
                _batch[i] = (key, texture = (IntPtr)CreateColours(key));
            }

            offset = UploadLayers(copy, key, (SDL_GPUTexture*)texture, transfer, offset);
        }

        if (temporary)
        {
            SDL_ReleaseGPUTransferBuffer(_device, transfer);
        }
    }

    /// <summary>The scratch's size for a chunk of textures (a larger texture gets a chunk of its own).</summary>
    private const int ChainChunk = 4 << 20;

    /// <summary>The batch's textures [<paramref name="first"/>, <paramref name="last"/>) expanded into
    /// <see cref="_chain"/> one after the other; several on every core (a bullet hole's one: no threads).</summary>
    private void ExpandChunk(int first, int last)
    {
        if (last - first == 1)
        {
            Expand(_batch[first].Key, _chain.AsSpan(0, ColourSize(_batch[first].Key)));
            return;
        }

        var offsets = new int[last - first + 1];
        for (var i = first; i < last; i++)
        {
            offsets[i - first + 1] = offsets[i - first] + ColourSize(_batch[i].Key);
        }

        var chain = _chain;
        Parallel.For(first, last, i => Expand(_batch[i].Key, chain.AsSpan(offsets[i - first], offsets[i - first + 1] - offsets[i - first])));
    }

    /// <summary>A colour texture's frames through the palette, each with its mips.</summary>
    private void Expand(ColourKey key, Span<byte> target)
    {
        var indices = _indexPixels[key.Texture];
        var palette = _palettePixels[key.Palette];
        var (width, height) = SizeOf(key);
        var frameSize = width * height;
        var chainSize = ColourMips.ChainSize(width, height);
        for (var f = 0; f < key.Frames; f++)
        {
            var chain = target.Slice(f * chainSize, chainSize);
            ColourMips.Expand(indices.Data.AsSpan(f * frameSize, frameSize), palette, chain);
            ColourMips.FillChain(chain, width, height);
        }
    }

    /// <summary>Uploads a colour texture's layers and mips from <paramref name="transfer"/> at
    /// <paramref name="offset"/>; returns the offset after them.</summary>
    private int UploadLayers(SDL_GPUCopyPass* copy, ColourKey key, SDL_GPUTexture* texture, SDL_GPUTransferBuffer* transfer, int offset)
    {
        var (width, height) = SizeOf(key);
        var levels = ColourMips.Levels(width, height);
        for (var layer = 0; layer < key.Frames; layer++)
        {
            var (w, h) = (width, height);
            for (var mip = 0; mip < levels; mip++)
            {
                var source = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = (uint)offset, pixels_per_row = (uint)w, rows_per_layer = (uint)h };
                var destination = new SDL_GPUTextureRegion { texture = texture, mip_level = (uint)mip, layer = (uint)layer, w = (uint)w, h = (uint)h, d = 1 };
                SDL_UploadToGPUTexture(copy, &source, &destination, false);
                offset += ColourMips.LevelSize(w, h);
                (w, h) = (ColourMips.Half(w), ColourMips.Half(h));
            }
        }

        return offset;
    }

    private SDL_GPUTexture* CreateColours(ColourKey key)
    {
        var (width, height) = SizeOf(key);
        var texture = CreateColours(width, height, key.Frames, ColourMips.Levels(width, height));
        _colourSizes[(IntPtr)texture] = (width, height);
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
        _stale.AddRange(_colours.Keys);
        ReleaseStale();
        SDL_ReleaseGPUTexture(_device, _blankColours);
        SDL_ReleaseGPUSampler(_device, _colourSampler);
    }
}
