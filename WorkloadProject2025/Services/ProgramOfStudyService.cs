using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class ProgramOfStudyService : IProgramOfStudyService
{
    ApplicationDbContext _context;

    public ProgramOfStudyService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<ProgramOfStudy> AddAsync(ProgramOfStudy program, CancellationToken cancellationToken = default)
    {
        if (program == null)
            throw new ArgumentNullException();

        if (program.Name.Trim() == "")
            throw new Exception("Program must have a name");

        _context.ProgramsOfStudy.Add(program);
        await _context.SaveChangesAsync();

        return program;
    }

    public Task<List<ProgramOfStudy>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.ProgramsOfStudy.ToListAsync(cancellationToken);
    }

    public Task<ProgramOfStudy?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.ProgramsOfStudy.FirstOrDefaultAsync(program => program.Id == id);
    }

    public async Task<bool> DeleteAsync(ProgramOfStudy program, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.ProgramsOfStudy.Remove(program);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
