using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services;

public class CourseService : ICourseService
{
    ApplicationDbContext _context;
    
    public CourseService(ApplicationDbContext db)
    {
        _context = db;
    }
    
    public async Task<Course> AddAsync(Course course, CancellationToken cancellationToken = default)
    {
        if (course == null)
            throw new ArgumentNullException();
            
        if (course.Name.Trim() == "")
            throw new Exception("Course must have a name");
            
        _context.Courses.Add(course);
        await _context.SaveChangesAsync();

        return course;
    }
    
    public Task<List<Course>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _context.Courses.ToListAsync(cancellationToken);
    }
    
    public Task<Course?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Courses.FirstOrDefaultAsync(course => course.Id == id);
    }

    public async Task<bool> DeleteAsync(Course course, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Courses.Remove(course);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}