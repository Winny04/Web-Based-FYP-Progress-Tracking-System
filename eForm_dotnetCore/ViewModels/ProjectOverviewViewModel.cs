using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class ProjectOverviewViewModel
    {
        public ProjectOverviewViewModel()
        {
            CardModels = new List<CardModel>();
            Projects = new List<Project>();
            Employees = new List<EmployeeInfo>();
        }

        public List<CardModel> CardModels { get; set; }
        public List<Project> Projects { get; set; }
        public List<EmployeeInfo> Employees { get; set; }
    }
}
