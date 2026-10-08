namespace Mdk.Engine.Platform;

/// <summary>The operating systems as the download sees them.</summary>
internal enum HostOs { Windows, Linux, MacOs, Android, Other }

/// <summary>The OS's HTTP library: WinHTTP (Windows), libcurl (Linux, macOS), none (Android).</summary>
internal enum HttpBackend { WinHttp, Curl, None }

internal enum Transport { Plain, Tls }

/// <summary>A URL as the connection needs it: https://github.com/a?b ─► github.com, 443, /a?b, Tls.</summary>
internal readonly record struct HttpTarget(string Host, int Port, string Path, Transport Transport)
{
    public static HttpTarget Of(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{uri}: not http(s)");
        }

        var transport = uri.Scheme == Uri.UriSchemeHttps ? Transport.Tls : Transport.Plain;
        return new HttpTarget(uri.IdnHost, uri.Port, uri.PathAndQuery, transport);
    }
}

/// <summary>Downloads a file over HTTP(S) through the OS's own library, not .NET's HttpClient
/// (2.8 MB of the native exe). Redirects are followed (GitHub's release links redirect).
/// <code>
///   Save ─► WinHTTP   (winhttp.dll)            Windows
///        ─► libcurl   (libcurl.so.4, .dylib)   Linux, macOS
/// </code></summary>
public static class HttpDownload
{
    /// <summary>Who asks (some servers refuse requests without one).</summary>
    internal const string UserAgent = "mdk-sdl";

    /// <summary>Saves <paramref name="url"/> into <paramref name="path"/> (<paramref name="progress"/>:
    /// bytes so far, total or 0). Throws IOException naming the failure.</summary>
    public static void Save(string url, string path, Action<long, long>? progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var uri = new Uri(url);
        using var file = File.Create(path);
        switch (BackendOf(CurrentOs()))
        {
            case HttpBackend.WinHttp:
                WinHttp.Get(uri, file, progress, cancel);
                return;
            case HttpBackend.Curl:
                Curl.Get(uri, file, progress, cancel);
                return;
            default:
                throw new PlatformNotSupportedException("No HTTP library on this platform");
        }
    }

    internal static HttpBackend BackendOf(HostOs os) => os switch
    {
        HostOs.Windows => HttpBackend.WinHttp,
        HostOs.Android => HttpBackend.None,
        _ => HttpBackend.Curl,
    };

    private static HostOs CurrentOs() =>
        OperatingSystem.IsWindows() ? HostOs.Windows
        : OperatingSystem.IsAndroid() ? HostOs.Android
        : OperatingSystem.IsLinux() ? HostOs.Linux
        : OperatingSystem.IsMacOS() ? HostOs.MacOs
        : HostOs.Other;

    /// <summary>The error of a URL's download.</summary>
    internal static IOException Failure(Uri uri, string why) => new($"{uri}: {why}");

    internal static string StatusText(long status) => $"HTTP {status}";
}
