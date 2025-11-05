using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class Department
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a department name")]
    public string Name { get; set; }
    [Required(ErrorMessage = "You must select a school")]
    public int? SchoolId { get; set; }
    public School School { get; set; }
    public List<ProgramOfStudy> ProgramsOfStudy { get; set; } = new();

}
