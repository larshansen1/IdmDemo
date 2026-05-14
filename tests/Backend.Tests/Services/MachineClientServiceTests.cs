using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Backend.Tests.Services;

public sealed class MachineClientServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsClient()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.ExistsByClientIdAsync("orders-service", Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateClientRequest { ClientId = "orders-service" });

        Assert.Equal("orders-service", result.ClientId);
        Assert.True(result.Active);
        await repo.Received(1).AddAsync(Arg.Any<MachineClient>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithActiveSetToFalse_CreatesInactiveClient()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.ExistsByClientIdAsync("orders-service", Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateClientRequest { ClientId = "orders-service", Active = false });

        Assert.False(result.Active);
    }

    [Fact]
    public async Task CreateAsync_DuplicateClientId_ThrowsConflictException()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.ExistsByClientIdAsync("orders-service", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateClientRequest { ClientId = "orders-service" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidClientId_ThrowsValidationException(string? clientId)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateClientRequest { ClientId = clientId }));
    }

    [Fact]
    public async Task CreateAsync_ClientIdTooLong_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateClientRequest { ClientId = new string('a', 257) }));
    }

    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_ClientIdWithNullChar_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateClientRequest { ClientId = "orders\0service" }));
    }

    [Fact]
    public async Task GetAsync_ExistingClient_ReturnsClient()
    {
        var client = MakeClient();
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var result = await service.GetAsync(client.Id);

        Assert.Equal(client.Id.ToString(), result.Id);
        Assert.Equal("orders-service", result.ClientId);
    }

    [Fact]
    public async Task GetAsync_NonExistingClient_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllClients()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.ListAsync(null, Arg.Any<CancellationToken>()).Returns([MakeClient("orders-service"), MakeClient("payments-service")]);
        var service = CreateService(repo);

        var result = await service.ListAsync(null);

        Assert.Equal(2, result.TotalResults);
        Assert.Equal(2, result.Resources.Count);
    }

    [Fact]
    public async Task ListAsync_WithClientIdFilter_PassesFilterToRepository()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.ListAsync("orders-service", Arg.Any<CancellationToken>()).Returns([MakeClient("orders-service")]);
        var service = CreateService(repo);

        var result = await service.ListAsync("clientId eq \"orders-service\"");

        Assert.Equal(1, result.TotalResults);
        Assert.Equal("orders-service", result.Resources[0].ClientId);
    }

    [Fact]
    public async Task ListAsync_UnsupportedFilterAttribute_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync("userName eq \"alice\""));
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesClient()
    {
        var client = MakeClient("orders-service");
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var result = await service.UpdateAsync(client.Id, new UpdateClientRequest
        {
            ClientId = "orders-service",
            DisplayName = "Orders Service Updated",
            Active = true,
        });

        Assert.Equal("orders-service", result.ClientId);
        await repo.Received(1).UpdateAsync(client, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ChangeClientIdToExisting_ThrowsConflictException()
    {
        var client = MakeClient("orders-service");
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        repo.ExistsByClientIdAsync("payments-service", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(client.Id, new UpdateClientRequest { ClientId = "payments-service", Active = true }));
    }

    [Fact]
    public async Task UpdateAsync_ClientNotFound_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateClientRequest { ClientId = "orders-service", Active = true }));
    }

    [Fact]
    public async Task PatchAsync_ReplaceDisplayName_UpdatesDisplayName()
    {
        var client = MakeClient("orders-service");
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace",
                    Path = "displayName",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement("New Name"),
                },
            ],
        };

        var result = await service.PatchAsync(client.Id, patch);

        Assert.Equal("New Name", result.DisplayName);
        await repo.Received(1).UpdateAsync(client, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_ReplaceActive_DeactivatesClient()
    {
        var client = MakeClient("orders-service");
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace",
                    Path = "active",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement(false),
                },
            ],
        };

        var result = await service.PatchAsync(client.Id, patch);

        Assert.False(result.Active);
    }

    [Fact]
    public async Task PatchAsync_ReplaceActiveTrue_ActivatesClient()
    {
        var client = MakeClient("orders-service", active: false);
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace",
                    Path = "active",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement(true),
                },
            ],
        };

        var result = await service.PatchAsync(client.Id, patch);

        Assert.True(result.Active);
    }

    [Fact]
    public async Task PatchAsync_UnsupportedOperation_ThrowsValidationException()
    {
        var client = MakeClient();
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation { Op = "add", Path = "displayName", Value = default },
            ],
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.PatchAsync(client.Id, patch));
    }

    [Fact]
    public async Task PatchAsync_UnsupportedPath_ThrowsValidationException()
    {
        var client = MakeClient();
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace",
                    Path = "externalId",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement("x"),
                },
            ],
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.PatchAsync(client.Id, patch));
    }

    [Fact]
    public async Task PatchAsync_ClientNotFound_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.PatchAsync(Guid.NewGuid(), new ScimPatchRequest()));
    }

    [Fact]
    public async Task DeleteAsync_ExistingClient_Deletes()
    {
        var client = MakeClient();
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repo);

        await service.DeleteAsync(client.Id);

        await repo.Received(1).DeleteAsync(client.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NonExistingClient_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IMachineClientRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    private static MachineClientService CreateService(IMachineClientRepository? repo = null, ILogger<MachineClientService>? logger = null)
    {
        return new MachineClientService(
            repo ?? Substitute.For<IMachineClientRepository>(),
            logger ?? Substitute.For<ILogger<MachineClientService>>());
    }

    private static MachineClient MakeClient(string clientId = "orders-service", bool active = true)
    {
        var client = MachineClient.Create(clientId, "Orders Service");
        if (!active)
        {
            client.Deactivate();
        }

        return client;
    }
}
