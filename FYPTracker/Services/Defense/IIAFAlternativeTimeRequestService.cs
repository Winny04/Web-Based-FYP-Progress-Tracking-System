using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FYPTracker.Models;

namespace FYPTracker.Services.Defense
{
    public interface IIAFAlternativeTimeRequestService
    {
        /// <summary>
        /// Retrieves an alternative time request by its primary key.
        /// </summary>
        Task<IAFAlternativeTimeRequest?> GetRequestByIdAsync(int requestId);

        /// <summary>
        /// Retrieves the currently active (Pending or Suggested) request for a milestone attempt.
        /// Returns null if no active request exists.
        /// </summary>
        Task<IAFAlternativeTimeRequest?> GetActiveRequestAsync(int milestoneId, int attemptNumber);

        /// <summary>
        /// Retrieves all requests associated with a milestone attempt (including finalized history).
        /// </summary>
        Task<List<IAFAlternativeTimeRequest>> GetRequestHistoryAsync(int milestoneId, int attemptNumber);

        /// <summary>
        /// Admin creates an alternative time request to an independent examiner/lecturer.
        /// Validates admin identity, prevents duplicate active requests, and verifies participant rules.
        /// </summary>
        Task<AlternativeTimeRequestResult> CreateRequestAsync(
            int milestoneId,
            int attemptNumber,
            int academicSessionId,
            int requestedLecturerId,
            string requestedLecturerRole,
            int currentAdminUserId,
            bool isAdminUser,
            int supervisorId,
            string? adminNotes);

        /// <summary>
        /// Assigned lecturer proposes an alternative date and time interval.
        /// Validates server-side lecturer identity, working hours, day boundaries, and concurrency token.
        /// NEVER auto-creates DateOverrides or bookings.
        /// </summary>
        Task<AlternativeTimeRequestResult> SubmitSuggestionAsync(
            int requestId,
            int currentLecturerUserId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            int durationMinutes,
            TimeSpan workingHoursStart,
            TimeSpan workingHoursEnd,
            string? lecturerNotes = null,
            byte[]? clientRowVersion = null);

        /// <summary>
        /// Admin marks a suggestion Accepted ONLY AFTER the authoritative booking service
        /// has committed a successful booking. Direct acceptance without a booking is strictly rejected.
        /// </summary>
        Task<AlternativeTimeRequestResult> FinalizeAcceptedAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            int confirmedBookingId,
            byte[]? clientRowVersion = null);

        /// <summary>
        /// Admin rejects a lecturer's suggestion.
        /// </summary>
        Task<AlternativeTimeRequestResult> RejectSuggestionAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            string reason,
            byte[]? clientRowVersion = null);

        /// <summary>
        /// Admin cancels a Pending or Suggested request.
        /// </summary>
        Task<AlternativeTimeRequestResult> CancelRequestAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            string reason,
            byte[]? clientRowVersion = null);
    }
}
