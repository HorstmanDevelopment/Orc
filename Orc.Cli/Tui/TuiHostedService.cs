using Microsoft.Extensions.Hosting;

namespace Orc.Cli.Tui;

internal sealed class TuiHostedService : BackgroundService
{
    private readonly Dashboard _dashboard;
    private readonly IHostApplicationLifetime _lifetime;

    public TuiHostedService(Dashboard dashboard, IHostApplicationLifetime lifetime)
    {
        _dashboard = dashboard;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await _dashboard.RunAsync(ct);
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
