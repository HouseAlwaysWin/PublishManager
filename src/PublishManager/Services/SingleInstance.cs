using System.Threading;

namespace PublishManager.Services;

/// <summary>
/// Keeps one copy of the app per user session. This matters because closing the
/// window only hides it: launching again is the natural way to "reopen", and
/// without this that would start a second copy — two tray icons, two run
/// monitors, and two writers racing over projects.json.
/// A second launch instead wakes the first and exits.
/// </summary>
public static class SingleInstance
{
    // Session-scoped, so separate users on one machine each get their own.
    private const string MutexName = @"Local\PublishManager.SingleInstance";
    private const string ActivateEventName = @"Local\PublishManager.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;

    /// <summary>
    /// True when this process owns the session. False means another copy is
    /// already running and has been asked to show itself.
    /// </summary>
    public static bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);

        if (isFirst)
        {
            // Named events are a Windows facility; elsewhere the mutex still
            // prevents a duplicate, there is just no way to wake the first copy.
            if (OperatingSystem.IsWindows())
                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            return true;
        }

        // Ask the running copy to come to the front, then step aside.
        if (OperatingSystem.IsWindows() &&
            EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
        {
            using (existing)
                existing.Set();
        }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>Invokes <paramref name="onActivated"/> whenever another launch asks for the window.</summary>
    public static void ListenForActivation(Action onActivated)
    {
        var handle = _activate;
        if (handle is null)
            return;

        // Background so it never keeps the process alive on its own.
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                    onActivated();
                }
                catch (ObjectDisposedException)
                {
                    return;   // shutting down
                }
            }
        })
        {
            IsBackground = true,
            Name = "single-instance-activation",
        };

        thread.Start();
    }

    public static void Release()
    {
        _activate?.Dispose();
        _activate = null;
        _mutex?.Dispose();
        _mutex = null;
    }
}
