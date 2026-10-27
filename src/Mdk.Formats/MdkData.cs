namespace Mdk.Formats;

/// <summary>Locates the original MDK installation and reads its files.</summary>
public sealed partial class MdkData
{
    /// <summary>A file every installation has.</summary>
    private const string Marker = "TRAVERSE/TRAVSPRT.BNI";
    private const string EnvironmentVariable = "MDK_DATA_DIR";

    public string Dir { get; }

    private MdkData(string dir) => Dir = dir;

    /// <summary>The installation, or null: <c>MDK_DATA_DIR</c>, <c>mdk</c> in <see cref="LocalPaths"/>,
    /// the folders above the program, GOG and Steam folders.</summary>
    public static MdkData? Find()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env))
        {
            candidates.Add(env);
        }

        var local = LocalPaths.Get(LocalPaths.Game);
        if (local.Length != 0)
        {
            candidates.Add(local);
        }

        // The project folder placed within the MDK folder: walk up from the executable.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            candidates.Add(dir.FullName);
        }

        candidates.Add("C:/GOG Games/MDK");
        candidates.Add("C:/Program Files (x86)/GOG Galaxy/Games/MDK");
        candidates.Add("C:/Program Files (x86)/Steam/steamapps/common/MDK");

        var found = candidates.FirstOrDefault(c => File.Exists(CaseInsensitivePath.Resolve(c, Marker)));
        return found == null ? null : new MdkData(found);
    }

    /// <summary>An installation read in place (Android: the folder the user chose): <paramref name="folder"/>
    /// or a folder of it (the user picked the folder above, e.g. "MDK"); null when there's none
    /// or it can't be read.</summary>
    public static MdkData? At(string folder)
    {
        try
        {
            if (HasMarker(folder))
            {
                return new MdkData(folder);
            }

            var inner = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).FirstOrDefault(HasMarker) : null;
            return inner == null ? null : new MdkData(inner);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasMarker(string folder) => File.Exists(CaseInsensitivePath.Resolve(folder, Marker));

    /// <summary>Absolute path of a data file, e.g. <c>TRAVERSE/LEVEL3/LEVEL3O.MTO</c>, named as on disk
    /// (<c>MISC/MDKFONT.FTI</c> may be <c>MISC/mdkfont.fti</c>).</summary>
    public string PathOf(string relative) => CaseInsensitivePath.Resolve(Dir, relative);

    public byte[] Read(string relative) => File.ReadAllBytes(PathOf(relative));
}
