using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mdk.Engine.Platform;

/// <summary>libcurl's options used (curl.h: type base + number).</summary>
internal enum CurlOption
{
    WriteData = 10001,
    Url = 10002,
    UserAgent = 10018,
    NoProgress = 43,
    FailOnError = 45,
    FollowLocation = 52,
    WriteFunction = 20011,
    XferInfoFunction = 20219,
    XferInfoData = 10057,
}

/// <summary>One curl_easy_setopt: a number, or a text when <see cref="Text"/> is set.</summary>
internal readonly record struct CurlSetting(CurlOption Option, long Number, string? Text);

/// <summary>How a variadic C function gets its first variadic argument: in a register (x64), or
/// on the stack (Apple arm64).</summary>
internal enum VarargsAbi { Registers, Stack }

/// <summary>libcurl, loaded at run time (Linux and macOS ship it):
/// <code>
///   easy_init ─► setopt(URL, FOLLOWLOCATION, FAILONERROR, USERAGENT, write ─► file, progress)
///             ─► easy_perform ─► getinfo(RESPONSE_CODE) on failure ─► easy_cleanup
/// </code></summary>
internal static unsafe class Curl
{
    private static readonly string[] Libraries = ["libcurl.so.4", "libcurl.so", "libcurl.4.dylib", "/usr/lib/libcurl.4.dylib"];
    private const int Ok = 0;
    private const int HttpReturnedError = 22;
    private const int AbortedByCallback = 42;
    private const int ResponseCode = 0x200000 + 2;
    private const int CallbackContinue = 0;
    private const int CallbackAbort = 1;

    private static readonly Lazy<Api> Loaded = new(Load);

    /// <summary>The options of a download of <paramref name="uri"/>, before the callbacks.</summary>
    internal static IReadOnlyList<CurlSetting> Settings(Uri uri) =>
    [
        new(CurlOption.Url, 0, uri.AbsoluteUri),
        new(CurlOption.UserAgent, 0, HttpDownload.UserAgent),
        new(CurlOption.FollowLocation, 1, null),
        // A 4xx or 5xx fails instead of saving the error page.
        new(CurlOption.FailOnError, 1, null),
        new(CurlOption.NoProgress, 0, null),
    ];

    /// <summary>A failure's text: an HTTP error by its status (22, 404 ─► "HTTP 404"), else curl's.</summary>
    internal static string Describe(int code, long status, string text) =>
        code == HttpReturnedError ? HttpDownload.StatusText(status) : text;

