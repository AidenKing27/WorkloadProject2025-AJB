// Services/SchoolService.cs
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services
{
    public class SchoolService : ISchoolService
    {
        private readonly ApplicationDbContext _context;

        public SchoolService(ApplicationDbContext context)
        {
            _context = context;
        }
        // Add this inside your existing SchoolService class
        public class DeptHours
        {
            public int DepartmentId { get; set; }
            public string? DepartmentName { get; set; }
            public decimal TotalHours { get; set; }
            public int ProgramCount { get; set; }
        }

        /// <summary>
        /// Compute department totals for a school from Courses -> Programs -> Departments.
        /// Paste this into the existing SchoolService class (no extra files required).
        /// </summary>
        public async Task<List<DeptHours>> GetDepartmentTotalsAsync(int schoolId, CancellationToken cancellationToken = default)
        {
            // 1) departments in the school
            var departments = await _context.Departments
                .AsNoTracking()
                .Where(d => d.SchoolId == schoolId)
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(cancellationToken);

            if (!departments.Any())
                return new List<DeptHours>();

            var deptIds = departments.Select(d => d.Id).ToList();

            // 2) programs under those departments
            var programs = await _context.ProgramsOfStudy
                .AsNoTracking()
                .Where(p => p.DepartmentId != null && deptIds.Contains(p.DepartmentId.Value))
                .Select(p => new { p.Id, DepartmentId = p.DepartmentId.Value })
                .ToListAsync(cancellationToken);

            var deptProgramMap = programs
                .GroupBy(p => p.DepartmentId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            var programIds = programs.Select(p => p.Id).ToList();

            // 3) sum course hours per program (grouped in DB)
            var courseSumsByProgram = new Dictionary<int, decimal>();
            if (programIds.Any())
            {
                courseSumsByProgram = await _context.Courses
                    .AsNoTracking()
                    .Where(c => c.ProgramId != null && programIds.Contains(c.ProgramId.Value))
                    .GroupBy(c => c.ProgramId!.Value)
                    .Select(g => new { ProgramId = g.Key, Total = g.Sum(c => (decimal?)c.Hours) ?? 0M })
                    .ToDictionaryAsync(x => x.ProgramId, x => x.Total, cancellationToken);
            }

            // 4) aggregate program totals into department totals
            var results = new List<DeptHours>(departments.Count);
            foreach (var d in departments)
            {
                decimal deptTotal = 0M;
                int progCount = 0;
                if (deptProgramMap.TryGetValue(d.Id, out var progList))
                {
                    progCount = progList.Count;
                    foreach (var pid in progList)
                    {
                        if (courseSumsByProgram.TryGetValue(pid, out var pTotal))
                            deptTotal += pTotal;
                    }
                }

                results.Add(new DeptHours
                {
                    DepartmentId = d.Id,
                    DepartmentName = d.Name,
                    TotalHours = deptTotal,
                    ProgramCount = progCount
                });
            }

            return results.OrderByDescending(r => r.TotalHours).ToList();
        }
        public async Task<List<School>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Schools
                .AsNoTracking()
                .Include(s => s.Departments)
                // If you want program counts per department, include ProgramsOfStudy too:
                // .ThenInclude(d => d.ProgramsOfStudy)
                .ToListAsync(cancellationToken);
        }
        public class ProgramHours
        {
            public int ProgramId { get; set; }
            public string? ProgramName { get; set; }
            public decimal TotalHours { get; set; }
        }

        /// <summary>
        /// Returns program totals (sum of Courses.Hours) for a given department.
        /// Includes programs with zero hours.
        /// </summary>
        public async Task<List<ProgramHours>> GetProgramTotalsByDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
        {
            var q =
                from p in _context.ProgramsOfStudy.AsNoTracking()
                where p.DepartmentId == departmentId
                join c in _context.Courses.AsNoTracking() on p.Id equals c.ProgramId into pc
                select new
                {
                    p.Id,
                    p.Name,
                    Total = pc.Sum(x => (decimal?)x.Hours) ?? 0M
                };

            var list = await q.ToListAsync(cancellationToken);

            return list
                .Select(x => new ProgramHours
                {
                    ProgramId = x.Id,
                    ProgramName = x.Name,
                    TotalHours = x.Total
                })
                .OrderByDescending(p => p.TotalHours)
                .ToList();
        }
        // SchoolService.cs - GetByIdAsync
        public async Task<School?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _context.Schools
                .AsNoTracking()
                .Include(s => s.Departments)
                    .ThenInclude(d => d.ProgramsOfStudy)   // <-- ensure programs are loaded
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task<School> AddAsync(School school, CancellationToken cancellationToken = default)
        {
            if (school == null) throw new ArgumentNullException(nameof(school));
            if (string.IsNullOrWhiteSpace(school.Name)) throw new ArgumentException("School must have a name", nameof(school));

            _context.Schools.Add(school);
            await _context.SaveChangesAsync(cancellationToken);
            return school;
        }

        public async Task<bool> DeleteAsync(School school, CancellationToken cancellationToken = default)
        {
            try
            {
                _context.Schools.Remove(school);
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}