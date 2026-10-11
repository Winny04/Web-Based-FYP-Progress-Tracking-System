using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FYPTracker.Models;

namespace FYPTracker.Services.Meetings
{
    public interface IWeeklyMeetingSchedulingService
    {
        Task<int?> GetMeetingDurationMinutesAsync();

        Task<(bool HasConflict, string ErrorMessage)> ValidateMeetingConflictAsync(
            int supervisorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int academicSessionId,
            int? ignoreMeetingId = null);

        Task<string?> ValidateMeetingConflictAsync(
            int supervisorId,
            DateTime startDateTime,
            DateTime endDateTime,
            int? excludeMeetingId = null,
            int? sessionId = null);

        Task<(int ScheduledCount, int ConflictCount, string Message)> GenerateWeeklyScheduleAsync(
            int studentId,
            int supervisorId,
            int meetingId);

        Task<System.Collections.Generic.List<OverrideMeetingConflictDto>> DetectOverrideMeetingConflictsAsync(
            int supervisorId,
            DateTime overrideDate,
            TimeSpan overrideStart,
            TimeSpan overrideEnd,
            bool isAvailable,
            int? overrideId = null);

        Task<System.Collections.Generic.List<OverrideMeetingConflictDto>> GetSupervisorActiveOverrideMeetingConflictsAsync(
            int supervisorId);

        Task<System.Collections.Generic.List<OverrideMeetingConflictDto>> GetStudentActiveOverrideMeetingConflictsAsync(
            int studentId);
    }
}
