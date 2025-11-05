using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class WorkloadCategoryService : IWorkloadCategoryService
{
    ApplicationDbContext _context;

    public WorkloadCategoryService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<WorkloadCategory> AddAsync(WorkloadCategory workloadCategory, CancellationToken cancellationToken = default)
    {
        if (workloadCategory == null)
            throw new ArgumentNullException();

        if (workloadCategory.MinimumHours == 0)
            throw new Exception("Minimum Hours Must have an Amount");

        if (workloadCategory.MaximumHours < workloadCategory.MinimumHours)
            throw new Exception("Maximum Hours Must have an Amount higher than Minimum Hours");

        if (workloadCategory.EndDate <= workloadCategory.StartDate)
            throw new Exception("The End Date must be After the Start Date");

        _context.WorkloadCategories.Add(workloadCategory);
        await _context.SaveChangesAsync();

        return workloadCategory;
    }

    public Task<List<WorkloadCategory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.WorkloadCategories.ToListAsync(cancellationToken);
    }

    public Task<WorkloadCategory?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.WorkloadCategories.FirstOrDefaultAsync(workloadCategory => workloadCategory.Id == id);
    }

    public async Task<bool> DeleteAsync(WorkloadCategory workloadCategory, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.WorkloadCategories.Remove(workloadCategory);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
