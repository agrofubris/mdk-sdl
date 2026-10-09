using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.HdTextures;
using Mdk.Game.Menu;

namespace Mdk.Game.Tests;

/// <summary>The display options: window sizes and screen modes, the GPU backend per OS, VSync's
/// present modes, the frame limiter, their settings and the Display page's items.</summary>
public class DisplayTests
{
    [Fact]
    public void WindowedSizesFitTheUsableArea()
    {
        var sizes = Resolutions.Windowed(new Resolution(1920, 1040), new Resolution(1280, 960));

        Assert.Contains(new Resolution(1280, 960), sizes);
        Assert.Contains(new Resolution(1600, 900), sizes);
        Assert.DoesNotContain(new Resolution(1920, 1080), sizes);
        Assert.DoesNotContain(new Resolution(1600, 1200), sizes);
        Assert.All(sizes, s => Assert.True(s.Width <= 1920 && s.Height <= 1040));
    }

    [Fact]
    public void WindowedSizesKeepTheCurrentOnceSorted()
    {
        var sizes = Resolutions.Windowed(new Resolution(3440, 1400), new Resolution(1234, 777));

        Assert.Single(sizes, s => s == new Resolution(1234, 777));
        Assert.Contains(new Resolution(1920, 1080), sizes);
        Assert.DoesNotContain(new Resolution(2560, 1440), sizes);
        Assert.Equal(sizes.OrderBy(s => s.Width * s.Height).ThenBy(s => s.Width), sizes);
        Assert.Equal(sizes.Distinct(), sizes);
    }

    [Fact]
    public void ExclusiveModesDedupeByHighestRefresh()
    {
        ScreenMode[] modes =
        [
            new(1920, 1080, 60f), new(1920, 1080, 144f), new(1920, 1080, 120f),
            new(1280, 720, 60f), new(3440, 1440, 100f), new(3440, 1440, 144f),
        ];

        var list = Resolutions.Exclusive(modes);

        Assert.Equal([new(1280, 720, 60f), new(1920, 1080, 144f), new(3440, 1440, 144f)], list);
    }

    [Fact]
    public void StepWrapsAndStartsFromUnknown()
    {
        Resolution[] list = [new(800, 600), new(1280, 960), new(1920, 1080)];

        Assert.Equal(new Resolution(1920, 1080), Resolutions.Step(list, new Resolution(1280, 960), 1));
        Assert.Equal(new Resolution(800, 600), Resolutions.Step(list, new Resolution(1920, 1080), 1));
        Assert.Equal(new Resolution(1920, 1080), Resolutions.Step(list, new Resolution(800, 600), -1));
        Assert.Equal(new Resolution(800, 600), Resolutions.Step(list, new Resolution(1, 1), 1));
    }

    [Fact]
    public void ResolutionParses()
    {
        Assert.True(Resolution.TryParse("1600x1200", out var size));
        Assert.Equal(new Resolution(1600, 1200), size);
        Assert.Equal("1600x1200", size.ToString());
        Assert.False(Resolution.TryParse("big", out _));
        Assert.False(Resolution.TryParse("0x600", out _));
    }

    [Theory]
    [InlineData(Os.Windows, new[] { GpuBackend.Auto, GpuBackend.Direct3D12, GpuBackend.Vulkan })]
    [InlineData(Os.Linux, new[] { GpuBackend.Auto, GpuBackend.Vulkan })]
    [InlineData(Os.MacOs, new[] { GpuBackend.Auto, GpuBackend.Metal })]
    [InlineData(Os.Android, new[] { GpuBackend.Vulkan })]
    public void BackendsPerOs(Os os, GpuBackend[] backends) => Assert.Equal(backends, GpuBackends.On(os));

    [Theory]
    [InlineData(GpuBackend.Vulkan, Os.Windows, GpuBackend.Vulkan)]
    [InlineData(GpuBackend.Direct3D12, Os.Linux, GpuBackend.Auto)]
    [InlineData(GpuBackend.Metal, Os.Windows, GpuBackend.Auto)]
    [InlineData(GpuBackend.Direct3D12, Os.Android, GpuBackend.Vulkan)]
    [InlineData(GpuBackend.Auto, Os.MacOs, GpuBackend.Auto)]
    public void UnavailableBackendFallsBack(GpuBackend wanted, Os os, GpuBackend used) =>
        Assert.Equal(used, GpuBackends.Resolve(wanted, os));

    [Fact]
    public void BackendAttemptsEndWithAuto()
    {
        Assert.Equal([GpuBackend.Vulkan, GpuBackend.Auto], GpuBackends.Attempts(GpuBackend.Vulkan, Os.Windows));
        Assert.Equal([GpuBackend.Auto], GpuBackends.Attempts(GpuBackend.Auto, Os.Windows));
        Assert.Equal([GpuBackend.Auto], GpuBackends.Attempts(GpuBackend.Metal, Os.Linux));
    }

