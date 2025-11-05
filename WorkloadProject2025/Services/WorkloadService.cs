using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class WorkloadService : IWorkloadService
{
    ApplicationDbContext _context;

    public WorkloadService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<Workload> AddAsync(Workload workload, CancellationToken cancellationToken = default)
    {
        if (workload == null)
            throw new ArgumentNullException();

        _context.Workloads.Add(workload);
        await _context.SaveChangesAsync();

        return workload;
    }

    public async Task<Workload> UpdateAsync(Workload workload, CancellationToken cancellationToken = default)
    {
        if (workload == null)
            throw new ArgumentNullException();

        _context.Workloads.Update(workload);
        await _context.SaveChangesAsync();

        return workload;
    }

    public Task<List<Workload>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Workloads
            .Include(x => x.Course)
            .ThenInclude(x => x.Term)
            .Include(x => x.FacultyMember)
            .ThenInclude(x => x.WorkloadCategory)
            .ToListAsync(cancellationToken);
    }

    public Task<Workload?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Workloads.FirstOrDefaultAsync(workload => workload.Id == id);
    }

    public async Task<bool> DeleteAsync(Workload workload, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Workloads.Remove(workload);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
