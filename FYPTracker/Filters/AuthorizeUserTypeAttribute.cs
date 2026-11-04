using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using System;
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
            bool isAjaxOrJson = context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                                context.HttpContext.Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                if (isAjaxOrJson)
                {
                    context.Result = new JsonResult(new { success = false, message = "You must be logged in to perform this action." })
                    {
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                    return;
                }
                context.Result =
                    new RedirectToActionResult("Login", "User", null);
                return;
            }

            // Current selected workspace role
            var activeRole =
                context.HttpContext.Session.GetString("ActiveRole");

            // If no ActiveRole yet, use one of the authenticated role claims
            if (string.IsNullOrEmpty(activeRole))
            {
                activeRole = user.Claims
                    .Where(c => c.Type == ClaimTypes.Role)
                    .Select(c => c.Value)
                    .FirstOrDefault();
            }

            // The currently selected role must match this controller
            if (string.IsNullOrEmpty(activeRole) ||
                !_allowedTypes.Any(r =>
                    string.Equals(
                        r,
                        activeRole,
                        StringComparison.OrdinalIgnoreCase)))
            {
                if (isAjaxOrJson)
                {
                    context.Result = new JsonResult(new { success = false, message = "Access denied: insufficient permissions." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
                context.Result =
                    new RedirectToActionResult(
                        "AccessDenied",
                        "Home",
                        null);

                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
