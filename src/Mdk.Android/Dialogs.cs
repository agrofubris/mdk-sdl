using Android.App;
using Android.Views;
using Android.Widget;

namespace Mdk.Android;

/// <summary>An answer of <see cref="Dialogs.Ask"/>.</summary>
internal enum Answer { Yes, No }

/// <summary>A dialog's title and message.</summary>
internal readonly record struct Prompt(string Title, string Text);

/// <summary>Android's dialogs, waited for from SDL's thread (they show on the UI thread):
/// platform Material in MDK's colours (Resources/values/styles.xml), beside the
/// <see cref="Backdrop"/> while it's up.</summary>
internal static class Dialogs
{
    /// <summary>A message with two buttons; returns the one tapped.</summary>
    public static Answer Ask(Activity activity, Prompt prompt, string yes, string no)
    {
        var answer = new TaskCompletionSource<Answer>();
        activity.RunOnUiThread(() => Present(activity, Build(activity, prompt.Title)
            .SetMessage(prompt.Text)!
            .SetPositiveButton(yes, (_, _) => answer.TrySetResult(Answer.Yes))!
            .SetNegativeButton(no, (_, _) => answer.TrySetResult(Answer.No))!));
        return answer.Task.Result;
    }

    /// <summary>A message with an OK button; returns once it's tapped.</summary>
    public static void Tell(Activity activity, Prompt prompt)
    {
        var closed = new TaskCompletionSource();
        activity.RunOnUiThread(() => Present(activity, Build(activity, prompt.Title)
            .SetMessage(prompt.Text)!
            .SetPositiveButton("OK", (_, _) => closed.TrySetResult())!));
        closed.Task.Wait();
    }

    /// <summary>A progress without buttons; the caller updates and closes it.</summary>
    public static ProgressBox Show(Activity activity, string title, string first)
    {
        var shown = new TaskCompletionSource<ProgressBox>();
        activity.RunOnUiThread(() =>
        {
            var view = activity.LayoutInflater.Inflate(Resource.Layout.progress, null)!;
            view.FindViewById<TextView>(Resource.Id.progress_label)!.Text = first;
            var dialog = Present(activity, Build(activity, title).SetView(view)!);
            shown.SetResult(new ProgressBox(activity, dialog, view));
        });
        return shown.Task.Result;
    }

    private static AlertDialog.Builder Build(Activity activity, string title) =>
        new AlertDialog.Builder(activity, Resource.Style.MdkDialog)
            .SetTitle(title)!
            .SetCancelable(false)!;

    private static AlertDialog Present(Activity activity, AlertDialog.Builder builder)
    {
        var dialog = builder.Show()!;
        Backdrop.Place(activity, dialog);
        return dialog;
    }
}

/// <summary>A shown progress (<see cref="Dialogs.Show"/>): what's copied, a bar, done of all.
/// Updated from any thread.</summary>
internal sealed class ProgressBox(Activity activity, AlertDialog dialog, View view)
{
    private readonly TextView _label = view.FindViewById<TextView>(Resource.Id.progress_label)!;
    private readonly ProgressBar _bar = view.FindViewById<ProgressBar>(Resource.Id.progress_bar)!;
    private readonly TextView _count = view.FindViewById<TextView>(Resource.Id.progress_count)!;

    /// <summary>E.g. "Game files", 120 of 401.</summary>
    public void Update(string what, int done, int total) => activity.RunOnUiThread(() =>
    {
        _label.Text = what;
        _bar.Indeterminate = false;
        _bar.Max = total;
        _bar.Progress = done;
        _count.Text = $"{done} of {total}";
    });

    public void Close() => activity.RunOnUiThread(dialog.Dismiss);
}
