using System.Security.Cryptography;
using System.Text.Json;
using Backend.As.Domain.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Backend.Infrastructure.Signing;

public sealed class LocalJwtSigningKeyStore : IJwtSigningKeyStore, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _path;
    private readonly IDataProtector _dataProtector;

    public LocalJwtSigningKeyStore(string path, IDataProtector dataProtector)
    {
        this._path = path;
        this._dataProtector = dataProtector;
    }

    public async Task<JwtSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
    {
        await this._semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(this._path))
            {
                return await this.ReadKeyAsync(cancellationToken).ConfigureAwait(false);
            }

            var key = CreateKey();
            await this.WriteKeyAsync(key, cancellationToken).ConfigureAwait(false);
            return key;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public void Dispose()
    {
        this._semaphore.Dispose();
    }

    private static JwtSigningKey CreateKey()
    {
        using var rsa = RSA.Create(2048);
        return new JwtSigningKey
        {
            KeyId = Guid.NewGuid().ToString("N"),
            Parameters = rsa.ExportParameters(true),
        };
    }

    private async Task<JwtSigningKey> ReadKeyAsync(CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(this._path);
        await using (stream.ConfigureAwait(false))
        {
            var file = await JsonSerializer.DeserializeAsync<SigningKeyFile>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Signing key file is invalid.");

            var plainPem = this._dataProtector.Unprotect(file.ProtectedKeyPem);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(plainPem);

            return new JwtSigningKey
            {
                KeyId = file.KeyId,
                Parameters = rsa.ExportParameters(true),
            };
        }
    }

    private async Task WriteKeyAsync(JwtSigningKey key, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(this._path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var rsa = RSA.Create();
        rsa.ImportParameters(key.Parameters);

        var file = new SigningKeyFile
        {
            KeyId = key.KeyId,
            ProtectedKeyPem = this._dataProtector.Protect(rsa.ExportRSAPrivateKeyPem()),
        };

        var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var stream = File.Open(this._path, fileOptions);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, file, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class SigningKeyFile
    {
        public string KeyId { get; init; } = string.Empty;

        public string ProtectedKeyPem { get; init; } = string.Empty;
    }
}
