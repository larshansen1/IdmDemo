using Backend.As.Domain.Services;
using Backend.Idp.Domain.Repositories;
using Backend.Idp.Domain.Services;
using Backend.Infrastructure.Certificates;
using Backend.Infrastructure.Persistence;
using Backend.Infrastructure.Repositories;
using Backend.Infrastructure.Signing;
using Microsoft.AspNetCore.DataProtection;
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

        services.AddDataProtection();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMachineClientRepository, MachineClientRepository>();
        services.AddScoped<IMachineClientCertificateRepository, MachineClientCertificateRepository>();
        services.AddScoped<IGlobalRoleRepository, GlobalRoleRepository>();
        services.AddScoped<IGlobalScopeRepository, GlobalScopeRepository>();
        services.AddSingleton<IJwtSigningKeyStore>(sp =>
        {
            var protector = sp.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Backend.Infrastructure.Signing.LocalJwtSigningKeyStore");
            return new LocalJwtSigningKeyStore(signingKeyPath, protector);
        });
        services.AddSingleton<ILocalCertificateAuthority>(sp =>
        {
            var protector = sp.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Backend.Infrastructure.Certificates.LocalDevelopmentCertificateAuthority");
            return new LocalDevelopmentCertificateAuthority(certificateAuthorityPath, protector);
        });

        return services;
    }

    public static async Task ApplyMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
}
