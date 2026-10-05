using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Kurt's items (decoy, bomb, mortar, twisters...) and blasts. Kurt doesn't use items yet;
/// the items' model animations (TRAVSPRT.BNI) are there.</summary>
// TODO port items.gd (with twister.gd)
public sealed class Items(Bni sprites)
{
    /// <summary>Kurt's thrown items and effects (0x1000), and active ones (0x4000).</summary>
    public const int FlagThrown = 0x1000;
    public const int FlagActive = 0x4000;

    /// <summary>The World's Most Interesting Bomb, and the decoy, while out.</summary>
    public MdkObject? Bomb;
    public MdkObject? Decoy;

    private readonly Dictionary<string, ModelAnimation?> _animations = [];

    public void Forget(MdkObject obj)
    {
        if (obj == Bomb)
        {
            Bomb = null;
        }

        if (obj == Decoy)
        {
            Decoy = null;
        }
    }

    /// <summary>A model animation of the items (TRAVSPRT.BNI: SW_INTER, SW_DUM_I, H150_R...).</summary>
    public ModelAnimation? GetAnimation(string name)
    {
        if (!_animations.TryGetValue(name, out var animation))
        {
            animation = _animations[name] = sprites.Has(name) ? ModelAnimation.Parse(name, sprites.Bytes, sprites.Entries[name].Offset) : null;
        }

        return animation;
    }

    public void UpdateThrown(MdkObject obj)
    {
    }

    public void UpdateTwisters()
    {
    }

    /// <summary>Whether a blast's kills count for the statistics.</summary>
    public enum Kills { Counted, Ignored }

    /// <summary>A blast: <paramref name="targets"/> 1 Kurt, 2 objects, 4 triangle groups.</summary>
    public void Blast(Vector3 center, int damage, float radius, int targets, int hitType, MdkObject? source, Kills kills = Kills.Counted)
    {
    }
}
