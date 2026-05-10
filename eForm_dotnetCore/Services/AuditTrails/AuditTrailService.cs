using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.Services.AuditTrails
{
    public class AuditTrailService : IAuditTrailService
    {
        private readonly EformSophicDemoContext _context;
        private readonly IWebHelper _webHelper;

        public AuditTrailService(
            EformSophicDemoContext context,
            IWebHelper webHelper)
        {
            _context = context;
            _webHelper = webHelper;
        }

        public virtual async Task ClearTrailAsync(DateTime? olderThan = null)
        {
            if (olderThan.HasValue)
            {
                var audits = _context.AuditTrail.Where(x => x.CreatedOn <= olderThan.Value).ToList();
                _context.AuditTrail.RemoveRange(audits);

                await _context.SaveChangesAsync();
            }
        }

        public virtual async Task DeleteTrailAsync(int? id)
        {
            if (!id.HasValue)
            {
                var audit = _context.AuditTrail.FirstOrDefault(x => x.Id == id);
                _context.AuditTrail.Remove(audit);

                await _context.SaveChangesAsync();
            }
        }

        public virtual async Task DeleteTrailsAsync(IList<AuditTrail> auditTrails)
        {
            _context.AuditTrail.RemoveRange(auditTrails);

            await _context.SaveChangesAsync();
        }

        public virtual async Task<IList<AuditTrail>> GetAllTrailAsync(DateTime? form = null, DateTime? to = null, string message = "", Common.LogLevel? logLevel = null)
        {
            var audits = _context.AuditTrail.AsQueryable();

            if (form.HasValue)
                audits = audits.Where(x => form.Value <= x.CreatedOn);
            if (form.HasValue)
                audits = audits.Where(x => to.Value >= x.CreatedOn);
            if (logLevel.HasValue)
            {
                var logLevelId = (int)logLevel.Value;
                audits = audits.Where(x => x.LogLevelId == logLevelId);
            }

            if (!string.IsNullOrEmpty(message))
                audits = audits.Where(x => x.ShortMessage.Contains(message) || x.FullMessage.Contains(message));
            audits = audits.OrderByDescending(x => x.CreatedOn);

            return audits.ToList();
        }

        public virtual async Task<AuditTrail> InsertLogAsync(Common.LogLevel logLevel, string shortMessage, int ProjectId, List<int>? pics =null, string fullMessage = "", string tableChange = "", string dataBefore = "", string dataAfter = "", int? empId = null)
        {
            var audit = new AuditTrail
            {
                LogLevel = logLevel,
                ShortMessage = shortMessage,
                FullMessage = fullMessage,
                IpAddress = _webHelper.GetCurrentIpAddress(),
                EmpId = empId,
                TableChange = tableChange,
                DataAfter = dataAfter,
                DataBefore = dataBefore,
                PageUrl = _webHelper.GetThisPageUrl(true),
                ReferrerUrl = _webHelper.GetUrlReferrer(),
                CreatedOn = DateTime.Now,
                ProjectId = ProjectId
            };

            await _context.AuditTrail.AddAsync(audit);
            await _context.SaveChangesAsync();

            if(pics != null)
            {
                await CreateTags(audit.Id, pics);
            }

            return audit;
        }

        public virtual async Task CreateTags(int auditId, List<int> empIds)
        {
            var tags = new List<AuditTag>();

            foreach (var empId in empIds)
            {
                tags.Add(new AuditTag
                {
                    AuditId = auditId,
                    EmployeeId = empId,
                    CreateAt = DateTime.Now,
                });
            }

            _context.AuditTag.AddRange(tags);
            await _context.SaveChangesAsync();
        }
    }
}
