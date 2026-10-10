using System;
using System.Threading.Tasks;

namespace FYPTracker.Services.Meetings
{
    public interface IAcademicSessionResolver
    {
        Task<Models.AcademicSession?> GetStudentSessionAsync(int studentId);
        Task<DateTime?> GetMeetingBookingCutoffAsync(int studentId, int stage);
    }
}
