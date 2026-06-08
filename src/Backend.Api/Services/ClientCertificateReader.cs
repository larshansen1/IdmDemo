using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Backend.Api.Services;

public sealed class ClientCertificateReader : IClientCertificateReader
{
    private const string _defaultForwardedCertificateHeader = "X-Client-Cert";

    private readonly bool _enableForwardedCertificate;
    private readonly string _forwardedCertificateHeader;
    private readonly List<IPNetwork> _trustedNetworks;

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
        this._trustedNetworks = proxyStrings.Select(ParseNetwork).ToList();
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

        if (!this._trustedNetworks.Exists(n => n.Contains(remoteIp)))
        {
            return null;
        }

        if (!context.Request.Headers.TryGetValue(this._forwardedCertificateHeader, out var headerValue))
        {
            return null;
        }

        return ParseForwardedCertificate(headerValue.ToString());
    }

    private static IPNetwork ParseNetwork(string entry)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.Parse(entry);
        }

        var ip = IPAddress.Parse(entry);
        var prefix = ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return new IPNetwork(ip, prefix);
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
