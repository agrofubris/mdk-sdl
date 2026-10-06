namespace Mdk.Formats;

/// <summary>Finds data files whatever their case: the game asks for <c>MISC/MDKFONT.FTI</c>, an
/// installation may have <c>MISC/mdkfont.fti</c>, which a case-sensitive file system (Linux) misses.</summary>
internal static class CaseInsensitivePath
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The path of <paramref name="relative"/> under <paramref name="dir"/>, each part named as
    /// on disk; parts not found keep their name.</summary>
    public static string Resolve(string dir, string relative)
    {
        var path = dir;
        foreach (var part in relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, Match(path, part));
        }

        return path;
    }

    /// <summary>The entry of a folder named <paramref name="name"/>: the exact name first, then any case.</summary>
    private static string Match(string dir, string name)
    {
        if (!Directory.Exists(dir))
        {
            return name;
        }

        string? other = null;
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            var entryName = Path.GetFileName(entry);
            if (entryName == name)
            {
                return name;
            }

            if (other == null && string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
            {
                other = entryName;
            }
        }

        return other ?? name;
    }
}
