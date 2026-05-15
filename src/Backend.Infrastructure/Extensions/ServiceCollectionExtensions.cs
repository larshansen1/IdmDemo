using Backend.Domain.Repositories;
using Backend.Domain.Services;
using Backend.Infrastructure.Certificates;
using Backend.Infrastructure.Persistence;
using Backend.Infrastructure.Repositories;
using Backend.Infrastructure.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        return AddInfrastructure(
            services,
            connectionString,
            Path.Combine(AppContext.BaseDirectory, "signing-key.json"),
            Path.Combine(AppContext.BaseDirectory, "certificate-authority.json"));
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, string signingKeyPath)
    {
        return AddInfrastructure(
            services,
            connectionString,
            signingKeyPath,
            Path.Combine(AppContext.BaseDirectory, "certificate-authority.json"));
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string signingKeyPath,
        string certificateAuthorityPath)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMachineClientRepository, MachineClientRepository>();
        services.AddScoped<IMachineClientCertificateRepository, MachineClientCertificateRepository>();
        services.AddScoped<IGlobalRoleRepository, GlobalRoleRepository>();
        services.AddScoped<IGlobalScopeRepository, GlobalScopeRepository>();
        services.AddSingleton<IJwtSigningKeyStore>(_ => new LocalJwtSigningKeyStore(signingKeyPath));
        services.AddSingleton<ILocalCertificateAuthority>(_ => new LocalDevelopmentCertificateAuthority(certificateAuthorityPath));

        return services;
    }

    public static async Task ApplyMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
}
