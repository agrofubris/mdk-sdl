namespace Mdk.Engine.Platform;

/// <summary>Physical sizes on a display: window units per millimetre from SDL's display scale
/// (the OS's scale over its base density) and the window's pixel density (pixels per unit).
/// <code>
///   units per mm = base dpi × display scale / pixel density / 25.4
///   Android: 160 dpi × 2.625 (420 dpi) / 1 / 25.4 = 16.5 px/mm
/// </code></summary>
public static class ScreenDensity
{
    private const float MmPerInch = 25.4f;
    /// <summary>Dots per inch at display scale 1: Android's density-independent pixel.</summary>
    public const float AndroidDpi = 160f;
    /// <summary>Dots per inch at display scale 1: Windows' and Linux' 100 %.</summary>
    public const float DesktopDpi = 96f;

    /// <summary>The OS's base density.</summary>
    public static float BaseDpi => OperatingSystem.IsAndroid() ? AndroidDpi : DesktopDpi;

    /// <summary>Window units per millimetre; 0 when SDL doesn't know the display.</summary>
    public static float UnitsPerMm(float displayScale, float pixelDensity, float baseDpi)
    {
        if (displayScale <= 0f || pixelDensity <= 0f)
        {
            return 0f;
        }

        return baseDpi * displayScale / pixelDensity / MmPerInch;
    }
}
