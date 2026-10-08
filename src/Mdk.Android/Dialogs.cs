using Android.App;

namespace Mdk.Android;

/// <summary>An answer of <see cref="Dialogs.Ask"/>.</summary>
internal enum Answer { Yes, No }

/// <summary>Android's dialogs, waited for from SDL's thread (they show on the UI thread).</summary>
internal static class Dialogs
{
    private const string Title = "MDK";

    /// <summary>A message with two buttons; returns the one tapped.</summary>
    public static Answer Ask(Activity activity, string message, string yes, string no)
    {
        var answer = new TaskCompletionSource<Answer>();
        activity.RunOnUiThread(() => new AlertDialog.Builder(activity)
            .SetTitle(Title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton(yes, (_, _) => answer.TrySetResult(Answer.Yes))!
            .SetNegativeButton(no, (_, _) => answer.TrySetResult(Answer.No))!
            .Show());
        return answer.Task.Result;
    }

    /// <summary>A message with an OK button; returns once it's tapped.</summary>
    public static void Tell(Activity activity, string message)
    {
        var closed = new TaskCompletionSource();
        activity.RunOnUiThread(() => new AlertDialog.Builder(activity)
            .SetTitle(Title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton("OK", (_, _) => closed.TrySetResult())!
            .Show());
        closed.Task.Wait();
    }

    /// <summary>A message without buttons (progress); the caller updates and closes it.</summary>
    public static AlertDialog Show(Activity activity, string message)
    {
        var shown = new TaskCompletionSource<AlertDialog>();
        activity.RunOnUiThread(() => shown.SetResult(new AlertDialog.Builder(activity)
            .SetTitle(Title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .Show()!));
        return shown.Task.Result;
    }
}
