using Backend.Application.Models.Scim;
using Backend.Application.Models.Users;
using Backend.Application.Services;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Backend.Tests.Services;

public sealed class UserServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsUser()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.ExistsByUserNameAsync("alice", Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateUserRequest { UserName = "alice" });

        Assert.Equal("alice", result.UserName);
        Assert.True(result.Active);
        await repo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithActiveSetToFalse_CreatesInactiveUser()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.ExistsByUserNameAsync("alice", Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(repo);

        var result = await service.CreateAsync(new CreateUserRequest { UserName = "alice", Active = false });

        Assert.False(result.Active);
    }

    [Fact]
    public async Task CreateAsync_DuplicateUserName_ThrowsConflictException()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.ExistsByUserNameAsync("alice", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateUserRequest { UserName = "alice" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidUserName_ThrowsValidationException(string? userName)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateUserRequest { UserName = userName }));
    }

    [Fact]
    public async Task CreateAsync_UserNameTooLong_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateUserRequest { UserName = new string('a', 257) }));
    }

    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_UserNameWithNullChar_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateUserRequest { UserName = "ali\0ce" }));
    }

    [Fact]
    public async Task GetAsync_ExistingUser_ReturnsUser()
    {
        var user = MakeUser();
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService(repo);

        var result = await service.GetAsync(user.Id);

        Assert.Equal(user.Id.ToString(), result.Id);
        Assert.Equal("alice", result.UserName);
    }

    [Fact]
    public async Task GetAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllUsers()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.ListAsync(null, Arg.Any<CancellationToken>()).Returns([MakeUser("alice"), MakeUser("bob")]);
        var service = CreateService(repo);

        var result = await service.ListAsync(null);

        Assert.Equal(2, result.TotalResults);
        Assert.Equal(2, result.Resources.Count);
    }

    [Fact]
    public async Task ListAsync_WithUserNameFilter_PassesFilterToRepository()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.ListAsync("alice", Arg.Any<CancellationToken>()).Returns([MakeUser("alice")]);
        var service = CreateService(repo);

        var result = await service.ListAsync("userName eq \"alice\"");

        Assert.Equal(1, result.TotalResults);
        Assert.Equal("alice", result.Resources[0].UserName);
    }

    [Fact]
    public async Task ListAsync_UnsupportedFilterAttribute_ThrowsValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync("email eq \"alice@example.com\""));
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesUser()
    {
        var user = MakeUser("alice");
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService(repo);

        var result = await service.UpdateAsync(user.Id, new UpdateUserRequest
        {
            UserName = "alice",
            DisplayName = "Alice Updated",
            Active = true,
        });

        Assert.Equal("alice", result.UserName);
        await repo.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ChangeUserNameToExisting_ThrowsConflictException()
    {
        var user = MakeUser("alice");
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        repo.ExistsByUserNameAsync("bob", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(user.Id, new UpdateUserRequest { UserName = "bob", Active = true }));
    }

    [Fact]
    public async Task UpdateAsync_UserNotFound_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateUserRequest { UserName = "alice", Active = true }));
    }

    [Fact]
    public async Task PatchAsync_ReplaceDisplayName_UpdatesDisplayName()
    {
        var user = MakeUser("alice");
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
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

        var result = await service.PatchAsync(user.Id, patch);

        Assert.Equal("New Name", result.DisplayName);
        await repo.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_ReplaceActive_DeactivatesUser()
    {
        var user = MakeUser("alice");
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
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

        var result = await service.PatchAsync(user.Id, patch);

        Assert.False(result.Active);
    }

    [Fact]
    public async Task PatchAsync_ReplaceActiveTrue_ActivatesUser()
    {
        var user = MakeUser("alice", active: false);
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
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

        var result = await service.PatchAsync(user.Id, patch);

        Assert.True(result.Active);
    }

    [Fact]
    public async Task PatchAsync_UnsupportedOperation_ThrowsValidationException()
    {
        var user = MakeUser();
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService(repo);

        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation { Op = "add", Path = "displayName", Value = default },
            ],
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.PatchAsync(user.Id, patch));
    }

    [Fact]
    public async Task PatchAsync_UnsupportedPath_ThrowsValidationException()
    {
        var user = MakeUser();
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
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

        await Assert.ThrowsAsync<ValidationException>(() => service.PatchAsync(user.Id, patch));
    }

    [Fact]
    public async Task PatchAsync_UserNotFound_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.PatchAsync(Guid.NewGuid(), new ScimPatchRequest()));
    }

    [Fact]
    public async Task DeleteAsync_ExistingUser_Deletes()
    {
        var user = MakeUser();
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService(repo);

        await service.DeleteAsync(user.Id);

        await repo.Received(1).DeleteAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var repo = Substitute.For<IUserRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    private static UserService CreateService(
        IUserRepository? repo = null,
        IGlobalRoleRepository? roleRepository = null,
        ILogger<UserService>? logger = null)
    {
        var roles = roleRepository ?? Substitute.For<IGlobalRoleRepository>();
        roles.ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        return new UserService(
            repo ?? Substitute.For<IUserRepository>(),
            roles,
            logger ?? Substitute.For<ILogger<UserService>>());
    }

    private static User MakeUser(string userName = "alice", bool active = true)
    {
        var user = User.Create(userName, "Alice Smith", null);
        if (!active)
        {
            user.Deactivate();
        }

        return user;
    }
}
