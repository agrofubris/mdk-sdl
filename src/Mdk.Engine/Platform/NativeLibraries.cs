using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Mdk.Engine.Platform;

/// <summary>Keeps the program a single file: SDL3's native library is embedded and, on first use,
/// written to the user's local data folder (once per version, named by its hash) and loaded from there.
/// <code>
///   mdk.exe ─(resource native/SDL3.dll)─► %LOCALAPPDATA%/mdk-sdl/&lt;hash&gt;/SDL3.dll ─► SDL3-CS imports
/// </code></summary>
internal static class NativeLibraries
{
    private const string Resource = "native/SDL3.dll";
    private const string LibraryName = "SDL3";
    private const string FolderName = "mdk-sdl";
    private const int HashCharacters = 16;

    private static bool _installed;

    /// <summary>Routes SDL3-CS's imports to the embedded library. Without the resource (other
    /// platforms) the package's own native library is used.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        var path = Extract();
        if (path == null)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(SDL.SDL3).Assembly, (name, _, _) =>
            name.Contains(LibraryName, StringComparison.OrdinalIgnoreCase) ? NativeLibrary.Load(path) : IntPtr.Zero);
    }

    private static string? Extract()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        if (stream == null)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..HashCharacters];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName, hash);
        var path = Path.Combine(folder, Path.GetFileName(Resource));
        if (File.Exists(path))
        {
            return path;
        }

        // Written under another name first, so a half-written file is never loaded.
        Directory.CreateDirectory(folder);
        var temporary = path + "." + Environment.ProcessId;
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
        return path;
    }
}
