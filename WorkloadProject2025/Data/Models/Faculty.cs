using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class Faculty
{
    [Key]
    [Required(ErrorMessage = "You must specify a email")]
    public string Email { get; set; }
    [Required(ErrorMessage = "You must specify a first name")]
    public string FirstName { get; set; }
    [Required(ErrorMessage = "You must specify a last name")]
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
    [Required(ErrorMessage = "You must select a workload category")]
    public int? WorkloadCategoryId { get; set; }
    public WorkloadCategory WorkloadCategory { get; set; }
}
