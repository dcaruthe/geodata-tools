using GeoTools.Shared.Presentation;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.SetupPresentationLayer();

var host = builder.Build();
await host.RunAsync();
