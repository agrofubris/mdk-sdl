using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Flying pieces: shattered triangle groups, sparks, objects' break-up models. Not drawn yet.</summary>
// TODO port debris.gd
public sealed class Debris
{
    public void Shatter(string arena, int group, float life, float size, float speed, Vector3 point, Vector3 direction)
    {
    }

    public void Spark(string arena, Vector3 point, int count, float size, int colour, int range, float speed = 1f)
    {
    }

    public void BreakUp(MdkObject obj, Model model)
    {
    }

    public bool HasPieces(string arena) => false;

    public int PieceCount() => 0;

    public void Update(float ticks)
    {
    }
}
