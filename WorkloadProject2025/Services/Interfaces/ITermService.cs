using WorkloadProject2025.Data.Models;

namespace WorkloadProject2025.Services.Interfaces;

public interface ITermService
{
    Task<Term> AddAsync(Term term, CancellationToken cancellationToken = default);
    Task<List<Term>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Term?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Term term, CancellationToken cancellationToken = default);
}
