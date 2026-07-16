using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;

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

            var typeClaim = user.Claims.FirstOrDefault(c => c.Type == "Type")?.Value;
            if (string.IsNullOrEmpty(typeClaim))
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                return;
            }

            if (!_allowedTypes.Contains(typeClaim))
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
