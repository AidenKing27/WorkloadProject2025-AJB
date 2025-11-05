using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class ProgramOfStudy
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a program name")]
    public string Name { get; set; }
    [Required(ErrorMessage = "You must select a department")]
    public int? DepartmentId { get; set; }
    public Department Department { get; set; }
    public List<Course> Courses { get; set; } = new();
}
