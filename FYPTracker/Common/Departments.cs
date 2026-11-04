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
                    context.Department.Add(new Department { Name = d, IsActive = true });
                }
                context.SaveChanges();
                depts = context.Department.OrderBy(d => d.Name).ToList();
            }

            return depts.Where(x => x.IsActive).Select(x => new SelectListItem { Text = x.Name, Value = x.Name }).ToList();
        }

        public static List<SelectListItem> ProgrammeListFromDb(FYPTrackerContext context)
        {
            var progs = context.Programme.OrderBy(p => p.Name).ToList();
            if (!progs.Any())
            {
                var defaults = new[] { "Software Engineering", "Computer Science", "Information Systems", "Business Administration", "Accounting", "Mechanical Engineering" };
                foreach (var p in defaults)
                {
                    context.Programme.Add(new Programme { Name = p, IsActive = true });
                }
                context.SaveChanges();
                progs = context.Programme.OrderBy(p => p.Name).ToList();
            }
            return progs.Where(p => p.IsActive).Select(p => new SelectListItem { Text = p.Name, Value = p.Name }).ToList();
        }

        public static List<SelectListItem> SpecializationListFromDb(FYPTrackerContext context)
        {
            var specs = context.Specialization.OrderBy(s => s.Name).ToList();
            if (!specs.Any())
            {
                var defaults = new[] { "Artificial Intelligence", "Cybersecurity", "Data Science", "Web Development", "Mobile Computing", "Network Engineering" };
                foreach (var s in defaults)
                {
                    context.Specialization.Add(new Specialization { Name = s, IsActive = true });
                }
                context.SaveChanges();
                specs = context.Specialization.OrderBy(s => s.Name).ToList();
            }
            return specs.Where(s => s.IsActive).Select(s => new SelectListItem { Text = s.Name, Value = s.Name }).ToList();
        }
    }
}
