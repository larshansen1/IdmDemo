using Backend.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IMachineClientService, MachineClientService>();

        return services;
    }
}
