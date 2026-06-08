using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Backend.IntegrationTests.Middleware;

public sealed class ClientCertificateReaderTests
{
    [Fact]
    public void Read_EnabledAndTrustedProxy_ReturnsCertificate()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["127.0.0.1"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.Loopback, Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.NotNull(result);
    }

    [Fact]
    public void Read_EnabledButUntrustedProxy_ReturnsNull()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["127.0.0.1"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.Parse("10.0.0.1"), Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Read_EnabledAndNullRemoteIp_ReturnsNull()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["127.0.0.1"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(remoteIp: null, Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Read_Disabled_ReturnsNullRegardlessOfIp()
    {
        var reader = CreateReader(enabled: false, trustedProxies: ["127.0.0.1"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.Loopback, Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Read_EnabledAndIpv4MappedIpv6TrustedProxy_ReturnsCertificate()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["172.22.0.0/16"]);
        using var cert = CreateCertificate();
        var mappedAddress = IPAddress.Parse("172.22.0.3").MapToIPv6();
        var ctx = CreateContext(mappedAddress, Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.NotNull(result);
    }

    [Fact]
    public void Read_EnabledAndIpInCidrRange_ReturnsCertificate()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["172.22.0.0/16"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.Parse("172.22.0.3"), Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.NotNull(result);
    }

    [Fact]
    public void Read_EnabledAndIpOutsideCidrRange_ReturnsNull()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["172.22.0.0/16"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.Parse("10.0.0.1"), Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Read_TlsClientCertificateOnConnection_ReturnsCertWithoutIpCheck()
    {
        var reader = CreateReader(enabled: false, trustedProxies: []);
        using var cert = CreateCertificate();
        var ctx = new DefaultHttpContext();
        ctx.Connection.ClientCertificate = cert;

        var result = reader.Read(ctx);

        Assert.NotNull(result);
    }

    [Fact]
    public void Read_EnabledTrustedProxyIpv6Loopback_ReturnsCertificate()
    {
        var reader = CreateReader(enabled: true, trustedProxies: ["::1"]);
        using var cert = CreateCertificate();
        var ctx = CreateContext(IPAddress.IPv6Loopback, Convert.ToBase64String(cert.RawData));

        var result = reader.Read(ctx);

        Assert.NotNull(result);
    }

    private static ClientCertificateReader CreateReader(bool enabled, IReadOnlyList<string> trustedProxies)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(BuildSettings(enabled, trustedProxies))
            .Build();
        return new ClientCertificateReader(config);
    }

    private static IEnumerable<KeyValuePair<string, string?>> BuildSettings(
        bool enabled, IReadOnlyList<string> trustedProxies)
    {
        yield return new("AuthorizationServer:EnableForwardedClientCertificate", enabled.ToString());
        yield return new("AuthorizationServer:ForwardedClientCertificateHeader", "X-Client-Cert");
        for (var i = 0; i < trustedProxies.Count; i++)
        {
            yield return new($"AuthorizationServer:TrustedProxies:{i}", trustedProxies[i]);
        }
    }

    private static DefaultHttpContext CreateContext(IPAddress? remoteIp, string headerValue)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = remoteIp;
        ctx.Request.Headers["X-Client-Cert"] = headerValue;
        return ctx;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    }
}
