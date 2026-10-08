using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.HdTextures;
using Mdk.Game.Kurt;
using Mdk.Game.Mods;

namespace Mdk.Game.Menu;

/// <summary>A list of menu items drawn like the original's menus (menu_items.gd, menu_entry.gd,
/// 0x42c374): <c>FONTBIG</c> in <c>SYS_PAL</c>, unselected items at 65 %, the selected one growing
/// to full size in 5 ticks. Rows of 36 from y 5 on the 600 x 360 view (baselines at 31 + 36 x row,
/// 0x4265c0); longer pages get lower rows, and below 24 the small font. Also the options and
/// controls pages shared by the main menu and the pause menu.
/// <code>
///   main menu: a column at the view's left edge, items centred on its width
///   other pages: centred on x 300
/// </code></summary>
public sealed class MenuItems
{
    /// <summary>Where the column of items stands on the view.</summary>
    public enum Align { Left, Centre }

    private enum Kind { Item, Title }

    private sealed class Entry(string text, Kind kind, Action? press, Action<int>? change)
    {
        public string Text = text;
        public readonly Kind Kind = kind;
        public readonly Action? Press = press;
        /// <summary>An option: changed by a step (+1 or -1).</summary>
        public readonly Action<int>? Change = change;
        public bool Disabled;
        public float Grow = Small;
        public RectangleF Area;

        public bool Selectable => Kind == Kind.Item && !Disabled;
    }

    private const float Top = 5f;
    private const float Row = 36f;
    /// <summary>The baseline is 26 below the row's top.</summary>
    private const float Baseline = 26f / 36f;
    private const float SmallFontRow = 24f;
    private const float Small = 0.65f;
    /// <summary>0.35 x 0.2 per tick.</summary>
    private const float GrowRate = 0.07f * 30f;
    private const float DisabledAlpha = 0.5f;
    private const int VolumeIncrement = 10;
    private static readonly float[] Sensitivities = [0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f];
    private const int DefaultSensitivity = 3;
    /// <summary>Longer mod names are cut (the row's width).</summary>
    private const int MaxModName = 24;
    private static readonly string[] DifficultyNames = ["Easy", "Normal", "Hard"];
    private static readonly string[] AntiAliasingNames = ["Off", "2x", "4x"];
    private static readonly int GraphicsCount = Enum.GetValues<Graphics>().Length;
    /// <summary>The options' sounds (0x42bb6c): the <c>OPTSONG</c> loop on the music bus and
    /// <c>OPTBUTT</c> on each change.</summary>
    private const string OptionSounds = "MISC/MDKSOUND.SNI";

    private readonly Ui _ui;
    private readonly Fonts _fonts;
    private readonly List<Entry> _entries = [];
    private readonly Sound? _click;
    private readonly float _clickGain;
    private readonly Sound? _song;
    private readonly Sound? _change;
    private int _songVoice;
    private int _selected = -1;
    /// <summary>The controls page waits for a key for this action (from the next frame on).</summary>
    private Key? _waiting;
    private Entry? _waitingEntry;
    private bool _armed;
    /// <summary>The HD textures being made, and the page's lines that follow it.</summary>
    private HdJob? _job;
    private Entry? _jobLine;
    private Entry? _jobBack;

    public Align Alignment = Align.Centre;
    /// <summary>The row the first item stands in.</summary>
    public int FirstRow;
    public bool Visible = true;

    public MenuItems(Ui ui, Fonts fonts, float clickGain = 1f)
    {
        _ui = ui;
        _fonts = fonts;
        _clickGain = clickGain;
        _click = Ui.SoundOf(ui.Fti.GetBytes("SND_PUSH"));
        MenuCursor.Apply(ui);
        var path = ui.Data.PathOf(OptionSounds);
        if (!File.Exists(path))
        {
            return;
        }

        var sounds = Sni.Load(path);
        _song = SniSound(sounds, "OPTSONG", Looping.Forever);
        _change = SniSound(sounds, "OPTBUTT", Looping.Once);
    }

    private static Sound? SniSound(Sni sni, string name, Looping looping)
    {
        var entry = sni.Entries.FirstOrDefault(e => e.Key == name).Value;
        return entry == null ? null : Ui.SoundOf(sni.GetBytes(entry), looping);
    }

