using Microsoft.AspNetCore.Authorization;

namespace FYPTracker.Configuration.Auhtorization
{
    public class PMOnlyRequirement : AuthorizationHandler<PMOnlyRequirement>, IAuthorizationRequirement
    {
        public PMOnlyRequirement()
        {
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PMOnlyRequirement requirement)
        {
            if (context.User.HasClaim(x => x.Type == "Type")){
                var userType = context.User.Claims.FirstOrDefault(x => x.Type == "Type").Value;
                if (userType == "PM" ||
                userType == "HOD" ||
                userType == "GFC" ||
                userType == "CEO" ||
                userType == "CDO" ||
                userType == "SuperAdminAssistant" ||
                userType == "SuperAdmin"
                )
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            context.Fail();
            return Task.CompletedTask;
        }

    }
}
