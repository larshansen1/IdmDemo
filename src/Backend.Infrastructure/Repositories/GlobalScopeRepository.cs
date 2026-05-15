using Backend.Domain.Entities;
using Backend.Domain.Repositories;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Repositories;

public sealed class GlobalScopeRepository : IGlobalScopeRepository
{
    private readonly AppDbContext _context;

    public GlobalScopeRepository(AppDbContext context)
    {
        this._context = context;
    }

    public async Task<GlobalScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalScopes
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GlobalScope?> GetByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalScopes
            .FirstOrDefaultAsync(s => s.Value == value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GlobalScope>> ListAsync(string? valueFilter, CancellationToken cancellationToken = default)
    {
        var query = this._context.GlobalScopes.AsQueryable();

        if (valueFilter is not null)
        {
            query = query.Where(s => s.Value == valueFilter);
        }

        return await query.OrderBy(s => s.Value).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalScopes
            .AnyAsync(s => s.Value == value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ExistsActiveByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await this._context.GlobalScopes
            .AnyAsync(s => s.Value == value && s.Active, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(GlobalScope scope, CancellationToken cancellationToken = default)
    {
        this._context.GlobalScopes.Add(scope);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GlobalScope scope, CancellationToken cancellationToken = default)
    {
        this._context.GlobalScopes.Update(scope);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await this._context.GlobalScopes
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (scope is not null)
        {
            this._context.GlobalScopes.Remove(scope);
            await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