    /// <summary>Writes the body of <paramref name="uri"/> (after redirects) into <paramref name="to"/>.</summary>
    public static void Get(Uri uri, Stream to, Action<long, long>? progress, CancellationToken cancel)
    {
        var api = Loaded.Value;
        var easy = api.Init();
        if (easy == 0)
        {
            throw HttpDownload.Failure(uri, "curl_easy_init failed");
        }

        var transfer = new Transfer(to, progress, cancel);
        var handle = GCHandle.Alloc(transfer);
        var texts = new List<nint>();
        try
        {
            foreach (var setting in Settings(uri))
            {
                var value = setting.Text == null ? (nint)setting.Number : Utf8(setting.Text, texts);
                api.Set(easy, setting.Option, value);
            }

            api.Set(easy, CurlOption.WriteFunction, (nint)(delegate* unmanaged[Cdecl]<byte*, nuint, nuint, nint, nuint>)&OnWrite);
            api.Set(easy, CurlOption.WriteData, GCHandle.ToIntPtr(handle));
            api.Set(easy, CurlOption.XferInfoFunction, (nint)(delegate* unmanaged[Cdecl]<nint, long, long, long, long, int>)&OnProgress);
            api.Set(easy, CurlOption.XferInfoData, GCHandle.ToIntPtr(handle));

            var code = api.Perform(easy);
            Finish(uri, api, easy, code, transfer);
        }
        finally
        {
            api.Cleanup(easy);
            handle.Free();
            texts.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    /// <summary>Throws for a failed transfer: the callbacks' own error first, then curl's.</summary>
    private static void Finish(Uri uri, Api api, nint easy, int code, Transfer transfer)
    {
        if (transfer.Error != null)
        {
            throw transfer.Error;
        }

        if (code == AbortedByCallback)
        {
            transfer.Cancel.ThrowIfCancellationRequested();
        }

        if (code == Ok)
        {
            return;
        }

        long status = 0;
        api.GetInfo(easy, ResponseCode, (nint)(&status));
        throw HttpDownload.Failure(uri, Describe(code, status, api.Error(code)));
    }

    private static nint Utf8(string text, List<nint> texts)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(text);
        texts.Add(pointer);
        return pointer;
    }

    /// <summary>curl's body callback: the bytes go to the file. Fewer bytes than given abort.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint OnWrite(byte* data, nuint size, nuint count, nint user)
    {
        var transfer = (Transfer)GCHandle.FromIntPtr(user).Target!;
        var length = (int)(size * count);
        try
        {
            transfer.To.Write(new ReadOnlySpan<byte>(data, length));
            return (nuint)length;
        }
        catch (Exception e)
        {
            transfer.Error = e;
            return 0;
        }
    }

    /// <summary>curl's progress callback: reports it, aborts when cancelled.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnProgress(nint user, long total, long done, long upTotal, long upDone)
    {
        var transfer = (Transfer)GCHandle.FromIntPtr(user).Target!;
        try
        {
            if (done > 0)
            {
                transfer.Progress?.Invoke(done, total);
            }

            return transfer.Cancel.IsCancellationRequested ? CallbackAbort : CallbackContinue;
        }
        catch (Exception e)
        {
            transfer.Error = e;
            return CallbackAbort;
        }
    }

    private static Api Load()
    {
        foreach (var name in Libraries)
        {
            if (NativeLibrary.TryLoad(name, out var library))
            {
                return new Api(library);
            }
        }

        throw new IOException("libcurl not found: install libcurl (e.g. apt install libcurl4)");
    }

    /// <summary>What a download's callbacks share with it.</summary>
    private sealed class Transfer(Stream to, Action<long, long>? progress, CancellationToken cancel)
    {
        public Stream To { get; } = to;
        public Action<long, long>? Progress { get; } = progress;
        public CancellationToken Cancel { get; } = cancel;
        public Exception? Error { get; set; }
    }

    /// <summary>libcurl's functions. setopt and getinfo are variadic: on Apple arm64 their value is
    /// passed on the stack, so they're called with the 6 argument registers left padded.</summary>
    private sealed class Api(nint library)
    {
        private readonly delegate* unmanaged[Cdecl]<nint> _init = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(library, "curl_easy_init");
        private readonly nint _setopt = NativeLibrary.GetExport(library, "curl_easy_setopt");
        private readonly nint _getinfo = NativeLibrary.GetExport(library, "curl_easy_getinfo");
        private readonly delegate* unmanaged[Cdecl]<nint, int> _perform = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(library, "curl_easy_perform");
        private readonly delegate* unmanaged[Cdecl]<int, byte*> _strerror = (delegate* unmanaged[Cdecl]<int, byte*>)NativeLibrary.GetExport(library, "curl_easy_strerror");
        private readonly delegate* unmanaged[Cdecl]<nint, void> _cleanup = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "curl_easy_cleanup");

        private static VarargsAbi Abi =>
            OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? VarargsAbi.Stack : VarargsAbi.Registers;

        public nint Init() => _init();

        public int Perform(nint easy) => _perform(easy);

        public void Cleanup(nint easy) => _cleanup(easy);

        public string Error(int code) => Marshal.PtrToStringUTF8((nint)_strerror(code)) ?? $"curl error {code}";

        public void Set(nint easy, CurlOption option, nint value)
        {
            var code = Variadic(_setopt, easy, (int)option, value);
            if (code != Ok)
            {
                throw new IOException($"curl_easy_setopt({option}): {Error(code)}");
            }
        }

        public void GetInfo(nint easy, int info, nint value) => Variadic(_getinfo, easy, info, value);

        /// <summary>f(handle, number, value...) with value as the first variadic argument.</summary>
        private static int Variadic(nint function, nint handle, int number, nint value)
        {
            if (Abi == VarargsAbi.Registers)
            {
                return ((delegate* unmanaged[Cdecl]<nint, int, nint, int>)function)(handle, number, value);
            }

            return ((delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, nint, nint, nint, nint, int>)function)(handle, number, 0, 0, 0, 0, 0, 0, value);
        }
    }
}
