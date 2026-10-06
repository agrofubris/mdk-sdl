using System.Numerics;
using Mdk.Engine.Render;

namespace Mdk.Game.Level;

/// <summary>How the scripts want the background drawn (0x574304, opcode 202; sky_draw 0x475b4c,
/// godot-mdk level.gd <c>show_sky</c>): 0 the sky, 1 black, anything else nothing (the last frame
/// stays, e.g. level 3's closed HMO_3).</summary>
public static class SkyModes
{
    public const int Shown = 0;
    public const int Black = 1;

    public static Backdrop BackdropOf(int mode) => mode switch
    {
        Shown => Backdrop.Sky,
        Black => Backdrop.Clear,
        _ => Backdrop.Keep,
    };

    /// <summary>The clear colour: black in mode 1, else <paramref name="sky"/> (above the panorama).</summary>
    public static Vector4 ClearOf(int mode, Vector4 sky) => mode == Black ? Vector4.UnitW : sky;
}
