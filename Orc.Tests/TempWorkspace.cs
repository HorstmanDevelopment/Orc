using Microsoft.Extensions.Options;
using Orc.Core.Configuration;

namespace Orc.Tests;

internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public WorkspaceLayout Layout { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "orc-test-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);
        Layout = new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = Root }));
        Layout.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
