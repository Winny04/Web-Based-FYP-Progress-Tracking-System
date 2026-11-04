using FYPTracker.Models;

namespace FYPTracker.Helpers
{
    /// <summary>
    /// Authorization resource carrying the FYP project context for policy evaluation.
    /// </summary>
    public class AuthResource
    {
        public AuthResource(string requireTypes, string requireValue, FYPProject resource)
        {
            RequireTypes = requireTypes;
            RequireValue = requireValue;
            Resource = resource;
        }

        public string RequireTypes { get; set; }
        public string RequireValue { get; set; }
        public FYPProject Resource { get; set; }
    }
}
