using Backend.Domain.Entities;

namespace Backend.Domain.Repositories;

public interface IMachineClientRepository
{
    Task<MachineClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MachineClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MachineClient>> ListAsync(string? clientIdFilter, CancellationToken cancellationToken = default);

    Task<bool> ExistsByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    Task AddAsync(MachineClient client, CancellationToken cancellationToken = default);

    Task UpdateAsync(MachineClient client, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
