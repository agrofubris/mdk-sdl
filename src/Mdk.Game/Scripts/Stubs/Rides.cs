using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>What Kurt rides: walkers (XD, XD2), the snowboard, the bomber (XE). Nothing is ridden yet.</summary>
// TODO port rides.gd (with snowboard.gd and bomber.gd)
public sealed class Rides
{
    public MdkObject? Ridden;

    public void Update()
    {
    }

    public bool OnBoard() => false;

    public bool Hides(MdkObject obj) => false;

    public bool TakesHits() => Ridden != null;

    public void Lost(MdkObject obj)
    {
    }
}
