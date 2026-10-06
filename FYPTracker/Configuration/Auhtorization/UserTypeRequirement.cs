using FYPTracker.Helpers;
using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace FYPTracker.Configuration.Auhtorization
{
    public class UserTypeRequirement: IAuthorizationRequirement
    {
        public UserTypeRequirement()
        {
        }
    }

    public class UserTypeHandler: AuthorizationHandler<UserTypeRequirement, AuthResource>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UserTypeRequirement requirement, AuthResource resource)
        {
            var roles = context.User.Claims.Where(x => x.Type == ClaimTypes.Role).Select(x => x.Value).ToList();
            if (roles.Any())
            {
                if (resource.RequireValue == "All" || roles.Contains("Admin") || roles.Contains("SuperAdmin"))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }

                if (!string.IsNullOrEmpty(resource.RequireValue))
                {
                    var listTypes = resource.RequireValue.Trim().Split(",");
                    if (listTypes.Intersect(roles).Any())
                    {
                        context.Succeed(requirement);
                        return Task.CompletedTask;
                    }
                }
            }
            context.Fail();
            return Task.CompletedTask;
        }
    }
}
