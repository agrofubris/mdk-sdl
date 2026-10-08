using Mdk.Engine.Platform;

namespace Mdk.Engine.Render;

/// <summary>SDL_GPU's driver: chosen by SDL (Auto) or asked for (Options, Display; --gpu).</summary>
public enum GpuBackend { Auto, Direct3D12, Vulkan, Metal }

/// <summary>The backends per OS and their SDL names. One that fails (or isn't on the OS) falls back
/// to SDL's choice.
/// <code>
///   Windows: Direct3D 12, Vulkan   Linux: Vulkan   macOS: Metal   Android: Vulkan only
/// </code></summary>
public static class GpuBackends
{
    public static IReadOnlyList<GpuBackend> On(Os os) => os switch
    {
        Os.Windows => [GpuBackend.Auto, GpuBackend.Direct3D12, GpuBackend.Vulkan],
        Os.MacOs => [GpuBackend.Auto, GpuBackend.Metal],
        Os.Android => [GpuBackend.Vulkan],
        _ => [GpuBackend.Auto, GpuBackend.Vulkan],
    };

    /// <summary>The backend asked for if the OS has it, else the OS's first.</summary>
    public static GpuBackend Resolve(GpuBackend wanted, Os os) => On(os).Contains(wanted) ? wanted : On(os)[0];

    /// <summary>The backends tried in turn: the one asked for, then SDL's choice.</summary>
    public static IReadOnlyList<GpuBackend> Attempts(GpuBackend wanted, Os os) =>
        Resolve(wanted, os) is var backend && backend != GpuBackend.Auto ? [backend, GpuBackend.Auto] : [GpuBackend.Auto];

    /// <summary>SDL's driver name (SDL_PROP_GPU_DEVICE_CREATE_NAME_STRING); none for Auto.</summary>
    public static string? Driver(GpuBackend backend) => backend switch
    {
        GpuBackend.Direct3D12 => "direct3d12",
        GpuBackend.Vulkan => "vulkan",
        GpuBackend.Metal => "metal",
        _ => null,
    };

    /// <summary>The backend of an SDL driver name (Auto if unknown).</summary>
    public static GpuBackend Of(string driver) =>
        Enum.GetValues<GpuBackend>().FirstOrDefault(b => Driver(b) == driver, GpuBackend.Auto);

    /// <summary>--gpu's names: d3d12 (or direct3d12), vulkan, metal, auto; null if none.</summary>
    public static GpuBackend? Parse(string text) => text.ToLowerInvariant() switch
    {
        "d3d12" or "direct3d12" => GpuBackend.Direct3D12,
        "vulkan" => GpuBackend.Vulkan,
        "metal" => GpuBackend.Metal,
        "auto" => GpuBackend.Auto,
        _ => null,
    };

    public static string Name(GpuBackend backend) => backend switch
    {
        GpuBackend.Direct3D12 => "Direct3D 12",
        _ => backend.ToString(),
    };
}

/// <summary>How frames meet the display's refresh: VSync on (SDL_GPU_PRESENTMODE_VSYNC), off
/// (IMMEDIATE: may tear) or adaptive (MAILBOX: no tearing, no waiting; the newest frame shows).</summary>
public enum VSync { On, Off, Adaptive }

public static class PresentModes
{
    /// <summary>The modes tried for each choice; VSync is always supported.</summary>
    private static VSync[] Order(VSync wanted) => wanted switch
    {
        VSync.Off => [VSync.Off, VSync.Adaptive, VSync.On],
        VSync.Adaptive => [VSync.Adaptive, VSync.On],
        _ => [VSync.On],
    };

    /// <summary>The first mode of the choice's order the window supports.</summary>
    public static VSync Choose(VSync wanted, Func<VSync, bool> supported) =>
        Order(wanted).FirstOrDefault(supported, VSync.On);

    /// <summary>SDL's name of a mode, without its prefix.</summary>
    public static string Name(VSync mode) => mode switch
    {
        VSync.Off => "IMMEDIATE",
        VSync.Adaptive => "MAILBOX",
        _ => "VSYNC",
    };
}

/// <summary>The GPU device: its backend, name and driver version (empty when SDL doesn't tell).</summary>
public readonly record struct GpuInfo(GpuBackend Backend, string Device, string DriverVersion);
