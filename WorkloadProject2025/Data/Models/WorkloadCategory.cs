using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class WorkloadCategory
{
    [Key]
    public int Id { get; set; }
    [Required(ErrorMessage = "You must set a minimum amount")]
    public int? MinimumHours { get; set; }
    [Required(ErrorMessage = "You must set a maximum amount")]
    public int? MaximumHours { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
