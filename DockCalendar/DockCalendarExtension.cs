using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace DockCalendar;

[Guid("5d1f6c0e-3b0a-4c59-9d5e-7a4f1c2e8b61")]
public sealed partial class DockCalendarExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly DockCalendarCommandsProvider _provider = new();

    public DockCalendarExtension(ManualResetEvent disposed)
    {
        _disposed = disposed;
    }

    public object? GetProvider(ProviderType providerType) =>
        providerType == ProviderType.Commands ? _provider : null;

    public void Dispose()
    {
        _provider.Dispose();
        _disposed.Set();
    }
}
