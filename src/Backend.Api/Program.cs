using Backend.Api.Extensions;
using Backend.Api.Middleware;
using Backend.Api.Services;
using Backend.Application.Extensions;
using Backend.Application.Models.Auth;
using Backend.Infrastructure.Extensions;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = false;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "IdmDemo API",
            Version = "v1",
        });
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=idm.db";

var authorizationServerOptions = builder.Configuration
    .GetSection("AuthorizationServer")
    .Get<AuthorizationServerOptions>()
    ?? new AuthorizationServerOptions();
var signingKeyPath = builder.Configuration["AuthorizationServer:SigningKeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "signing-key.json");

builder.Services.AddInfrastructure(connectionString, signingKeyPath);
builder.Services.AddApplication(authorizationServerOptions);
builder.Services.AddSingleton<IClientCertificateReader, ClientCertificateReader>();

builder.Services.AddExceptionHandler<ScimExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Backend.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

await app.Services.ApplyMigrationsAsync().ConfigureAwait(false);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
}
