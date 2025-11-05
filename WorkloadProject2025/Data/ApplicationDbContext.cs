using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data.Models;

namespace WorkloadProject2025.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<School> Schools { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Faculty> Faculty { get; set; }
        public DbSet<Term> Terms { get; set; }
        public DbSet<WorkloadCategory> WorkloadCategories { get; set; }
        public DbSet<ProgramOfStudy> ProgramsOfStudy { get; set; }
        public DbSet<Workload> Workloads { get; set; }
    }
}
