using Backend.Infrastructure.Signing;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Backend.IntegrationTests.Infrastructure;

public sealed class LocalJwtSigningKeyStoreTests
{
    [Fact]
    public async Task GetActiveKeyAsync_NoExistingKey_CreatesAndPersistsKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"idm_signing_{Guid.NewGuid():N}.json");
        try
        {
            var protector = new EphemeralDataProtectionProvider().CreateProtector("test");
            using var store = new LocalJwtSigningKeyStore(path, protector);

            var key = await store.GetActiveKeyAsync();

            Assert.True(File.Exists(path));
            Assert.NotEmpty(key.KeyId);
            Assert.NotNull(key.Parameters.Modulus);
            Assert.NotNull(key.Parameters.Exponent);
            Assert.NotNull(key.Parameters.D);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task GetActiveKeyAsync_ExistingKey_ReturnsPersistedKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"idm_signing_{Guid.NewGuid():N}.json");
        try
        {
            var provider = new EphemeralDataProtectionProvider();

            string firstKeyId;
            using (var firstStore = new LocalJwtSigningKeyStore(path, provider.CreateProtector("test")))
            {
                var firstKey = await firstStore.GetActiveKeyAsync();
                firstKeyId = firstKey.KeyId;
            }

            using var secondStore = new LocalJwtSigningKeyStore(path, provider.CreateProtector("test"));
            var secondKey = await secondStore.GetActiveKeyAsync();

            Assert.Equal(firstKeyId, secondKey.KeyId);
            Assert.NotNull(secondKey.Parameters.Modulus);
            Assert.NotNull(secondKey.Parameters.Exponent);
            Assert.NotNull(secondKey.Parameters.D);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task GetActiveKeyAsync_WrittenFile_ContainsNoPlaintextPem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"idm_signing_{Guid.NewGuid():N}.json");
        try
        {
            var protector = new EphemeralDataProtectionProvider().CreateProtector("test");
            using var store = new LocalJwtSigningKeyStore(path, protector);
            await store.GetActiveKeyAsync();

            var content = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("-----BEGIN", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
