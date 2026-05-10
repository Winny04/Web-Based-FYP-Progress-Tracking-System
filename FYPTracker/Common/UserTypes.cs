using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.ObjectModel;

namespace FYPTracker.Common
{
    public class UserTypes
    {
        public static readonly IList<String> DefaultRoles = new ReadOnlyCollection<string>(new List<String> {"Admin" });

        public static List<SelectListItem> UserTypeList()
        {
            var list = new List<SelectListItem> {
                new SelectListItem { Text = "Student", Value = "Student" },
                new SelectListItem { Text = "Supervisor", Value = "Supervisor" },
                new SelectListItem { Text = "Admin", Value = "Admin" }
            };

            return list;
        }

        public static List<SelectListItem> UserTypeFilterList()
        {
            var list = new List<SelectListItem> {
                new SelectListItem { Text = "ALL", Value = "" },
                new SelectListItem { Text = "Student", Value = "Student" },
                new SelectListItem { Text = "Supervisor", Value = "Supervisor" },
                new SelectListItem { Text = "Admin", Value = "Admin" }
            };

            return list;
        }
    }
}
