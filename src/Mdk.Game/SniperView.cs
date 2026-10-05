using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Hud;
using Mdk.Game.Kurt;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>What sniper mode draws besides the world through the scope (godot-mdk sniper_overlay.gd,
/// strike_scene.gd): the rounds in flight, the round cameras and the loaded rounds as renderer
/// insets, the sniper screen's state for the HUD, and the full-screen strike.
/// <code>
///   world (scope view) ──► round cameras (insets: the world from behind each round)
///                     ──► HUD canvas (frame, mask, crosshair, zoom, ammo)
///                     ──► clip (inset over the canvas: the loaded rounds' models)
/// </code></summary>
public sealed class SniperView(Renderer renderer, ObjectView objects, LevelData level, HudView hud)
{
    /// <summary>Round cameras are 90° wide (0x461d80).</summary>
    private const float RoundFieldWidth = 90f;
    private const float RoundNear = 0.5f;
    private const float Far = 20000f;

    // The loaded rounds (0x41eb10): round i at time clip timer + i (shown between 0 and 3, not at 0),
    // between keys of position, angles (z, y, x: Rz(a) Ry(-b) Rx(c)) and scale (0x490e4c), seen from
    // the origin along -Y (right +X, down +Z) with a focal length of 250 on the view.
    private static readonly (Vector3 Position, Vector3 Angles, float Scale)[] ClipKeys =
    [
        (new(-225f, -242f, 65f), new(-30f, 180f, 90f), 4f),
        (new(-203f, -242f, 107f), new(0f, 180f, 0f), 9f),
        (new(-176f, -242f, 145f), new(0f, 180f, 0f), 9f),
        (new(-154f, -242f, 174f), new(0f, 180f, 0f), 9f),
    ];
    private const float ClipFocal = 250f;
    private const float ClipNear = 1f;
    private const float ClipFar = 1000f;
    private static readonly string[] RoundModels = ["SW_SHOT", "SW_HOME", "SW_SGREN", "SW_HGREN", "SW_LGREN", "SW_BONES"];

    private readonly Dictionary<string, ObjectView.Look> _looks = [];
    private readonly MdkObject[] _clip = [new(), new(), new()];

    /// <summary>Each frame: the rounds in flight, then (in sniper mode) the sniper screen's insets
    /// and state.</summary>
    public void Draw(Kurt.Kurt kurt, ScriptRuntime scripts, float delta)
    {
        foreach (var round in scripts.SniperRounds.Visuals)
        {
            objects.Draw(round, LookOf(round.Arena));
        }

        if (!kurt.Sniping)
        {
            hud.Sniper.Update(delta, null);
            return;
        }

        var cameras = Enumerable.Range(0, SniperRounds.Slots).Select(scripts.SniperRounds.Camera).ToList();
        hud.Sniper.Update(delta, new SniperHud(kurt.Scope.Zoom, kurt.Inventory.Ammo, kurt.Inventory.SelectedAmmo, cameras, Aim(kurt, scripts)));
        var (scale, left) = SniperOverlay.Placement(renderer.CanvasWidth);
        for (var i = 0; i < cameras.Count; i++)
        {
            DrawRoundCamera(cameras[i], OnCanvas(SniperOverlay.RoundViews[i], scale, left));
        }

        DrawClip(kurt, scripts, OnCanvas(new RectangleF(0f, 0f, SniperScreen.ViewWidth, SniperScreen.ViewHeight), scale, left));
    }

    /// <summary>The air strike's target test, only with the strike selected and the clip ready.</summary>
    private static StrikeAim Aim(Kurt.Kurt kurt, ScriptRuntime scripts)
    {
        if (kurt.Inventory.SelectedAmmo != Scope.Strike || kurt.Scope.ClipTime > 0f)
        {
            return StrikeAim.NotChecked;
        }

        return scripts.AirStrike.FindTarget(kurt.SniperEye, kurt.SniperForward) != null ? StrikeAim.Target : StrikeAim.None;
    }

    /// <summary>A rectangle of the 600x360 view on the canvas.</summary>
    private static RectangleF OnCanvas(RectangleF inView, float scale, float left) =>
        new(left + (SniperOverlay.View.X + inView.X) * scale, (SniperOverlay.View.Y + inView.Y) * scale, inView.Width * scale, inView.Height * scale);

    /// <summary>A round camera watching its round: the world from behind it.</summary>
    private void DrawRoundCamera(SniperRounds.RoundCamera camera, RectangleF area)
    {
        if (camera.Shot != SniperRounds.Shot.Watching)
        {
            return;
        }

        var aspect = area.Width / area.Height;
        var fieldOfView = float.RadiansToDegrees(2f * MathF.Atan(MathF.Tan(float.DegreesToRadians(RoundFieldWidth) / 2f) / aspect));
        var view = CameraMath.View(camera.Position, camera.Forward, FollowCamera.UpOf(camera.Forward), fieldOfView, aspect, RoundNear, Far);
        renderer.BeginInset(view, area, InsetContent.Scene, InsetLayer.UnderCanvas);
        renderer.EndInset();
    }

    /// <summary>The loaded rounds of the selected type between the clip's keys; they slide into the
    /// chamber (key 0) after a shot.</summary>
    private void DrawClip(Kurt.Kurt kurt, ScriptRuntime scripts, RectangleF area)
    {
        var model = scripts.FindModel(scripts.CurrentArena, RoundModels[kurt.Inventory.SelectedAmmo]);
        if (model == null)
        {
            return;
        }

        var fieldOfView = float.RadiansToDegrees(2f * MathF.Atan(SniperScreen.ViewHeight / 2f / ClipFocal));
        var view = CameraMath.View(Vector3.Zero, -Vector3.UnitY, -Vector3.UnitZ, fieldOfView, area.Width / area.Height, ClipNear, ClipFar);
        var look = LookOf(scripts.CurrentArena);
        var open = false;
        for (var i = 0; i < Scope.ClipSize; i++)
        {
            var t = kurt.Scope.ClipTime + i;
            if (i >= kurt.Scope.ClipRounds || t == 0f || t >= ClipKeys.Length - 1)
            {
                continue;
            }

            if (!open)
            {
                renderer.BeginInset(view, area, InsetContent.Own, InsetLayer.OverCanvas);
                open = true;
            }

            var round = _clip[i];
            round.Model = model;
            round.Flags = MdkObject.FlagRolling;
            (round.RollingBasis, round.Position, round.Scale) = ClipPlace(t);
            objects.Draw(round, look);
        }

        if (open)
        {
            renderer.EndInset();
        }
    }

    /// <summary>A loaded round's rotation, position and scale at clip time <paramref name="t"/>.</summary>
    private static (Matrix4x4, Vector3, float) ClipPlace(float t)
    {
        var k = (int)t;
        var f = t - k;
        var (pa, aa, sa) = ClipKeys[k];
        var (pb, ab, sb) = ClipKeys[k + 1];
        var angles = Vector3.Lerp(aa, ab, f);
        var rotation = Matrix4x4.CreateRotationX(float.DegreesToRadians(angles.Z))
            * Matrix4x4.CreateRotationY(-float.DegreesToRadians(angles.Y))
            * Matrix4x4.CreateRotationZ(float.DegreesToRadians(angles.X));
        return (rotation, Vector3.Lerp(pa, pb, f), float.Lerp(sa, sb, f));
    }

    /// <summary>The full-screen strike, if one plays: it advances (Esc skips it) and only its model is
    /// drawn, over the sky. Returns its camera, or null.</summary>
    public View? DrawStrike(ScriptRuntime scripts, StrikeSkip skip, float delta)
    {
        if (scripts.Strike is not { Active: true } strike)
        {
            return null;
        }

        if (skip == StrikeSkip.Now)
        {
            strike.Skip();
            return null;
        }

        strike.Update(delta);
        if (!strike.Active)
        {
            return null;
        }

        var (eye, target) = strike.Camera;
        var forward = Vector3.Normalize(target - eye);
        objects.Draw(strike.Subject, LookOf(strike.Subject.Arena));
        return CameraMath.View(eye, forward, FollowCamera.UpOf(forward), StrikeScene.FieldOfView, renderer.AspectRatio, RoundNear, Far);
    }

    /// <summary>Whether Esc was pressed during the strike.</summary>
    public enum StrikeSkip { No, Now }

    private ObjectView.Look LookOf(string name)
    {
        if (_looks.TryGetValue(name, out var look))
        {
            return look;
        }

        var arena = level.Arenas.Find(a => a.Name == name);
        return _looks[name] = arena != null
            ? new ObjectView.Look(level.PaletteOf(arena), level.ArchivesOf(arena))
            : new ObjectView.Look(level.Dti.Palette, [level.LevelTextures]);
    }
}
