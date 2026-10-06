using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Flow;

namespace Mdk.Game.Menu;

/// <summary>The pause menu (Esc during the game; pause_menu.gd): resume, options, back to the main
/// menu, quit, over the dimmed game. The game waits while it's open.</summary>
public sealed class PauseMenu
{
    private const float ClickGain = 0.5f;
    private static readonly Vector4 Dim = new(0f, 0f, 0f, 0.6f);

    private readonly Ui _ui;
    private readonly MenuItems _items;
    private Event _next = Event.None;

    public bool Open { get; private set; }

    public PauseMenu(Ui ui)
    {
        _ui = ui;
        _items = new MenuItems(ui, new Fonts(ui.Renderer, ui.Fti), ClickGain);
    }

    public void Show()
    {
        Open = true;
        ShowMain();
    }

    /// <summary>A frame while open: the menu over the dimmed game. Returns the game's next screen.</summary>
    public Event Update(float delta)
    {
        _ui.View.Layout(ScreenView.Fit.Inside);
        if (_ui.Input.WasPressed(MenuKey.Back))
        {
            Resume();
            return _next;
        }

        _items.Update(delta);
        _ui.View.FillCanvas(Dim);
        _items.Draw();
        return _next;
    }

    private void ShowMain()
    {
        _items.Clear();
        _items.AddItem("Resume", Resume);
        _items.AddItem("Options", () => _items.ShowOptions(ShowMain));
        _items.AddItem("Main menu", () => _next = Event.Menu);
        _items.AddItem("Quit", () => _next = Event.Quit);
        Console.WriteLine($"Menu: {_items.Describe()}");
    }

    private void Resume()
    {
        _items.Clear();
        Open = false;
    }
}
