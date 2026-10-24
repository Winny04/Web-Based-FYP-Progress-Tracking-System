using System;
using System.Threading.Tasks;
using FYPTracker.Models;

namespace FYPTracker.Services.Meetings
{
    public class StageBookingWindow
    {
        public bool IsValid { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? ErrorMessage { get; set; }
        public AcademicSession? Session { get; set; }
    }

    public interface IAcademicSessionResolver
    {
        Task<AcademicSession?> GetStudentSessionAsync(int studentId);
        Task<AcademicSession?> GetStageSessionAsync(int studentId, int stage);
        Task<DateTime?> GetMeetingBookingCutoffAsync(int studentId, int stage);
        Task<StageBookingWindow> GetStageBookingWindowAsync(int studentId, int stage);
    }
}

