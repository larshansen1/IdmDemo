using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Repositories;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Backend.Tests.Services;

public sealed class IdpIssuanceContextProviderTests
{
    [Fact]
    public async Task ResolveAsync_ValidClientAuthentication_ReturnsIssuanceContext()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read"]);
        client.AssignRoles(["service-admin"]);
        var fixture = CreateFixture(client);

        var context = await fixture.Provider.ResolveAsync(client.ClientId, certificate, CancellationToken.None);

        Assert.Equal(client.Id, context.ClientRecordId);
        Assert.Equal(client.ClientId, context.ClientId);
        Assert.Same(certificate, context.Certificate);
        Assert.Equal(["orders.read"], context.ActiveScopes);
        Assert.Equal(["service-admin"], context.ActiveRoles);
    }

    [Fact]
    public async Task ResolveAsync_UnknownClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var fixture = CreateFixture();
        fixture.ClientRepository.GetByClientIdAsync("orders-service", Arg.Any<CancellationToken>()).ReturnsNull();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.Provider.ResolveAsync("orders-service", certificate, CancellationToken.None));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveAsync_InactiveClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.Deactivate();
        var fixture = CreateFixture(client);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.Provider.ResolveAsync(client.ClientId, certificate, CancellationToken.None));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveAsync_CertificateMismatch_ThrowsInvalidClient()
    {
        using var registeredCertificate = CreateCertificate();
        using var presentedCertificate = CreateCertificate("presented-service");
        var client = CreateClient(registeredCertificate);
        var fixture = CreateFixture(client);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.Provider.ResolveAsync(client.ClientId, presentedCertificate, CancellationToken.None));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveAsync_RevokedCertificateRecord_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var client = MachineClient.Create("orders-service", "Orders Service");
        var certificateRecord = CreateCertificateRecord(client, certificate);
        certificateRecord.Revoke("rotated");
        var fixture = CreateFixture(client);
        fixture.CertificateRepository
            .GetByThumbprintAsync(client.Id, ComputeThumbprint(certificate), Arg.Any<CancellationToken>())
            .Returns(certificateRecord);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.Provider.ResolveAsync(client.ClientId, certificate, CancellationToken.None));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveAsync_FiltersInactiveAssignedScopes()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read", "orders.write"]);
        var fixture = CreateFixture(client);
        fixture.ScopeRepository
            .ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult((string)call[0] == "orders.read"));

        var context = await fixture.Provider.ResolveAsync(client.ClientId, certificate, CancellationToken.None);

        Assert.Equal(["orders.read"], context.ActiveScopes);
    }

    [Fact]
    public async Task ResolveAsync_FiltersInactiveAssignedRoles()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.AssignRoles(["service-admin", "retired-role"]);
        var fixture = CreateFixture(client);
        fixture.RoleRepository
            .ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult((string)call[0] == "service-admin"));

        var context = await fixture.Provider.ResolveAsync(client.ClientId, certificate, CancellationToken.None);

        Assert.Equal(["service-admin"], context.ActiveRoles);
    }

    private static TestFixture CreateFixture(MachineClient? client = null)
    {
        var clientRepository = Substitute.For<IMachineClientRepository>();
        if (client is not null)
        {
            clientRepository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        }

        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        var scopeRepository = Substitute.For<IGlobalScopeRepository>();
        scopeRepository.ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var roleRepository = Substitute.For<IGlobalRoleRepository>();
        roleRepository.ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var provider = new IdpIssuanceContextProvider(
            clientRepository,
            certificateRepository,
            scopeRepository,
            roleRepository);

        return new TestFixture(provider, clientRepository, certificateRepository, scopeRepository, roleRepository);
    }

    private static MachineClient CreateClient(X509Certificate2 certificate)
    {
        var client = MachineClient.Create("orders-service", "Orders Service");
        client.UpdateCertificate(ComputeThumbprint(certificate), certificate.Subject, certificate.NotAfter);
        return client;
    }

    private static MachineClientCertificate CreateCertificateRecord(MachineClient client, X509Certificate2 certificate)
    {
        return MachineClientCertificate.Create(
            client.Id,
            "test certificate",
            ComputeThumbprint(certificate),
            certificate.Subject,
            certificate.Issuer,
            certificate.SerialNumber,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.ExportCertificatePem());
    }

    private static string ComputeThumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static X509Certificate2 CreateCertificate(string subjectName = "orders-service")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private sealed record TestFixture(
        IdpIssuanceContextProvider Provider,
        IMachineClientRepository ClientRepository,
        IMachineClientCertificateRepository CertificateRepository,
        IGlobalScopeRepository ScopeRepository,
        IGlobalRoleRepository RoleRepository);
}
