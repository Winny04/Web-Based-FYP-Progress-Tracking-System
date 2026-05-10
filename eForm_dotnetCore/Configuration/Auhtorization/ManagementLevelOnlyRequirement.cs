using Microsoft.AspNetCore.Authorization;

namespace eForm_dotnetCore.Configuration.Auhtorization
{
    public class ManagementLevelOnlyRequirement :AuthorizationHandler<ManagementLevelOnlyRequirement>, IAuthorizationRequirement
    {
        public ManagementLevelOnlyRequirement()
        {
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ManagementLevelOnlyRequirement requirement)
        {
            if (context.User.HasClaim(x => x.Type == "Type")){
                var userType = context.User.Claims.FirstOrDefault(x => x.Type == "Type").Value;
                if (
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
