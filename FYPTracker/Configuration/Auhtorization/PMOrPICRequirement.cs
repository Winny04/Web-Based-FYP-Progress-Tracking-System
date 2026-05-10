using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;

namespace FYPTracker.Configuration.Auhtorization
{
    /// <summary>
    /// Authorization handler: succeeds if the user is a Supervisor or Admin of the given FYP project.
    /// </summary>
    public class PMOrPICRequirement : AuthorizationHandler<PMOrPICRequirement, FYPProject>, IAuthorizationRequirement
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PMOrPICRequirement requirement,
            FYPProject project)
        {
            if (context.User.HasClaim(x => x.Type == "Type"))
            {
                var userType = context.User.Claims.FirstOrDefault(x => x.Type == "Type")?.Value;
                if (userType == "Admin" || userType == "Supervisor")
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            // Check if the logged-in user is the assigned supervisor
            var userIdClaim = context.User.Claims.FirstOrDefault(x => x.Type == "Id");
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                if (project.SupervisorId.HasValue && project.SupervisorId.Value == userId)
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
