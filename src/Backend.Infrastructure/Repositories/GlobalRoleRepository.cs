using Backend.Domain.Entities;
using Backend.Domain.Repositories;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Repositories;

public sealed class GlobalRoleRepository : IGlobalRoleRepository
{
    private readonly AppDbContext _context;

    public GlobalRoleRepository(AppDbContext context)
    {
        this._context = context;
    }

    public async Task<GlobalRole?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalRoles
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GlobalRole?> GetByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalRoles
            .FirstOrDefaultAsync(r => r.Value == value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GlobalRole>> ListAsync(string? valueFilter, CancellationToken cancellationToken = default)
    {
        var query = this._context.GlobalRoles.AsQueryable();

        if (valueFilter is not null)
        {
            query = query.Where(r => r.Value == valueFilter);
        }

        return await query.OrderBy(r => r.Value).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalRoles
            .AnyAsync(r => r.Value == value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ExistsActiveByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalRoles
            .AnyAsync(r => r.Value == value && r.Active, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(GlobalRole role, CancellationToken cancellationToken = default)
    {
        this._context.GlobalRoles.Add(role);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GlobalRole role, CancellationToken cancellationToken = default)
    {
        this._context.GlobalRoles.Update(role);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await this._context.GlobalRoles
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (role is not null)
        {
            this._context.GlobalRoles.Remove(role);
            await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
