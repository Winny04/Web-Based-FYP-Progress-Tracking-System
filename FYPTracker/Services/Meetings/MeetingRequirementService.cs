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

            var originalSession = await _context.AcademicSessions
                .FirstOrDefaultAsync(s => s.Name == project.AcademicSession);

            if (originalSession == null) return null;

            AcademicSession stageSession = originalSession;
            if (stage == 2)
            {
                if (originalSession.StartDate.HasValue)
                {
                    stageSession = await _context.AcademicSessions
                        .Where(s => s.IsActive && s.Id != originalSession.Id && s.StartDate != null && s.StartDate > originalSession.StartDate)
                        .OrderBy(s => s.StartDate)
                        .FirstOrDefaultAsync() ?? originalSession;
                }
                else
                {
                    stageSession = await _context.AcademicSessions
                        .Where(s => s.IsActive && s.Id > originalSession.Id)
                        .OrderBy(s => s.Id)
                        .FirstOrDefaultAsync() ?? originalSession;
                }
            }

            int? requiredCount = stage == 1
                ? originalSession.RequiredMeetingsStage1
                : (stageSession.RequiredMeetingsStage2 ?? originalSession.RequiredMeetingsStage2);

            var completedCount = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .Where(m => 
                    m.StudentId == studentId &&
                    m.Stage == stage &&
                    m.Status == "Approved" &&
                    m.MeetingLog != null &&
                    m.MeetingLog.LogStatus == "Signed"
                )
                .Select(m => m.Id)
                .Distinct()
                .CountAsync();

            return new MeetingProgressVM
            {
                Required = requiredCount,
                Completed = completedCount,
                SessionName = stageSession.Name,
                Stage = stage
            };
        }
    }
}
