using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkloadProject2025.Data;
using WorkloadProject2025.Data.Models;
using WorkloadProject2025.Services.Interfaces;

namespace WorkloadProject2025.Services
{
    /// <summary>
    /// ProgramOfStudyService with defensive fallbacks for schema drift (missing Term/TermId columns).
    /// Attempts EF Includes first; if that fails (e.g. "Invalid column name 'TermId'"), falls back to raw SQL.
    /// The raw SQL tries an extended query that includes TermId and TermName (LEFT JOIN Terms).
    /// If that extended raw query fails for any reason it falls back to a simpler raw query that only selects
    /// Id, Name, DepartmentId for programs and Id, Name, Hours, ProgramId for courses.
    /// </summary>
    public class ProgramOfStudyService : IProgramOfStudyService
    {
        private readonly ApplicationDbContext _context;

        public ProgramOfStudyService(ApplicationDbContext db)
        {
            _context = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<ProgramOfStudy> AddAsync(ProgramOfStudy program, CancellationToken cancellationToken = default)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (string.IsNullOrWhiteSpace(program.Name)) throw new Exception("Program must have a name");

            _context.ProgramsOfStudy.Add(program);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return program;
        }

        public async Task<List<ProgramOfStudy>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.ProgramsOfStudy
                    .AsNoTracking()
                    .Include(p => p.Courses)
                    .OrderBy(p => p.Name)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqlColumnError(ex))
            {
                // EF query failed likely due to schema drift; use raw fallback that tries to fetch term info too
                return await GetAllProgramsRawWithTermFallbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<ProgramOfStudy?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.ProgramsOfStudy
                    .AsNoTracking()
                    .Include(p => p.Courses)
                    .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqlColumnError(ex))
            {
                var results = await GetProgramsByIdsRawWithTermFallbackAsync(new[] { id }, cancellationToken).ConfigureAwait(false);
                return results.FirstOrDefault();
            }
        }

        public async Task<bool> DeleteAsync(ProgramOfStudy program, CancellationToken cancellationToken = default)
        {
            if (program == null) return false;
            try
            {
                _context.ProgramsOfStudy.Remove(program);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string[]> GetDistinctProgramNamesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.ProgramsOfStudy
                    .AsNoTracking()
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => p.Name!.Trim())
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqlColumnError(ex))
            {
                // Fallback: raw select distinct names
                await using var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT DISTINCT LTRIM(RTRIM(Name)) AS Name FROM dbo.ProgramsOfStudy WHERE Name IS NOT NULL ORDER BY Name";
                    var list = new List<string>();
                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        list.Add(reader["Name"] as string ?? string.Empty);
                    }
                    return list.ToArray();
                }
                finally
                {
                    if (conn.State == ConnectionState.Open) conn.Close();
                }
            }
        }

        public async Task<List<ProgramOfStudy>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<ProgramOfStudy>();
            var trimmed = name.Trim();

            try
            {
                return await _context.ProgramsOfStudy
                    .AsNoTracking()
                    .Where(p => p.Name != null && p.Name.Trim() == trimmed)
                    .Include(p => p.Courses)
                    .OrderBy(p => p.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqlColumnError(ex))
            {
                // Fallback: attempt raw read including term info; if that fails, fallback to simple raw read
                return await GetProgramsByNameRawWithTermFallbackAsync(trimmed, cancellationToken).ConfigureAwait(false);
            }
        }

        // ----- Raw SQL fallbacks with optional term columns -----

        private async Task<List<ProgramOfStudy>> GetAllProgramsRawWithTermFallbackAsync(CancellationToken cancellationToken = default)
        {
            // Try extended raw query (includes TermId/TermName). If that fails, fall back to simpler query.
            try
            {
                return await GetAllProgramsRawIncludeTermAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return await GetAllProgramsRawAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<List<ProgramOfStudy>> GetProgramsByIdsRawWithTermFallbackAsync(int[] ids, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetProgramsByIdsRawIncludeTermAsync(ids, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return await GetProgramsByIdsRawAsync(ids, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<List<ProgramOfStudy>> GetProgramsByNameRawWithTermFallbackAsync(string trimmedName, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetProgramsByNameRawIncludeTermAsync(trimmedName, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // fallback to simple raw read
                var programs = new List<ProgramOfStudy>();
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy WHERE LTRIM(RTRIM(Name)) = @name";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@name";
                    p.Value = trimmedName;
                    cmd.Parameters.Add(p);

                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    var ids = new List<int>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                        var pname = reader["Name"] as string ?? $"Program {pid}";
                        var prog = new ProgramOfStudy
                        {
                            Id = pid,
                            Name = pname,
                            DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                            Courses = new List<Course>()
                        };
                        programs.Add(prog);
                        ids.Add(pid);
                    }
                    reader.Close();

                    if (!ids.Any()) return programs;

                    var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = $"SELECT Id, Name, Hours, ProgramId FROM dbo.Courses WHERE ProgramId IN ({paramList})";
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var par = cmd2.CreateParameter();
                        par.ParameterName = $"@p{i}";
                        par.Value = ids[i];
                        cmd2.Parameters.Add(par);
                    }

                    using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    var courseMap = new Dictionary<int, List<Course>>();
                    while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                        var cname = reader2["Name"] as string ?? $"Course {cid}";
                        int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                        var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;

                        var course = new Course
                        {
                            Id = cid,
                            Name = cname,
                            Hours = chours,
                            ProgramId = progId
                        };

                        if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                        courseMap[progId].Add(course);
                    }

                    foreach (var prog in programs)
                    {
                        if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                        else prog.Courses = new List<Course>();
                    }

                    return programs;
                }
                finally
                {
                    if (conn.State == ConnectionState.Open) conn.Close();
                }
            }
        }

        // Attempt to include Term info (TermId, TermName) by LEFT JOINing Terms table.
        // If the DB/table/column does not exist this will throw and be caught by callers.
        private async Task<List<ProgramOfStudy>> GetAllProgramsRawIncludeTermAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<ProgramOfStudy>();
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT p.Id AS ProgramId, p.Name AS ProgramName, p.DepartmentId
FROM dbo.ProgramsOfStudy p
ORDER BY p.Name";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var ids = new List<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pid = reader["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader["ProgramId"]) : 0;
                    var pname = reader["ProgramName"] as string ?? $"Program {pid}";
                    var prog = new ProgramOfStudy
                    {
                        Id = pid,
                        Name = pname,
                        DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                        Courses = new List<Course>()
                    };
                    results.Add(prog);
                    ids.Add(pid);
                }
                reader.Close();

                if (!ids.Any()) return results;

                var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                using var cmd2 = conn.CreateCommand();
                // include TermId and TermName via LEFT JOIN; will throw if TermId or Terms table missing
                cmd2.CommandText = $@"
