namespace eForm_dotnetCore.Models
{
    public class Department
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<EmployeeInfo> EmployeeInfo { get; set; } = new List<EmployeeInfo>();
    }
}
