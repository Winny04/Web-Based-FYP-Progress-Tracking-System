using System;
using System.Threading.Tasks;

namespace FYPTracker.Services.Meetings
{
    public interface IWeeklyMeetingSchedulingService
    {
        Task<(int ScheduledCount, int ConflictCount, string Message)> GenerateWeeklyScheduleAsync(int studentId, int supervisorId, int meetingId);
    }
}