    /// <summary>The texts of the page, for tests.</summary>
    public string Describe() => string.Join(" | ", _entries.Select(e => e.Text));

    public void Clear()
    {
        // Leaving the progress page stops the HD textures.
        _job?.Cancel();
        _job = null;
        _ui.Audio.Stop(_songVoice);
        _songVoice = 0;
        Alignment = Align.Centre;
        FirstRow = 0;
        _entries.Clear();
        _selected = -1;
        _waiting = null;
    }

    public void AddItem(string text, Action press) => Add(new Entry(text, Kind.Item, press, null));

    /// <summary>An item that can't be chosen (shown greyed).</summary>
    public void AddDisabled(string text) => Add(new Entry(text, Kind.Item, null, null) { Disabled = true });

    /// <summary>A line at full size that can't be selected ("Really Quit?", list titles).</summary>
    public void AddTitle(string text) => Add(new Entry(text, Kind.Title, null, null) { Grow = 1f });

    /// <summary>An item that changes a setting by a step: a click or Right goes on, a right click or
    /// Left back. <paramref name="text"/> gives its label.</summary>
    public void AddOption(Func<string> text, Action<int> change)
    {
        Entry? entry = null;
        entry = new Entry(text(), Kind.Item, () => Step(1), Step);
        Add(entry);

        void Step(int step)
        {
            change(step);
            _ui.ApplySettings();
            entry!.Text = text();
            _ui.Play(_change);
        }
    }

    private void Add(Entry entry)
    {
        _entries.Add(entry);
        if (_selected < 0 && entry.Selectable)
        {
            _selected = _entries.Count - 1;
        }
    }

    /// <summary>Selects the item at <paramref name="index"/> (the first selectable one after it).</summary>
    public void Select(int index)
    {
        for (var i = index; i < _entries.Count; i++)
        {
            if (_entries[i].Selectable)
            {
                _selected = i;
                return;
            }
        }
    }

    /// <summary>A frame: keys, the mouse and the items' growth.</summary>
    public void Update(float delta)
    {
        ShowJob();
        Layout();
        if (Visible)
        {
            HandleInput();
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var target = i == _selected || entry.Kind == Kind.Title ? 1f : Small;
            entry.Grow = MoveToward(entry.Grow, target, GrowRate * delta);
        }
    }

    private static float MoveToward(float value, float target, float step) =>
        value < target ? MathF.Min(value + step, target) : MathF.Max(value - step, target);

    private void HandleInput()
    {
        var input = _ui.Input;
        if (_waiting != null)
        {
            WaitForControl(input);
            return;
        }

        if (input.WasPressed(MenuKey.Up))
        {
            Move(-1);
        }

        if (input.WasPressed(MenuKey.Down))
        {
            Move(1);
        }

        if (input.PointerMoved || input.WasClicked(Pointer.Left) || input.WasClicked(Pointer.Right))
        {
            Hover(_ui.View.Pointer(input));
        }

        if (_selected < 0 || _selected >= _entries.Count)
        {
            return;
        }

        var selected = _entries[_selected];
        var pointed = selected.Area.Contains(_ui.View.Pointer(input).X, _ui.View.Pointer(input).Y);
        if (input.WasPressed(MenuKey.Accept) || (input.WasClicked(Pointer.Left) && pointed))
        {
            Click();
            selected.Press?.Invoke();
            return;
        }

        if (selected.Change == null)
        {
            return;
        }

        if (input.WasPressed(MenuKey.Right))
        {
            selected.Change(1);
        }
        else if (input.WasPressed(MenuKey.Left) || (input.WasClicked(Pointer.Right) && pointed))
        {
            selected.Change(-1);
        }
    }

    /// <summary>The next selectable item up or down.</summary>
    private void Move(int step)
    {
        for (var i = _selected + step; i >= 0 && i < _entries.Count; i += step)
        {
            if (!_entries[i].Selectable)
            {
                continue;
            }

            _selected = i;
            Click();
            return;
        }
    }

