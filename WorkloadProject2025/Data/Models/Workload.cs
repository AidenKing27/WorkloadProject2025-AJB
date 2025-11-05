using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class Workload
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a faculty member")]
    public string FacultyEmail { get; set; }
    public Faculty FacultyMember { get; set; }
    [Required(ErrorMessage = "You must select a course")]
    public int? CourseId { get; set; }
    public Course Course { get; set; }
    public CourseType CourseType { get; set; }
    [MaxLength(1)]
    public string Section { get; set; }
    public int? Hours { get; set; }
}
