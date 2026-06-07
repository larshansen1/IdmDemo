using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Repositories;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        this._context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await this._context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<User>> ListAsync(string? userNameFilter, CancellationToken cancellationToken = default)
    {
        var query = this._context.Users.AsQueryable();

        if (userNameFilter is not null)
        {
            query = query.Where(u => u.UserName == userNameFilter);
        }

        return await query.OrderBy(u => u.UserName).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        return await this._context.Users
            .AnyAsync(u => u.UserName == userName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        this._context.Users.Add(user);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        this._context.Users.Update(user);
        await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await this._context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
        {
            this._context.Users.Remove(user);
            await this._context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
