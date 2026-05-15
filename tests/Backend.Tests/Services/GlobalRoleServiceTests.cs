using Backend.Application.Models.Roles;
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

public sealed class GlobalRoleServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsRole()
    {
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.ExistsByValueAsync("admin", Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateRoleRequest
        {
            Value = "admin",
            DisplayName = "Admin",
            Description = "Administrators",
        });

        Assert.Equal("admin", result.Value);
        Assert.True(result.Active);
        await repo.Received(1).AddAsync(Arg.Any<GlobalRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_InactiveRequest_ReturnsInactiveRole()
    {
        var repo = Substitute.For<IGlobalRoleRepository>();
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateRoleRequest { Value = "admin", Active = false });

        Assert.False(result.Active);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admin role")]
    public async Task CreateAsync_InvalidValue_ThrowsValidationException(string? value)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateRoleRequest { Value = value }));
    }

    [Fact]
    public async Task CreateAsync_DuplicateValue_ThrowsConflictException()
    {
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.ExistsByValueAsync("admin", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateRoleRequest { Value = "admin" }));
    }

    [Fact]
    public async Task GetAsync_ExistingRole_ReturnsRole()
    {
        var role = GlobalRole.Create("admin", "Admin", null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var service = CreateService(repo);

        var result = await service.GetAsync(role.Id);

        Assert.Equal(role.Id.ToString(), result.Id);
    }

    [Fact]
    public async Task GetAsync_MissingRole_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_WithValueFilter_ReturnsMatchingRoles()
    {
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.ListAsync("admin", Arg.Any<CancellationToken>()).Returns([GlobalRole.Create("admin", null, null)]);
        var service = CreateService(repo);

        var result = await service.ListAsync("value eq \"admin\"");

        Assert.Equal(1, result.TotalResults);
        Assert.Equal("admin", result.Resources[0].Value);
    }

    [Fact]
    public async Task ListAsync_UnsupportedFilter_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync("displayName eq \"Admin\""));
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesRole()
    {
        var role = GlobalRole.Create("admin", null, null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var service = CreateService(repo);

        var result = await service.UpdateAsync(role.Id, new UpdateRoleRequest
        {
            Value = "operator",
            DisplayName = "Operator",
            Description = "Operators",
            Active = false,
        });

        Assert.Equal("operator", result.Value);
        Assert.False(result.Active);
        await repo.Received(1).UpdateAsync(role, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_DuplicateValue_ThrowsConflictException()
    {
        var role = GlobalRole.Create("admin", null, null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        repo.ExistsByValueAsync("operator", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(role.Id, new UpdateRoleRequest { Value = "operator" }));
    }

    [Fact]
    public async Task PatchAsync_ReplaceFields_UpdatesRole()
    {
        var role = GlobalRole.Create("admin", null, null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var service = CreateService(repo);

        var result = await service.PatchAsync(role.Id, new ScimPatchRequest
        {
            Operations =
            [
                Patch("displayName", "Admin"),
                Patch("description", "Administrators"),
                Patch("active", false),
            ],
        });

        Assert.Equal("Admin", result.DisplayName);
        Assert.Equal("Administrators", result.Description);
        Assert.False(result.Active);
    }

    [Fact]
    public async Task PatchAsync_UnsupportedPath_ThrowsValidationException()
    {
        var role = GlobalRole.Create("admin", null, null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.PatchAsync(role.Id, new ScimPatchRequest { Operations = [Patch("externalId", "x")] }));
    }

    [Fact]
    public async Task DeleteAsync_UnassignedRole_Deletes()
    {
        var role = GlobalRole.Create("admin", null, null);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var service = CreateService(repo);

        await service.DeleteAsync(role.Id);

        await repo.Received(1).DeleteAsync(role.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_AssignedUserRole_ThrowsConflictException()
    {
        var role = GlobalRole.Create("admin", null, null);
        var user = User.Create("alice", null, null);
        user.AssignRoles(["admin"]);
        var repo = Substitute.For<IGlobalRoleRepository>();
        repo.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var userRepository = Substitute.For<IUserRepository>();
        userRepository.ListAsync(null, Arg.Any<CancellationToken>()).Returns([user]);
        var service = CreateService(repo, userRepository);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(role.Id));
    }

    private static GlobalRoleService CreateService(
        IGlobalRoleRepository? roleRepository = null,
        IUserRepository? userRepository = null,
        IMachineClientRepository? clientRepository = null)
    {
        return new GlobalRoleService(
            roleRepository ?? Substitute.For<IGlobalRoleRepository>(),
            userRepository ?? Substitute.For<IUserRepository>(),
            clientRepository ?? Substitute.For<IMachineClientRepository>(),
            Substitute.For<ILogger<GlobalRoleService>>());
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