    private void Hover(Vector2 point)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!_entries[i].Selectable || !_entries[i].Area.Contains(point.X, point.Y) || i == _selected)
            {
                continue;
            }

            _selected = i;
            Click();
            return;
        }
    }

    private void Click() => _ui.Play(_click, Bus.Effects, _clickGain);

    /// <summary>Rows on the view (0x4265c0).</summary>
    private void Layout()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var row = RowFor(_entries.Count);
        var font = UsesBigFont(row) ? _fonts.Big : _fonts.Small;
        var width = _entries.Max(e => font.Width(Encoding.ASCII.GetBytes(e.Text)));
        var x = Alignment == Align.Left ? 0f : (ScreenView.Size.X - width) / 2f;
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i].Area = new RectangleF(x, RowTop(row, i + FirstRow), width, row);
        }
    }

    /// <summary>The height of a row for a page of <paramref name="entries"/> items: 36, less when they don't fit.</summary>
    public static float RowFor(int entries) => MathF.Min(Row, (ScreenView.Size.Y - Top * 2f) / Math.Max(entries, 1));

    /// <summary>Rows below 24 use the small font.</summary>
    public static bool UsesBigFont(float row) => row >= SmallFontRow;

    /// <summary>A row's top on the view.</summary>
    public static float RowTop(float row, int index) => Top + row * index;

    public void Draw()
    {
        if (!Visible || _entries.Count == 0)
        {
            return;
        }

        // A press may have just opened another page: its items have no rows yet.
        Layout();
        var row = _entries[0].Area.Height;
        var font = UsesBigFont(row) ? _fonts.Big : _fonts.Small;
        var baseline = MathF.Round(row * Baseline);
        foreach (var entry in _entries)
        {
            var text = Encoding.ASCII.GetBytes(entry.Text);
            var x = entry.Area.X + (entry.Area.Width - font.Width(text) * entry.Grow) / 2f;
            var alpha = entry.Disabled ? DisabledAlpha : 1f;
            _ui.View.Text(font, text, MathF.Round(x), entry.Area.Y + baseline, entry.Grow, alpha);
        }
    }

    // --- Options --------------------------------------------------------------------------------

    /// <summary>The options (see <see cref="Settings"/>), applied and saved on each change.</summary>
    public void ShowOptions(Action back)
    {
        Clear();
        var s = _ui.Settings;
        AddOption(() => $"Master volume: {s.MasterVolume}", step => s.MasterVolume = VolumeStep(s.MasterVolume, step));
        AddOption(() => $"Music volume: {s.MusicVolume}", step => s.MusicVolume = VolumeStep(s.MusicVolume, step));
        AddOption(() => $"Effects volume: {s.EffectsVolume}", step => s.EffectsVolume = VolumeStep(s.EffectsVolume, step));
        AddOption(() => $"Music filter: {OnOff(s.MusicFilter)}", _ => s.MusicFilter = !s.MusicFilter);
        AddOption(() => string.Create(CultureInfo.InvariantCulture, $"Mouse sensitivity: {s.MouseSensitivity:0.00}"),
            step => s.MouseSensitivity = SensitivityStep(s.MouseSensitivity, step));
        AddOption(() => $"Invert mouse: {OnOff(s.InvertMouse)}", _ => s.InvertMouse = !s.InvertMouse);
        AddOption(() => $"Fullscreen: {OnOff(s.Fullscreen)}", _ => s.Fullscreen = !s.Fullscreen);
        AddOption(() => $"Anti-aliasing: {AntiAliasingNames[(int)s.AntiAliasing]}",
            step => s.AntiAliasing = (AntiAliasing)Wrap((int)s.AntiAliasing + step, AntiAliasingNames.Length));
        AddOption(() => $"Difficulty: {DifficultyNames[(int)s.Difficulty]}", step => s.Difficulty = (Difficulty)Wrap((int)s.Difficulty + step, DifficultyNames.Length));
        AddOption(() => $"Graphics: {s.Graphics}", step => s.Graphics = (Graphics)Wrap((int)s.Graphics + step, GraphicsCount));
        // HD textures are a mod (switched on the Mods page); Android never makes them.
        AddItem("Mods", () => ShowMods(() => ShowOptions(back)));
        if (HdMenu.Items(HdMenu.Current).Contains(HdItem.Make))
        {
            AddItem("Make HD textures", () => ShowHdTextures(() => ShowOptions(back)));
        }

        // Android: pick the MDK folder again (new or changed files, HD textures made on a PC).
        if (_ui.Import is { } import)
        {
            AddItem("Import from folder", () =>
            {
                import();
                ShowOptions(back);
            });
        }
        AddOption(() => $"Gore: {OnOff(s.Gore)}", _ => s.Gore = !s.Gore);
        AddItem("Controls", () => ShowControls(() => ShowOptions(back)));
        AddItem("Back", back);
        _songVoice = _ui.Play(_song, Bus.Music);
    }

    /// <summary>The mods found (<see cref="ModCatalog"/>), each switched on or off; the enhanced look
    /// takes them from the next level (2D images at once).</summary>
    private void ShowMods(Action back)
    {
        Clear();
        _ui.ScanMods();
        var s = _ui.Settings;
        AddTitle("Mods: enhanced look");
        foreach (var mod in _ui.Mods.Mods)
        {
            var name = mod.Name.Length > MaxModName ? mod.Name[..MaxModName] : mod.Name;
            AddOption(() => $"{name}: {OnOff(s.StateOf(mod.Folder) == ModState.On)}",
                _ => s.Mods[mod.Folder] = s.StateOf(mod.Folder) == ModState.On ? ModState.Off : ModState.On);
        }

        if (_ui.Mods.Mods.Count == 0)
        {
            AddDisabled("No mods in mods/");
        }

        AddItem("Back", back);
    }

    /// <summary>Makes the HD textures (<see cref="HdGenerator"/>, minutes on a GPU) while showing
    /// its progress; leaving the page cancels it.</summary>
    private void ShowHdTextures(Action back)
    {
        Clear();
        AddTitle("HD textures");
        AddTitle("Real-ESRGAN, on the GPU");
        _jobLine = new Entry("", Kind.Title, null, null) { Grow = 1f };
        Add(_jobLine);
        _jobBack = new Entry("Cancel", Kind.Item, back, null);
        Add(_jobBack);
        _job = HdJob.Start(_ui.Data, _ui.UserFolder, HdOptions.Default);
        ShowJob();
    }

    /// <summary>The progress line, and "Back" once the job is over.</summary>
    private void ShowJob()
    {
        if (_job == null)
        {
            return;
        }

        _jobLine!.Text = _job.Progress.Describe();
        _jobBack!.Text = _job.Progress.Finished ? "Back" : "Cancel";
    }

    /// <summary>The key bindings: choosing an item waits for a key or a mouse button (Esc cancels).</summary>
    public void ShowControls(Action back)
    {
        Clear();
        foreach (var (key, name) in Settings.Actions)
        {
            Entry? entry = null;
            entry = new Entry(BindingText(key, name), Kind.Item, () =>
            {
                _waiting = key;
                _waitingEntry = entry;
                _armed = false;
                entry!.Text = $"{name}: press a key...";
            }, null);
            Add(entry);
        }

        AddItem("Default keys", () =>
        {
            _ui.Settings.Bindings.Clear();
            _ui.ApplySettings();
            ShowControls(back);
        });
        AddItem("Back", back);
    }

    /// <summary>The key or button pressed after the item was chosen becomes the binding.</summary>
    private void WaitForControl(Input input)
    {
        if (!_armed)
        {
            _armed = true;
            return;
        }

        if (input.LastControl.Length == 0 || _waiting is not { } key)
        {
            return;
        }

        if (!input.WasPressed(MenuKey.Back))
        {
            _ui.Settings.Bindings[key] = input.LastControl;
            _ui.ApplySettings();
        }

        _waitingEntry!.Text = BindingText(key, Settings.Actions.First(a => a.Key == key).Name);
        _waiting = null;
    }

    private string BindingText(Key key, string name) => $"{name}: {_ui.Input.Describe(key)}";

    /// <summary>Volumes go by 10 and wrap (0-100).</summary>
    public static int VolumeStep(int value, int step) => Wrap(value + step * VolumeIncrement, Settings.MaxVolume + VolumeIncrement);

    /// <summary>The next sensitivity of the list (wrapping; from 1 when it isn't in it).</summary>
    public static float SensitivityStep(float value, int step)
    {
        var index = Array.IndexOf(Sensitivities, value);
        return Sensitivities[Wrap((index >= 0 ? index : DefaultSensitivity) + step, Sensitivities.Length)];
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    private static string OnOff(bool value) => value ? "On" : "Off";
}
