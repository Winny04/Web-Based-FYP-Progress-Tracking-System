using System.Security.Cryptography;
using System.Text;

namespace FYPTracker.Common
{
    public class Encryption
    {
        public static string MD5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                if (!String.IsNullOrEmpty(input))
                {
                    var result = md5.ComputeHash(Encoding.ASCII.GetBytes(input));
                    return Encoding.ASCII.GetString(result);
                }
                else
                {
                    return "";
                }
            }
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
