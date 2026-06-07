using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Repositories;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Repositories;

public sealed class MachineClientRepository : IMachineClientRepository
{
    private readonly AppDbContext _context;

    public MachineClientRepository(AppDbContext context)
    {
        this._context = context;
    }

    public async Task<MachineClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClients
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MachineClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClients
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MachineClient>> ListAsync(string? clientIdFilter, CancellationToken cancellationToken = default)
    {
        var query = this._context.MachineClients.AsQueryable();

        if (clientIdFilter is not null)
        {
            query = query.Where(c => c.ClientId == clientIdFilter);
        }

        return await query.OrderBy(c => c.ClientId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return await this._context.MachineClients
            .AnyAsync(c => c.ClientId == clientId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(MachineClient client, CancellationToken cancellationToken = default)
    {
        this._context.MachineClients.Add(client);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(MachineClient client, CancellationToken cancellationToken = default)
    {
        this._context.MachineClients.Update(client);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await this._context.MachineClients
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (client is not null)
        {
            this._context.MachineClients.Remove(client);
            await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
