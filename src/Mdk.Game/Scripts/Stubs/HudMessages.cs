namespace Mdk.Game.Scripts;

/// <summary>On-screen messages (hud_message). Not shown yet.</summary>
// TODO port hud/hud_messages.gd
public sealed class HudMessages
{
    public const int FlagZoom = 1;
    public const int FlagFront = 2;

    public bool Push(string textName, int flags, float seconds) => false;
}
