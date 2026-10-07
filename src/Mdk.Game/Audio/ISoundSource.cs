using System.Numerics;

namespace Mdk.Game.Audio;

/// <summary>Something a sound follows (an object): where a point of it is now.</summary>
public interface ISoundSource
{
    /// <summary>The world position of <paramref name="offset"/> in the source's own frame.</summary>
    Vector3 SoundPosition(Vector3 offset);
}
