using GeoTools.Shared.Application.Diagnostics;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Logging;
using GeoTools.Shared.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length > 0 && args[0] == "--geodata")
    return await HeadlessGeodataRunner.RunAsync(args[1..]);

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory }
);

// The launcher's stdout belongs to whoever started it, so the link hands agents a bounded tail
// of the same log instead.
var logStore = new InMemoryLogStore();
builder.Logging.AddProvider(logStore);
builder.Services.AddSingleton<ILogStore>(logStore);

// The render loop runs during host startup, so the lifetime status line would print after the
// window has already closed.
builder.Services.Configure<ConsoleLifetimeOptions>(options =>
    options.SuppressStatusMessages = true
);
builder.Services.SetupPresentationLayer();

var host = builder.Build();
await host.Services.GetRequiredService<IStateInitializer>().InitializeAsync();
await host.RunAsync();
return 0;
