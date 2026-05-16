using ModelContextProtocol.Client;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpStdioSmokeTests
{
    [Fact]
    public async Task IdmListMachineClients_ClosedApiPort_ReturnsToolErrorInsteadOfProtocolFailure()
    {
        var repoRoot = FindRepoRoot();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "idmdemo-smoke",
            Command = "dotnet",
            WorkingDirectory = repoRoot,
            Arguments =
            [
                "run",
                "--project",
                Path.Combine(repoRoot, "src/Backend.Mcp/Backend.Mcp.csproj"),
                "--no-build",
            ],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["IdmApiInstances__local__BaseUrl"] = "http://127.0.0.1:1",
                ["IdmApiInstances__local__ApiKey"] = "changeme-development-key",
                ["Mcp__DefaultInstance"] = "local",
                ["Mcp__ReadOnly"] = "false",
            },
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "idm_list_machine_clients");

        var result = await client.CallToolAsync(
            "idm_list_machine_clients",
            new Dictionary<string, object?>
            {
                ["filter"] = null,
                ["instance"] = null,
            });

        Assert.True(result.IsError);
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("Could not reach the IdM API", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Template.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
