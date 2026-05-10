using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.Helpers
{
    public class AuthResource
    {
        public AuthResource(string requireTypes, string requireValue, Project resource)
        {
            RequireTypes = requireTypes;
            RequireValue = requireValue;
            Resource = resource;
        }

        public string RequireTypes { get; set; }
        public string RequireValue { get; set; }
        public Project Resource { get; set; }
    }
}
