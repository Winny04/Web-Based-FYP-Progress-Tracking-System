using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class AuditTrailsViewModel<T>
    {
        public AuditTrailsViewModel()
        {
            Today = new List<T>();
            Week = new List<T>();
            Month = new List<T>();
            All = new List<T>();
        }

        public List<T> Today { get; set; }
        public List<T> Week { get; set; }
        public List<T> Month { get; set; }
        public List<T> All { get; set; }
    }
}
