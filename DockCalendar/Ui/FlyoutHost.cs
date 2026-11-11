using System.Windows;
using System.Windows.Threading;

namespace DockCalendar.Ui;

/// <param name="Pinned">Stay open when the window loses focus.</param>
/// <param name="ExitWhenHidden">Standalone run: closing the flyout ends the process.</param>
/// <param name="InitialView">"month", "week" or "day"; null uses the saved view.</param>
internal sealed record FlyoutOptions(bool Pinned, bool ExitWhenHidden, string? InitialView);

/// <summary>
/// Runs the flyout window on its own STA thread. The extension's COM calls arrive on
/// thread-pool threads, which cannot own WPF windows.
/// </summary>
internal sealed class FlyoutHost
{
    private readonly AppServices _services;
    private readonly FlyoutOptions _options;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private FlyoutWindow? _window;

    public FlyoutHost(AppServices services, FlyoutOptions options)
    {
        _services = services;
        _options = options;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = !_options.ExitWhenHidden, Name = "DockCalendar UI" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Shows the flyout next to the mouse pointer, or hides it when it is open.</summary>
    public void Toggle()
    {
        var cursor = Native.CursorPosition();
        _dispatcher?.BeginInvoke(() => _window?.Toggle(cursor));
    }

    public void WaitForExit() => _thread?.Join();

    public void Shutdown() => _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);

    private void Run()
    {
        // An Application object registers the pack:// scheme the compiled XAML is loaded through.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new FlyoutWindow(_services, _options);
        if (_options.ExitWhenHidden)
        {
            _window.Hidden += (_, _) => application.Shutdown();
        }

        _ready.Set();
        application.Run();
    }
}
