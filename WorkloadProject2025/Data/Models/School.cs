using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class School
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a school name")]
    public string Name { get; set; }
    public List<Department> Departments { get; set; } = new();
}
