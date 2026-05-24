using Backend.Mcp;
using Backend.Mcp.Health;
using Backend.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

var bootstrap = Host.CreateApplicationBuilder(args);
var runtimeOptions = bootstrap.Configuration
    .GetSection(McpRuntimeOptions.SectionName)
    .Get<McpRuntimeOptions>() ?? new McpRuntimeOptions();
var transport = McpRuntimeProfileResolver.Resolve(runtimeOptions).Transport;

if (transport == McpTransport.Http)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();

    builder.Services.AddIdmMcp(builder.Configuration);
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "idm-demo-mcp", Version = "1.0.0" };
        })
        .WithHttpTransport(options =>
        {
            options.Stateless = true;
        })
        .WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => (context, cancellationToken) =>
            {
                var services = context.Services ?? context.Server.Services!;
                var guard = services.GetRequiredService<IMcpMutationGuard>();
                guard.EnsureToolAllowed(context.Params?.Name ?? string.Empty, context.Params?.Arguments);
                return next(context, cancellationToken);
            });
        })
        .WithTools<IdmAdminTools>();

    var app = builder.Build();
    app.MapIdmMcpHealthEndpoints();
    app.UseMcpHostedAuthentication();
    app.MapMcp("/mcp");

    await app.RunAsync().ConfigureAwait(false);
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();

    builder.Services.AddIdmMcp(builder.Configuration);
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "idm-demo-mcp", Version = "1.0.0" };
        })
        .WithStdioServerTransport()
        .WithTools<IdmAdminTools>();
    await builder.Build().RunAsync().ConfigureAwait(false);
}
