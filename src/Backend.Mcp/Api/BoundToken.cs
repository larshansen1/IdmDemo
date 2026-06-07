using System.Security.Cryptography;

namespace Backend.Mcp.Api;

public sealed record BoundToken(string AccessToken, RSA DpopKey);
