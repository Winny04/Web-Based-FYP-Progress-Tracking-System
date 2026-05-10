using FYPTracker.Models;

namespace FYPTracker.Extensions
{
    public static class QueryExtension
    {
        /// <summary>
        /// Filters FYP milestones by status — replaces the old BOM-specific extension.
        /// </summary>
        public static IQueryable<FYPMilestone> WhereStatus(this IQueryable<FYPMilestone> source, string status)
        {
            if (string.IsNullOrEmpty(status)) return source;
            return source.Where(x => x.Status == status);
        }

        public static IQueryable<FYPProject> WhereActiveProjects(this IQueryable<FYPProject> source)
        {
            return source.Where(x => x.Status != "Completed" && x.Status != "Rejected");
        }
    }
}
