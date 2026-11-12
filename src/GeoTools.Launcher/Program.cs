using GeoTools.Shared.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// The render loop runs during host startup, so the lifetime status line would print after the
// window has already closed.
builder.Services.Configure<ConsoleLifetimeOptions>(options =>
    options.SuppressStatusMessages = true
);
builder.Services.SetupPresentationLayer();

var host = builder.Build();
await host.RunAsync();
