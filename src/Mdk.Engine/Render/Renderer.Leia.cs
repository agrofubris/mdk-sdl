using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Render;

/// <summary>Leia SR's weave path: the patched SDL3.dll's D3D12 present-weave bridge (the
/// starfox interop ABI) and the native shim (mdk_leia.dll) that drives the Simulated Reality
/// runtime. Every stage declines gracefully: a stock SDL3.dll has no bridge, a machine without
/// the SR Platform has no weaver, and a failed weave is latched off for the device — the plain
/// side-by-side pair is presented instead. The environment variable
/// <c>MDK_DISABLE_LEIA_WEAVE</c> forces the fallback without touching the weaver.
/// <code>
///   pair ─► bridge (patched SDL: transitions, target bound) ─► shim callback ─► SR weaver ─► woven
///          no bridge / no runtime / failed weave ─────────────────────────────► the pair, plainly
/// </code></summary>
public sealed unsafe partial class Renderer
{
    /// <summary>The starfox interop ABI's property names (see third_party/sdl3-interop).</summary>
    private const string LeiaDeviceProperty = "starfox.gpu.d3d12.device.v2";
    private const string LeiaWeaveProperty = "starfox.gpu.d3d12.present-weave.v1";
    private const string LeiaHwndProperty = "SDL.window.win32.hwnd";
    private const string LeiaDisableVariable = "MDK_DISABLE_LEIA_WEAVE";
    private const string LeiaShimName = "mdk_leia.dll";

    private static readonly byte[] DevicePropertyName = "starfox.gpu.d3d12.device.v2\0"u8.ToArray();
    private static readonly byte[] WeavePropertyName = "starfox.gpu.d3d12.present-weave.v1\0"u8.ToArray();
    private static readonly byte[] HwndPropertyName = "SDL.window.win32.hwnd\0"u8.ToArray();

    /// <summary>The detailed weave trace (the probe's pointers, each decline's stage).</summary>
    private static bool LeiaTrace => Environment.GetEnvironmentVariable("MDK_TRACE_LEIA") is { Length: > 0 };

    /// <summary>The bridge as the patched SDL sets it (third_party/sdl3-interop/sdl_d3d12_bridge.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WeaveBridge
    {
        public uint Version;
        public IntPtr Weave;
    }

    /// <summary>The shim's exports (native/mdk_leia).</summary>
    private IntPtr _leiaShim;
    private IntPtr _leiaEnsure;
    private IntPtr _leiaCallback;
    private IntPtr _leiaRelease;
    private IntPtr _leiaDisable;
    private bool _leiaProbed;
    private bool _leiaUnavailable;
    private IntPtr _leiaWeave;

    /// <summary>The pair woven for this frame; false leaves the pair to the plain fallback.</summary>
    private bool WeaveLeia(SDL_GPUCommandBuffer* commands)
    {
        if (Environment.GetEnvironmentVariable(LeiaDisableVariable) is { Length: > 0 } || _leiaUnavailable)
        {
            return false;
        }

        if (!_leiaProbed)
        {
            ProbeLeia();
        }

        if (_leiaWeave == IntPtr.Zero || _leiaEnsure == IntPtr.Zero)
        {
            return false;
        }

        var device = Property(SDL_GetGPUDeviceProperties(_device), DevicePropertyName);
        var hwnd = Property(SDL_GetWindowProperties(_window.Handle), HwndPropertyName);
        if (device == IntPtr.Zero || hwnd == IntPtr.Zero)
        {
            _leiaUnavailable = true;
            Console.Error.WriteLine("leia-sr: no D3D12 device or window handle; presenting side-by-side");
            return false;
        }

        var ensure = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)_leiaEnsure;
        if (ensure(device, hwnd) == 0)
        {
            // The shim logged the SR probe's failure; keep presenting the pair.
            _leiaUnavailable = true;
            return false;
        }

        var weave = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, uint, uint, IntPtr, IntPtr, byte>)_leiaWeave;
        if (weave((IntPtr)commands, (IntPtr)_leiaPair, (IntPtr)_leiaWoven, _leiaWidth, _leiaHeight, _leiaCallback, IntPtr.Zero) == 0)
        {
            // One broken weave must not blank the window: this frame stays on the pair and the
            // device's weaver is dropped and latched off (no per-frame retries).
            if (_leiaDisable != IntPtr.Zero)
            {
                ((delegate* unmanaged[Cdecl]<void>)_leiaDisable)();
            }

            _leiaUnavailable = true;
            Console.Error.WriteLine("leia-sr: weave failed; presenting side-by-side");
            return false;
        }

        return true;
    }

    /// <summary>The one-time probe: D3D12, the patched SDL's bridge, and the shim with its
    /// exports. Any miss logs once and latches the fallback.</summary>
    private void ProbeLeia()
    {
        _leiaProbed = true;
        var driver = Unsafe_SDL_GetGPUDeviceDriver(_device);
        if (driver == null || Marshal.PtrToStringUTF8((IntPtr)driver) != "direct3d12")
        {
            _leiaUnavailable = true;
            Console.Error.WriteLine("leia-sr: not the Direct3D12 backend; presenting side-by-side");
            return;
        }

        var props = SDL_GetGPUDeviceProperties(_device);
        var bridge = Property(props, WeavePropertyName);
        if (LeiaTrace)
        {
            Console.Error.WriteLine($"leia-sr: probe props={props} device={Property(props, DevicePropertyName)} weave={bridge}");
        }

        if (bridge == IntPtr.Zero)
        {
            _leiaUnavailable = true;
            Console.Error.WriteLine("leia-sr: no SDL weave bridge (stock SDL3.dll); presenting side-by-side");
            return;
        }

        var weave = Marshal.PtrToStructure<WeaveBridge>(bridge);
        if (weave.Version != 1 || weave.Weave == IntPtr.Zero)
        {
            _leiaUnavailable = true;
            Console.Error.WriteLine($"leia-sr: weave bridge version {weave.Version}; presenting side-by-side");
            return;
        }

        if (!NativeLibrary.TryLoad(LeiaShimName, out _leiaShim)
            || !NativeLibrary.TryGetExport(_leiaShim, "mdk_leia_ensure", out _leiaEnsure)
            || !NativeLibrary.TryGetExport(_leiaShim, "mdk_leia_weave", out _leiaCallback)
            || !NativeLibrary.TryGetExport(_leiaShim, "mdk_leia_release", out _leiaRelease)
            || !NativeLibrary.TryGetExport(_leiaShim, "mdk_leia_disable", out _leiaDisable))
        {
            _leiaUnavailable = true;
            Console.Error.WriteLine($"leia-sr: {LeiaShimName} not found; presenting side-by-side");
            return;
        }

        _leiaWeave = weave.Weave;
    }

    private static IntPtr Property(SDL_PropertiesID properties, byte[] name)
    {
        fixed (byte* utf8 = name)
        {
            return SDL_GetPointerProperty(properties, utf8, IntPtr.Zero);
        }
    }

    /// <summary>The weaver is dropped before the device goes: called when the renderer ends.</summary>
    private void ReleaseLeiaShim()
    {
        if (_leiaRelease != IntPtr.Zero)
        {
            ((delegate* unmanaged[Cdecl]<void>)_leiaRelease)();
        }

        if (_leiaShim != IntPtr.Zero)
        {
            NativeLibrary.Free(_leiaShim);
            _leiaShim = IntPtr.Zero;
            _leiaEnsure = _leiaCallback = _leiaRelease = _leiaDisable = _leiaWeave = IntPtr.Zero;
        }
    }
}
