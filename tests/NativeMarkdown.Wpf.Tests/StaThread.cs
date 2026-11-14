using System.Runtime.ExceptionServices;

namespace NativeMarkdown.Wpf.Tests;

/// <summary>
/// Runs WPF test bodies on a dedicated STA thread, which WPF controls require.
/// </summary>
internal static class StaThread
{
    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
