using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;

namespace Mdk.Game.Menu;

/// <summary>Plays an FLC on the 600 x 360 view (video.gd): one frame per game frame of the original
/// (34 ms; the file's own speed is ignored), the palette per frame; the last frame stays. Still
/// images use the same view. An MVE plays at its own rate with its sound, centred on the 640 x 480
/// screen.
/// <code>
///   FLC ──34 ms──► frame ──► image (indices + palette, brightened) ──► view
///   MVE ──frame time, or sooner when the sound runs low──► frame + sound ──► stream voice
/// </code></summary>
public sealed class VideoPlayer(Ui ui) : IDisposable
{
    /// <summary>The original's game frame is at least 34 ms.</summary>
    private const float FrameSeconds = 0.034f;
    /// <summary>MVEs are centred on the whole screen.</summary>
    private static readonly Vector2 MovieScreen = new(640f, 480f);
    /// <summary>The movie's sound is a bit shorter than its frames: with less queued, the next frame
    /// comes at once.</summary>
    private const float SoundLow = 0.25f;
    private const float MicrosecondsPerSecond = 1e6f;
    private const int PaletteBytes = 768;
    private const int MaxColour = 255;

    private Flc? _flc;
    private Mve? _mve;
    private PalettedImage? _image;
    private Vector2 _view = ScreenView.Size;
    private float _time;
    private float _hold;
    private float _brighten;
    private int _soundVoice;
    private byte[] _palette = new byte[PaletteBytes];

    /// <summary>A frame was shown: its number (from 1).</summary>
    public event Action<int>? FrameShown;
    public event Action? Finished;

    public bool Playing => _flc != null || _mve != null;

    /// <summary>The image on the view (indices and palette), for the slideshow's return.</summary>
    public (int Width, int Height, byte[] Indices, byte[] Palette)? Still { get; private set; }

    /// <summary>Starts an FLC (relative to the game's data). False when it can't be read.</summary>
    public bool Play(string path)
    {
        var full = ui.Data.PathOf(path);
        _flc = File.Exists(full) ? Flc.Load(full) : null;
        if (_flc == null)
        {
            return false;
        }

        _view = ScreenView.Size;
        _time = 0f;
        ShowNext();
        return true;
    }

    /// <summary>Starts an MVE with its sound. False when it can't be read.</summary>
    public bool PlayMovie(string path)
    {
        var full = ui.Data.PathOf(path);
        _mve = File.Exists(full) ? Mve.Load(full) : null;
        if (_mve == null)
        {
            return false;
        }

        _view = MovieScreen;
        _time = 0f;
        if (!NextMovieFrame())
        {
            _mve = null;
            return false;
        }

        _soundVoice = ui.Audio.PlayStream(_mve.SampleRate, 1f);
        PushSound();
        return true;
    }

    /// <summary>Stays on the current frame for <paramref name="seconds"/>.</summary>
    public void Hold(float seconds) => _hold = seconds;

    /// <summary>Raises every colour by <paramref name="amount"/> (0-1, the end movie's flash).</summary>
    public void Brighten(float amount)
    {
        _brighten = amount;
        _image?.SetPalette(Brightened(_palette));
    }

    /// <summary>Shows a still image of palette indices with its palette (768 RGB bytes).</summary>
    public void ShowStill(int width, int height, byte[] indices, byte[] palette)
    {
        if (_image == null || _image.Width != width || _image.Height != height)
        {
            _image = new PalettedImage(ui.Renderer, width, height, indices, palette);
        }
        else
        {
            _image.SetIndices(indices);
        }

        _palette = (byte[])palette.Clone();
        _image.SetPalette(Brightened(_palette));
        Still = (width, height, (byte[])indices.Clone(), _palette);
    }

    public void Stop()
    {
        _flc = null;
        _mve = null;
        ui.Audio.Stop(_soundVoice);
        _soundVoice = 0;
    }

    public void Update(float delta)
    {
        if (_mve != null)
        {
            UpdateMovie(delta);
            return;
        }

        if (_flc == null)
        {
            return;
        }

        if (_hold > 0f)
        {
            _hold -= delta;
            return;
        }

        _time += delta;
        while (_flc != null && _hold <= 0f && _time >= FrameSeconds)
        {
            _time -= FrameSeconds;
            ShowNext();
        }
    }

    /// <summary>The image fitted in its view (the 600 x 360 screen, or the movie's), centred.</summary>
    public void Draw()
    {
        if (_image == null)
        {
            return;
        }

        var view = ui.View;
        view.Layout(ScreenView.Fit.Inside, _view);
        _image.Draw(view, (_view - _image.Size) / 2f);
        view.Layout(ScreenView.Fit.Inside);
    }

    private void ShowNext()
    {
        if (!_flc!.NextFrame())
        {
            Finish();
            return;
        }

        ShowStill(_flc.Width, _flc.Height, _flc.Indices, _flc.Palette);
        FrameShown?.Invoke(_flc.Frame);
    }

    private void UpdateMovie(float delta)
    {
        _time += delta * MicrosecondsPerSecond;
        while (_mve != null && (_time >= _mve.FrameTime || ui.Audio.Queued(_soundVoice) < SoundLow))
        {
            _time = MathF.Max(_time - _mve.FrameTime, 0f);
            if (!NextMovieFrame())
            {
                Finish();
                return;
            }

            PushSound();
        }
    }

    private bool NextMovieFrame()
    {
        if (!_mve!.NextFrame())
        {
            return false;
        }

        ShowStill(_mve.Width, _mve.Height, _mve.Indices, _mve.Palette);
        FrameShown?.Invoke(_mve.Frame);
        return true;
    }

    private void PushSound()
    {
        if (_mve == null || _mve.Audio.Count == 0)
        {
            return;
        }

        ui.Audio.Push(_soundVoice, _mve.Audio.Select(a => (a.X, a.Y)));
    }

    private byte[] Brightened(byte[] palette)
    {
        if (_brighten <= 0f)
        {
            return palette;
        }

        var raise = (int)MathF.Round(_brighten * MaxColour);
        return palette.Select(c => (byte)Math.Min(c + raise, MaxColour)).ToArray();
    }

    private void Finish()
    {
        _flc = null;
        _mve = null;
        Finished?.Invoke();
    }

    public void Dispose() => Stop();
}
