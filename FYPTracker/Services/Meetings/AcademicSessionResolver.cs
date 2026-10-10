using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;

namespace FYPTracker.Services.Meetings
{
    public class AcademicSessionResolver : IAcademicSessionResolver
    {
        private readonly FYPTrackerContext _context;

        public AcademicSessionResolver(FYPTrackerContext context)
        {
            _context = context;
        }

        public async Task<AcademicSession?> GetStudentSessionAsync(int studentId)
        {
            // First try active project
            var sessionName = await _context.FYPProjects
                .Where(p => p.StudentId == studentId && p.Status == "Active")
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => p.AcademicSession)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(sessionName))
            {
                // Fallback to student's profile session
                sessionName = await _context.ApplicationUsers
                    .Where(u => u.Id == studentId)
                    .Select(u => u.AcademicSession)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrEmpty(sessionName))
                return null;

            return await _context.AcademicSessions
                .FirstOrDefaultAsync(s => s.Name == sessionName);
        }

        public async Task<DateTime?> GetMeetingBookingCutoffAsync(int studentId, int stage = 1)
        {
            var session = await GetStudentSessionAsync(studentId);

            if (session?.EndDate == null)
                return null;

            string targetCode = stage == 1 ? "STAGE1_FINAL" : "STAGE2_FINAL";

            // Find Stage Final Default Milestone for student's session
            var finalDueDate = await _context.DefaultMilestoneTemplates
                .Where(m =>
                    m.AcademicSessionId == session.Id &&
                    m.Stage == stage &&
                    m.MilestoneCode == targetCode &&
                    m.IsActive)
                .Select(m => m.DueDate)
                .FirstOrDefaultAsync();

            if (!finalDueDate.HasValue)
                return null;

            // All meetings must be scheduled at least 7 days before final due date
            var cutoff = finalDueDate.Value.Date.AddDays(-7);

            // Also respect Academic Session end date
            return cutoff < session.EndDate.Value.Date
                ? cutoff
                : session.EndDate.Value.Date;
        }
    }
}
