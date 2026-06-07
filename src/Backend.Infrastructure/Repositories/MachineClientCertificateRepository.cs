using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Repositories;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Repositories;

public sealed class MachineClientCertificateRepository : IMachineClientCertificateRepository
{
    private readonly AppDbContext _context;

    public MachineClientCertificateRepository(AppDbContext context)
    {
        this._context = context;
    }

    public async Task<MachineClientCertificate?> GetByIdAsync(
        Guid machineClientId,
        Guid certificateId,
        CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClientCertificates
            .FirstOrDefaultAsync(
                c => c.MachineClientId == machineClientId && c.Id == certificateId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MachineClientCertificate?> GetByThumbprintAsync(
        Guid machineClientId,
        string thumbprintSha256,
        CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClientCertificates
            .FirstOrDefaultAsync(
                c => c.MachineClientId == machineClientId && c.ThumbprintSha256 == thumbprintSha256,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MachineClientCertificate>> ListAsync(
        Guid machineClientId,
        CancellationToken cancellationToken = default)
    {
        var certificates = await this._context.MachineClientCertificates
            .Where(c => c.MachineClientId == machineClientId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return certificates
            .OrderBy(c => c.ExpiresAt)
            .ThenBy(c => c.Id)
            .ToList();
    }

    public async Task<bool> ExistsByThumbprintAsync(
        string thumbprintSha256,
        CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClientCertificates
            .AnyAsync(c => c.ThumbprintSha256 == thumbprintSha256, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(MachineClientCertificate certificate, CancellationToken cancellationToken = default)
    {
        this._context.MachineClientCertificates.Add(certificate);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(MachineClientCertificate certificate, CancellationToken cancellationToken = default)
    {
        this._context.MachineClientCertificates.Update(certificate);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