SELECT c.Id, c.Name, c.Hours, c.ProgramId, c.TermId, t.Name AS TermName
FROM dbo.Courses c
LEFT JOIN dbo.Terms t ON c.TermId = t.Id
WHERE c.ProgramId IN ({paramList})";
                for (int i = 0; i < ids.Count; i++)
                {
                    var par = cmd2.CreateParameter();
                    par.ParameterName = $"@p{i}";
                    par.Value = ids[i];
                    cmd2.Parameters.Add(par);
                }

                using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var courseMap = new Dictionary<int, List<Course>>();
                while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                    var cname = reader2["Name"] as string ?? $"Course {cid}";
                    int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                    var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;
                    int? termId = reader2["TermId"] != DBNull.Value ? Convert.ToInt32(reader2["TermId"]) : (int?)null;
                    var termName = reader2["TermName"] as string;

                    var course = new Course
                    {
                        Id = cid,
                        Name = cname,
                        Hours = chours,
                        ProgramId = progId
                    };

                    // Try to set TermId if Course model contains it via reflection (safety)
                    var termIdProp = course.GetType().GetProperty("TermId");
                    if (termIdProp != null && termIdProp.CanWrite)
                        termIdProp.SetValue(course, termId);

                    // Try to set TermName property if present (not all models include TermName)
                    var termNameProp = course.GetType().GetProperty("TermName");
                    if (termNameProp != null && termNameProp.CanWrite)
                        termNameProp.SetValue(course, termName);

                    if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                    courseMap[progId].Add(course);
                }

                foreach (var prog in results)
                {
                    if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                    else prog.Courses = new List<Course>();
                }

                return results;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }

        private async Task<List<ProgramOfStudy>> GetProgramsByIdsRawIncludeTermAsync(int[] ids, CancellationToken cancellationToken = default)
        {
            var results = new List<ProgramOfStudy>();
            if (ids == null || ids.Length == 0) return results;

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy WHERE Id IN ({paramList})";
                for (int i = 0; i < ids.Length; i++)
                {
                    var par = cmd.CreateParameter();
                    par.ParameterName = $"@p{i}";
                    par.Value = ids[i];
                    cmd.Parameters.Add(par);
                }

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var foundIds = new List<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                    var pname = reader["Name"] as string ?? $"Program {pid}";
                    var prog = new ProgramOfStudy
                    {
                        Id = pid,
                        Name = pname,
                        DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                        Courses = new List<Course>()
                    };
                    results.Add(prog);
                    foundIds.Add(pid);
                }
                reader.Close();

                if (!foundIds.Any()) return results;

                var paramList2 = string.Join(", ", foundIds.Select((_, i) => $"@q{i}"));
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $"SELECT Id, Name, Hours, ProgramId, TermId, t.Name AS TermName FROM dbo.Courses c LEFT JOIN dbo.Terms t ON c.TermId = t.Id WHERE ProgramId IN ({paramList2})";
                for (int i = 0; i < foundIds.Count; i++)
                {
                    var par = cmd2.CreateParameter();
                    par.ParameterName = $"@q{i}";
                    par.Value = foundIds[i];
                    cmd2.Parameters.Add(par);
                }

                using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var courseMap = new Dictionary<int, List<Course>>();
                while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                    var cname = reader2["Name"] as string ?? $"Course {cid}";
                    int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                    var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;
                    int? termId = reader2["TermId"] != DBNull.Value ? Convert.ToInt32(reader2["TermId"]) : (int?)null;
                    var termName = reader2["TermName"] as string;

                    var course = new Course
                    {
                        Id = cid,
                        Name = cname,
                        Hours = chours,
                        ProgramId = progId
                    };

                    var termIdProp = course.GetType().GetProperty("TermId");
                    if (termIdProp != null && termIdProp.CanWrite)
                        termIdProp.SetValue(course, termId);

                    var termNameProp = course.GetType().GetProperty("TermName");
                    if (termNameProp != null && termNameProp.CanWrite)
                        termNameProp.SetValue(course, termName);

                    if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                    courseMap[progId].Add(course);
                }

                foreach (var prog in results)
                {
                    if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                    else prog.Courses = new List<Course>();
                }

                return results;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }

        // ---- NEW: Get programs by name including term info via raw SQL ----
        private async Task<List<ProgramOfStudy>> GetProgramsByNameRawIncludeTermAsync(string trimmedName, CancellationToken cancellationToken = default)
        {
            var programs = new List<ProgramOfStudy>();
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy WHERE LTRIM(RTRIM(Name)) = @name";
                var p = cmd.CreateParameter();
                p.ParameterName = "@name";
                p.Value = trimmedName;
                cmd.Parameters.Add(p);

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var ids = new List<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                    var pname = reader["Name"] as string ?? $"Program {pid}";
                    var prog = new ProgramOfStudy
                    {
                        Id = pid,
                        Name = pname,
                        DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                        Courses = new List<Course>()
                    };
                    programs.Add(prog);
                    ids.Add(pid);
                }
                reader.Close();

                if (!ids.Any()) return programs;

                var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $@"
