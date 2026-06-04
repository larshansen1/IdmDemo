using System.Threading.RateLimiting;
using Backend.Api.Extensions;
using Backend.Api.Middleware;
using Backend.Api.Services;
using Backend.Api.Startup;
using Backend.Application.Extensions;
using Backend.Application.Models.Auth;
using Backend.Infrastructure.Extensions;
using Microsoft.AspNetCore.RateLimiting;
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

var authorizationServerOptions = new AuthorizationServerOptions
{
    DpopSupportedAlgorithms = [],
};
builder.Configuration
    .GetSection("AuthorizationServer")
    .Bind(authorizationServerOptions);
var signingKeyPath = builder.Configuration["AuthorizationServer:SigningKeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "signing-key.json");
var certificateAuthorityPath = builder.Configuration["CertificateAuthority:KeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "certificate-authority.json");

builder.Services.AddInfrastructure(connectionString, signingKeyPath, certificateAuthorityPath);
builder.Services.AddApplication(authorizationServerOptions);
builder.Services.AddSingleton<IClientCertificateReader, ClientCertificateReader>();

var tokenRateLimitSection = builder.Configuration.GetSection("RateLimiting:TokenEndpoint");
var tokenRateLimitPermitLimit = tokenRateLimitSection.GetValue("PermitLimit", 60);
var tokenRateLimitWindowSeconds = tokenRateLimitSection.GetValue("WindowSeconds", 60);
var tokenRateLimitSegmentsPerWindow = tokenRateLimitSection.GetValue("SegmentsPerWindow", 4);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("token-endpoint-per-ip", httpContext =>
    {
        var remoteIpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            remoteIpAddress,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = tokenRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(tokenRateLimitWindowSeconds),
                SegmentsPerWindow = tokenRateLimitSegmentsPerWindow,
                QueueLimit = 0,
            });
    });
});

builder.Services.AddExceptionHandler<ScimExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<ScimAdminSeeder>();

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
app.UseRateLimiter();
app.UseMiddleware<ScimOAuthMiddleware>();
app.UseMiddleware<ScimAdminAuditMiddleware>();

app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
}
