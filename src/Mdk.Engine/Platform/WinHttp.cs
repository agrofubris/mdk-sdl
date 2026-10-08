using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mdk.Engine.Platform;

/// <summary>Windows' HTTP library (winhttp.dll), synchronous:
/// <code>
///   Open ─► Connect(host, port) ─► OpenRequest(GET path, TLS?) ─► Send ─► ReceiveResponse
///        ─► QueryHeaders(status, length) ─► ReadData … 0 bytes
/// </code>
/// Redirects are followed by default; the system's proxy settings apply.</summary>
internal static unsafe partial class WinHttp
{
    private const string Library = "winhttp.dll";
    private const uint AccessAutomaticProxy = 4;
    private const uint FlagSecure = 0x00800000;
    private const uint QueryStatusCode = 19;
    private const uint QueryContentLength = 5;
    private const uint QueryFlagNumber = 0x20000000;
    private const uint QueryFlagNumber64 = 0x08000000;
    private const int StatusOk = 200;
    private const int ReadBuffer = 1 << 16;

    // WinHTTP's errors (winhttp.h), named in failures.
    private const int ErrorTimeout = 12002;
    private const int ErrorNameNotResolved = 12007;
    private const int ErrorCannotConnect = 12029;
    private const int ErrorConnectionLost = 12030;
    private const int ErrorSecureFailure = 12175;

    /// <summary>Writes the body of <paramref name="uri"/> (after redirects) into <paramref name="to"/>.</summary>
    public static void Get(Uri uri, Stream to, Action<long, long>? progress, CancellationToken cancel)
    {
        var target = HttpTarget.Of(uri);
        using var session = Check(uri, WinHttpOpen(HttpDownload.UserAgent, AccessAutomaticProxy, 0, 0, 0));
        using var connection = Check(uri, WinHttpConnect(session, target.Host, (ushort)target.Port, 0));
        var flags = target.Transport == Transport.Tls ? FlagSecure : 0;
        using var request = Check(uri, WinHttpOpenRequest(connection, "GET", target.Path, 0, 0, 0, flags));
        Check(uri, WinHttpSendRequest(request, 0, 0, 0, 0, 0, 0));
        Check(uri, WinHttpReceiveResponse(request, 0));

        uint status = 0;
        uint size = sizeof(uint);
        Check(uri, WinHttpQueryHeaders(request, QueryStatusCode | QueryFlagNumber, 0, &status, ref size, 0));
        if (status != StatusOk)
        {
            throw HttpDownload.Failure(uri, HttpDownload.StatusText(status));
        }

        // No Content-Length (chunked): the total is unknown, 0.
        long total = 0;
        size = sizeof(long);
        WinHttpQueryHeaders(request, QueryContentLength | QueryFlagNumber64, 0, &total, ref size, 0);

        Read(uri, request, to, total, progress, cancel);
    }

    /// <summary>The body's bytes until WinHTTP gives none.</summary>
    private static void Read(Uri uri, Handle request, Stream to, long total, Action<long, long>? progress, CancellationToken cancel)
    {
        var buffer = new byte[ReadBuffer];
        long done = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            uint read;
            fixed (byte* bytes = buffer)
            {
                Check(uri, WinHttpReadData(request, bytes, ReadBuffer, out read));
            }

            if (read == 0)
            {
                return;
            }

            to.Write(buffer, 0, (int)read);
            done += read;
            progress?.Invoke(done, total);
        }
    }

    /// <summary>A WinHTTP error's name: 12007 ─► "host not found".</summary>
    internal static string Describe(int error) => error switch
    {
        ErrorTimeout => "timed out",
        ErrorNameNotResolved => "host not found",
        ErrorCannotConnect => "can't connect",
        ErrorConnectionLost => "connection lost",
        ErrorSecureFailure => "secure connection failed",
        _ => $"WinHTTP error {error}",
    };

    private static Handle Check(Uri uri, Handle handle)
    {
        if (handle.IsInvalid)
        {
            throw HttpDownload.Failure(uri, Describe(Marshal.GetLastPInvokeError()));
        }

        return handle;
    }

    private static void Check(Uri uri, bool done)
    {
        if (!done)
        {
            throw HttpDownload.Failure(uri, Describe(Marshal.GetLastPInvokeError()));
        }
    }

    /// <summary>A WinHTTP handle (session, connection, request), closed once.</summary>
    internal sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Handle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => WinHttpCloseHandle(handle);
    }

    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial Handle WinHttpOpen(string agent, uint accessType, nint proxy, nint bypass, uint flags);

    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial Handle WinHttpConnect(Handle session, string server, ushort port, uint reserved);

    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial Handle WinHttpOpenRequest(Handle connection, string verb, string path, nint version, nint referrer, nint acceptTypes, uint flags);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpSendRequest(Handle request, nint headers, uint headersLength, nint optional, uint optionalLength, uint totalLength, nuint context);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpReceiveResponse(Handle request, nint reserved);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpQueryHeaders(Handle request, uint infoLevel, nint name, void* buffer, ref uint bufferLength, nint index);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpReadData(Handle request, byte* buffer, uint toRead, out uint read);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpCloseHandle(nint handle);
}
