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
        services.AddScoped<IAuthorizationServerService, AuthorizationServerService>();

        return services;
    }
}
