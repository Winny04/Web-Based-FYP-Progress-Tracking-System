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
    public partial class EmployeeService : IEmployeeService
    {
        private readonly FYPTrackerContext _context;
        private readonly IAuthenticationService _authenticationService;

        public EmployeeService(FYPTrackerContext context, IAuthenticationService authenticationService)
        {
            _context = context;
            _authenticationService = authenticationService;
        }

        public virtual async Task<EmployeeInfo> GetEmpByUserNameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            var query = from e in _context.EmployeeInfos
                        where e.UserName == username
                        select e;

            var emp = await query.Include(x => x.EmpDepartment).FirstOrDefaultAsync();

            if(emp == null)
            {
                 query = from e in _context.EmployeeInfos
                            where e.Email == username
                            select e;
                 emp = await query.Include(x => x.EmpDepartment).FirstOrDefaultAsync();

            }

            return emp;
        }

        public virtual async Task<IActionResult> SignInEmployeeAsync(EmployeeInfo employee, bool isPersist = true)
        {
            await _authenticationService.SignInAsync(employee, isPersist);

            return new OkResult();
        }

        public virtual async Task<bool> ValidateEmployeeAsync(string username,string password)
        {
            return await _context.EmployeeInfos.Where(x => (x.UserName == username || x.Email == username) && x.PassCode == GetHash(password) && !x.IsResign).FirstOrDefaultAsync() != null;
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
