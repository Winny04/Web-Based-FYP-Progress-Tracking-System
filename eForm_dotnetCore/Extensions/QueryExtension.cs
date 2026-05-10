using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.Extensions
{
    public static class QueryExtension
    {
        public static IQueryable<Bom> WhereRFQAvailableStatus(this IQueryable<Bom> source,string status)
        {
            var availableIds = new List<int>();
            foreach(var item in source)
            {
                if(CommonHelpers.availableBaseStatus(status,item.Status) && item.Project.Status != "Completed")
                    availableIds.Add(item.Id);
            }

            return source.Where(x => availableIds.Contains(x.Id));
        }

        public static IQueryable<Bom> WhereRFQAvailableStatus2(this IQueryable<Bom> source, string status)
        {
            var availableIds = new List<int>();
            foreach (var item in source)
            {
                if (CommonHelpers.availableBaseStatus(status, item.Status))
                    availableIds.Add(item.Id);
            }

            return source.Where(x => availableIds.Contains(x.Id));
        }
    }
}
