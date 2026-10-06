using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Mdk.Engine.Platform;

/// <summary>Keeps the program a single file: SDL3's native library for the target platform is embedded
/// and, on first use, written to the user's local data folder (once per version, named by its hash) and
/// loaded from there.
/// <code>
///   mdk ─(resource native/SDL3.dll, libSDL3.so or libSDL3.dylib)─► &lt;local data&gt;/mdk-sdl/&lt;hash&gt;/ ─► SDL3-CS imports
/// </code></summary>
internal static class NativeLibraries
{
    /// <summary>The embedded library's resource name starts with it (Mdk.Engine.csproj).</summary>
    private const string ResourceFolder = "native/";
    private const string LibraryName = "SDL3";
    private const string FolderName = "mdk-sdl";
    private const int HashCharacters = 16;

    private static readonly Lock Installing = new();
    private static bool _installed;

    /// <summary>Routes SDL3-CS's imports to the embedded library. Without the resource (other
    /// platforms) the package's own native library is used. Callers racing to the first SDL use
    /// wait until the route is set.</summary>
    public static void Install()
    {
        lock (Installing)
        {
            if (_installed)
            {
                return;
            }

            Route();
            _installed = true;
        }
    }

    private static void Route()
    {
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
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(ResourceFolder, StringComparison.Ordinal));
        if (resource == null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..HashCharacters];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName, hash);
        var path = Path.Combine(folder, Path.GetFileName(resource));
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
