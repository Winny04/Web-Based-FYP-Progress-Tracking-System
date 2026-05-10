using System;
using System.Collections.Generic;

namespace eForm_dotnetCore.Models
{
    public partial class Category
    {

        public int Id { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryShortName { get; set; }
    }
}
