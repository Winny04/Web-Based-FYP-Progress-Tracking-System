using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FYPTracker.Models;

namespace FYPTracker.Services.Defense
{
    public class DefenseBookingResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public IAFDefenseBooking? Booking { get; set; }
        public IAFDefenseHistory? HistoryRecord { get; set; }

        public static DefenseBookingResult Ok(IAFDefenseBooking booking, IAFDefenseHistory history) =>
            new DefenseBookingResult { Success = true, Booking = booking, HistoryRecord = history };

        public static DefenseBookingResult Fail(string errorCode, string errorMessage) =>
            new DefenseBookingResult { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
    }

    public class DefenseOutcomeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public IAFDefenseBooking? Booking { get; set; }
        public IAFDefenseHistory? HistoryRecord { get; set; }

        public static DefenseOutcomeResult Ok(IAFDefenseBooking booking, IAFDefenseHistory history) =>
            new DefenseOutcomeResult { Success = true, Booking = booking, HistoryRecord = history };

        public static DefenseOutcomeResult Fail(string errorCode, string errorMessage) =>
            new DefenseOutcomeResult { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
    }

    public class DefenseValidationContext
    {
        public FYPMilestone Milestone { get; set; } = null!;
        public int AttemptNumber { get; set; }
        public int AcademicSessionId { get; set; }
        public ApplicationUser Examiner { get; set; } = null!;
        public int? OriginalSupervisorId { get; set; }
        public bool IsSupervisorRequired { get; set; }
        public int DurationMinutes { get; set; }
        public DateTime DefenseDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Venue { get; set; } = string.Empty;
    }

    public interface IIAFDefenseBookingService
    {
        /// <summary>
        /// Atomically schedules or reschedules an IAF defense attempt using distributed SQL Server applocks.
        /// Revalidates all academic constraints and mutual lecturer availability inside the transaction.
        /// </summary>
        Task<DefenseBookingResult> ScheduleOrRescheduleDefenseAsync(
            int milestoneId,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            string venue,
            int currentUserId,
            string? changeReason = null);

        /// <summary>
        /// Validates scheduling rules and conflicts without persisting. Useful for preflight checks.
        /// </summary>
        Task<DefenseBookingResult> ValidateDefenseScheduleAsync(
            int milestoneId,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            string venue);

        /// <summary>
        /// Atomically records Pass or Fail for the current IAF Defense attempt.
        /// Synchronizes IAFDefenseBooking, IAFDefenseHistory, and FYPMilestone.
        /// </summary>
        Task<DefenseOutcomeResult> RecordDefenseOutcomeAsync(
            int milestoneId,
            string outcome, // "AdminApproved" or "ChangeOfTopicRequired"
            string? outcomeComments,
            int currentUserId,
            bool isEarlyOutcomeTestOverride = false);

        /// <summary>
        /// Resolves the current active IAF Defense booking for a milestone.
        /// </summary>
        Task<IAFDefenseBooking?> GetActiveBookingAsync(int milestoneId, int? attemptNumber = null);
    }
}
