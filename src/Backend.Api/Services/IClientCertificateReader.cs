using System.Security.Cryptography.X509Certificates;

namespace Backend.Api.Services;

public interface IClientCertificateReader
{
    X509Certificate2? Read(HttpContext context);
}
