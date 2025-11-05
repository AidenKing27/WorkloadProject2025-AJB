using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class SchoolService : ISchoolService
{
    ApplicationDbContext _context;

    public SchoolService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<School> AddAsync(School school, CancellationToken cancellationToken = default)
    {
        if (school == null)
            throw new ArgumentNullException();

        if (school.Name.Trim() == "")
            throw new Exception("School must have a name");

        _context.Schools.Add(school);
        await _context.SaveChangesAsync();

        return school;
    }

    public Task<List<School>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Schools.ToListAsync(cancellationToken);
    }

    public Task<School?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Schools.FirstOrDefaultAsync(school => school.Id == id);
    }

    public async Task<bool> DeleteAsync(School school, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Schools.Remove(school);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
