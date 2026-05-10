using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace FYPTracker.Configuration.Auhtorization
{
    public class ClaimRequirementAttribute : AuthorizeAttribute, IAuthorizationFilter
    {
        private readonly string _claimType;
        private readonly string _claimValue;
        public ClaimRequirementAttribute(string claimType, string claimValue)
        {
            _claimType = claimType;
            _claimValue = claimValue;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            //Validate if any permissions are passed when using attribute at controller or action level
            if (string.IsNullOrEmpty(_claimType) && string.IsNullOrEmpty(_claimValue))
            {
                //Validation cannot take place without any permissions so returning unauthorized
                context.Result = new UnauthorizedResult();
                return;
            }

            var requiredPermissions = _claimValue.Split(",");
            var requiredClaims = _claimType.Split(",");

            foreach(var requiredClaim in requiredClaims)
            {
                var claimValues = context.HttpContext.User.Claims.Where(x => x.Type == requiredClaim).Select(x => x.Value).FirstOrDefault();
                if (claimValues == "SuperAdmin" || claimValues == "SuperAdminAssistant")
                    return;

                if (context.HttpContext.User.Claims.Any(c => c.Type == requiredClaim) &&
                requiredPermissions.Contains(claimValues))
                    return;
            }

            context.Result = new UnauthorizedResult();
            //return;
        }
    }
}
