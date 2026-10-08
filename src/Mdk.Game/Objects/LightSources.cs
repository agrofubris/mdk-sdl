using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Scripts;

namespace Mdk.Game.Objects;

/// <summary>The game's point lights for the enhanced look, gathered each frame: Kurt's muzzle flash
/// (it flickers with the flash sprite), sniper rounds in flight, explosions (fading as their
/// animation plays) and spawn_box fires (flickering).
/// <code>
///   Kurt.Muzzle ──────────────┐
///   SniperRounds.Visuals ─────┤
///   explosions (EffectFrames) ┼──► PointLights ──► Renderer
///   Boxes "FIRE" ─────────────┘
/// </code></summary>
public static class LightSources
{
    /// <summary>A fire dims by up to this share as it flickers.</summary>
    public const float FlickerDepth = 0.3f;
    private const string FireTexture = "FIRE";

    /// <summary>Colours (linear, strength included) and reaches (units; Kurt is 5 tall).</summary>
    private static readonly Vector3 MuzzleColour = new Vector3(1f, 0.75f, 0.4f) * 6f;
    private const float MuzzleReach = 30f;
    /// <summary>The chain gun is about this high above Kurt's feet.</summary>
    private const float MuzzleHeight = 3.5f;
    private static readonly Vector3 RoundColour = new Vector3(1f, 0.9f, 0.6f) * 3f;
    private const float RoundReach = 15f;
    private static readonly Vector3 ExplosionColour = new Vector3(1f, 0.55f, 0.2f) * 20f;
    private const float ExplosionReach = 60f;
    private static readonly Vector3 FireColour = new Vector3(1f, 0.5f, 0.15f) * 3f;
    /// <summary>A fire reaches this many times its box's widest side, at least the minimum.</summary>
    private const float FireReachPerSize = 4f;
    private const float FireReachMin = 20f;
    /// <summary>Flicker: radians per tick, and between fires (pseudo-random phases).</summary>
    private const float FlickerSpeed = 1.3f;
    private const float FlickerPhase = 2.1f;
    private const float FlickerBeat = 0.37f;

    /// <summary>Fills <paramref name="lights"/> with this frame's lights.</summary>
    public static void Gather(PointLights lights, ScriptRuntime scripts, Kurt.Kurt kurt)
    {
        lights.Clear();
        if (kurt.Muzzle != null)
        {
            lights.Add(new PointLight(kurt.Feet + Vector3.UnitZ * MuzzleHeight, MuzzleColour, MuzzleReach));
        }

        var rounds = scripts.SniperRounds.Visuals;
        for (var i = 0; i < rounds.Count; i++)
        {
            lights.Add(new PointLight(rounds[i].Position, RoundColour, RoundReach));
        }

        var objects = scripts.Objects;
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj.Dead || obj.EffectFrames <= 0)
            {
                continue;
            }

            var reach = ExplosionReach * MathF.Max(obj.Scale, 1f);
            lights.Add(new PointLight(obj.Position, ExplosionColour * Fade(obj.EffectTime, obj.EffectFrames), reach));
        }

        var boxes = scripts.Boxes;
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            if (box.Dead || !box.Visible || box.Model is not { Name: FireTexture } model)
            {
                continue;
            }

            var size = model.Bounds.Size();
            var reach = MathF.Max(MathF.Max(size.X, size.Y) * FireReachPerSize, FireReachMin);
            lights.Add(new PointLight(box.Position, FireColour * Flicker(scripts.TickCount, i), reach));
        }
    }

    /// <summary>An explosion's strength: 1 as it starts, 0 once its <paramref name="frames"/> have played.</summary>
    public static float Fade(float time, int frames) => Math.Clamp(1f - time / frames, 0f, 1f);

    /// <summary>A fire's strength at a tick: 1 - depth to 1, beating differently per <paramref name="seed"/>.</summary>
    public static float Flicker(int tick, int seed)
    {
        var phase = tick * FlickerSpeed + seed * FlickerPhase;
        var wave = MathF.Sin(phase) * MathF.Sin(phase * FlickerBeat + seed);
        return 1f - FlickerDepth * (0.5f + 0.5f * wave);
    }
}
