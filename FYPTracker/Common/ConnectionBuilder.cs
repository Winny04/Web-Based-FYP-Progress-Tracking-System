

namespace FYPTracker.Common
{
    public class ConnectionBuilder
    {
        public static string GetConnString()
        {
            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false);
            IConfiguration config = builder.Build();
            string dbConnString = config.GetValue<string>("ConnectionStrings:Entities");
            return dbConnString;
        }
    }
}
