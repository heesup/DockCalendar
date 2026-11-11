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

        // Started by hand: show the flyout on its own, without Command Palette.
        //   --demo         sample events instead of Google Calendar
        //   --pin          stay open when the window loses focus
        //   --view=week    month | week | day
        var view = args.FirstOrDefault(arg => arg.StartsWith("--view=", StringComparison.OrdinalIgnoreCase))?[7..];
        var services = AppServices.Create(demo: args.Contains("--demo"));
        var host = new FlyoutHost(services, new FlyoutOptions(
            Pinned: args.Contains("--pin"),
            ExitWhenHidden: true,
            InitialView: view));
        host.Start();
        host.Toggle();
        host.WaitForExit();
        services.Dispose();
        return 0;
    }
}
