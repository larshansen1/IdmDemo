using Backend.Mcp;
using Backend.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
