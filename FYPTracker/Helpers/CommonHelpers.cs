using FYPTracker.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.RegularExpressions;

namespace FYPTracker.Helpers
{
    public class CommonHelpers
    {
        private static IHttpContextAccessor httpContextAccessor;
        private static IWebHostEnvironment _env;

        public static void SetHttpContextAccessor(
            IHttpContextAccessor accessor,
            IWebHostEnvironment env)
        {
            httpContextAccessor = accessor;
            _env = env;
        }

        public static string RandomString(int size)
        {
            Random random = new Random((int)DateTime.Now.Ticks);
            StringBuilder builder = new StringBuilder();
            char ch;
            for (int i = 0; i < size; i++)
            {
                ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * random.NextDouble() + 65)));
                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static bool isImage(string type)
        {
            var imageExtension = new List<string> { ".png", ".jpeg", ".jpg" };
            return imageExtension.Contains(type);
        }

        public static string generateUID()
        {
            var uid = Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
            return uid;
        }
    }
}
