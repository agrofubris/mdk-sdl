using System.Numerics;
using System.Text;
using Mdk.Engine.Platform;
using Mdk.Game.Flow;

namespace Mdk.Game.Menu;

/// <summary>The pause menu (Esc during the game; pause_menu.gd): resume, quick save, quick load,
/// options, back to the main menu, quit, over the dimmed game. The game waits while it's open. Its
/// first page names the quick save and quick load keys at the bottom (small font).</summary>
public sealed class PauseMenu
{
    /// <summary>The level's quick save and quick load (what the keys do), and whether there's a
    /// save to load (Quick load is greyed out otherwise). The fall has none.</summary>
    public sealed record Quick(Action Save, Action Load, Func<bool> CanLoad);

    private enum Page { Main, Options }

    private const float ClickGain = 0.5f;
    private static readonly Vector4 Dim = new(0f, 0f, 0f, 0.6f);
    /// <summary>The keys' hint: its baseline on the 600 x 360 view.</summary>
    private const float HintY = 345f;

    private readonly Ui _ui;
    private readonly Fonts _fonts;
    private readonly MenuItems _items;
    private readonly Quick? _quick;
    private Event _next = Event.None;
    private Page _page;

    public bool Open { get; private set; }

    public PauseMenu(Ui ui, Quick? quick = null)
    {
        _ui = ui;
        _quick = quick;
        _fonts = new Fonts(ui.Renderer, ui.Fti, mods: ui.CanvasMods());
        _items = new MenuItems(ui, _fonts, ClickGain);
    }

    /// <summary>The bound quick keys: "Quick save: F2   Quick load: F9".</summary>
    public static string KeysHint(Input input) =>
        $"Quick save: {input.Describe(Key.QuickSave)}   Quick load: {input.Describe(Key.QuickLoad)}";

    public void Show()
    {
        Open = true;
        ShowMain();
    }

    /// <summary>A frame while open: the menu over the dimmed game. Returns the game's next screen.</summary>
    public Event Update(float delta)
    {
        _ui.View.Layout(ScreenView.Fit.Inside);
        // Esc resumes; on the options' pages it goes up a page.
        if (_ui.Input.WasPressed(MenuKey.Back) && !_items.HandlesBack)
        {
            Resume();
            return _next;
        }

        _items.Update(delta);
        _ui.View.FillCanvas(Dim);
        _items.Draw();
        DrawHint();
        return _next;
    }

    private void DrawHint()
    {
        if (_page != Page.Main)
        {
            return;
        }

        var text = Encoding.ASCII.GetBytes(KeysHint(_ui.Input));
        var font = _fonts.Small;
        _ui.View.Text(font, text, MathF.Round((ScreenView.Size.X - font.Width(text)) / 2f), HintY);
    }

    private void ShowMain()
    {
        _items.Clear();
        _page = Page.Main;
        _items.AddItem("Resume", Resume);

        AddQuick();
        _items.AddItem("Options", () =>
        {
            _page = Page.Options;
            _items.ShowOptions(ShowMain);
        });
        _items.AddItem("Main menu", () => _next = Event.Menu);
        _items.AddItem("Quit", () => _next = Event.Quit);
        Console.WriteLine($"Menu: {_items.Describe()}");
        Console.WriteLine($"Hint: {KeysHint(_ui.Input)}");
    }

    /// <summary>Quick save and quick load: the menu closes first, quick save's name prompt and the
    /// loaded level take the screen.</summary>
    private void AddQuick()
    {
        if (_quick is not { } quick)
        {
            return;
        }

        _items.AddItem("Quick save", () => ResumeThen(quick.Save));
        if (!quick.CanLoad())
        {
            _items.AddDisabled("Quick load");
            return;
        }

        _items.AddItem("Quick load", () => ResumeThen(quick.Load));
    }

    private void Resume()
    {
        _items.Clear();
        Open = false;
    }

    private void ResumeThen(Action quick)
    {
        Resume();
        quick();
    }
}
