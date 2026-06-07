using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Services;
using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Exceptions;
using Backend.Idp.Domain.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Backend.Tests.Services;

public sealed class GlobalScopeServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsScope()
    {
        var repo = Substitute.For<IGlobalScopeRepository>();
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateScopeRequest { Value = "orders.read" });

        Assert.Equal("orders.read", result.Value);
        Assert.True(result.Active);
        await repo.Received(1).AddAsync(Arg.Any<GlobalScope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DuplicateValue_ThrowsConflictException()
    {
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.ExistsByValueAsync("orders.read", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateScopeRequest { Value = "orders.read" }));
    }

    [Fact]
    public async Task CreateAsync_InactiveRequest_ReturnsInactiveScope()
    {
        var service = CreateService();

        var result = await service.CreateAsync(new CreateScopeRequest { Value = "orders.read", Active = false });

        Assert.False(result.Active);
    }

    [Fact]
    public async Task GetAsync_MissingScope_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_WithValueFilter_ReturnsMatchingScopes()
    {
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.ListAsync("orders.read", Arg.Any<CancellationToken>()).Returns([GlobalScope.Create("orders.read", null, null)]);
        var service = CreateService(repo);

        var result = await service.ListAsync("value eq \"orders.read\"");

        Assert.Equal(1, result.TotalResults);
        Assert.Equal("orders.read", result.Resources[0].Value);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesScope()
    {
        var scope = GlobalScope.Create("orders.read", null, null);
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(scope.Id, Arg.Any<CancellationToken>()).Returns(scope);
        var service = CreateService(repo);

        var result = await service.UpdateAsync(scope.Id, new UpdateScopeRequest
        {
            Value = "orders.write",
            DisplayName = "Orders Write",
            Description = "Write orders",
            Active = false,
        });

        Assert.Equal("orders.write", result.Value);
        Assert.False(result.Active);
    }

    [Fact]
    public async Task PatchAsync_ReplaceFields_UpdatesScope()
    {
        var scope = GlobalScope.Create("orders.read", null, null);
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(scope.Id, Arg.Any<CancellationToken>()).Returns(scope);
        var service = CreateService(repo);

        var result = await service.PatchAsync(scope.Id, new ScimPatchRequest
        {
            Operations =
            [
                Patch("value", "orders.write"),
                Patch("displayName", "Orders Write"),
                Patch("description", "Write orders"),
                Patch("active", false),
            ],
        });

        Assert.Equal("orders.write", result.Value);
        Assert.Equal("Orders Write", result.DisplayName);
        Assert.Equal("Write orders", result.Description);
        Assert.False(result.Active);
    }

    [Fact]
    public async Task PatchAsync_InvalidActiveValue_ThrowsValidationException()
    {
        var scope = GlobalScope.Create("orders.read", null, null);
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(scope.Id, Arg.Any<CancellationToken>()).Returns(scope);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.PatchAsync(scope.Id, new ScimPatchRequest { Operations = [Patch("active", "false")] }));
    }

    [Fact]
    public async Task DeleteAsync_AssignedScope_ThrowsConflictException()
    {
        var scope = GlobalScope.Create("orders.read", null, null);
        var client = MachineClient.Create("orders-service", null);
        client.AssignScopes(["orders.read"]);
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(scope.Id, Arg.Any<CancellationToken>()).Returns(scope);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.ListAsync(null, Arg.Any<CancellationToken>()).Returns([client]);
        var service = CreateService(repo, clientRepository);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(scope.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnassignedScope_Deletes()
    {
        var scope = GlobalScope.Create("orders.read", null, null);
        var repo = Substitute.For<IGlobalScopeRepository>();
        repo.GetByIdAsync(scope.Id, Arg.Any<CancellationToken>()).Returns(scope);
        var service = CreateService(repo);

        await service.DeleteAsync(scope.Id);

        await repo.Received(1).DeleteAsync(scope.Id, Arg.Any<CancellationToken>());
    }

    private static GlobalScopeService CreateService(
        IGlobalScopeRepository? scopeRepository = null,
        IMachineClientRepository? clientRepository = null)
    {
        return new GlobalScopeService(
            scopeRepository ?? Substitute.For<IGlobalScopeRepository>(),
            clientRepository ?? Substitute.For<IMachineClientRepository>(),
            Substitute.For<ILogger<GlobalScopeService>>());
    }

    private static ScimPatchOperation Patch<T>(string path, T value)
    {
        return new ScimPatchOperation
        {
            Op = "replace",
            Path = path,
            Value = System.Text.Json.JsonSerializer.SerializeToElement(value),
        };
    }
}
