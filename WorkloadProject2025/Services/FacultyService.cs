using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class FacultyService : IFacultyService
{
    ApplicationDbContext _context;

    public FacultyService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<Faculty> AddAsync(Faculty faculty, CancellationToken cancellationToken = default)
    {
        if (faculty == null)
            throw new ArgumentNullException();

        if (faculty.Email.Trim() == "")
            throw new Exception("Email must be entered");

        _context.Faculty.Add(faculty);
        await _context.SaveChangesAsync();

        return faculty;
    }

    public Task<List<Faculty>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Faculty.Include(x => x.WorkloadCategory).ToListAsync(cancellationToken);
    }

    public Task<Faculty?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return _context.Faculty.FirstOrDefaultAsync(faculty => faculty.Email == email);
    }

    public async Task<bool> DeleteAsync(Faculty faculty, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Faculty.Remove(faculty);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
