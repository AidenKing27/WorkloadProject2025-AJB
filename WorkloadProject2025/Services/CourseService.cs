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
    // Robust CourseService using IDbContextFactory, EF Includes + raw SQL fallbacks (single raw reader implementations).
    public class CourseService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public CourseService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        }

        public async Task<Course> AddAsync(Course course, CancellationToken cancellationToken = default)
        {
            if (course == null) throw new ArgumentNullException(nameof(course));
            if (string.IsNullOrWhiteSpace(course.Name)) throw new Exception("Course must have a name");

            await using var ctx = _dbFactory.CreateDbContext();
            ctx.Courses.Add(course);
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return course;
        }

        public async Task<List<Course>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await using var ctx = _dbFactory.CreateDbContext();

            try
            {
                var list = await ctx.Courses
                    .AsNoTracking()
                    .Include(c => c.Program)
                    .Include(c => c.Term)
                    .OrderBy(c => c.Name)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return list ?? new List<Course>();
            }
            catch
            {
                return await GetAllCoursesRawAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<Course?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var ctx = _dbFactory.CreateDbContext();

            try
            {
                return await ctx.Courses
                    .AsNoTracking()
                    .Include(c => c.Program)
                    .Include(c => c.Term)
                    .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                var list = await GetCourseByIdRawAsync(id, cancellationToken).ConfigureAwait(false);
                return list.FirstOrDefault();
            }
        }

        public async Task<bool> DeleteAsync(Course course, CancellationToken cancellationToken = default)
        {
            if (course == null) return false;
            await using var ctx = _dbFactory.CreateDbContext();
            try
            {
                ctx.Courses.Remove(course);
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ----- Term map -----
        public async Task<Dictionary<int, string>> GetDistinctTermIdNameMapAsync(CancellationToken cancellationToken = default)
        {
            var map = new Dictionary<int, string>(capacity: 16);
            await using var ctx = _dbFactory.CreateDbContext();

            var attempts = new[]
            {
                "SELECT Id, Name FROM dbo.Terms WHERE Name IS NOT NULL",
                "SELECT Id, Name FROM dbo.Term WHERE Name IS NOT NULL"
            };

            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                foreach (var sql in attempts)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = sql;
                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            try
                            {
                                var id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                                var name = reader["Name"] as string;
                                if (id != 0 && !string.IsNullOrWhiteSpace(name) && !map.ContainsKey(id))
                                    map[id] = name.Trim();
                            }
                            catch { /* ignore row parse errors */ }
                        }
                        reader.Close();

                        if (map.Any()) return map;
                    }
                    catch { /* try next */ }
                }

                return map;
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open) conn.Close();
            }
        }

        // ----- Workload readers (EF first, raw fallback using SELECT * and HasColumn guard) -----
        public async Task<List<Workload>> GetAllWorkloadsAsync(CancellationToken cancellationToken = default)
        {
            await using var ctx = _dbFactory.CreateDbContext();
            try
            {
                var list = await ctx.Workloads
                    .AsNoTracking()
                    .Include(w => w.Course)
                    .Include(w => w.FacultyMember)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return list ?? new List<Workload>();
            }
            catch
            {
                return await GetAllWorkloadsRawAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<List<Workload>> GetWorkloadsByCourseIdAsync(int courseId, CancellationToken cancellationToken = default)
        {
            await using var ctx = _dbFactory.CreateDbContext();
            try
            {
                var list = await ctx.Workloads
                    .AsNoTracking()
                    .Where(w => w.CourseId == courseId)
                    .Include(w => w.Course)
                    .Include(w => w.FacultyMember)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return list ?? new List<Workload>();
            }
            catch
            {
                // fallback raw reader using SELECT * and reading columns defensively
                var results = new List<Workload>();
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM dbo.Workloads WHERE CourseId = @cid";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@cid";
                    p.Value = courseId;
                    cmd.Parameters.Add(p);

                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    bool HasColumn(IDataRecord r, string name)
                    {
                        for (int i = 0; i < r.FieldCount; i++)
                            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
                        return false;
                    }

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            var wid = HasColumn(reader, "Id") && reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                            var cid = HasColumn(reader, "CourseId") && reader["CourseId"] != DBNull.Value ? Convert.ToInt32(reader["CourseId"]) : 0;
                            var facultyEmail = HasColumn(reader, "FacultyEmail") ? reader["FacultyEmail"] as string : null;
                            var section = HasColumn(reader, "Section") ? reader["Section"] as string : null;

                            decimal? hoursDec = null;
                            if (HasColumn(reader, "Hours") && reader["Hours"] != DBNull.Value)
                            {
                                var hoursObj = reader["Hours"];
                                if (hoursObj is decimal d) hoursDec = d;
                                else if (hoursObj is int i) hoursDec = i;
                                else if (hoursObj is double dbl) hoursDec = Convert.ToDecimal(dbl);
                                else if (decimal.TryParse(hoursObj.ToString(), out var pd)) hoursDec = pd;
                            }

                            var w = new Workload
                            {
                                Id = wid,
                                CourseId = cid,
                                FacultyEmail = facultyEmail,
                                Section = section
                            };

                            // set Hours via reflection to handle model numeric type differences
                            try
                            {
                                var hoursProp = w.GetType().GetProperty("Hours");
                                if (hoursProp != null && hoursProp.CanWrite && hoursDec.HasValue)
                                {
                                    var propType = Nullable.GetUnderlyingType(hoursProp.PropertyType) ?? hoursProp.PropertyType;
                                    if (propType == typeof(int))
                                        hoursProp.SetValue(w, Convert.ChangeType(Convert.ToInt32(hoursDec.Value), propType));
                                    else if (propType == typeof(decimal))
                                        hoursProp.SetValue(w, Convert.ChangeType(hoursDec.Value, propType));
                                    else if (propType == typeof(double))
                                        hoursProp.SetValue(w, Convert.ChangeType((double)hoursDec.Value, propType));
                                    else
                                        hoursProp.SetValue(w, Convert.ChangeType(hoursDec.Value, propType));
                                }
                            }
                            catch { /* ignore */ }

                            // set other optional string fields if model has them
                            try { if (HasColumn(reader, "CourseName")) w.GetType().GetProperty("CourseName")?.SetValue(w, reader["CourseName"] as string); } catch { }
                            try { if (HasColumn(reader, "CourseTitle")) w.GetType().GetProperty("CourseTitle")?.SetValue(w, reader["CourseTitle"] as string); } catch { }

                            results.Add(w);
                        }
                        catch
                        {
                            // ignore row parse error
                        }
                    }
                    reader.Close();
                }
                finally
                {
                    if (conn.State == System.Data.ConnectionState.Open) conn.Close();
                }

                return results;
            }
        }

        private async Task<List<Workload>> GetAllWorkloadsRawAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<Workload>();
            await using var ctx = _dbFactory.CreateDbContext();
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                // SELECT * avoids invalid-column errors; HasColumn will guard reading
                cmd.CommandText = "SELECT * FROM dbo.Workloads";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                bool HasColumn(IDataRecord r, string name)
                {
                    for (int i = 0; i < r.FieldCount; i++)
                        if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                }

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        var wid = HasColumn(reader, "Id") && reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                        var cid = HasColumn(reader, "CourseId") && reader["CourseId"] != DBNull.Value ? Convert.ToInt32(reader["CourseId"]) : 0;
                        var facultyEmail = HasColumn(reader, "FacultyEmail") ? reader["FacultyEmail"] as string : null;
                        var section = HasColumn(reader, "Section") ? reader["Section"] as string : null;

                        decimal? hoursDec = null;
                        if (HasColumn(reader, "Hours") && reader["Hours"] != DBNull.Value)
                        {
                            var hoursObj = reader["Hours"];
                            if (hoursObj is decimal d) hoursDec = d;
                            else if (hoursObj is int i) hoursDec = i;
                            else if (hoursObj is double dbl) hoursDec = Convert.ToDecimal(dbl);
                            else if (decimal.TryParse(hoursObj.ToString(), out var pd)) hoursDec = pd;
                        }

                        var w = new Workload
                        {
                            Id = wid,
                            CourseId = cid,
                            FacultyEmail = facultyEmail,
                            Section = section
                        };

                        try
                        {
                            var hoursProp = w.GetType().GetProperty("Hours");
                            if (hoursProp != null && hoursProp.CanWrite && hoursDec.HasValue)
                            {
                                var propType = Nullable.GetUnderlyingType(hoursProp.PropertyType) ?? hoursProp.PropertyType;
                                if (propType == typeof(int))
                                    hoursProp.SetValue(w, Convert.ChangeType(Convert.ToInt32(hoursDec.Value), propType));
                                else if (propType == typeof(decimal))
                                    hoursProp.SetValue(w, Convert.ChangeType(hoursDec.Value, propType));
                                else if (propType == typeof(double))
                                    hoursProp.SetValue(w, Convert.ChangeType((double)hoursDec.Value, propType));
                                else
                                    hoursProp.SetValue(w, Convert.ChangeType(hoursDec.Value, propType));
                            }
                        }
                        catch { /* ignore */ }

                        try { if (HasColumn(reader, "CourseName")) w.GetType().GetProperty("CourseName")?.SetValue(w, reader["CourseName"] as string); } catch { }
                        try { if (HasColumn(reader, "CourseTitle")) w.GetType().GetProperty("CourseTitle")?.SetValue(w, reader["CourseTitle"] as string); } catch { }

                        results.Add(w);
                    }
                    catch
                    {
                        // ignore row parse error
                    }
                }
                reader.Close();
                return results;
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open) conn.Close();
            }
        }

        // ----- Courses raw fallback helpers (no TermId assumed) -----
        private async Task<List<Course>> GetAllCoursesRawAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<Course>();
            await using var ctx = _dbFactory.CreateDbContext();
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Hours, ProgramId FROM dbo.Courses ORDER BY Name";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        var cid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                        var cname = reader["Name"] as string ?? $"Course {cid}";

                        object rawHours = reader["Hours"];
                        int? hoursInt = null;
                        decimal? hoursDec = null;
                        if (rawHours != DBNull.Value)
                        {
                            if (rawHours is int hi) hoursInt = hi;
                            else if (rawHours is long hl) hoursInt = (int)hl;
                            else if (rawHours is decimal hd) hoursDec = hd;
                            else if (rawHours is double dbl) hoursDec = Convert.ToDecimal(dbl);
                            else
                            {
                                if (int.TryParse(rawHours.ToString(), out var pi)) hoursInt = pi;
                                else if (decimal.TryParse(rawHours.ToString(), out var pd)) hoursDec = pd;
                            }
                        }

                        var progId = reader["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader["ProgramId"]) : 0;

                        var course = new Course
                        {
                            Id = cid,
                            Name = cname
                        };

                        // set Hours property via reflection (convert to the property type)
                        var hoursProp = course.GetType().GetProperty("Hours");
                        if (hoursProp != null && hoursProp.CanWrite)
                        {
                            var propType = Nullable.GetUnderlyingType(hoursProp.PropertyType) ?? hoursProp.PropertyType;
                            if (propType == typeof(int))
                            {
                                if (hoursInt.HasValue) hoursProp.SetValue(course, hoursInt);
                                else if (hoursDec.HasValue) hoursProp.SetValue(course, Convert.ChangeType((int)hoursDec.Value, propType));
                            }
                            else if (propType == typeof(decimal))
                            {
                                if (hoursDec.HasValue) hoursProp.SetValue(course, hoursDec);
                                else if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType((decimal)hoursInt.Value, propType));
                            }
                            else if (propType == typeof(double))
                            {
                                if (hoursDec.HasValue) hoursProp.SetValue(course, (double)hoursDec.Value);
                                else if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType((double)hoursInt.Value, propType));
                            }
                            else
                            {
                                if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType(hoursInt.Value, propType));
                                else if (hoursDec.HasValue) hoursProp.SetValue(course, Convert.ChangeType(hoursDec.Value, propType));
                            }
                        }

                        var progProp = course.GetType().GetProperty("ProgramId");
                        if (progProp != null && progProp.CanWrite)
                        {
                            if (progProp.PropertyType == typeof(int) || progProp.PropertyType == typeof(int?))
                                progProp.SetValue(course, progId);
                            else
                                progProp.SetValue(course, Convert.ChangeType(progId, progProp.PropertyType));
                        }

                        results.Add(course);
                    }
                    catch { /* ignore row */ }
                }

                reader.Close();
                return results;
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open) conn.Close();
            }
        }

        private async Task<List<Course>> GetCourseByIdRawAsync(int id, CancellationToken cancellationToken = default)
        {
            var results = new List<Course>();
            await using var ctx = _dbFactory.CreateDbContext();
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Hours, ProgramId FROM dbo.Courses WHERE Id = @id";
                var p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = id;
                cmd.Parameters.Add(p);

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        var cid = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0;
                        var cname = reader["Name"] as string ?? $"Course {cid}";

                        object rawHours = reader["Hours"];
                        int? hoursInt = null;
                        decimal? hoursDec = null;
                        if (rawHours != DBNull.Value)
                        {
                            if (rawHours is int i) hoursInt = i;
                            else if (rawHours is long l) hoursInt = (int)l;
                            else if (rawHours is decimal dec) hoursDec = dec;
                            else if (rawHours is double dbl) hoursDec = Convert.ToDecimal(dbl);
                            else
                            {
                                if (int.TryParse(rawHours.ToString(), out var parsedInt)) hoursInt = parsedInt;
                                else if (decimal.TryParse(rawHours.ToString(), out var parsedDec)) hoursDec = parsedDec;
                            }
                        }

                        var progId = reader["ProgramId"] != DBNull.Value ? Convert.ToInt32(reader["ProgramId"]) : 0;

                        var course = new Course
                        {
                            Id = cid,
                            Name = cname
                        };

                        var hoursProp = course.GetType().GetProperty("Hours");
                        if (hoursProp != null && hoursProp.CanWrite)
                        {
                            var propType = Nullable.GetUnderlyingType(hoursProp.PropertyType) ?? hoursProp.PropertyType;
                            if (propType == typeof(int))
                            {
                                if (hoursInt.HasValue) hoursProp.SetValue(course, hoursInt);
                                else if (hoursDec.HasValue) hoursProp.SetValue(course, Convert.ChangeType((int)hoursDec.Value, propType));
                            }
                            else if (propType == typeof(decimal))
                            {
                                if (hoursDec.HasValue) hoursProp.SetValue(course, hoursDec);
                                else if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType((decimal)hoursInt.Value, propType));
                            }
                            else if (propType == typeof(double))
                            {
                                if (hoursDec.HasValue) hoursProp.SetValue(course, (double)hoursDec.Value);
                                else if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType((double)hoursInt.Value, propType));
                            }
                            else
                            {
                                if (hoursInt.HasValue) hoursProp.SetValue(course, Convert.ChangeType(hoursInt.Value, propType));
                                else if (hoursDec.HasValue) hoursProp.SetValue(course, Convert.ChangeType(hoursDec.Value, propType));
                            }
                        }

                        var progIdProp = course.GetType().GetProperty("ProgramId");
                        if (progIdProp != null && progIdProp.CanWrite)
                        {
                            if (progIdProp.PropertyType == typeof(int) || progIdProp.PropertyType == typeof(int?))
                                progIdProp.SetValue(course, progId);
                            else
                                progIdProp.SetValue(course, Convert.ChangeType(progId, progIdProp.PropertyType));
                        }

                        results.Add(course);
                    }
                    catch { /* ignore row */ }
                }

                reader.Close();
                return results;
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open) conn.Close();
            }
        }

        // ----- DB diagnostic counts helper -----
        public async Task<Dictionary<string, object>> GetDbCountsAsync(CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                await using var ctx = _dbFactory.CreateDbContext();
                var conn = ctx.Database.GetDbConnection();
                if (conn == null)
                {
                    result["error"] = "No DB connection object available.";
                    return result;
                }

                var cs = ctx.Database.GetConnectionString();
                result["connectionStringPresent"] = !string.IsNullOrWhiteSpace(cs);

                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                string[] tables = { "Courses", "Workloads", "Terms", "ProgramsOfStudy", "Departments" };
                foreach (var t in tables)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT COUNT(1) FROM dbo.[{t}]";
                        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        result[t] = scalar ?? 0;
                    }
                    catch (Exception exTable)
                    {
                        result[t] = $"error: {exTable.Message}";
                    }
                }

                try
                {
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = "SELECT TOP(1) Id, Name, Hours, ProgramId FROM dbo.Courses";
                    using var reader = await cmd2.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var sample = new
                        {
                            Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                            Name = reader["Name"] as string,
                            Hours = reader["Hours"] != DBNull.Value ? reader["Hours"] : null,
                            ProgramId = reader["ProgramId"] != DBNull.Value ? reader["ProgramId"] : null
                        };
                        result["sampleCourse"] = sample;
                    }
                    else
                    {
                        result["sampleCourse"] = null;
                    }
                    reader.Close();
                }
                catch (Exception exSample)
                {
                    result["sampleCourse"] = $"error: {exSample.Message}";
                }

                return result;
            }
            catch (Exception ex)
            {
                result["error"] = ex.Message;
                return result;
            }
        }
    }
}