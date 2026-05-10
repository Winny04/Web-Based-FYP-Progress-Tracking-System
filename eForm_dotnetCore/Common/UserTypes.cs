using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.ObjectModel;

namespace eForm_dotnetCore.Common
{
    public class UserTypes
    {
        public static readonly IList<String> CLevels = new ReadOnlyCollection<string>(new List<String> {"HOD","CEO","GFC" });


        public static List<SelectListItem> UserTypeList()
        {
            var list = new List<SelectListItem> {
                new SelectListItem { Text = "CEO", Value = "CEO" },
                new SelectListItem { Text = "GFC", Value = "GFC" },
                new SelectListItem { Text = "HOD", Value = "HOD" },
                new SelectListItem { Text = "Project Engineer", Value = "PM" },
                new SelectListItem { Text = "Engineer", Value = "Requestor" },
                new SelectListItem { Text = "Buyer", Value = "Buyer" },
                new SelectListItem { Text = "Finance", Value = "Finance" },
                new SelectListItem { Text = "Quality Engineer", Value = "Quality" },
                new SelectListItem { Text = "Sales", Value = "Sales" },
                new SelectListItem { Text = "Purchaser", Value = "Purchaser" },
                new SelectListItem { Text = "Requestor", Value = "Requestor" },
            };

            return list;
        }

        public static List<SelectListItem> UserTypeFilterList()
        {
            var list = new List<SelectListItem> {
                new SelectListItem { Text = "ALL", Value = "" },
                new SelectListItem { Text = "CEO", Value = "CEO" },
                new SelectListItem { Text = "GFC", Value = "GFC" },
                new SelectListItem { Text = "HOD", Value = "HOD" },
                new SelectListItem { Text = "Project Engineer", Value = "PM" },
                new SelectListItem { Text = "Engineer", Value = "Engineer" },
                new SelectListItem { Text = "Buyer", Value = "Buyer" },
                new SelectListItem { Text = "Finance", Value = "Finance" },
                new SelectListItem { Text = "Quality Engineer", Value = "Quality" },
                new SelectListItem { Text = "Sales", Value = "Sales" },
                new SelectListItem { Text = "Requestor", Value = "Requestor" },
            };

            return list;
        }
    }
}
