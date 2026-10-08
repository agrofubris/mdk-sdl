using Android.App;
using Android.Content;
using Android.Content.PM;
using Mdk.Game;
using Mdk.Game.Flow;
using Org.Libsdl.App;

namespace Mdk.Android;

/// <summary>The app: SDL's activity (its Java side comes with ppy.SDL3-CS) running the game on
/// SDL's thread, from the main menu, with the files imported on first start (<see cref="DataSetup"/>).
/// Settings and saves live in the app's external files folder.</summary>
[Activity(
    Label = "MDK",
    MainLauncher = true,
    Exported = true,
    Icon = "@drawable/icon",
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation
        | ConfigChanges.UiMode | ConfigChanges.Locale | ConfigChanges.LayoutDirection | ConfigChanges.Density)]
public sealed class MainActivity : SDLActivity
{
    /// <summary>The level the menu's "new game" would start with otherwise (unused from the menu).</summary>
    private const int DefaultLevel = 7;
    /// <summary>The imported game files, under the app's files folder.</summary>
    private const string DataFolder = "mdk";
    /// <summary>The game's user folder (settings, saves): see Game.</summary>
    private const string UserFolderVariable = "MDK_USER_DIR";

    private DataSetup? _setup;

    protected override string[] GetLibraries() => ["SDL3"];

    /// <summary>SDL's thread: the files, then the game until it quits.</summary>
    protected override void Main()
    {
        var files = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        var setup = new DataSetup(this, Path.Combine(files, DataFolder), files);
        _setup = setup;
        try
        {
            var data = setup.Get();
            if (data == null)
            {
                return;
            }

            Environment.SetEnvironmentVariable(UserFolderVariable, files);
            var level = new ViewerOptions(DefaultLevel, null, null, 0f, SoundMode.On);
            using var game = new Mdk.Game.Flow.Game(data, new GameOptions(Start.Menu, level) { Splash = true, Import = setup.Reimport });
            game.Run();
        }
        catch (Exception e)
        {
            // No console on a phone: say why the game stopped (e.g. no Vulkan for SDL_GPU).
            Console.Error.WriteLine(e);
            Dialogs.Tell(this, $"MDK stopped: {e.Message}");
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (_setup?.OnResult(requestCode, resultCode, data) == true)
        {
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }
}
