using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;

namespace WorkloadProject2025.Services
{
    public class DepartmentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public DepartmentService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        }

        public async Task<Department> AddAsync(Department department, CancellationToken cancellationToken = default)
        {
            if (department == null) throw new ArgumentNullException(nameof(department));
            if (string.IsNullOrWhiteSpace(department.Name)) throw new Exception("Department must have a name");

            await using var db = _dbFactory.CreateDbContext();
            db.Departments.Add(department);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return department;
        }

        public async Task<List<Department>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Departments
                .AsNoTracking()
                .OrderBy(d => d.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Return distinct department names trimmed (so "Accounting " and "Accounting" collapse)
        public async Task<string[]> GetDistinctDepartmentNamesAsync(CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();

            // Trim in SQL via EF Core translation, then distinct
            return await db.Departments
                .AsNoTracking()
                .Where(d => !string.IsNullOrEmpty(d.Name))
                .Select(d => d.Name!.Trim())
                .Distinct()
                .OrderBy(n => n)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Return all Department rows matching a (trimmed) name, eager-loading Programs->Courses where possible.
        /// Falls back to safe raw-selects if EF includes fail due to schema mismatches.
        /// </summary>
        public async Task<List<Department>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<Department>();
            var trimmedName = name.Trim();

            await using var db = _dbFactory.CreateDbContext();

            try
            {
                // Preferred EF path - loads programs and courses
                var rows = await db.Departments
                    .AsNoTracking()
                    .Where(d => d.Name != null && d.Name.Trim() == trimmedName)
                    .Include(d => d.ProgramsOfStudy!)
                        .ThenInclude(p => p.Courses!)
                    .AsSplitQuery()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return rows ?? new List<Department>();
            }
            catch (Exception ex)
            {
                var msg = ex?.Message ?? string.Empty;
                if (!(msg.Contains("Invalid column", StringComparison.OrdinalIgnoreCase)
                      || msg.Contains("TermId", StringComparison.OrdinalIgnoreCase)
                      || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    // Unknown error: rethrow
                    throw;
                }

                // Fallback: raw read using only safe columns and matching trimmed name
                var results = new List<Department>();
                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    // 1) Read departments that match trimmed name
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Name, SchoolId FROM dbo.Departments WHERE LTRIM(RTRIM(Name)) = @name";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@name";
                        p.Value = trimmedName;
                        cmd.Parameters.Add(p);

                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var dept = new Department
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                Name = reader["Name"] as string ?? string.Empty
                            };
                            try { if (reader["SchoolId"] != DBNull.Value) dept.GetType().GetProperty("SchoolId")?.SetValue(dept, Convert.ToInt32(reader["SchoolId"])); } catch { }
                            dept.ProgramsOfStudy = new List<ProgramOfStudy>();
                            results.Add(dept);
                        }
                    }

                    if (!results.Any()) return results;

                    // 2) Read programs for all found departments
                    var deptIds = results.Select(d => d.Id).ToArray();
                    var paramList = string.Join(", ", deptIds.Select((_, i) => $"@d{i}"));
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy WHERE DepartmentId IN ({paramList})";
                        for (int i = 0; i < deptIds.Length; i++)
                        {
                            var par = cmd.CreateParameter();
                            par.ParameterName = $"@d{i}";
                            par.Value = deptIds[i];
                            cmd.Parameters.Add(par);
                        }

                        var programMap = new Dictionary<int, List<ProgramOfStudy>>();
                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                            var pname = reader["Name"] as string ?? $"Program {pid}";
                            var departmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0;

                            var prog = new ProgramOfStudy
                            {
                                Id = pid,
                                Name = pname,
                                DepartmentId = departmentId,
                                Courses = new List<Course>()
                            };

                            if (!programMap.TryGetValue(departmentId, out var list))
                            {
                                list = new List<ProgramOfStudy>();
                                programMap[departmentId] = list;
                            }
                            list.Add(prog);
                        }

                        // attach programs to departments
                        foreach (var dept in results)
                        {
                            if (programMap.TryGetValue(dept.Id, out var list))
                                dept.ProgramsOfStudy = list;
                            else
                                dept.ProgramsOfStudy = new List<ProgramOfStudy>();
                        }
                    }

                    // 3) Read courses for those programs (if any)
                    var allProgramIds = results.SelectMany(d => d.ProgramsOfStudy!).Select(p => p.Id).Distinct().ToArray();
                    if (allProgramIds.Any())
                    {
                        var paramList2 = string.Join(", ", allProgramIds.Select((_, i) => $"@p{i}"));
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT Id, Name, Hours, ProgramId FROM dbo.Courses WHERE ProgramId IN ({paramList2})";
                        for (int i = 0; i < allProgramIds.Length; i++)
                        {
                            var par = cmd.CreateParameter();
                            par.ParameterName = $"@p{i}";
                            par.Value = allProgramIds[i];
                            cmd.Parameters.Add(par);
                        }

                        var courseMap = new Dictionary<int, List<Course>>();
                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var cid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                            var cname = reader["Name"] as string ?? $"Course {cid}";
                            var chours = reader["Hours"] != DBNull.Value ? Convert.ToInt32(reader["Hours"]) : 0;
                            var progId = reader["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader["ProgramId"]) : 0;

                            var course = new Course
                            {
                                Id = cid,
                                Name = cname,
                                Hours = chours,
                                ProgramId = progId
                            };

                            if (!courseMap.TryGetValue(progId, out var list))
                            {
                                list = new List<Course>();
                                courseMap[progId] = list;
                            }
                            courseMap[progId].Add(course);
                        }

                        // attach courses to each program in each department
                        foreach (var dept in results)
                        {
                            foreach (var prog in dept.ProgramsOfStudy!)
                            {
                                if (courseMap.TryGetValue(prog.Id, out var clist))
                                    prog.Courses = clist;
                                else
                                    prog.Courses = new List<Course>();
                            }
                        }
                    }
                }
                finally
                {
                    if (conn.State == ConnectionState.Open) conn.Close();
                }

                return results;
            }
        }

        public async Task<Department?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();

            try
            {
                // Preferred path: eager load Programs -> Courses using EF
                return await db.Departments
                    .AsNoTracking()
                    .Where(d => d.Id == id)
                    .Include(d => d.ProgramsOfStudy!)
                        .ThenInclude(p => p.Courses!)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var msg = ex?.Message ?? string.Empty;
                if (!(msg.Contains("Invalid column", StringComparison.OrdinalIgnoreCase)
                      || msg.Contains("TermId", StringComparison.OrdinalIgnoreCase)
                      || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    throw;
                }

                // fallback: reuse GetByNameAsync logic by reading row-by-row (simple select)
                // We will build a department via safe selects (similar to earlier fallback),
                // but to keep code minimal here, call GetByNameAsync on the department's Name.
                // First read the department name:
                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Name FROM dbo.Departments WHERE Id = @id";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@id";
                    p.Value = id;
                    cmd.Parameters.Add(p);

                    var nameObj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    if (nameObj == null || nameObj == DBNull.Value) return null;
                    var name = (nameObj as string ?? string.Empty).Trim();
                    var list = await GetByNameAsync(name, cancellationToken).ConfigureAwait(false);
                    return list.FirstOrDefault();
                }
                finally
                {
                    if (conn.State == ConnectionState.Open) conn.Close();
                }
            }
        }

        public async Task<bool> DeleteAsync(Department department, CancellationToken cancellationToken = default)
        {
            if (department == null) return false;

            await using var db = _dbFactory.CreateDbContext();
            try
            {
                db.Departments.Remove(department);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}