
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FYPTracker.Models
{
    /// <summary>
    /// EF Core design-time factory.
    /// Uses a non-production placeholder connection.
    /// Must never be used for database deployment.
    /// </summary>
    public class FYPTrackerDesignTimeDbContextFactory
        : IDesignTimeDbContextFactory<FYPTrackerContext>
    {
        public FYPTrackerContext CreateDbContext(string[] args)
        {
            var optionsBuilder =
                new DbContextOptionsBuilder<FYPTrackerContext>();

            // Deliberately invalid, non-production connection.
            // No database connection is opened here.
            optionsBuilder.UseSqlServer(
                "Server=127.0.0.1,1;" +
                "Database=__DESIGN_TIME_ONLY_DO_NOT_CONNECT__;" +
                "User Id=__INVALID_DESIGN_TIME_USER__;" +
                "Password=__INVALID_DESIGN_TIME_PASSWORD__;" +
                "Encrypt=False;" +
                "Connect Timeout=1;");

            return new FYPTrackerContext(optionsBuilder.Options);
        }
    }
}
