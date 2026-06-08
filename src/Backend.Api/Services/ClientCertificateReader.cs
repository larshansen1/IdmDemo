using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Backend.Api.Services;

public sealed class ClientCertificateReader : IClientCertificateReader
{
    private const string _defaultForwardedCertificateHeader = "X-Client-Cert";

    private readonly bool _enableForwardedCertificate;
    private readonly string _forwardedCertificateHeader;
    private readonly HashSet<IPAddress> _trustedProxies;

    public ClientCertificateReader(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this._enableForwardedCertificate = configuration.GetValue<bool>(
            "AuthorizationServer:EnableForwardedClientCertificate");
        this._forwardedCertificateHeader = configuration["AuthorizationServer:ForwardedClientCertificateHeader"]
            ?? _defaultForwardedCertificateHeader;

        var proxyStrings = configuration
            .GetSection("AuthorizationServer:TrustedProxies")
            .Get<string[]>() ?? [];
        this._trustedProxies = proxyStrings.Select(IPAddress.Parse).ToHashSet();
    }

    public X509Certificate2? Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Connection.ClientCertificate is not null)
        {
            return context.Connection.ClientCertificate;
        }

        if (!this._enableForwardedCertificate)
        {
            return null;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return null;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        if (!this._trustedProxies.Contains(remoteIp))
        {
            return null;
        }

        if (!context.Request.Headers.TryGetValue(this._forwardedCertificateHeader, out var headerValue))
        {
            return null;
        }

        return ParseForwardedCertificate(headerValue.ToString());
    }

    private static X509Certificate2? ParseForwardedCertificate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = Uri.UnescapeDataString(value);

        if (value.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        {
            return X509Certificate2.CreateFromPem(value);
        }

        return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(value));
    }
}
