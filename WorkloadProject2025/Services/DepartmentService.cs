using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class DepartmentService : IDepartmentService
{
    ApplicationDbContext _context;

    public DepartmentService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<Department> AddAsync(Department department, CancellationToken cancellationToken = default)
    {
        if (department == null)
            throw new ArgumentNullException();

        if (department.Name.Trim() == "")
            throw new Exception("Department must have a name");

        _context.Departments.Add(department);
        await _context.SaveChangesAsync();

        return department;
    }

    public Task<List<Department>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Departments.ToListAsync(cancellationToken);
    }

    public Task<Department?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Departments.FirstOrDefaultAsync(department => department.Id == id);
    }

    public async Task<bool> DeleteAsync(Department department, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Departments.Remove(department);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
