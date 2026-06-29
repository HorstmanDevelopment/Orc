using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orc.Cli.Tui;
using Orc.Core.Configuration;
using Orc.Core.Hosting;
using Orc.Core.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables(prefix: "ORC_");

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// Persist logs to a file regardless of the (suppressed) console level, so task
// failures and host/service exceptions are diagnosable after the fact.
var wsOptions = builder.Configuration.GetSection(WorkspaceOptions.Section).Get<WorkspaceOptions>()
                ?? new WorkspaceOptions();
var wsLayout = new WorkspaceLayout(Options.Create(wsOptions));
builder.Logging.AddProvider(new FileLoggerProvider(wsLayout.LogsDir));
builder.Logging.AddFilter<FileLoggerProvider>(null, LogLevel.Information);

builder.Services.AddOrcCore(builder.Configuration);
builder.Services.AddSingleton<Dashboard>();
builder.Services.AddHostedService<TuiHostedService>();

var host = builder.Build();

// Create workspace folders before any service uses them
host.Services.GetRequiredService<WorkspaceLayout>().EnsureCreated();

await host.RunAsync();
