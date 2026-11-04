using FYPTracker.Models;

namespace FYPTracker.ViewModels
{
    public class ManageProjectItemVM
    {
        public ApplicationUser Student { get; set; } = new ApplicationUser();
        public FYPProject? Project { get; set; }
    }
}
