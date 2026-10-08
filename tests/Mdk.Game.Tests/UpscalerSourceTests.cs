using System.IO.Compression;
using System.Security.Cryptography;
using Mdk.Engine.Upscale;
using Mdk.Game.Flow;
using Mdk.Game.HdTextures;

namespace Mdk.Game.Tests;

/// <summary>Where the upscaler comes from: settings.cfg's upscaler_url and upscaler_sha256 over the
/// platform's defaults, and the install checked against the SHA-256.</summary>
public class UpscalerSourceTests
{
    private const string Url = "https://example.org/realesrgan.zip";
    private const string Sha256 = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Fact]
    public void DefaultsAreThePlatformsRelease()
    {
        var source = UpscalerSource.Default;

        Assert.StartsWith("https://github.com/xinntao/Real-ESRGAN/releases/download/", source.Url);
        Assert.EndsWith(".zip", source.Url);
        Assert.Equal(64, source.Sha256.Length);
        Assert.Equal(source, new Settings().Upscaler);
    }

    [Fact]
    public void EmptyValuesAreTheDefaults()
    {
        var settings = Settings.Parse("upscaler_url=\nupscaler_sha256=\n");

        Assert.Equal(UpscalerSource.Default, settings.Upscaler);
        Assert.Contains("upscaler_url=\n", settings.Format());
        Assert.Contains("upscaler_sha256=\n", settings.Format());
    }

    [Fact]
    public void UserValuesOverride()
    {
        var settings = Settings.Parse($"upscaler_url={Url}\nupscaler_sha256={Sha256.ToUpperInvariant()}\n");

        Assert.Equal(new UpscalerSource(Url, Sha256), settings.Upscaler);
        Assert.Equal(settings.Format(), Settings.Parse(settings.Format()).Format());
        Assert.Contains($"upscaler_url={Url}\n", settings.Format());
    }

    [Fact]
    public void OptionsCarryTheSource()
    {
        var settings = Settings.Parse($"upscaler_url={Url}\nupscaler_sha256={Sha256}\n");

        Assert.Equal(UpscalerSource.Default, HdOptions.Default.Source);
        Assert.Equal(settings.Upscaler, (HdOptions.Default with { Source = settings.Upscaler }).Source);
    }

    [Fact]
    public void InstallsACheckedLocalArchive()
    {
        using var temp = new TempFolder();
        var (zip, sha256) = FakeRelease(temp.Path);

        var tool = RealEsrgan.Install(Path.Combine(temp.Path, "tool"), new UpscalerSource(new Uri(zip).AbsoluteUri, sha256), null, CancellationToken.None);

        Assert.NotNull(tool);
        Assert.True(RealEsrgan.IsInstalled(Path.Combine(temp.Path, "tool"), new UpscalerSource(zip, sha256)));
        // Another checksum: another release, installed again.
        Assert.False(RealEsrgan.IsInstalled(Path.Combine(temp.Path, "tool"), new UpscalerSource(zip, Sha256)));
    }

    [Fact]
    public void RefusesAWrongChecksum()
    {
        using var temp = new TempFolder();
        var (zip, _) = FakeRelease(temp.Path);
        var folder = Path.Combine(temp.Path, "tool");

        var error = Assert.Throws<InvalidDataException>(() => RealEsrgan.Install(folder, new UpscalerSource(zip, Sha256), null, CancellationToken.None));

        Assert.Contains(Sha256, error.Message);
        Assert.False(RealEsrgan.IsInstalled(folder, new UpscalerSource(zip, Sha256)));
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>A release archive holding a stand-in tool, and its SHA-256.</summary>
    private static (string Zip, string Sha256) FakeRelease(string folder)
    {
        var zip = Path.Combine(folder, "release.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var name = OperatingSystem.IsWindows() ? "realesrgan-ncnn-vulkan.exe" : "realesrgan-ncnn-vulkan";
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("tool");
        }

        return (zip, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("mdk-upscaler-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
