using Backend.Idp.Domain.Entities;

namespace Backend.Idp.Domain.Repositories;

public interface IGlobalRoleRepository
{
    Task<GlobalRole?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GlobalRole?> GetByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalRole>> ListAsync(string? valueFilter, CancellationToken cancellationToken = default);

    Task<bool> ExistsByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveByValueAsync(string value, CancellationToken cancellationToken = default);

    Task AddAsync(GlobalRole role, CancellationToken cancellationToken = default);

    Task UpdateAsync(GlobalRole role, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
