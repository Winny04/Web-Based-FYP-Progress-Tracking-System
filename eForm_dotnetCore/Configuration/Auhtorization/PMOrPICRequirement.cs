using eForm_dotnetCore.Models;
using Microsoft.AspNetCore.Authorization;

namespace eForm_dotnetCore.Configuration.Auhtorization
{
    public class PMOrPICRequirement : AuthorizationHandler<PMOrPICRequirement, Project>, IAuthorizationRequirement
    {
        public PMOrPICRequirement()
        {
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PMOrPICRequirement requirement, Project project)
        {
            if (context.User.HasClaim(x => x.Type == "Type")){
                var userType = context.User.Claims.FirstOrDefault(x => x.Type == "Type").Value;
                if(userType == "PM" ||
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

            var userId = int.Parse(context.User.Claims.FirstOrDefault(x => x.Type == "Id").Value);
            if (
                 project.MechanicalPic.HasValue && project.MechanicalPic.Value == userId ||
                project.Fsepic.HasValue && project.Fsepic.Value == userId ||
                project.SoftwarePic.HasValue && project.SoftwarePic.Value == userId ||
                project.SalesPic.HasValue && project.SalesPic.Value == userId ||
                !String.IsNullOrEmpty(project.PICS) && project.PICS.Contains(userId.ToString())
                )
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            context.Fail();
            return Task.CompletedTask;
        }
    }
}
