namespace Backend.Api.Composition;

public sealed record ApiBoundaryMetadata(string Name)
{
    public static readonly ApiBoundaryMetadata AuthorizationServer = new("AuthorizationServer");
    public static readonly ApiBoundaryMetadata IdpAdminApi = new("IdpAdminApi");
}
