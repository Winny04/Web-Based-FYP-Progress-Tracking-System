using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace eForm_dotnetCore.Domain
{
    public class DataTableParameters
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public int Draw { get; set; }
        public Search Search { get; set; }

    }

    public class Search
    {
        public string value { get; set; }
    }

    public class BomDataTableParams : DataTableParameters
    {
        public string? Status { get; set; }
        public string? Category { get; set; }
        public int ProjectId { get; set; }
    }
}
