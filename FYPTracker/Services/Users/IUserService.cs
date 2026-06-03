using FYPTracker.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FYPTracker.Services.Employees
{
    public partial interface IUserService
    {
        /// <summary>
        /// Get customer by email
        /// </summary>
        /// <param name="email">Email</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the customer
        /// </returns>
        Task<ApplicationUser> GetEmpByUserNameAsync(string email);

        /// <summary>
        /// Login passed user
        /// </summary>
        /// <param name="employee">User to login</param>
        /// <param name="returnUrl">URL to which the user will return after authentication</param>
        /// <param name="isPersist">Is remember me</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result of an authentication
        /// </returns>
        Task<IActionResult> SignInEmployeeAsync(ApplicationUser employee, bool isPersist = true);

        Task<bool> ValidateEmployeeAsync(string username, string password);
    }
}



