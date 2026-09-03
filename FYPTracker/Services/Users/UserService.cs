using FYPTracker.Models;
using FYPTracker.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FYPTracker.Services.Employees
{
    public partial class UserService : IUserService
    {
        private readonly FYPTrackerContext _context;
        private readonly IAuthenticationService _authenticationService;

        public UserService(FYPTrackerContext context, IAuthenticationService authenticationService)
        {
            _context = context;
            _authenticationService = authenticationService;
        }

        public virtual async Task<ApplicationUser> GetEmpByUserNameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            var query = from e in _context.ApplicationUsers
                        where e.UserName == username
                        select e;

            var emp = await query.Include(x => x.EmpDepartment).Include(x => x.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync();

            if(emp == null)
            {
                 query = from e in _context.ApplicationUsers
                            where e.Email == username
                            select e;
                 emp = await query.Include(x => x.EmpDepartment).Include(x => x.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync();

            }

            return emp;
        }

        public virtual async Task<IActionResult> SignInEmployeeAsync(ApplicationUser employee, bool isPersist = true)
        {
            await _authenticationService.SignInAsync(employee, isPersist);

            return new OkResult();
        }

        public virtual async Task<bool> ValidateEmployeeAsync(string username,string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
            return await _context.ApplicationUsers.Where(x => (x.UserName == username || x.Email == username) && x.PassCode == GetHash(password) && !x.IsResign).FirstOrDefaultAsync() != null;
        }

        public static string GetHash(string password)
        {
            MD5 md5 = MD5.Create();

            byte[] hashData = md5.ComputeHash(Encoding.Default.GetBytes(password));

            StringBuilder returnValue = new StringBuilder();

            for (int i = 0; i < hashData.Length; i++)
            {
                returnValue.Append(hashData[i].ToString());
            }

            return returnValue.ToString();
        }
    }
}