    [Theory]
    [InlineData("d3d12", GpuBackend.Direct3D12)]
    [InlineData("direct3d12", GpuBackend.Direct3D12)]
    [InlineData("Vulkan", GpuBackend.Vulkan)]
    [InlineData("metal", GpuBackend.Metal)]
    [InlineData("auto", GpuBackend.Auto)]
    public void BackendParses(string text, GpuBackend backend) => Assert.Equal(backend, GpuBackends.Parse(text));

    [Fact]
    public void BackendNames()
    {
        Assert.Null(GpuBackends.Parse("glide"));
        Assert.Equal("direct3d12", GpuBackends.Driver(GpuBackend.Direct3D12));
        Assert.Equal("vulkan", GpuBackends.Driver(GpuBackend.Vulkan));
        Assert.Equal("metal", GpuBackends.Driver(GpuBackend.Metal));
        Assert.Null(GpuBackends.Driver(GpuBackend.Auto));
        Assert.Equal(GpuBackend.Direct3D12, GpuBackends.Of("direct3d12"));
        Assert.Equal("Direct3D 12", GpuBackends.Name(GpuBackend.Direct3D12));
    }

    [Theory]
    [InlineData(VSync.On, new VSync[0], VSync.On)]
    [InlineData(VSync.Off, new[] { VSync.Off }, VSync.Off)]
    [InlineData(VSync.Off, new[] { VSync.Adaptive }, VSync.Adaptive)]
    [InlineData(VSync.Off, new VSync[0], VSync.On)]
    [InlineData(VSync.Adaptive, new[] { VSync.Adaptive }, VSync.Adaptive)]
    [InlineData(VSync.Adaptive, new[] { VSync.Off }, VSync.On)]
    public void PresentModeFallsBack(VSync wanted, VSync[] supported, VSync used) =>
        Assert.Equal(used, PresentModes.Choose(wanted, mode => mode == VSync.On || supported.Contains(mode)));

    [Fact]
    public void FrameLimiterWaitsTheRest()
    {
        Assert.Equal(TimeSpan.Zero, FrameLimiter.Remaining(TimeSpan.FromMilliseconds(5), FrameLimiter.Off));
        Assert.Equal(TimeSpan.Zero, FrameLimiter.Remaining(TimeSpan.FromMilliseconds(20), 60));
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60 - TimeSpan.TicksPerMillisecond * 5),
            FrameLimiter.Remaining(TimeSpan.FromMilliseconds(5), 60));
    }

    [Fact]
    public void OldSettingsKeepDisplayDefaults()
    {
        var on = Settings.Parse("fullscreen=True\n");
        var off = Settings.Parse("fullscreen=False\n");

        Assert.Equal(Fullscreen.Desktop, on.Fullscreen);
        Assert.Equal(Fullscreen.Off, off.Fullscreen);
        Assert.Equal(new Resolution(1280, 960), off.WindowSize);
        Assert.Null(off.ExclusiveSize);
        Assert.Equal(100, off.RenderScale);
        Assert.Equal(VSync.On, off.VSync);
        Assert.Equal(FrameLimiter.Off, off.FrameLimit);
        Assert.Equal(GpuBackend.Auto, off.Backend);
    }

    [Fact]
    public void DisplaySettingsRoundTrip()
    {
        var settings = new Settings
        {
            Fullscreen = Fullscreen.Exclusive,
            WindowSize = new Resolution(1600, 1200),
            ExclusiveSize = new Resolution(2560, 1440),
            RenderScale = 150,
            VSync = VSync.Adaptive,
            FrameLimit = 144,
            Backend = GpuBackend.Vulkan,
        };

        var read = Settings.Parse(settings.Format());

        Assert.Equal(settings.Format(), read.Format());
        Assert.Equal(Fullscreen.Exclusive, read.Fullscreen);
        Assert.Equal(new Resolution(1600, 1200), read.WindowSize);
        Assert.Equal(new Resolution(2560, 1440), read.ExclusiveSize);
        Assert.Equal(150, read.RenderScale);
        Assert.Equal(VSync.Adaptive, read.VSync);
        Assert.Equal(144, read.FrameLimit);
        Assert.Equal(GpuBackend.Vulkan, read.Backend);
    }

    [Fact]
    public void BadDisplaySettingsKeepDefaults()
    {
        var read = Settings.Parse("fullscreen=Maybe\nwindow_size=big\nexclusive_size=0x0\nrender_scale=33\nvsync=7\nframe_limit=59\ngpu_backend=Glide\n");
        var defaults = new Settings();

        Assert.Equal(defaults.Format(), read.Format());
    }

    [Fact]
    public void AndroidHidesWindowAndBackend()
    {
        Assert.Equal([DisplayItem.Mode, DisplayItem.Resolution, DisplayItem.Scale, DisplayItem.VSync, DisplayItem.FrameLimit, DisplayItem.Backend, DisplayItem.Gamma],
            DisplayMenu.Items(Host.Desktop));
        Assert.Equal([DisplayItem.Scale, DisplayItem.VSync, DisplayItem.FrameLimit, DisplayItem.Gamma], DisplayMenu.Items(Host.Android));
    }
}
