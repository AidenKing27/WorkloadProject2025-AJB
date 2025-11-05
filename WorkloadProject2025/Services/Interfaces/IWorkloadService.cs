using WorkloadProject2025.Data.Models;

namespace WorkloadProject2025.Services.Interfaces;

public interface IWorkloadService
{
    Task<Workload> AddAsync(Workload workload, CancellationToken cancellationToken = default);
    Task<List<Workload>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Workload?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Workload workload, CancellationToken cancellationToken = default);
}
