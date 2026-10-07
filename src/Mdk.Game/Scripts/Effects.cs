using System.Numerics;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Sprite effects of the arenas (a port of godot-mdk's <c>effects.gd</c>; the original's
/// pool of 96 effects, arena+0x5c): slime bleeding from wounds (attach_effect 128), slime drops
/// (spawn_debris 136), bubbles (spawn_effect 132) and the smoke trails of sparks and pieces.
/// Sprites are animated textures drawn as billboards width × scale / 256 units wide (0x407048);
/// frame F - 1 - (life × speed mod F), so they play forwards as their life runs out.
/// <code>
///   wound ──(bursts)──► drops ─► fall, bounce ─► gone
///   bubble ─► rise, grow ─► pop (BUBB_POP) ─► gone
///   trail ─► fall ─► gone (11 ticks)
/// </code></summary>
public sealed class Effects(Random rng)
{
    public enum Kind { Wound, Drop, Bubble, Pop, Trail }

    /// <summary>Most effects alive at once.</summary>
    public const int MaxEffects = 96;
    /// <summary>Sprites are scale / 256 units per texel.</summary>
    public const float TexelsPerUnit = 256f;
    /// <summary>Gravity of drops and trails, units per tick² (about 64 units/s²).</summary>
    private const float Gravity = Debris.Gravity;
    private const float Bounce = 1.4f;
    private const int BounceTicks = 20;
    /// <summary>A wound spits drops for this many ticks at a time, at half a unit per tick.</summary>
    private const float BurstTicks = 30f;
    private const float BurstSpeed = 0.5f;
    private const float WoundDropScale = 4f;
    /// <summary>A wound's chance of a burst (out of 32768), weaker by 128-255 every loop.</summary>
    private const int FullIntensity = 0x7fff;
    private const int IntensityLossMin = 0x80;
    private const int IntensityLossMask = 0x7f;
    private const int RandRange = 32768;
    private const int RandHalf = 0x4000;
    private const float JitterScale = 1f / 65536f;
    /// <summary>Bubbles: scale 10 ± 3.3, 64 ticks, growing 4 a second, drifting ±1.6 u/s² sideways
    /// and rising at 0.5 u/s².</summary>
    private const float BubbleScale = 10f;
    private const float BubbleScaleSpread = 0.0002f;
    private const float BubbleLife = 64f;
    private const float BubbleGrowth = 4f;
    private const float BubbleDrift = 1e-4f;
    private const float BubbleRise = 0.5f;
    /// <summary>Trails: scale 4, one frame per tick for 11 ticks.</summary>
    private const float TrailScale = 4f;
    private const float TrailLife = 11f;
    private const float TicksPerSecond = 30f;
    private const float ContactBackoff = 0.01f;

    private const string WoundTexture = "SL_BIG";
    private const string BigDrop = "SL_MED";
    private const string SmallDrop = "SL_SMA";
    private const string TrailTexture = "TRAIL";
    private const string BubbleTexture = "BUBB";
    private const string PopTexture = "BUBB_POP";

    public sealed class Effect
    {
        public Kind Kind;
        /// <summary>The arena whose pool holds it, and its sprite's texture there.</summary>
        public string Arena = "";
        public string Texture = "";
        public int Frames = 1;
        public Vector3 Position;
        /// <summary>Units per tick.</summary>
        public Vector3 Velocity;
        public float Scale = 1f;
        /// <summary>Frames per tick.</summary>
        public float Speed = 0.5f;
        public float Life;
        // Wounds: the object, the point it bleeds at, the point the drops fly towards, the chance
        // of a burst (out of 32768) and the burst's time left and jitter.
        public MdkObject? Owner;
        public int Slot;
        public int Towards;
        public int Intensity = FullIntensity;
        public float Burst;
        public Vector3 Jitter;
        public bool Done;

        /// <summary>The sprite's frame now.</summary>
        public int Frame => Math.Clamp(Frames - 1 - (int)(Life * Speed) % Frames, 0, Frames - 1);
    }

    /// <summary>The frame count of an arena's texture, 0 when it has none (arena, name).</summary>
    public Func<string, string, int>? FrameCount;
    /// <summary>The arena's nearest hit along a segment: (arena, from, to).</summary>
    public Func<string, Vector3, Vector3, ScriptRuntime.RayHit?>? Ray;

    private readonly List<Effect> _effects = [];
    private readonly Dictionary<(MdkObject, int), Effect> _wounds = [];

    public IReadOnlyList<Effect> All => _effects;

    /// <summary>Starts the bleeding of a wound at the object's reference point <paramref name="slot"/>
    /// (0x4067b8): an SL_BIG blob looping at 15 fps that squirts drops towards <paramref name="towards"/>.</summary>
    public void Attach(MdkObject obj, int slot, int towards)
    {
        const float WoundScale = 10f;
        if (_wounds.ContainsKey((obj, slot)))
        {
            return;
        }

        var effect = Create(obj.Arena, WoundTexture, Kind.Wound, obj.ReferencePoint(slot), WoundScale, 0.5f);
        if (effect == null)
        {
            return;
        }

        effect.Owner = obj;
        effect.Slot = slot;
        effect.Towards = towards;
        effect.Life = effect.Frames * 2 - 1;
        _wounds[(obj, slot)] = effect;
    }

    /// <summary>Stops the bleeding at a reference point (attach_effect "OFF", 0x405250).</summary>
    public void Detach(MdkObject obj, int slot)
    {
        if (_wounds.TryGetValue((obj, slot), out var effect))
        {
            Remove(effect);
        }
    }

    /// <summary>A slime drop (0x406b3c): SL_MED or SL_SMA, playing once over 4 seconds.</summary>
    public void SpawnDrop(string arena, Vector3 point, Vector3 velocity, float scale)
    {
        var effect = Create(arena, rng.Next(2) == 0 ? BigDrop : SmallDrop, Kind.Drop, point, scale, 0.25f);
        if (effect == null)
        {
            return;
        }

        effect.Velocity = velocity;
        effect.Life = effect.Frames * 4 - 1;
    }

    /// <summary>A puff of the smoke trail of sparks and pieces (0x406070): TRAIL, one frame per tick
    /// for 11 ticks, falling.</summary>
    public void SpawnTrail(string arena, Vector3 point)
    {
        var effect = Create(arena, TrailTexture, Kind.Trail, point, TrailScale, 1f);
        if (effect != null)
        {
            effect.Life = TrailLife;
        }
    }

    /// <summary>A bubble (0x406434): BUBB, rising, growing and wobbling, then popping.</summary>
    public void SpawnBubble(string arena, Vector3 point)
    {
        var scale = (rng.Next(RandRange) - RandHalf) * BubbleScaleSpread + BubbleScale;
        var effect = Create(arena, BubbleTexture, Kind.Bubble, point, scale, 0.5f);
        if (effect != null)
        {
            effect.Life = BubbleLife;
        }
    }

    /// <summary>Whether an arena has any effect.</summary>
    public bool HasEffects(string arena) => _effects.Any(e => e.Arena == arena);

    private Effect? Create(string arena, string texture, Kind kind, Vector3 point, float scale, float speed)
    {
        if (_effects.Count >= MaxEffects)
        {
            return null;
        }

        var frames = FrameCount?.Invoke(arena, texture) ?? 0;
        if (frames == 0)
        {
            return null;
        }

        var effect = new Effect
        {
            Kind = kind,
            Arena = arena,
            Texture = texture,
            Frames = frames,
            Position = point,
            Scale = scale,
            Speed = speed,
        };
        _effects.Add(effect);
        return effect;
    }

    private void Remove(Effect effect)
    {
        if (effect.Owner != null && _wounds.GetValueOrDefault((effect.Owner, effect.Slot)) == effect)
        {
            _wounds.Remove((effect.Owner, effect.Slot));
        }

        _effects.Remove(effect);
    }

    /// <summary>Updates the effects by <paramref name="ticks"/>.</summary>
    public void Update(float ticks)
    {
        using var copy = ListCopy<Effect>.Of(_effects);
        foreach (var effect in copy)
        {
            switch (effect.Kind)
            {
                case Kind.Wound:
                    UpdateWound(effect, ticks);
                    break;
                case Kind.Drop or Kind.Trail:
                    Move(effect, ticks);
                    effect.Life -= ticks;
                    break;
                case Kind.Bubble:
                    UpdateBubble(effect, ticks);
                    break;
                case Kind.Pop:
                    effect.Life -= ticks;
                    break;
            }

            if (effect.Done || (effect.Life <= 0f && effect.Kind != Kind.Wound))
            {
                Remove(effect);
            }
        }
    }

    /// <summary>A bubble drifts, rises and grows; it pops (BUBB_POP) when it hits something or its
    /// life runs out.</summary>
    private void UpdateBubble(Effect effect, float ticks)
    {
        var dt = ticks / TicksPerSecond;
        var drift = new Vector3(rng.Next(RandRange) - RandHalf, rng.Next(RandRange) - RandHalf, 0f) * BubbleDrift;
        effect.Velocity += (drift + new Vector3(0f, 0f, BubbleRise)) * dt;
        effect.Scale += BubbleGrowth * dt;
        if (Move(effect, ticks))
        {
            effect.Life = 0f;
        }

        effect.Life -= ticks;
        if (effect.Life >= 1f)
        {
            return;
        }

        var frames = FrameCount?.Invoke(effect.Arena, PopTexture) ?? 0;
        if (frames == 0)
        {
            return;
        }

        effect.Kind = Kind.Pop;
        effect.Texture = PopTexture;
        effect.Frames = frames;
        effect.Velocity = Vector3.Zero;
        effect.Life = frames * 2 - 1;
    }

    /// <summary>A wound (0x40690c): the blob loops forever, weaker every loop; now and then it squirts
    /// a burst of drops (one per update for 30 ticks) towards the second point.</summary>
    private void UpdateWound(Effect effect, float ticks)
    {
        var obj = effect.Owner!;
        if (obj.Dead)
        {
            effect.Done = true;
            return;
        }

        effect.Life -= ticks;
        if (effect.Life < 0f)
        {
            effect.Life = effect.Frames * 2 - 1;
            effect.Intensity = Math.Max(effect.Intensity - ((rng.Next(RandRange) & IntensityLossMask) + IntensityLossMin), 0);
        }

        effect.Position = obj.ReferencePoint(effect.Slot);
        if (effect.Burst < 1f && rng.Next(RandRange) < effect.Intensity)
        {
            effect.Burst = BurstTicks;
            effect.Jitter = new Vector3(RandomHalf(), RandomHalf(), RandomHalf()) * JitterScale;
        }

        if (effect.Burst <= 0f)
        {
            return;
        }

        effect.Burst -= ticks;
        var towards = obj.ReferencePoint(effect.Towards) - effect.Position;
        var direction = towards == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(towards);
        SpawnDrop(obj.Arena, effect.Position, (direction + effect.Jitter) * BurstSpeed, WoundDropScale);
    }

    private int RandomHalf() => rng.Next(RandRange) - RandHalf;

    /// <summary>Moves an effect by its velocity (0x4061d8); drops and trails fall and bounce off the
    /// arena, bubbles stop. Returns whether it hit something.</summary>
    private bool Move(Effect effect, float ticks)
    {
        var motion = effect.Velocity * ticks;
        if (motion == Vector3.Zero)
        {
            return false;
        }

        var falls = effect.Kind is Kind.Drop or Kind.Trail;
        if (Ray?.Invoke(effect.Arena, effect.Position, effect.Position + motion) is not { } hit)
        {
            effect.Position += motion;
            if (falls)
            {
                effect.Velocity.Z -= Gravity * ticks;
            }

            return false;
        }

        effect.Position = hit.Point - Vector3.Normalize(motion) * ContactBackoff;
        if (!falls)
        {
            effect.Velocity = Vector3.Zero;
            return true;
        }

        effect.Velocity -= hit.Normal * Vector3.Dot(effect.Velocity, hit.Normal) * Bounce;
        effect.Life -= BounceTicks;
        return true;
    }
}
