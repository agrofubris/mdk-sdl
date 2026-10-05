namespace Mdk.Formats;

/// <summary>A BNI archive (<c>TRAVSPRT.BNI</c>, <c>OPTIONS.BNI</c>...): images and sprite animations,
/// names of 12 characters (see <see cref="ArchiveIndex"/>).</summary>
public sealed class Bni
{
    private readonly ArchiveIndex _index;
    private readonly Dictionary<string, Texture> _images = [];
    private readonly Dictionary<string, SpriteAnimation> _animations = [];

    public byte[] Bytes => _index.Bytes;
    public IReadOnlyDictionary<string, ArchiveIndex.Entry> Entries => _index.Entries;

    private Bni(byte[] bytes) => _index = ArchiveIndex.Read(bytes, 12);

    public static Bni Load(string path) => new(File.ReadAllBytes(path));

    public bool Has(string name) => _index.Entries.ContainsKey(name);

    /// <summary>A plain image entry (<c>u16 width, u16 height</c>, palette indices).</summary>
    public Texture GetImage(string name)
    {
        if (!_images.TryGetValue(name, out var image))
        {
            image = _images[name] = Texture.Parse(name, Bytes, _index.Entries[name].Offset);
        }

        return image;
    }

    /// <summary>A sprite animation entry (<c>u32 size</c>, then the animation).</summary>
    public SpriteAnimation GetAnimation(string name)
    {
        if (!_animations.TryGetValue(name, out var animation))
        {
            animation = _animations[name] = SpriteAnimation.Parse(name, Bytes, _index.Entries[name].Offset + 4);
        }

        return animation;
    }
}
