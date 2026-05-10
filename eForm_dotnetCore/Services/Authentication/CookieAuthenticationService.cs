using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.Employees;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace eForm_dotnetCore.Services.Authentication
{
    public partial class CookieAuthenticationService: IAuthenticationService
    {
        #region Fields
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;

        private EmployeeInfo _cachedEmp;

        #endregion

        #region Ctor
        public CookieAuthenticationService(IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
        {
            _httpContextAccessor = httpContextAccessor;
            _env = env;
        }

        #endregion

        #region Methods
        public virtual async Task SignInAsync(EmployeeInfo employee, bool isPersistent)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            //create claims for customer's username and email
            var claims = new List<Claim>()
            {
                new Claim("Id",employee.Id.ToString()),
                new Claim("Type",employee.UserType ?? String.Empty),
                new Claim("DepartmentId",employee?.DepartmentId.ToString() ?? String.Empty),
                new Claim("Department",employee.EmpDepartment?.Name ?? String.Empty),
                new Claim("ShortName",employee.ShortName ?? String.Empty),
                new Claim("EmployeeId",employee.EmployeeId ?? String.Empty)
            };

            if (!string.IsNullOrEmpty(employee.UserName))
                claims.Add(new Claim(ClaimTypes.Name, employee.UserName, ClaimValueTypes.String, AuthenticationDefaults.ClaimsIssuer));

            if (!string.IsNullOrEmpty(employee.Email))
                claims.Add(new Claim(ClaimTypes.Email, employee.Email, ClaimValueTypes.Email, AuthenticationDefaults.ClaimsIssuer));

            //create principal for the current authentication scheme
            var userIdentity = new ClaimsIdentity(claims, AuthenticationDefaults.AuthenticationScheme);
            var userPrincipal = new ClaimsPrincipal(userIdentity);

            var authenticationProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                IssuedUtc = DateTime.UtcNow
            };

            //sign in
            await _httpContextAccessor.HttpContext.SignInAsync(AuthenticationDefaults.AuthenticationScheme, userPrincipal, authenticationProperties);

            //cache authenticated customer
            _cachedEmp = employee;

        }

        public virtual async Task SignOutAsync()
        {
            _cachedEmp = null;

            //and sign out from the current authentication scheme
            await _httpContextAccessor.HttpContext.SignOutAsync(AuthenticationDefaults.AuthenticationScheme);
        }
        #endregion

    }
}
