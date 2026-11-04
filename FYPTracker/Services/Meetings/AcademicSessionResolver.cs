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
            // First try active or approved project
            var sessionName = await _context.FYPProjects
                .Where(p => p.StudentId == studentId && (p.Status == "Active" || p.Status == "Approved"))
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => p.AcademicSession)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(sessionName))
            {
                // Fallback to any project for student
                sessionName = await _context.FYPProjects
                    .Where(p => p.StudentId == studentId)
                    .OrderByDescending(p => p.CreatedDate)
                    .Select(p => p.AcademicSession)
                    .FirstOrDefaultAsync();
            }

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

        public async Task<AcademicSession?> GetStageSessionAsync(int studentId, int stage = 1)
        {
            if (stage == 1)
            {
                return await GetStudentSessionAsync(studentId);
            }
            else if (stage == 2)
            {
                var originalSession = await GetStudentSessionAsync(studentId);
                if (originalSession == null)
                    return null;

                AcademicSession? subsequentSession = null;
                if (originalSession.StartDate.HasValue)
                {
                    subsequentSession = await _context.AcademicSessions
                        .Where(s => s.IsActive && s.Id != originalSession.Id && s.StartDate != null && s.StartDate > originalSession.StartDate)
                        .OrderBy(s => s.StartDate)
                        .FirstOrDefaultAsync();
                }

                if (subsequentSession == null)
                {
                    subsequentSession = await _context.AcademicSessions
                        .Where(s => s.IsActive && s.Id > originalSession.Id)
                        .OrderBy(s => s.Id)
                        .FirstOrDefaultAsync();
                }

                return subsequentSession;
            }

            return null;
        }

        public async Task<StageBookingWindow> GetStageBookingWindowAsync(int studentId, int stage = 1)
        {
            var project = await _context.FYPProjects
                .Where(p => p.StudentId == studentId && (p.Status == "Active" || p.Status == "Approved"))
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync();

            if (stage == 1)
            {
                var session = await GetStageSessionAsync(studentId, 1);
                if (session == null || !session.StartDate.HasValue || !session.EndDate.HasValue)
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "There is no active Academic Session configured with valid dates."
                    };
                }

                DateTime startDate = session.StartDate.Value.Date;

                // Stage 1 Cutoff: STAGE1_FINAL deadline minus 7 days, capped at session.EndDate
                DateTime? s1DueDate = null;
                if (project != null)
                {
                    s1DueDate = await _context.FYPMilestones
                        .Where(m => m.FYPProjectId == project.Id && m.Stage == 1 &&
                                    (m.MilestoneCode == "STAGE1_FINAL" || m.Title.Contains("Stage 1 Final") || m.Title.Contains("Proposal")) &&
                                    m.DueDate != null)
                        .OrderByDescending(m => m.DueDate)
                        .Select(m => m.DueDate)
                        .FirstOrDefaultAsync();
                }

                if (!s1DueDate.HasValue)
                {
                    s1DueDate = await _context.DefaultMilestoneTemplates
                        .Where(m => m.AcademicSessionId == session.Id && m.Stage == 1 &&
                                    m.MilestoneCode == "STAGE1_FINAL" && m.IsActive)
                        .Select(m => m.DueDate)
                        .FirstOrDefaultAsync();
                }

                DateTime cutoff;
                if (s1DueDate.HasValue)
                {
                    cutoff = s1DueDate.Value.Date.AddDays(-7);
                    if (cutoff > session.EndDate.Value.Date)
                        cutoff = session.EndDate.Value.Date;
                }
                else
                {
                    cutoff = session.EndDate.Value.Date;
                }

                if (startDate > cutoff)
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "Configured Stage 1 meeting window is invalid (start date is after cutoff date)."
                    };
                }

                return new StageBookingWindow
                {
                    IsValid = true,
                    StartDate = startDate,
                    EndDate = cutoff,
                    Session = session
                };
            }
            else if (stage == 2)
            {
                // Business Rule: Stage 1 Pass controls whether Stage 2 is unlocked.
                if (project == null)
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "No active FYP project found for student."
                    };
                }

                if (project.Stage1Grade != "Pass")
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "Stage 2 meetings can begin only after Stage 1 is officially Passed."
                    };
                }

                // Stage 2 Academic Period: Use the configured subsequent Academic Session.
                var stage2Session = await GetStageSessionAsync(studentId, 2);
                if (stage2Session == null || !stage2Session.StartDate.HasValue || !stage2Session.EndDate.HasValue)
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "No subsequent Academic Session is configured for Stage 2. Please contact Admin."
                    };
                }

                DateTime stage2StartDate = stage2Session.StartDate.Value.Date;
                DateTime stage2EndDate = stage2Session.EndDate.Value.Date;

                if (stage2StartDate > stage2EndDate)
                {
                    return new StageBookingWindow
                    {
                        IsValid = false,
                        ErrorMessage = "Configured Stage 2 academic session has an invalid date range."
                    };
                }

                return new StageBookingWindow
                {
                    IsValid = true,
                    StartDate = stage2StartDate,
                    EndDate = stage2EndDate,
                    Session = stage2Session
                };
            }

            return new StageBookingWindow
            {
                IsValid = false,
                ErrorMessage = $"Invalid stage '{stage}' specified."
            };
        }

        public async Task<DateTime?> GetMeetingBookingCutoffAsync(int studentId, int stage = 1)
        {
            var window = await GetStageBookingWindowAsync(studentId, stage);
            return window.IsValid ? window.EndDate : null;
        }
    }
}
