using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>The tests' sniper keys (like the Godot port's --sniper and --strike): sniper mode once
/// Kurt stands, at a zoom and pitch; the zoom key held for a while; one shot when the clip is ready;
/// Bones' full-screen strike.
/// <code>
///   lands ──► EnterSniper (zoom, pitch) ──► zoom in for --zoom seconds
///                                      └──1 s──► fire for 0.1 s (--sniper-fire)
/// </code></summary>
public sealed class SniperTest(ViewerOptions options)
{
    /// <summary>Kurt has stood for this long before sniping (he lands first).</summary>
    private const float SettleTime = 0.5f;
    /// <summary>The clip is loaded 0.75 s after entering: the shot comes after 1 s, for 0.1 s.</summary>
    private const float ShotStart = 1f;
    private const float ShotTime = 0.1f;
    /// <summary>The strike starts after 1 s, once Kurt's arena is known.</summary>
    private const float StrikeStart = 1f;

    private float? _entered;
    private bool _struck;

    /// <summary>Before each game step at game time <paramref name="time"/>.</summary>
    public void Step(Kurt.Kurt kurt, ScriptRuntime scripts, Input input, float time)
    {
        if (options.Strike is { } plane && !_struck && time >= StrikeStart)
        {
            _struck = true;
            scripts.PlayStrikeScene(StrikeScene.Kind.Bones, plane);
        }

        if (options.Sniper is not { } setup)
        {
            return;
        }

        if (_entered == null)
        {
            if (!kurt.OnFloor || time < SettleTime)
            {
                return;
            }

            kurt.EnterSniper();
            kurt.Scope.Zoom = setup.X;
            kurt.Scope.Pitch = setup.Y;
            _entered = time;
        }

        var since = time - _entered.Value;
        input.Hold(Key.ZoomIn, since < options.Zoom ? Input.State.Down : Input.State.Up);
        var shot = options.SniperFire && since >= ShotStart && since < ShotStart + ShotTime;
        input.Hold(Key.Fire, options.Fire || shot ? Input.State.Down : Input.State.Up);
    }

    /// <summary>Sniper mode's zoom and pitch from <c>--sniper=zoom,pitch</c> (both optional).</summary>
    public static Vector2 Setup(string text)
    {
        var values = text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new Vector2(values.Length > 0 ? values[0] : 1f, values.Length > 1 ? values[1] : 0f);
    }
}
