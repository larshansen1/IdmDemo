using Backend.Idp.Domain.Entities;

namespace Backend.Idp.Domain.Repositories;

public interface IGlobalScopeRepository
{
    Task<GlobalScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GlobalScope?> GetByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalScope>> ListAsync(string? valueFilter, CancellationToken cancellationToken = default);

    Task<bool> ExistsByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveByValueAsync(string value, CancellationToken cancellationToken = default);

    Task AddAsync(GlobalScope scope, CancellationToken cancellationToken = default);

    Task UpdateAsync(GlobalScope scope, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
