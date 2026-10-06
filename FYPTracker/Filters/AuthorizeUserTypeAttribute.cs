using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;
using System.Security.Claims;

namespace FYPTracker.Filters
{
    public class AuthorizeUserTypeAttribute : ActionFilterAttribute
    {
        private readonly string[] _allowedTypes;

        public AuthorizeUserTypeAttribute(params string[] allowedTypes)
        {
            _allowedTypes = allowedTypes;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User;
            if (user == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new RedirectToActionResult("Login", "User", null);
                return;
            }

            var roleClaims = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
            if (!roleClaims.Any())
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                return;
            }

            if (!_allowedTypes.Intersect(roleClaims).Any())
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
