using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace FYPTracker.Common
{
    public class Departments
    {
        public static List<SelectListItem> DepartmentList()
        {
            var depts = new List<string> { "IT", "Engineering", "Business" };
            return depts.Select(x => new SelectListItem { Text = x, Value = x }).ToList();
        }
    }
}
