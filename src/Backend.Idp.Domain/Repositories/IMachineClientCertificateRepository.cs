using Backend.Idp.Domain.Entities;

namespace Backend.Idp.Domain.Repositories;

public interface IMachineClientCertificateRepository
{
    Task<MachineClientCertificate?> GetByIdAsync(
        Guid machineClientId,
        Guid certificateId,
        CancellationToken cancellationToken = default);

    Task<MachineClientCertificate?> GetByThumbprintAsync(
        Guid machineClientId,
        string thumbprintSha256,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MachineClientCertificate>> ListAsync(
        Guid machineClientId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByThumbprintAsync(
        string thumbprintSha256,
        CancellationToken cancellationToken = default);

    Task AddAsync(MachineClientCertificate certificate, CancellationToken cancellationToken = default);

    Task UpdateAsync(MachineClientCertificate certificate, CancellationToken cancellationToken = default);
}
