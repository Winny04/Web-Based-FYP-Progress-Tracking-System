using System;
using System.Collections.Generic;
using System.Linq;
using FYPTracker.Models;

namespace FYPTracker.Services.Defense
{
    public class AlternativeTimeRequestResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public IAFAlternativeTimeRequest? Request { get; set; }

        public static AlternativeTimeRequestResult Ok(IAFAlternativeTimeRequest request) =>
            new() { Success = true, Request = request };

        public static AlternativeTimeRequestResult Fail(string errorCode, string errorMessage) =>
            new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
    }

    /// <summary>
    /// Pure, database-independent validation and state machine engine for IAF Defense Alternative Time Requests.
    /// Enforces all Step 36.8-B3.1 business, lifecycle, ownership, and safety rules without requiring a database connection.
    /// </summary>
    public static class IAFAlternativeTimeValidationEngine
    {
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            IAFAlternativeTimeRequest.StatusPending,
            IAFAlternativeTimeRequest.StatusSuggested,
            IAFAlternativeTimeRequest.StatusAccepted,
            IAFAlternativeTimeRequest.StatusRejected,
            IAFAlternativeTimeRequest.StatusCancelled
        };

        // Allowed state transitions
        private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            // From Pending: Lecturer can Suggest, or Admin can Cancel
            [IAFAlternativeTimeRequest.StatusPending] = new(StringComparer.OrdinalIgnoreCase)
            {
                IAFAlternativeTimeRequest.StatusSuggested,
                IAFAlternativeTimeRequest.StatusCancelled
            },
            // From Suggested: Admin can Accept (only with committed booking confirmation), Reject, or Cancel
            [IAFAlternativeTimeRequest.StatusSuggested] = new(StringComparer.OrdinalIgnoreCase)
            {
                IAFAlternativeTimeRequest.StatusAccepted,
                IAFAlternativeTimeRequest.StatusRejected,
                IAFAlternativeTimeRequest.StatusCancelled
            },
            // Terminal states: no further transitions permitted
            [IAFAlternativeTimeRequest.StatusAccepted] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [IAFAlternativeTimeRequest.StatusRejected] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [IAFAlternativeTimeRequest.StatusCancelled] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        /// <summary>
        /// Validates creation of an alternative time request by Admin.
        /// </summary>
        public static AlternativeTimeRequestResult ValidateCreateRequest(
            int milestoneId,
            int attemptNumber,
            int academicSessionId,
            int requestedLecturerId,
            string requestedLecturerRole,
            int requestedByAdminId,
            bool isAdminUser,
            int supervisorId,
            IEnumerable<IAFAlternativeTimeRequest>? existingRequestsForMilestoneAndAttempt,
            string? adminNotes = null)
        {
            if (milestoneId <= 0)
                return AlternativeTimeRequestResult.Fail("InvalidMilestone", "Milestone ID must be greater than zero.");

            if (attemptNumber <= 0)
                return AlternativeTimeRequestResult.Fail("InvalidAttempt", "Attempt number must be greater than zero.");

            if (academicSessionId <= 0)
                return AlternativeTimeRequestResult.Fail("InvalidSession", "Academic session ID must be greater than zero.");

            if (requestedByAdminId <= 0 || !isAdminUser)
                return AlternativeTimeRequestResult.Fail("Unauthorized", "Only authorized administrators can initiate an alternative time request.");

            if (requestedLecturerId <= 0)
                return AlternativeTimeRequestResult.Fail("InvalidLecturer", "A valid lecturer must be designated for the request.");

            if (string.IsNullOrWhiteSpace(requestedLecturerRole) ||
                (!string.Equals(requestedLecturerRole, "Examiner", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(requestedLecturerRole, "Supervisor", StringComparison.OrdinalIgnoreCase)))
            {
                return AlternativeTimeRequestResult.Fail("InvalidRole", "Requested lecturer role must be 'Examiner' or 'Supervisor'.");
            }

            // Examiner Independence Check: Original supervisor cannot serve as independent examiner
            if (string.Equals(requestedLecturerRole, "Examiner", StringComparison.OrdinalIgnoreCase) &&
                supervisorId > 0 && requestedLecturerId == supervisorId)
            {
                return AlternativeTimeRequestResult.Fail("SupervisorCannotExamine", "The student's original supervisor cannot serve as the independent examiner for this defense.");
            }

            // Duplicate Prevention: Reject if an active (Pending or Suggested) request already exists for this milestone, attempt, and lecturer
            if (existingRequestsForMilestoneAndAttempt != null)
            {

                bool hasActiveDuplicate = existingRequestsForMilestoneAndAttempt.Any(r =>
                    r.MilestoneId == milestoneId &&
                    r.AttemptNumber == attemptNumber &&
                    (string.Equals(
                        r.Status,
                        IAFAlternativeTimeRequest.StatusPending,
                        StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                        r.Status,
                        IAFAlternativeTimeRequest.StatusSuggested,
                        StringComparison.OrdinalIgnoreCase)));


                if (hasActiveDuplicate)
                {
                    return AlternativeTimeRequestResult.Fail("DuplicateActiveRequest", "An active alternative time request already exists for this milestone attempt and lecturer.");
                }
            }

            var newRequest = new IAFAlternativeTimeRequest
            {
                MilestoneId = milestoneId,
                AttemptNumber = attemptNumber,
                AcademicSessionId = academicSessionId,
                RequestedLecturerId = requestedLecturerId,
                RequestedLecturerRole = requestedLecturerRole,
                RequestedByAdminId = requestedByAdminId,
                AdminNotes = adminNotes?.Trim(),
                Status = IAFAlternativeTimeRequest.StatusPending,
                CreatedAt = DateTime.Now
            };

            return AlternativeTimeRequestResult.Ok(newRequest);
        }

        /// <summary>
        /// Validates a suggestion submitted by the designated Lecturer.
        /// Suggested times are proposals only: they do not create bookings or date overrides.
        /// </summary>
        public static AlternativeTimeRequestResult ValidateLecturerSuggestion(
            IAFAlternativeTimeRequest request,
            int respondingUserId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            int durationMinutes,
            TimeSpan workingHoursStart,
            TimeSpan workingHoursEnd,
            string? lecturerNotes = null)
        {
            if (request == null)
                return AlternativeTimeRequestResult.Fail("NullRequest", "Target request cannot be null.");

            // Status Check: Can only suggest when currently Pending
            if (!string.Equals(request.Status, IAFAlternativeTimeRequest.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail("InvalidStatusTransition", $"Cannot submit a suggestion for a request in '{request.Status}' status (expected 'Pending').");
            }

            // Ownership Check: Only the requested lecturer can respond
            if (respondingUserId != request.RequestedLecturerId)
            {
                return AlternativeTimeRequestResult.Fail("UnauthorizedLecturer", "Only the assigned lecturer is authorized to propose an alternative time for this request.");
            }

            // Date validation: Date must not be in the past
            if (suggestedDate.Date < DateTime.Today)
            {
                return AlternativeTimeRequestResult.Fail("PastDate", "Suggested defense date cannot be in the past.");
            }

            // Duration validation
            if (durationMinutes < 15 || durationMinutes > 180)
            {
                return AlternativeTimeRequestResult.Fail("InvalidDuration", "Defense duration must be between 15 and 180 minutes.");
            }

            TimeSpan suggestedEndTime = suggestedStartTime.Add(TimeSpan.FromMinutes(durationMinutes));

            // Calendar day boundaries
            if (suggestedStartTime < TimeSpan.Zero || suggestedEndTime > TimeSpan.FromHours(24) || suggestedEndTime <= suggestedStartTime)
            {
                return AlternativeTimeRequestResult.Fail("InvalidTimeRange", "Suggested time interval must fit within a single calendar day (00:00 to 24:00).");
            }

            // Working hours validation: Must fit within configured working hours
            if (workingHoursStart >= workingHoursEnd)
            {
                return AlternativeTimeRequestResult.Fail("InvalidWorkingHoursConfig", "Authoritative working hours configuration is invalid.");
            }

            if (suggestedStartTime < workingHoursStart || suggestedEndTime > workingHoursEnd)
            {
                return AlternativeTimeRequestResult.Fail("OutsideWorkingHours", $"Suggested defense time ({suggestedStartTime:hh\\:mm} - {suggestedEndTime:hh\\:mm}) falls outside configured working hours ({workingHoursStart:hh\\:mm} - {workingHoursEnd:hh\\:mm}).");
            }

            // Populate proposal details without altering booking records or creating availability
            request.SuggestedDate = suggestedDate.Date;
            request.SuggestedStartTime = suggestedStartTime;
            request.SuggestedEndTime = suggestedEndTime;
            request.SuggestedDurationMinutes = durationMinutes;
            request.LecturerNotes = lecturerNotes?.Trim();
            request.Status = IAFAlternativeTimeRequest.StatusSuggested;
            request.RespondedAt = DateTime.Now;

            return AlternativeTimeRequestResult.Ok(request);
        }

        /// <summary>
        /// Validates the transition to Accepted status.
        /// Acceptance is strictly prohibited unless an authoritative committed booking has been verified.
        /// </summary>
        public static AlternativeTimeRequestResult ValidateAcceptSuggestion(
            IAFAlternativeTimeRequest request,
            int adminUserId,
            bool isAdminUser,
            bool isCommittedBookingConfirmed,
            int? confirmedBookingId)
        {
            if (request == null)
                return AlternativeTimeRequestResult.Fail("NullRequest", "Target request cannot be null.");

            if (adminUserId <= 0 || !isAdminUser)
                return AlternativeTimeRequestResult.Fail("Unauthorized", "Only administrators can accept an alternative time suggestion.");

            if (!string.Equals(request.Status, IAFAlternativeTimeRequest.StatusSuggested, StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail("InvalidStatusTransition", $"Cannot accept a request in '{request.Status}' status (expected 'Suggested').");
            }

            // Critical Guard: Direct acceptance without a committed booking is strictly forbidden
            if (!isCommittedBookingConfirmed || !confirmedBookingId.HasValue || confirmedBookingId.Value <= 0)
            {
                return AlternativeTimeRequestResult.Fail("BookingNotCommitted", "An alternative time suggestion cannot be marked 'Accepted' without a verified, successfully committed booking from the authoritative booking engine.");
            }

            request.Status = IAFAlternativeTimeRequest.StatusAccepted;
            request.FinalizedAt = DateTime.Now;
            request.FinalizedByAdminId = adminUserId;
            request.DecisionReason = $"Accepted with Booking #{confirmedBookingId.Value}";

            return AlternativeTimeRequestResult.Ok(request);
        }


        /// <summary>
        /// Checks whether a booking matches an IAF alternative time suggestion.
        /// Pure validation only. Does not modify request, booking, or database.
        /// The caller must obtain the booking from an authoritative server-side source.
        /// </summary>
        public static AlternativeTimeRequestResult ValidateBookingForAcceptance(
            IAFAlternativeTimeRequest request,
            IAFDefenseBooking booking)
        {
            if (request == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "NullRequest",
                    "Alternative time request cannot be null.");
            }

            if (booking == null || booking.Id <= 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "InvalidBooking",
                    "A valid persisted booking is required.");
            }

            if (!string.Equals(
                request.Status,
                IAFAlternativeTimeRequest.StatusSuggested,
                StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail(
                    "InvalidRequestStatus",
                    "Only a Suggested request can be accepted.");
            }

            if (request.MilestoneId != booking.MilestoneId ||
                request.AttemptNumber != booking.AttemptNumber ||
                request.AcademicSessionId != booking.AcademicSessionId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "BookingIdentityMismatch",
                    "Booking does not match the request's milestone, attempt or academic session.");
            }

            if (!string.Equals(
                request.RequestedLecturerRole,
                "Examiner",
                StringComparison.OrdinalIgnoreCase) ||
                request.RequestedLecturerId != booking.ExaminerId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ExaminerMismatch",
                    "Booking examiner does not match the requested examiner.");
            }

            if (!request.SuggestedDate.HasValue ||
                !request.SuggestedStartTime.HasValue ||
                !request.SuggestedEndTime.HasValue ||
                !request.SuggestedDurationMinutes.HasValue)
            {
                return AlternativeTimeRequestResult.Fail(
                    "MissingSuggestion",
                    "The request does not contain a complete suggested time.");
            }

            if (request.SuggestedDate.Value.Date != booking.DefenseDate.Date ||
                request.SuggestedStartTime.Value != booking.StartTime ||
                request.SuggestedEndTime.Value != booking.EndTime ||
                request.SuggestedDurationMinutes.Value != booking.DurationMinutes)
            {
                return AlternativeTimeRequestResult.Fail(
                    "BookingTimeMismatch",
                    "Booking date, time or duration does not match the lecturer's suggestion.");
            }

            if (booking.EndTime <= booking.StartTime ||
                booking.DurationMinutes <= 0 ||
                booking.EndTime - booking.StartTime !=
                    TimeSpan.FromMinutes(booking.DurationMinutes))
            {
                return AlternativeTimeRequestResult.Fail(
                    "InvalidBookingTime",
                    "Booking time interval or duration is inconsistent.");
            }

            if (booking.BookingStatus != "Scheduled" &&
                booking.BookingStatus != "Rescheduled")
            {
                return AlternativeTimeRequestResult.Fail(
                    "InvalidBookingStatus",
                    "Booking must be Scheduled or Rescheduled.");
            }

            return AlternativeTimeRequestResult.Ok(request);
        }

        public static AlternativeTimeRequestResult ValidateRejectSuggestion(
            IAFAlternativeTimeRequest request,
            int adminUserId,
            bool isAdminUser,
            string? rejectionReason)
        {
            if (request == null)
                return AlternativeTimeRequestResult.Fail("NullRequest", "Target request cannot be null.");

            if (adminUserId <= 0 || !isAdminUser)
                return AlternativeTimeRequestResult.Fail("Unauthorized", "Only administrators can reject an alternative time suggestion.");

            if (!string.Equals(request.Status, IAFAlternativeTimeRequest.StatusSuggested, StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail("InvalidStatusTransition", $"Cannot reject a request in '{request.Status}' status (expected 'Suggested').");
            }

            request.Status = IAFAlternativeTimeRequest.StatusRejected;
            request.FinalizedAt = DateTime.Now;
            request.FinalizedByAdminId = adminUserId;
            request.DecisionReason = rejectionReason?.Trim() ?? "Rejected by Administrator.";

            return AlternativeTimeRequestResult.Ok(request);
        }

        /// <summary>
        /// Validates cancellation of an open request by Admin.
        /// Allowed only when request is currently Pending or Suggested.
        /// </summary>
        public static AlternativeTimeRequestResult ValidateCancelRequest(
            IAFAlternativeTimeRequest request,
            int adminUserId,
            bool isAdminUser,
            string? cancellationReason)
        {
            if (request == null)
                return AlternativeTimeRequestResult.Fail("NullRequest", "Target request cannot be null.");

            if (adminUserId <= 0 || !isAdminUser)
                return AlternativeTimeRequestResult.Fail("Unauthorized", "Only administrators can cancel an alternative time request.");

            if (!string.Equals(request.Status, IAFAlternativeTimeRequest.StatusPending, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Status, IAFAlternativeTimeRequest.StatusSuggested, StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail("InvalidStatusTransition", $"Cannot cancel a request in '{request.Status}' status (can only cancel 'Pending' or 'Suggested').");
            }

            request.Status = IAFAlternativeTimeRequest.StatusCancelled;
            request.FinalizedAt = DateTime.Now;
            request.FinalizedByAdminId = adminUserId;
            request.DecisionReason = cancellationReason?.Trim() ?? "Cancelled by Administrator.";

            return AlternativeTimeRequestResult.Ok(request);
        }

        /// <summary>
        /// Evaluates whether a suggestion requires revalidation against current live availability and conflicts.
        /// Suggestions must never bypass authoritative revalidation before scheduling.
        /// </summary>
        public static bool RequiresAuthoritativeRevalidation(IAFAlternativeTimeRequest request)
        {
            if (request == null) return false;
            return string.Equals(request.Status, IAFAlternativeTimeRequest.StatusSuggested, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates that required participants comply with policy when reviewing or preparing a suggestion for scheduling.
        /// If supervisor is required, both examiner and supervisor IDs must be present.
        /// </summary>
        public static (bool Valid, List<int> RequiredLecturerIds, string? ErrorCode, string? ErrorMessage) ResolveParticipantsForSuggestion(
            int examinerId,
            bool isSupervisorRequired,
            int? supervisorId)
        {
            if (examinerId <= 0)
            {
                return (false, new List<int>(), "InvalidExaminer", "Independent examiner must be specified.");
            }

            if (isSupervisorRequired)
            {
                if (!supervisorId.HasValue || supervisorId.Value <= 0)
                {
                    return (false, new List<int>(), "SupervisorRequired", "Supervisor attendance is required for this defense, but no valid supervisor is assigned.");
                }

                if (examinerId == supervisorId.Value)
                {
                    return (false, new List<int>(), "SupervisorCannotExamine", "The student's original supervisor cannot serve as the independent examiner for this defense.");
                }

                return (true, new List<int> { examinerId, supervisorId.Value }, null, null);
            }

            return (true, new List<int> { examinerId }, null, null);
        }

        /// <summary>
        /// Verifies whether an arbitrary status transition is formally permitted.
        /// </summary>
        public static bool CanTransition(string currentStatus, string targetStatus)
        {
            if (string.IsNullOrWhiteSpace(currentStatus) || string.IsNullOrWhiteSpace(targetStatus))
                return false;

            if (!ValidTransitions.TryGetValue(currentStatus, out var allowedTargets))
                return false;

            return allowedTargets.Contains(targetStatus);
        }
    }
}
