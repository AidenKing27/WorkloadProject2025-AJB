using WorkloadProject2025.Data.Models;

namespace WorkloadProject2025.Services.Interfaces;

public interface IProgramOfStudyService
{
    Task<ProgramOfStudy> AddAsync(ProgramOfStudy program, CancellationToken cancellationToken = default);
    Task<List<ProgramOfStudy>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProgramOfStudy?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(ProgramOfStudy program, CancellationToken cancellationToken = default);
}
