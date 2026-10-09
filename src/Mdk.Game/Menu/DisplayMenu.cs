using Mdk.Game.HdTextures;

namespace Mdk.Game.Menu;

/// <summary>The Display page's items.</summary>
public enum DisplayItem { Mode, Resolution, Scale, VSync, FrameLimit, Backend, Gamma }

/// <summary>Which Display items a platform shows: a phone is always fullscreen on Vulkan, so it has
/// no display mode, resolution or backend.</summary>
public static class DisplayMenu
{
    public static IReadOnlyList<DisplayItem> Items(Host host) => host == Host.Android
        ? [DisplayItem.Scale, DisplayItem.VSync, DisplayItem.FrameLimit, DisplayItem.Gamma]
        : Enum.GetValues<DisplayItem>();
}
