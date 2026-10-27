using Mdk.Formats;
using Mdk.Game.Flow;

namespace Mdk.Game.Tests;

/// <summary>Android's game files: a picked folder's path, the folder chosen to play from in place
/// (remembered, checked each start) or the copy in the app.</summary>
public sealed class StorageChoiceTests : IDisposable
{
    private const string Primary = "/storage/emulated/0";

    private readonly string _temp = Directory.CreateTempSubdirectory("mdk-storage-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string App => Path.Combine(_temp, "app");

    /// <summary>An installation under the temp folder; returns its folder.</summary>
    private string Installation(string under)
    {
        var folder = Path.Combine(_temp, under);
        Directory.CreateDirectory(Path.Combine(folder, "TRAVERSE"));
        File.WriteAllText(Path.Combine(folder, "TRAVERSE", "travsprt.bni"), "");
        return folder;
    }

    [Theory]
    [InlineData("primary:MDK", Primary + "/MDK")]
    [InlineData("primary:Games/MDK", Primary + "/Games/MDK")]
    [InlineData("primary:", Primary)]
    [InlineData("home:MDK", Primary + "/Documents/MDK")]
    [InlineData("1234-ABCD:MDK", "/storage/1234-ABCD/MDK")]
    public void DeviceStorageFoldersHavePaths(string document, string path) =>
        Assert.Equal(path, TreePaths.Resolve(TreePaths.ExternalStorage, document, Primary));

    [Fact]
    public void DownloadsFoldersHavePathsWhenRaw() =>
        Assert.Equal(Primary + "/Download/MDK", TreePaths.Resolve(TreePaths.Downloads, "raw:" + Primary + "/Download/MDK", Primary));

    [Theory]
    [InlineData(TreePaths.Downloads, "msf:42")]
    [InlineData("com.google.android.apps.docs.storage", "acc=1;doc=2")]
    [InlineData(TreePaths.ExternalStorage, "MDK")]
    // Only folders on the storage volumes, never climbing out of them.
    [InlineData(TreePaths.Downloads, "raw:/data/data/io.github.nemo22.mdk")]
    [InlineData(TreePaths.Downloads, "raw:/storage/emulated/0/../../../data")]
    [InlineData(TreePaths.ExternalStorage, "primary:MDK/../../..")]
    [InlineData(TreePaths.ExternalStorage, "../data:MDK")]
    public void OtherFoldersHaveNoPath(string authority, string document) =>
        Assert.Null(TreePaths.Resolve(authority, document, Primary));

    [Fact]
    public void ThePrimaryStorageIsAboveTheAppsFolder()
    {
        Assert.Equal(Primary, TreePaths.PrimaryRoot(Primary + "/Android/data/io.github.nemo22.mdk/files"));
        Assert.Null(TreePaths.PrimaryRoot("/data/user/0/io.github.nemo22.mdk/files"));
    }

    [Fact]
    public void WithoutAChoiceAccessIsAsked() =>
        Assert.Equal(StorageKind.AskAccess, new StorageChoice(App).Plan(FolderAccess.Askable, null).Kind);

    [Fact]
    public void AStoreBuildCopies() =>
        Assert.Equal(StorageKind.CopyFlow, new StorageChoice(App).Plan(FolderAccess.Unavailable, null).Kind);

    [Fact]
    public void AnEarlierCopyIsKept()
    {
        var copy = MdkData.At(Installation(Path.Combine("app", "mdk")));

        var plan = new StorageChoice(App).Plan(FolderAccess.Granted, copy);

        Assert.Equal(StorageKind.Copied, plan.Kind);
        Assert.Same(copy, plan.Data);
    }

    [Fact]
    public void TheChosenFolderIsRememberedAndPlayedInPlace()
    {
        var folder = Installation("MDK");
        new StorageChoice(App).Choose(folder);

        var choice = new StorageChoice(App);
        var plan = choice.Plan(FolderAccess.Granted, MdkData.At(Installation(Path.Combine("app", "mdk"))));

        Assert.Equal(folder, choice.Folder);
        Assert.Equal(StorageKind.InPlace, plan.Kind);
        Assert.Equal(folder, plan.Data!.Dir);
    }

    [Fact]
    public void AGoneFolderIsLost()
    {
        var folder = Installation("MDK");
        var choice = new StorageChoice(App);
        choice.Choose(folder);
        Directory.Delete(folder, recursive: true);

        Assert.Equal(StorageKind.Lost, choice.Plan(FolderAccess.Granted, null).Kind);
    }

    [Fact]
    public void ARevokedAccessIsLost()
    {
        var choice = new StorageChoice(App);
        choice.Choose(Installation("MDK"));

        Assert.Equal(StorageKind.Lost, choice.Plan(FolderAccess.Askable, null).Kind);
    }

    [Fact]
    public void AForgottenChoiceAsksAgain()
    {
        var choice = new StorageChoice(App);
        choice.Choose(Installation("MDK"));

        choice.Forget();

        Assert.Null(new StorageChoice(App).Folder);
        Assert.Equal(StorageKind.AskAccess, choice.Plan(FolderAccess.Granted, null).Kind);
    }

    [Fact]
    public void TheInstallationIsThePickedFolderOrOneInIt()
    {
        var folder = Installation(Path.Combine("Games", "MDK"));

        Assert.Equal(folder, MdkData.At(folder)!.Dir);
        Assert.Equal(folder, MdkData.At(Path.Combine(_temp, "Games"))!.Dir);
        Assert.Null(MdkData.At(_temp));
        Assert.Null(MdkData.At(Path.Combine(_temp, "none")));
    }

    [Fact]
    public void SettingsAndSavesMoveWithoutOverwriting()
    {
        var from = Path.Combine(_temp, "app");
        var to = Installation("MDK");
        var saves = SaveGames.In(from);
        saves.Write("1", new SaveGame(SaveKind.Snapshot, 3, SaveGame.FullHealth, 0, false, ""));
        saves.Write("2", new SaveGame(SaveKind.Snapshot, 4, SaveGame.FullHealth, 0, false, ""));
        File.WriteAllText(Settings.PathIn(from), "master_volume=10\n");
        SaveGames.In(to).Write("2", new SaveGame(SaveKind.Snapshot, 7, SaveGame.FullHealth, 0, false, ""));

        StorageChoice.CopyUserFiles(from, to);

        Assert.Equal("master_volume=10\n", File.ReadAllText(Settings.PathIn(to)));
        Assert.Equal(3, SaveGames.In(to).Read("1")!.Level);
        Assert.Equal(7, SaveGames.In(to).Read("2")!.Level);
    }
}
