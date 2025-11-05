using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class Course
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a course name")]
    public string Name { get; set; }
    [Required(ErrorMessage = "You must set the amount of hours in the course")]
    public int? Hours { get; set; }
    [Required(ErrorMessage = "You must select a program")]
    public int? ProgramId { get; set; }
    public ProgramOfStudy Program { get; set; }
    [Required(ErrorMessage = "You must select a term")]
    public int? TermId { get; set; }
    public Term Term { get; set; }
}
