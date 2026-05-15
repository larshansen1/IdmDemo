using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(new AuthorizationServerOptions());
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IMachineClientService, MachineClientService>();
        services.AddScoped<IMachineClientCertificateService, MachineClientCertificateService>();
        services.AddSingleton<IDpopReplayCache, InMemoryDpopReplayCache>();
        services.AddScoped<IDpopProofValidator, DpopProofValidator>();
        services.AddScoped<IAccessTokenValidator, AccessTokenValidator>();
        services.AddScoped<IDpopBoundAccessTokenValidator, DpopBoundAccessTokenValidator>();
        services.AddScoped<IAuthorizationServerService, AuthorizationServerService>();

        return services;
    }

    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        AuthorizationServerOptions authorizationServerOptions)
    {
        ArgumentNullException.ThrowIfNull(authorizationServerOptions);

        services.AddSingleton(authorizationServerOptions);
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IMachineClientService, MachineClientService>();
        services.AddScoped<IMachineClientCertificateService, MachineClientCertificateService>();
        services.AddSingleton<IDpopReplayCache, InMemoryDpopReplayCache>();
        services.AddScoped<IDpopProofValidator, DpopProofValidator>();
        services.AddScoped<IAccessTokenValidator, AccessTokenValidator>();
        services.AddScoped<IDpopBoundAccessTokenValidator, DpopBoundAccessTokenValidator>();
        services.AddScoped<IAuthorizationServerService, AuthorizationServerService>();

        return services;
    }
}
