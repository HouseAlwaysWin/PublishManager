using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace PublishManager.Services;

/// <summary>
/// Keeps the app alive in the notification area. Closing the window hides it
/// rather than quitting, because a release keeps polling its workflow run long
/// after there is anything to look at; quitting would abandon the monitor.
/// Leaving is explicit, via the tray menu.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private static readonly Uri IconUri = new("avares://PublishManager/Assets/publishmanager.png");

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Window _window;
    private readonly TrayIcon _trayIcon;

    /// <summary>Set only by the tray's Exit item, so Closing knows to let it through.</summary>
    private bool _exiting;

    public TrayIconController(IClassicDesktopStyleApplicationLifetime lifetime, Window window)
    {
        _lifetime = lifetime;
        _window = window;

        // Hiding the last window would otherwise end the app.
        _lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var show = new NativeMenuItem("顯示主視窗");
        show.Click += (_, _) => ShowWindow();

        var exit = new NativeMenuItem("結束");
        exit.Click += (_, _) => Exit();

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "PublishManager — GitHub Action 發版管理",
            IsVisible = true,
            Menu = [show, new NativeMenuItemSeparator(), exit],
        };

        _trayIcon.Clicked += (_, _) => ShowWindow();
        _window.Closing += OnWindowClosing;

        // Launching the app again is the obvious way to "reopen" it once the
        // window is hidden, so treat that as a request to come back.
        SingleInstance.ListenForActivation(ShowWindow);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting)
            return;

        // Closing means "get out of my way", not "abandon what is running".
        e.Cancel = true;
        _window.Hide();
    }

    private void ShowWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
        });
    }

    private void Exit()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _exiting = true;
            Dispose();
            _lifetime.Shutdown();
        });
    }

    public void Dispose()
    {
        _window.Closing -= OnWindowClosing;
        // Without this the icon can outlive the process in the notification area.
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
