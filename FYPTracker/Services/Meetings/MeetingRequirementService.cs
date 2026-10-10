using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;
using FYPTracker.ViewModels;

namespace FYPTracker.Services.Meetings
{
    public interface IMeetingRequirementService
    {
        Task<MeetingProgressVM?> GetStudentProgressAsync(int studentId, int stage);
    }

    public class MeetingRequirementService : IMeetingRequirementService
    {
        private readonly FYPTrackerContext _context;

        public MeetingRequirementService(FYPTrackerContext context)
        {
            _context = context;
        }

        public async Task<MeetingProgressVM?> GetStudentProgressAsync(int studentId, int stage)
        {
            var project = await _context.FYPProjects
                .Where(p => p.StudentId == studentId && p.Status != "Archived")
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync();

            if (project == null || string.IsNullOrWhiteSpace(project.AcademicSession)) 
                return null;

            var session = await _context.AcademicSessions
                .FirstOrDefaultAsync(s => s.Name == project.AcademicSession);

            if (session == null) return null;

            int? requiredCount = stage == 1 ? session.RequiredMeetingsStage1 : session.RequiredMeetingsStage2;

            var completedCount = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .Where(m => 
                    m.StudentId == studentId &&
                    m.Stage == stage &&
                    m.Status == "Approved" &&
                    m.MeetingLog != null &&
                    m.MeetingLog.LogStatus == "Signed" &&
                    (m.AcademicSessionId == session.Id || (m.AcademicSessionId == null && m.FYPProjectId == project.Id))
                )
                .Select(m => m.Id)
                .Distinct()
                .CountAsync();

            return new MeetingProgressVM
            {
                Required = requiredCount,
                Completed = completedCount,
                SessionName = session.Name,
                Stage = stage
            };
        }
    }
}
