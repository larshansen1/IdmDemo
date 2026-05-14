using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Certificates;
using Backend.Application.Services;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Backend.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Backend.Tests.Services;

public sealed class MachineClientCertificateServiceTests
{
    [Fact]
    public async Task CreateAsync_WithCsr_IssuesAndStoresCertificate()
    {
        var client = CreateClient();
        var issued = CreateIssuedCertificate(client.ClientId);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.ExistsByThumbprintAsync(issued.ThumbprintSha256, Arg.Any<CancellationToken>()).Returns(false);
        var certificateAuthority = Substitute.For<ILocalCertificateAuthority>();
        certificateAuthority
            .IssueCertificateAsync("csr-pem", 45, Arg.Any<CancellationToken>())
            .Returns(issued);
        var service = CreateService(clientRepository, certificateRepository, certificateAuthority);

        var result = await service.CreateAsync(client.Id, new CreateCertificateRequest
        {
            Mode = "csr",
            CertificateSigningRequestPem = "csr-pem",
            DisplayName = "primary",
            ValidityDays = 45,
        });

        Assert.Equal(client.ClientId, result.ClientId);
        Assert.Equal("primary", result.DisplayName);
        Assert.Equal(issued.ThumbprintSha256, result.ThumbprintSha256);
        Assert.Equal("Active", result.Status);
        await certificateAuthority.Received(1).IssueCertificateAsync("csr-pem", 45, Arg.Any<CancellationToken>());
        await certificateRepository.Received(1).AddAsync(
            Arg.Is<MachineClientCertificate>(c => c.ThumbprintSha256 == issued.ThumbprintSha256),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithCsrDefaultValidity_UsesThirtyDays()
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        var certificateAuthority = Substitute.For<ILocalCertificateAuthority>();
        certificateAuthority
            .IssueCertificateAsync("csr-pem", 30, Arg.Any<CancellationToken>())
            .Returns(CreateIssuedCertificate(client.ClientId));
        var service = CreateService(clientRepository, certificateRepository, certificateAuthority);

        await service.CreateAsync(client.Id, new CreateCertificateRequest
        {
            Mode = "CSR",
            CertificateSigningRequestPem = "csr-pem",
        });

        await certificateAuthority.Received(1).IssueCertificateAsync("csr-pem", 30, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task CreateAsync_WithInvalidValidityDays_ThrowsValidationException(int validityDays)
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "csr",
                CertificateSigningRequestPem = "csr-pem",
                ValidityDays = validityDays,
            }));
    }

    [Fact]
    public async Task CreateAsync_WithMissingCsr_ThrowsValidationException()
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest { Mode = "csr" }));
    }

    [Fact]
    public async Task CreateAsync_WithExternalCertificate_StoresCertificateMetadata()
    {
        var client = CreateClient();
        using var certificate = CreateSelfSignedCertificate("external-orders", DateTimeOffset.UtcNow.AddDays(30));
        var certificatePem = certificate.ExportCertificatePem();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.ExistsByThumbprintAsync(ComputeThumbprint(certificate), Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService(clientRepository, certificateRepository);

        var result = await service.CreateAsync(client.Id, new CreateCertificateRequest
        {
            Mode = "external",
            CertificatePem = certificatePem,
            DisplayName = "external",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
        });

        Assert.Equal("external", result.DisplayName);
        Assert.Equal(ComputeThumbprint(certificate), result.ThumbprintSha256);
        Assert.Equal(certificate.Subject, result.Subject);
        Assert.Equal(certificate.Issuer, result.Issuer);
        Assert.Equal("Active", result.Status);
        Assert.Contains("BEGIN CERTIFICATE", result.CertificatePem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_WithExternalExpiryAfterCertificateNotAfter_ThrowsValidationException()
    {
        var client = CreateClient();
        using var certificate = CreateSelfSignedCertificate("external-orders", DateTimeOffset.UtcNow.AddDays(5));
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "external",
                CertificatePem = certificate.ExportCertificatePem(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            }));
    }

    [Fact]
    public async Task CreateAsync_WithExpiredExternalCertificate_ThrowsValidationException()
    {
        var client = CreateClient();
        using var certificate = CreateSelfSignedCertificate(
            "external-orders",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(-2));
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "external",
                CertificatePem = certificate.ExportCertificatePem(),
            }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cert")]
    public async Task CreateAsync_WithInvalidExternalCertificate_ThrowsValidationException(string? certificatePem)
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "external",
                CertificatePem = certificatePem,
            }));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateThumbprint_ThrowsConflictException()
    {
        var client = CreateClient();
        var issued = CreateIssuedCertificate(client.ClientId);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.ExistsByThumbprintAsync(issued.ThumbprintSha256, Arg.Any<CancellationToken>()).Returns(true);
        var certificateAuthority = Substitute.For<ILocalCertificateAuthority>();
        certificateAuthority
            .IssueCertificateAsync("csr-pem", 30, Arg.Any<CancellationToken>())
            .Returns(issued);
        var service = CreateService(clientRepository, certificateRepository, certificateAuthority);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "csr",
                CertificateSigningRequestPem = "csr-pem",
            }));
    }

    [Fact]
    public async Task CreateAsync_WithUnknownMode_ThrowsValidationException()
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest { Mode = "manual" }));
    }

    [Fact]
    public async Task CreateAsync_WithDisplayNameTooLong_ThrowsValidationException()
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(client.Id, new CreateCertificateRequest
            {
                Mode = "csr",
                DisplayName = new string('a', 513),
            }));
    }

    [Fact]
    public async Task CreateAsync_WithUnknownClient_ThrowsNotFoundException()
    {
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(clientRepository);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(Guid.NewGuid(), new CreateCertificateRequest { Mode = "csr" }));
    }

    [Fact]
    public async Task GetAsync_ReturnsCertificate()
    {
        var client = CreateClient();
        var certificate = CreateCertificateRecord(client);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.GetByIdAsync(client.Id, certificate.Id, Arg.Any<CancellationToken>()).Returns(certificate);
        var service = CreateService(clientRepository, certificateRepository);

        var result = await service.GetAsync(client.Id, certificate.Id);

        Assert.Equal(certificate.Id.ToString(), result.Id);
        Assert.Equal(client.ClientId, result.ClientId);
        Assert.Equal($"/scim/v2/Clients/{client.Id}/Certificates/{certificate.Id}", result.Meta.Location);
    }

    [Fact]
    public async Task GetAsync_WithUnknownCertificate_ThrowsNotFoundException()
    {
        var client = CreateClient();
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.GetByIdAsync(client.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(clientRepository, certificateRepository);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(client.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_ReturnsActiveRevokedAndExpiredStatuses()
    {
        var client = CreateClient();
        var active = CreateCertificateRecord(client, "active", DateTimeOffset.UtcNow.AddDays(1));
        var revoked = CreateCertificateRecord(client, "revoked", DateTimeOffset.UtcNow.AddDays(1));
        revoked.Revoke("rotated");
        var expired = CreateCertificateRecord(client, "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.ListAsync(client.Id, Arg.Any<CancellationToken>()).Returns([active, revoked, expired]);
        var service = CreateService(clientRepository, certificateRepository);

        var result = await service.ListAsync(client.Id);

        Assert.Equal(3, result.TotalResults);
        Assert.Equal(3, result.ItemsPerPage);
        Assert.Equal(["Active", "Revoked", "Expired"], result.Resources.Select(c => c.Status).ToArray());
        Assert.Equal("rotated", result.Resources[1].RevocationReason);
    }

    [Fact]
    public async Task RevokeAsync_UpdatesCertificate()
    {
        var client = CreateClient();
        var certificate = CreateCertificateRecord(client);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.GetByIdAsync(client.Id, certificate.Id, Arg.Any<CancellationToken>()).Returns(certificate);
        var service = CreateService(clientRepository, certificateRepository);

        var result = await service.RevokeAsync(client.Id, certificate.Id, new RevokeCertificateRequest { Reason = "rotation" });

        Assert.Equal("Revoked", result.Status);
        Assert.Equal("rotation", result.RevocationReason);
        Assert.NotNull(result.RevokedAt);
        await certificateRepository.Received(1).UpdateAsync(certificate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_WithLongReason_ThrowsValidationException()
    {
        var client = CreateClient();
        var certificate = CreateCertificateRecord(client);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository.GetByIdAsync(client.Id, certificate.Id, Arg.Any<CancellationToken>()).Returns(certificate);
        var service = CreateService(clientRepository, certificateRepository);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RevokeAsync(client.Id, certificate.Id, new RevokeCertificateRequest { Reason = new string('r', 513) }));
    }

    [Fact]
    public async Task RevokeAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    [Fact]
    public async Task GetCertificateAuthorityAsync_ReturnsCaMetadata()
    {
        var certificateAuthority = Substitute.For<ILocalCertificateAuthority>();
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(365);
        certificateAuthority.GetCertificateAsync(Arg.Any<CancellationToken>()).Returns(new CertificateAuthorityCertificate
        {
            CertificatePem = "ca-pem",
            Subject = "CN=CA",
            Issuer = "CN=CA",
            SerialNumber = "01",
            NotBefore = notBefore,
            ExpiresAt = expiresAt,
        });
        var service = CreateService(certificateAuthority: certificateAuthority);

        var result = await service.GetCertificateAuthorityAsync();

        Assert.Equal("ca-pem", result.CertificatePem);
        Assert.Equal("CN=CA", result.Subject);
        Assert.Equal("01", result.SerialNumber);
        Assert.Equal(notBefore, result.NotBefore);
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task CreateAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(Guid.NewGuid(), null!));
    }

    private static MachineClientCertificateService CreateService(
        IMachineClientRepository? clientRepository = null,
        IMachineClientCertificateRepository? certificateRepository = null,
        ILocalCertificateAuthority? certificateAuthority = null)
    {
        return new MachineClientCertificateService(
            clientRepository ?? Substitute.For<IMachineClientRepository>(),
            certificateRepository ?? Substitute.For<IMachineClientCertificateRepository>(),
            certificateAuthority ?? Substitute.For<ILocalCertificateAuthority>(),
            Substitute.For<ILogger<MachineClientCertificateService>>());
    }

    private static MachineClient CreateClient()
    {
        return MachineClient.Create("orders-service", "Orders Service");
    }

    private static IssuedClientCertificate CreateIssuedCertificate(string subjectName)
    {
        using var certificate = CreateSelfSignedCertificate(subjectName, DateTimeOffset.UtcNow.AddDays(30));
        return new IssuedClientCertificate
        {
            CertificatePem = certificate.ExportCertificatePem(),
            ThumbprintSha256 = ComputeThumbprint(certificate),
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            SerialNumber = certificate.SerialNumber,
            NotBefore = certificate.NotBefore,
            ExpiresAt = certificate.NotAfter,
        };
    }

    private static MachineClientCertificate CreateCertificateRecord(
        MachineClient client,
        string displayName = "primary",
        DateTimeOffset? expiresAt = null)
    {
        using var certificate = CreateSelfSignedCertificate(displayName, DateTimeOffset.UtcNow.AddDays(30));
        return MachineClientCertificate.Create(
            client.Id,
            displayName,
            ComputeThumbprint(certificate),
            certificate.Subject,
            certificate.Issuer,
            certificate.SerialNumber,
            certificate.NotBefore,
            expiresAt ?? certificate.NotAfter,
            certificate.ExportCertificatePem());
    }

    private static X509Certificate2 CreateSelfSignedCertificate(
        string subjectName,
        DateTimeOffset notAfter,
        DateTimeOffset? notBefore = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5), notAfter);
    }

    private static string ComputeThumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }
}
