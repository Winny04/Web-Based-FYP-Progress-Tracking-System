using eForm_dotnetCore.Models;


namespace eForm_dotnetCore.Services.AuditTrails
{
    public interface IAuditTrailService
    {
        Task DeleteTrailAsync(int? id);
        Task DeleteTrailsAsync(IList<AuditTrail> auditTrails);
        Task ClearTrailAsync(DateTime? olderThan = null);
        Task<IList<AuditTrail>> GetAllTrailAsync(DateTime? form = null,DateTime? to = null,
            string message = "", Common.LogLevel? logLevel = null);
        Task<AuditTrail> InsertLogAsync(Common.LogLevel logLevel,string shortMessage, int ProjectId, List<int>? pics = null,
            string fullMessage ="",string tableChange = "",string dataBefore = "",
            string dataAfter = "", int? empId = null);

        Task CreateTags(int auditId, List<int> empIds);
    }
}
