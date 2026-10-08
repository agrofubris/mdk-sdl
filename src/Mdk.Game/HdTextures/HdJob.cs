using Mdk.Formats;

namespace Mdk.Game.HdTextures;

/// <summary>A run of <see cref="HdGenerator"/> on a worker thread (the options' progress page):
/// its progress, cancelled on request; a failure ends it with its message.</summary>
public sealed class HdJob
{
    private readonly CancellationTokenSource _cancel = new();

    public HdProgress Progress { get; } = new();

    private HdJob()
    {
    }

    public static HdJob Start(MdkData data, string userFolder, HdOptions options)
    {
        var job = new HdJob();
        Task.Run(() => job.Run(data, userFolder, options));
        return job;
    }

    private void Run(MdkData data, string userFolder, HdOptions options)
    {
        try
        {
            HdGenerator.Run(data, userFolder, options, Progress, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Progress.Set(HdProgress.Stage.Cancelled, 0, 0);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"HD textures failed: {e}");
            Progress.Set(HdProgress.Stage.Failed, 0, 0, e.Message.Split('\n')[0]);
        }
    }

    public void Cancel() => _cancel.Cancel();
}
