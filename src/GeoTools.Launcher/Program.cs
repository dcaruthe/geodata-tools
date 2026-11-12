using GeoTools.Shared.Application.State;
using GeoTools.Shared.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length > 0 && args[0] == "--geodata")
    return await HeadlessGeodataRunner.RunAsync(args[1..]);

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory }
);

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
