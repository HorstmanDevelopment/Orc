using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orc.Cli.Tui;
using Orc.Core.Configuration;
using Orc.Core.Hosting;

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

builder.Services.AddOrcCore(builder.Configuration);
builder.Services.AddSingleton<Dashboard>();
builder.Services.AddHostedService<TuiHostedService>();

var host = builder.Build();

// Create workspace folders before any service uses them
host.Services.GetRequiredService<WorkspaceLayout>().EnsureCreated();

await host.RunAsync();
