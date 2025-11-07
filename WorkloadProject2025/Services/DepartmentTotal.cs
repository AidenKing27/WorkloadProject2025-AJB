namespace WorkloadProject2025.Services
{
    public class DepartmentTotal
    {
        public int DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public decimal TotalCourseHours { get; set; }
        public int ProgramCount { get; set; }
    }
}