SELECT c.Id, c.Name, c.Hours, c.ProgramId, c.TermId, t.Name AS TermName
FROM dbo.Courses c
LEFT JOIN dbo.Terms t ON c.TermId = t.Id
WHERE c.ProgramId IN ({paramList})";
                for (int i = 0; i < ids.Count; i++)
                {
                    var par = cmd2.CreateParameter();
                    par.ParameterName = $"@p{i}";
                    par.Value = ids[i];
                    cmd2.Parameters.Add(par);
                }

                using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var courseMap = new Dictionary<int, List<Course>>();
                while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                    var cname = reader2["Name"] as string ?? $"Course {cid}";
                    int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                    var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;
                    int? termId = reader2["TermId"] != DBNull.Value ? Convert.ToInt32(reader2["TermId"]) : (int?)null;
                    var termName = reader2["TermName"] as string;

                    var course = new Course
                    {
                        Id = cid,
                        Name = cname,
                        Hours = chours,
                        ProgramId = progId
                    };

                    // set TermId/TermName on the Course if those properties exist
                    var termIdProp = course.GetType().GetProperty("TermId");
                    if (termIdProp != null && termIdProp.CanWrite)
                        termIdProp.SetValue(course, termId);

                    var termNameProp = course.GetType().GetProperty("TermName");
                    if (termNameProp != null && termNameProp.CanWrite)
                        termNameProp.SetValue(course, termName);

                    if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                    courseMap[progId].Add(course);
                }

                foreach (var prog in programs)
                {
                    if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                    else prog.Courses = new List<Course>();
                }

                return programs;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }

        // Original simple raw fallback (no term info)
        private async Task<List<ProgramOfStudy>> GetAllProgramsRawAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<ProgramOfStudy>();
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy ORDER BY Name";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var ids = new List<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                    var pname = reader["Name"] as string ?? $"Program {pid}";
                    var prog = new ProgramOfStudy
                    {
                        Id = pid,
                        Name = pname,
                        DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                        Courses = new List<Course>()
                    };
                    results.Add(prog);
                    ids.Add(pid);
                }
                reader.Close();

                if (!ids.Any()) return results;

                var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $"SELECT Id, Name, Hours, ProgramId FROM dbo.Courses WHERE ProgramId IN ({paramList})";
                for (int i = 0; i < ids.Count; i++)
                {
                    var par = cmd2.CreateParameter();
                    par.ParameterName = $"@p{i}";
                    par.Value = ids[i];
                    cmd2.Parameters.Add(par);
                }

                using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var courseMap = new Dictionary<int, List<Course>>();
                while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                    var cname = reader2["Name"] as string ?? $"Course {cid}";
                    int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                    var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;

                    var course = new Course
                    {
                        Id = cid,
                        Name = cname,
                        Hours = chours,
                        ProgramId = progId
                    };

                    if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                    courseMap[progId].Add(course);
                }

                foreach (var prog in results)
                {
                    if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                    else prog.Courses = new List<Course>();
                }

                return results;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }

        private async Task<List<ProgramOfStudy>> GetProgramsByIdsRawAsync(int[] ids, CancellationToken cancellationToken = default)
        {
            var results = new List<ProgramOfStudy>();
            if (ids == null || ids.Length == 0) return results;

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var paramList = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Id, Name, DepartmentId FROM dbo.ProgramsOfStudy WHERE Id IN ({paramList})";
                for (int i = 0; i < ids.Length; i++)
                {
                    var par = cmd.CreateParameter();
                    par.ParameterName = $"@p{i}";
                    par.Value = ids[i];
                    cmd.Parameters.Add(par);
                }

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var foundIds = new List<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                    var pname = reader["Name"] as string ?? $"Program {pid}";
                    var prog = new ProgramOfStudy
                    {
                        Id = pid,
                        Name = pname,
                        DepartmentId = reader["DepartmentId"] != DBNull.Value ? Convert.ToInt32(reader["DepartmentId"]) : 0,
                        Courses = new List<Course>()
                    };
                    results.Add(prog);
                    foundIds.Add(pid);
                }
                reader.Close();

                if (!foundIds.Any()) return results;

                var paramList2 = string.Join(", ", foundIds.Select((_, i) => $"@q{i}"));
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $"SELECT Id, Name, Hours, ProgramId FROM dbo.Courses WHERE ProgramId IN ({paramList2})";
                for (int i = 0; i < foundIds.Count; i++)
                {
                    var par = cmd2.CreateParameter();
                    par.ParameterName = $"@q{i}";
                    par.Value = foundIds[i];
                    cmd2.Parameters.Add(par);
                }

                using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var courseMap = new Dictionary<int, List<Course>>();
                while (await reader2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cid = reader2["Id"] != DBNull.Value ? Convert.ToInt32(reader2["Id"]) : 0;
                    var cname = reader2["Name"] as string ?? $"Course {cid}";
                    int? chours = reader2["Hours"] != DBNull.Value ? Convert.ToInt32(reader2["Hours"]) : (int?)null;
                    var progId = reader2["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader2["ProgramId"]) : 0;

                    var course = new Course
                    {
                        Id = cid,
                        Name = cname,
                        Hours = chours,
                        ProgramId = progId
                    };

                    if (!courseMap.TryGetValue(progId, out var list)) { list = new List<Course>(); courseMap[progId] = list; }
                    courseMap[progId].Add(course);
                }

                foreach (var prog in results)
                {
                    if (courseMap.TryGetValue(prog.Id, out var clist)) prog.Courses = clist;
                    else prog.Courses = new List<Course>();
                }

                return results;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }

        // Helper: detect SQL column errors (Invalid column name ...)
        private static bool IsSqlColumnError(Exception ex)
        {
            if (ex == null) return false;
            var msg = ex.Message ?? string.Empty;
            if (msg.IndexOf("Invalid column name", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ex.InnerException != null) return IsSqlColumnError(ex.InnerException);
            return false;
        }
    }
}