using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Scim;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Backend.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class MachineClientCertificateService : IMachineClientCertificateService
{
    private const int _defaultValidityDays = 30;
    private const int _maximumValidityDays = 90;

    private readonly IMachineClientRepository _clientRepository;
    private readonly IMachineClientCertificateRepository _certificateRepository;
    private readonly ILocalCertificateAuthority _certificateAuthority;
    private readonly ILogger<MachineClientCertificateService> _logger;

    public MachineClientCertificateService(
        IMachineClientRepository clientRepository,
        IMachineClientCertificateRepository certificateRepository,
        ILocalCertificateAuthority certificateAuthority,
        ILogger<MachineClientCertificateService> logger)
    {
        this._clientRepository = clientRepository;
        this._certificateRepository = certificateRepository;
        this._certificateAuthority = certificateAuthority;
        this._logger = logger;
    }

    public async Task<CertificateResponse> CreateAsync(
        Guid clientId,
        CreateCertificateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await this.GetClientAsync(clientId, cancellationToken).ConfigureAwait(false);
        ValidateDisplayName(request.DisplayName);

        var certificate = await this.CreateCertificateEntityAsync(client, request, cancellationToken).ConfigureAwait(false);
        if (await this._certificateRepository.ExistsByThumbprintAsync(certificate.ThumbprintSha256, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("thumbprintSha256", certificate.ThumbprintSha256);
        }

        await this._certificateRepository.AddAsync(certificate, cancellationToken).ConfigureAwait(false);

        LogCertificateCreated(this._logger, client.Id, certificate.Id, certificate.ThumbprintSha256);

        return ToResponse(client.ClientId, certificate);
    }

    public async Task<CertificateResponse> GetAsync(
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken = default)
    {
        var client = await this.GetClientAsync(clientId, cancellationToken).ConfigureAwait(false);
        var certificate = await this.GetCertificateAsync(client.Id, certificateId, cancellationToken).ConfigureAwait(false);

        return ToResponse(client.ClientId, certificate);
    }

    public async Task<ScimListResponse<CertificateResponse>> ListAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var client = await this.GetClientAsync(clientId, cancellationToken).ConfigureAwait(false);
        var certificates = await this._certificateRepository.ListAsync(client.Id, cancellationToken).ConfigureAwait(false);
        var resources = certificates.Select(c => ToResponse(client.ClientId, c)).ToList();

        return new ScimListResponse<CertificateResponse>
        {
            TotalResults = resources.Count,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<CertificateResponse> RevokeAsync(
        Guid clientId,
        Guid certificateId,
        RevokeCertificateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await this.GetClientAsync(clientId, cancellationToken).ConfigureAwait(false);
        var certificate = await this.GetCertificateAsync(client.Id, certificateId, cancellationToken).ConfigureAwait(false);
        ValidateRevocationReason(request.Reason);

        certificate.Revoke(request.Reason);
        await this._certificateRepository.UpdateAsync(certificate, cancellationToken).ConfigureAwait(false);

        LogCertificateRevoked(this._logger, client.Id, certificate.Id);

        return ToResponse(client.ClientId, certificate);
    }

    public async Task<CertificateAuthorityResponse> GetCertificateAuthorityAsync(CancellationToken cancellationToken = default)
    {
        var certificate = await this._certificateAuthority.GetCertificateAsync(cancellationToken).ConfigureAwait(false);
        return new CertificateAuthorityResponse
        {
            CertificatePem = certificate.CertificatePem,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            SerialNumber = certificate.SerialNumber,
            NotBefore = certificate.NotBefore,
            ExpiresAt = certificate.ExpiresAt,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientCertificateCreated {ClientRecordId} {CertificateId} {ThumbprintSha256}")]
    private static partial void LogCertificateCreated(
        ILogger logger,
        Guid clientRecordId,
        Guid certificateId,
        string thumbprintSha256);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientCertificateRevoked {ClientRecordId} {CertificateId}")]
    private static partial void LogCertificateRevoked(ILogger logger, Guid clientRecordId, Guid certificateId);

    private static string ComputeThumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero);
    }

    private static X509Certificate2 ParseCertificate(string? certificatePem)
    {
        if (string.IsNullOrWhiteSpace(certificatePem))
        {
            throw new ValidationException("certificatePem is required for external certificate registration.");
        }

        try
        {
            return X509Certificate2.CreateFromPem(certificatePem);
        }
        catch (CryptographicException exception)
        {
            throw new ValidationException("certificatePem must contain a valid PEM-encoded certificate.", exception);
        }
    }

    private static int ValidateValidityDays(int? requestedValidityDays)
    {
        var validityDays = requestedValidityDays ?? _defaultValidityDays;
        if (validityDays <= 0 || validityDays > _maximumValidityDays)
        {
            throw new ValidationException($"validityDays must be between 1 and {_maximumValidityDays}.");
        }

        return validityDays;
    }

    private static void ValidateDisplayName(string? displayName)
    {
        if (displayName?.Length > 512)
        {
            throw new ValidationException("displayName must not exceed 512 characters.");
        }
    }

    private static void ValidateRevocationReason(string? reason)
    {
        if (reason?.Length > 512)
        {
            throw new ValidationException("reason must not exceed 512 characters.");
        }
    }

    private static string ResolveStatus(MachineClientCertificate certificate)
    {
        if (certificate.Status == MachineClientCertificateStatus.Revoked)
        {
            return "Revoked";
        }

        return DateTimeOffset.UtcNow > certificate.ExpiresAt ? "Expired" : "Active";
    }

    private static CertificateResponse ToResponse(string clientExternalId, MachineClientCertificate certificate)
    {
        return new CertificateResponse
        {
            Id = certificate.Id.ToString(),
            ClientId = clientExternalId,
            DisplayName = certificate.DisplayName,
            ThumbprintSha256 = certificate.ThumbprintSha256,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            SerialNumber = certificate.SerialNumber,
            NotBefore = certificate.NotBefore,
            ExpiresAt = certificate.ExpiresAt,
            CertificatePem = certificate.CertificatePem,
            Status = ResolveStatus(certificate),
            RevokedAt = certificate.RevokedAt,
            RevocationReason = certificate.RevocationReason,
            Meta = new ScimMeta
            {
                ResourceType = "ClientCertificate",
                Created = certificate.CreatedAt,
                LastModified = certificate.UpdatedAt,
                Location = $"/scim/v2/Clients/{certificate.MachineClientId}/Certificates/{certificate.Id}",
            },
        };
    }

    private static MachineClientCertificate CreateExternalCertificate(
        MachineClient client,
        CreateCertificateRequest request)
    {
        using var parsedCertificate = ParseCertificate(request.CertificatePem);
        var notBefore = ToDateTimeOffset(parsedCertificate.NotBefore);
        var certificateExpiresAt = ToDateTimeOffset(parsedCertificate.NotAfter);
        var expiresAt = request.ExpiresAt ?? certificateExpiresAt;

        if (expiresAt > certificateExpiresAt)
        {
            throw new ValidationException("expiresAt must not be later than the certificate NotAfter value.");
        }

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            throw new ValidationException("certificate must not already be expired.");
        }

        return MachineClientCertificate.Create(
            client.Id,
            request.DisplayName,
            ComputeThumbprint(parsedCertificate),
            parsedCertificate.Subject,
            parsedCertificate.Issuer,
            parsedCertificate.SerialNumber,
            notBefore,
            expiresAt,
            parsedCertificate.ExportCertificatePem());
    }

    private async Task<MachineClientCertificate> CreateCertificateEntityAsync(
        MachineClient client,
        CreateCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Mode, "csr", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.CertificateSigningRequestPem))
            {
                throw new ValidationException("certificateSigningRequestPem is required for CSR issuance.");
            }

            var issuedCertificate = await this._certificateAuthority.IssueCertificateAsync(
                request.CertificateSigningRequestPem,
                ValidateValidityDays(request.ValidityDays),
                cancellationToken).ConfigureAwait(false);

            return MachineClientCertificate.Create(
                client.Id,
                request.DisplayName,
                issuedCertificate.ThumbprintSha256,
                issuedCertificate.Subject,
                issuedCertificate.Issuer,
                issuedCertificate.SerialNumber,
                issuedCertificate.NotBefore,
                issuedCertificate.ExpiresAt,
                issuedCertificate.CertificatePem);
        }

        if (string.Equals(request.Mode, "external", StringComparison.OrdinalIgnoreCase))
        {
            return CreateExternalCertificate(client, request);
        }

        throw new ValidationException("mode must be either 'csr' or 'external'.");
    }

    private async Task<MachineClient> GetClientAsync(Guid clientId, CancellationToken cancellationToken)
    {
        return await this._clientRepository.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", clientId.ToString());
    }

    private async Task<MachineClientCertificate> GetCertificateAsync(
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        return await this._certificateRepository.GetByIdAsync(clientId, certificateId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Certificate", certificateId.ToString());
    }
}
