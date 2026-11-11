using DockCalendar.Ui;
using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace DockCalendar;

public static class Program
{
    [MTAThread]
    public static int Main(string[] args)
    {
        // Command Palette starts the packaged exe as a COM server.
        if (args.Any(arg => arg.Equals("-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase)))
        {
            ExtensionHostRunner.RunAsync(
                args,
                new ExtensionHostRunnerParameters
                {
                    PublisherMoniker = "DockCalendar",
                    ProductMoniker = "DockCalendar",
                    ExtensionFactories = new()
                    {
                        new DelegateExtensionFactory(disposed => new DockCalendarExtension(disposed)),
                    },
                }).GetAwaiter().GetResult();
            return 0;
        }

        // Started with no options, as an app: for example from an icon pinned to the Dock
        // or Start. If the extension (or an earlier launch) is running, it owns the flyout,
        // so toggle that one. Otherwise this process stays and becomes the owner.
        var asApp = args.Length == 0;
        if (asApp && ToggleSignal.TrySignalRunningInstance())
        {
            return 0;
        }

        // With options it is a development run that ends when the flyout closes.
        //   --demo         sample events instead of Google Calendar
        //   --pin          stay open when the window loses focus
        //   --view=week    month | week | day
        var view = args.FirstOrDefault(arg => arg.StartsWith("--view=", StringComparison.OrdinalIgnoreCase))?[7..];
        var services = AppServices.Create(demo: args.Contains("--demo"));
        var host = new FlyoutHost(services, new FlyoutOptions(
            Pinned: args.Contains("--pin"),
            ExitWhenHidden: !asApp,
            InitialView: view));
        host.Start();
        if (asApp)
        {
            ToggleSignal.Listen(host.Toggle);
        }

        host.Toggle();
        host.WaitForExit();
        services.Dispose();
        return 0;
    }
}
