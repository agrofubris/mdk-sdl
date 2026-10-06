using Mdk.Formats;

namespace Mdk.Game.Menu;

/// <summary>The main menu's slideshow (0x4279a0): once <c>MDK12.FLC</c> ends, an idle menu shows
/// <c>MISC/MDKS_001.GIF</c> after 5 s (without the items) for 4 s, then the others 2 s each with the
/// items, then the video's last frame again, and so on. Any input starts the wait again; on the first
/// image it shows the next one at once.
/// <code>
///   last frame ──5 s──► MDKS_001 (no items) ──4 s──► MDKS_002 ──2 s──► ... ──2 s──► last frame
/// </code></summary>
public sealed class Slideshow(Ui ui, VideoPlayer video)
{
    private const string ImagePath = "MISC/MDKS_{0:000}.GIF";
    private const float FirstDelay = 5f;
    private const float FirstTime = 4f;
    private const float NextTime = 2f;
    /// <summary>The image without the menu items.</summary>
    private const int Bare = 1;

    private readonly List<Gif> _images = [];
    private (int Width, int Height, byte[] Indices, byte[] Palette)? _last;
    private float _time;
    private bool _running;

    /// <summary>The image shown: 0 the video's last frame, 1... the GIFs.</summary>
    public int Image { get; private set; }

    /// <summary>Whether the menu items show over the image.</summary>
    public bool ItemsShown => Image != Bare;

    /// <summary>Starts once the video has ended, keeping its last frame.</summary>
    public void Start()
    {
        _last = video.Still;
        _running = true;
        _time = 0f;
        if (_images.Count == 0)
        {
            Load();
        }
    }

    public void Update(float delta)
    {
        if (!_running || _images.Count == 0)
        {
            return;
        }

        _time += delta;
        if (_time >= Duration())
        {
            Next();
        }
    }

    /// <summary>Menu input: the wait starts again, and the first image gives way at once.</summary>
    public void Reset()
    {
        _time = Image == Bare ? Duration() : 0f;
    }

    private float Duration() => Image switch
    {
        0 => FirstDelay,
        Bare => FirstTime,
        _ => NextTime,
    };

    private void Next()
    {
        _time = 0f;
        Image = (Image + 1) % (_images.Count + 1);
        if (Image == 0)
        {
            if (_last is { } last)
            {
                video.ShowStill(last.Width, last.Height, last.Indices, last.Palette);
            }

            return;
        }

        var gif = _images[Image - 1];
        video.ShowStill(gif.Width, gif.Height, gif.Indices, gif.Palette);
    }

    private void Load()
    {
        for (var i = 1; ; i++)
        {
            var path = ui.Data.PathOf(string.Format(ImagePath, i));
            var gif = File.Exists(path) ? Gif.Load(path) : null;
            if (gif == null)
            {
                return;
            }

            _images.Add(gif);
        }
    }
}
