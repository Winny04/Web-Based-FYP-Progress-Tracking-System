using FYPTracker.Helpers;
using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;

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
            if (context.User.HasClaim(x => x.Type == "Type"))
            {
                var userType = context.User.Claims.Single(x => x.Type == "Type").Value;
                if (resource.RequireValue == "All" || userType == "Admin" || userType == "SuperAdmin")
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }

                if (!string.IsNullOrEmpty(resource.RequireValue))
                {
                    var listTypes = resource.RequireValue.Trim().Split(",");
                    if (listTypes.Contains(userType))
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
