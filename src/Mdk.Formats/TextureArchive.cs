namespace Mdk.Formats;

/// <summary>A texture archive: an arena's <c>HMO_n.MAT</c> or a level's <c>LEVELnS.MTI</c>.
/// <code>
/// char[12] name, u32 size, u32 count, then per entry:
///   char[8] name, u32 kind, u32 value, f32 ?, u32 offset (from base)
/// </code>
/// <c>kind</c> 0xFFFFFFFF: a palette colour (<c>value</c>; 256 and up are special). High 16 bits set: animated.</summary>
public sealed class TextureArchive
{
    private const uint KindColor = 0xFFFFFFFF;
    private const uint KindAnimatedMask = 0xFFFF0000;
    /// <summary>Standalone files start with a size word.</summary>
    private const int FileBase = 4;
    /// <summary>The name and size before the count; the 1996 demo's archives have none.</summary>
    private const int Header = 16;

    /// <summary>The 1996 demo's names are in lower case; the port's in upper case.</summary>
    internal enum Names { AsStored, Upper }

    public Dictionary<string, Texture> Textures { get; } = [];
    /// <summary>Name to palette index (or special value of 256 and up).</summary>
    public Dictionary<string, int> Colors { get; } = [];

    public static TextureArchive Parse(byte[] bytes, int baseOffset) => Parse(bytes, baseOffset, Header, Names.AsStored);

    /// <summary>An archive whose count is <paramref name="header"/> bytes after <paramref name="baseOffset"/>.</summary>
    internal static TextureArchive Parse(byte[] bytes, int baseOffset, int header, Names names)
    {
        var archive = new TextureArchive();
        var r = new BinReader(bytes, baseOffset + header);
        var count = r.U32();
        for (var i = 0; i < count; i++)
        {
            var name = names == Names.Upper ? r.Name(8).ToUpperInvariant() : r.Name(8);
            var kind = r.U32();
            var value = r.U32();
            r.F32();
            var offset = baseOffset + (int)r.U32();
            if (kind == KindColor)
            {
                archive.Colors[name] = (int)value;
                continue;
            }

            archive.Textures[name] = (kind & KindAnimatedMask) != 0
                ? Texture.ParseAnimated(name, bytes, offset, kind)
                : Texture.Parse(name, bytes, offset);
        }

        return archive;
    }

    public static TextureArchive Load(string path) => Parse(File.ReadAllBytes(path), FileBase);
}
