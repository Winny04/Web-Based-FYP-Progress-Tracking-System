using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;
using FYPTracker.Models;

namespace FYPTracker.Common
{
    public class Departments
    {
        /// <summary>
        /// Returns a hardcoded fallback list. Prefer loading from DB via controller context.
        /// </summary>
        public static List<SelectListItem> DepartmentList()
        {
            var depts = new List<string> { "IT", "Engineering", "Business" };
            return depts.Select(x => new SelectListItem { Text = x, Value = x }).ToList();
        }

        /// <summary>
        /// Returns departments loaded dynamically from the database.
        /// </summary>
        public static List<SelectListItem> DepartmentListFromDb(FYPTrackerContext context)
        {
            var depts = context.Department.OrderBy(d => d.Name).ToList();
            
            // Auto-seed if the table is empty so users have choices
            if (!depts.Any())
            {
                var defaultDepts = new[] { "IT", "Computer Science", "Business", "Engineering", "Arts", "Science", "Software Engineering" };
                foreach (var d in defaultDepts)
                {
                    context.Department.Add(new Department { Name = d });
                }
                context.SaveChanges();
                depts = context.Department.OrderBy(d => d.Name).ToList();
            }

            return depts.Select(x => new SelectListItem { Text = x.Name, Value = x.Name }).ToList();
        }
    }
}
