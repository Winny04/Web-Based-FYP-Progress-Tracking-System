using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using Microsoft.AspNetCore.Authorization;

namespace eForm_dotnetCore.Configuration.Auhtorization
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
                if (resource.RequireValue == "All")
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }

                var userType = context.User.Claims.Single(x => x.Type == "Type").Value;
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

                if (!string.IsNullOrEmpty(resource.RequireValue))
                {
                    var listTypes = resource.RequireValue.Trim().Split(",");
                    if (listTypes.Contains(userType))
                    {
                        context.Succeed(requirement);
                        return Task.CompletedTask;
                    }
                }

                var project = resource.Resource;
                if (resource.Resource != null && !string.IsNullOrEmpty(resource.Resource.ProjectPIC))
                {
                    var pics = !string.IsNullOrEmpty(project.ProjectPIC) ? project.ProjectPIC.Split(',') : null;
                    var shortNameWithDepartment = context.User.Claims.FirstOrDefault(x => x.Type == "ShortName").Value + "(" + context.User.Claims.FirstOrDefault(x => x.Type == "Department") + ")";
                    if (pics.Contains(shortNameWithDepartment))
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
