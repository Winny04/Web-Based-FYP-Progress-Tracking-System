namespace FYPTracker.Helpers
{
    public static class HttpContextExtension
    {
        public static string GetUserId(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            var claim = httpContext.User.Claims.FirstOrDefault(x => x.Type == "Id");
            return claim != null ? claim.Value : string.Empty;
            //return httpContext.User.Claims.Single(x => x.Type == "Id").Value;
        }

        public static string GetUserType(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            var claim = httpContext.User.Claims.FirstOrDefault(x => x.Type == "Type");
            return claim != null ? claim.Value : string.Empty;
            //return httpContext.User.Claims.Single(x => x.Type == "Type").Value;
        }

        public static string GetUserDept(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            return httpContext.User.Claims.FirstOrDefault(x => x.Type == "Department")?.Value ?? string.Empty;
        }

        public static string GetUserDeptId(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            return httpContext.User.Claims.FirstOrDefault(x => x.Type == "DepartmentId")?.Value ?? string.Empty;
        }

        public static string GetFullName(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            var claim = httpContext.User.Claims.FirstOrDefault(x => x.Type == "FullName");
            return claim != null ? claim.Value : string.Empty;
        }

        public static string GetUserEmpId(this HttpContext httpContext)
        {
            if (httpContext.User == null)
                return string.Empty;

            return httpContext.User.Claims.FirstOrDefault(x => x.Type == "EmployeeId")?.Value ?? string.Empty;
        }
    }
}
