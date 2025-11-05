using System.ComponentModel.DataAnnotations;

namespace WorkloadProject2025.Data.Models;

public class Term
{
    public int Id { get; set; }
    [Required(ErrorMessage = "You must specify a term name")]
    public string Name { get; set; } 
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
