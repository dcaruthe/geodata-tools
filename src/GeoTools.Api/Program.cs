using System.Text.Json.Serialization;
using GeoTools.Api;
using GeoTools.Api.Contracts;
using GeoTools.Api.Facade;
using GeoTools.Api.Mcp;
using GeoTools.Api.Rest;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Infrastructure;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Link;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.SetupInfrastructureDataLayer();
builder.Services.AddReadOnlyGeometryCache();
builder.Services.AddReadOnlyRegionIndexWriter();
builder.Services.AddSingleton<ILauncherLink, LauncherLinkClient>();
builder.Services.AddSingleton<GeodataQueryService>();
builder.Services.AddSingleton<GeoToolsFacade>();
builder.Services.AddHostedService<ApiStartupService>();
builder.Services.AddHostedService<LinkRotationService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder
    .Services.AddGraphQLServer()
    .AddQueryType<GeoTools.Api.Graph.Query>()
    .AddMutationType<GeoTools.Api.Graph.Mutation>();
builder
    .Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly(typeof(GeoToolsTools).Assembly);

var app = builder.Build();

app.UseExceptionHandler(handler =>
    handler.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var status = error switch
        {
            LinkUnavailableException => StatusCodes.Status409Conflict,
            BorderQueryTooLargeException => StatusCodes.Status413PayloadTooLarge,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new
            {
                error = error?.Message ?? "Internal error.",
                code = (error as LinkUnavailableException)?.Code,
            }
        );
    })
);

app.UseSwagger();
app.UseSwaggerUI();
app.MapGeoToolsRest();
app.MapGraphQL("/graphql");
app.MapMcp("/mcp");
app.Run();

public partial class Program;
