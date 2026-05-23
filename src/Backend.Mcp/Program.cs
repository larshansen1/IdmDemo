using Backend.Mcp;
using Backend.Mcp.Health;
using Backend.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

var bootstrap = Host.CreateApplicationBuilder(args);
var transport = bootstrap.Configuration
    .GetSection(McpRuntimeOptions.SectionName)
    .Get<McpRuntimeOptions>()?
    .Transport ?? McpTransport.Stdio;

if (transport == McpTransport.Http)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();

    builder.Services.AddIdmMcp();
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "idm-demo-mcp", Version = "1.0.0" };
        })
        .WithHttpTransport(options =>
        {
            options.Stateless = true;
        })
        .WithTools<IdmAdminTools>();

    var app = builder.Build();
    app.MapIdmMcpHealthEndpoints();
    app.MapMcp("/mcp");

    await app.RunAsync().ConfigureAwait(false);
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();

    builder.Services.AddIdmMcp();
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "idm-demo-mcp", Version = "1.0.0" };
        })
        .WithStdioServerTransport()
        .WithTools<IdmAdminTools>();
    await builder.Build().RunAsync().ConfigureAwait(false);
}
