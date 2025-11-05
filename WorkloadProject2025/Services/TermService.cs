using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class TermService : ITermService
{
    ApplicationDbContext _context;

    public TermService(ApplicationDbContext db)
    {
        _context = db;
    }

    public async Task<Term> AddAsync(Term term, CancellationToken cancellationToken = default)
    {
        if (term == null)
            throw new ArgumentNullException();

        if (term.Name.Trim() == "")
            throw new Exception("Term must have a name");

        if (term.EndDate <= term.StartDate)
            throw new ArgumentException("End date must be after start date");

        _context.Terms.Add(term);
        await _context.SaveChangesAsync();

        return term;
    }   

    public Task<List<Term>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Terms.ToListAsync(cancellationToken);
    }

    public Task<Term?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Terms.FirstOrDefaultAsync(term => term.Id == id);
    }

    public async Task<bool> DeleteAsync(Term term, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Terms.Remove(term);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
