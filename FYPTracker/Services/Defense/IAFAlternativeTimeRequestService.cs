using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace FYPTracker.Services.Defense
{
    /// <summary>
    /// Backend service managing the lifecycle and persistence contracts of IAF Alternative Time Requests.
    /// Integrates authoritative rules from IAFAlternativeTimeValidationEngine.
    /// 
    /// Note on Phase B3.2 boundaries:
    /// This service contains the full mapping and state machine logic.
    /// Database table 'IAFAlternativeTimeRequest' will be physically created in a future migration phase.
    /// No writes or migration executions are performed in Phase B3.2.
    /// </summary>
    public class IAFAlternativeTimeRequestService : IIAFAlternativeTimeRequestService
    {

        private readonly FYPTrackerContext _context;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public IAFAlternativeTimeRequestService(
            FYPTrackerContext context,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }



        public async Task<IAFAlternativeTimeRequest?> GetRequestByIdAsync(int requestId)
        {
            // B3.6: Fail closed unless the current user is an
            // authenticated and authorized Admin.
            var principal = _httpContextAccessor?.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true ||
                !int.TryParse(principal.FindFirst("Id")?.Value, out int currentAdminUserId) ||
                currentAdminUserId <= 0 ||
                await ValidateCurrentAdminAsync(currentAdminUserId) != null)
            {
                return null;
            }

            if (requestId <= 0)
            {
                return null;
            }

            return await _context.IAFAlternativeTimeRequests
                .Include(r => r.Milestone)
                .Include(r => r.RequestedLecturer)
                .Include(r => r.RequestedByAdmin)
                .Include(r => r.FinalizedByAdmin)
                .FirstOrDefaultAsync(r => r.Id == requestId);
        }



        public async Task<IAFAlternativeTimeRequest?> GetActiveRequestAsync(
            int milestoneId, int attemptNumber)
        {
            // B3.6: Validate authenticated Admin identity before reading.
            var principal = _httpContextAccessor?.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true ||
                !int.TryParse(principal.FindFirst("Id")?.Value, out int currentAdminUserId) ||
                currentAdminUserId <= 0 ||
                await ValidateCurrentAdminAsync(currentAdminUserId) != null)
            {
                return null;
            }

            // Reject invalid milestone or attempt identifiers.
            if (milestoneId <= 0 || attemptNumber <= 0)
            {
                return null;
            }

            return await _context.IAFAlternativeTimeRequests
                .Where(r => r.MilestoneId == milestoneId &&
                            r.AttemptNumber == attemptNumber &&
                            (r.Status == IAFAlternativeTimeRequest.StatusPending ||
                             r.Status == IAFAlternativeTimeRequest.StatusSuggested))
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();
        }



        public async Task<List<IAFAlternativeTimeRequest>> GetRequestHistoryAsync(
            int milestoneId, int attemptNumber)
        {
            // B3.6: Fail closed unless the current user is an
            // authenticated and authorized Admin.
            var principal = _httpContextAccessor?.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true ||
                !int.TryParse(principal.FindFirst("Id")?.Value, out int currentAdminUserId) ||
                currentAdminUserId <= 0 ||
                await ValidateCurrentAdminAsync(currentAdminUserId) != null)
            {
                return new List<IAFAlternativeTimeRequest>();
            }

            // Reject invalid milestone or attempt identifiers.
            if (milestoneId <= 0 || attemptNumber <= 0)
            {
                return new List<IAFAlternativeTimeRequest>();
            }

            return await _context.IAFAlternativeTimeRequests
                .Where(r => r.MilestoneId == milestoneId &&
                            r.AttemptNumber == attemptNumber)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }


        public async Task<AlternativeTimeRequestResult> CreateRequestAsync(
            int milestoneId,
            int attemptNumber,
            int academicSessionId,
            int requestedLecturerId,
            string requestedLecturerRole,
            int currentAdminUserId,
            bool isAdminUser,
            int supervisorId,
            string? adminNotes)
        {
            // Basic input validation.
            // Actual Admin authorization is verified below.
            if (currentAdminUserId <= 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "UNAUTHORIZED_ACCESS",
                    "Only authorized administrators can initiate an alternative time request.");
            }

            // Authoritative Admin authentication.
            // Never trust the caller-supplied isAdminUser flag.
            var authorizationError =
                await ValidateCurrentAdminAsync(currentAdminUserId);

            if (authorizationError != null)
            {
                return authorizationError;
            }


            // B3.5.1: Validate authoritative milestone and attempt.
            // Never trust an attempt number supplied by the caller.

            var milestone = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "MILESTONE_NOT_FOUND",
                    "The selected IAF milestone does not exist.");
            }

            if (milestone.Project == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "PROJECT_NOT_FOUND",
                    "The selected milestone has no associated project.");
            }

            if (milestone.Status != "DefenseRequired" &&
                milestone.Status != "DefenseScheduled")
            {
                return AlternativeTimeRequestResult.Fail(
                    "INELIGIBLE_MILESTONE",
                    "The milestone is not eligible for IAF Defense scheduling.");
            }

            var attemptResult =
                IAFDefenseBookingService.ResolveAuthoritativeAttemptNumber(
                    milestone.Remarks);

            if (!attemptResult.Success)
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_ATTEMPT_HISTORY",
                    attemptResult.ErrorMessage ??
                    "Unable to determine the authoritative IAF attempt.");
            }

            if (attemptNumber != attemptResult.AttemptNumber)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ATTEMPT_MISMATCH",
                    "The requested attempt does not match the current IAF attempt.");
            }


            // B3.5.1: Resolve Academic Session from authoritative project/student data.
            // Follow the same resolution rules as IAFDefenseBookingService.

            string? sessionName = milestone.Project.AcademicSession;

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                sessionName = await _context.ApplicationUsers
                    .AsNoTracking()
                    .Where(u => u.Id == milestone.Project.StudentId)
                    .Select(u => u.AcademicSession)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_NOT_FOUND",
                    "No academic session is configured for this project or student.");
            }

            sessionName = sessionName.Trim();

            int? authoritativeSessionId = await _context.AcademicSessions
                .AsNoTracking()
                .Where(s => s.Name == sessionName)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();

            if (!authoritativeSessionId.HasValue)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_NOT_FOUND",
                    "The project's academic session could not be resolved.");
            }

            if (academicSessionId != authoritativeSessionId.Value)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_MISMATCH",
                    "The requested academic session does not match the project's authoritative academic session.");
            }


            // B3.5.1: Validate the authoritative independent Examiner.
            // Alternative Time Requests in this workflow target Examiners only.

            if (!string.Equals(
                requestedLecturerRole,
                "Examiner",
                StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_LECTURER_ROLE",
                    "IAF Defense Alternative Time Requests must target an Examiner.");
            }

            var examiner = await _context.ApplicationUsers
                .AsNoTracking()
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u =>
                    u.Id == requestedLecturerId && !u.IsResign);

            if (examiner == null ||
                !examiner.UserRoles.Any(ur =>
                    ur.Role != null &&
                    ur.Role.Name == "Examiner"))
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_EXAMINER",
                    "The selected Examiner does not exist, is inactive, or does not have the Examiner role.");
            }

            // The original Supervisor must come from the project.
            int authoritativeSupervisorId =
                milestone.Project.SupervisorId ?? 0;

            if (supervisorId != authoritativeSupervisorId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SUPERVISOR_MISMATCH",
                    "The supplied Supervisor does not match the project's original Supervisor.");
            }

            if (authoritativeSupervisorId > 0 &&
                requestedLecturerId == authoritativeSupervisorId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SUPERVISOR_CANNOT_EXAMINE",
                    "The original Supervisor cannot act as the independent Examiner.");
            }

            // Follow the existing Booking Service supervisor policy.
            var supervisorSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Key == "IAFDefenseSupervisorRequired");

            bool isSupervisorRequired = false;

            if (supervisorSetting != null &&
                bool.TryParse(supervisorSetting.Value, out bool required))
            {
                isSupervisorRequired = required;
            }

            if (isSupervisorRequired && authoritativeSupervisorId <= 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SUPERVISOR_REQUIRED",
                    "Supervisor attendance is required, but the project has no assigned Supervisor.");
            }


            // 2. Query existing requests for duplicate prevention
            var existingRequests = await _context.IAFAlternativeTimeRequests
                .Where(r => r.MilestoneId == milestoneId && r.AttemptNumber == attemptNumber)
                .ToListAsync();

            // 3. Authoritative pure validation
            var validation = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId,
                attemptNumber,
                academicSessionId,
                requestedLecturerId,
                requestedLecturerRole,
                currentAdminUserId,
                isAdminUser: true, // Verified by ValidateCurrentAdminAsync above.
                supervisorId,
                existingRequests,
                adminNotes);

            if (!validation.Success || validation.Request == null)
            {
                return validation;
            }

            // 4. Persistence staging

            _context.IAFAlternativeTimeRequests.Add(validation.Request);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is SqlException sqlEx &&
                (sqlEx.Number == 2601 || sqlEx.Number == 2627) &&
                sqlEx.Message.Contains(
                    "UQ_IAFAlternativeTimeRequest_ActiveMilestoneAttempt",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Do not leave the failed insert tracked for a later SaveChanges.
                _context.Entry(validation.Request).State = EntityState.Detached;

                return AlternativeTimeRequestResult.Fail(
                    "DUPLICATE_ACTIVE_REQUEST",
                    "Another active alternative time request already exists " +
                    "for this IAF milestone and attempt. Please refresh.");
            }

            return AlternativeTimeRequestResult.Ok(validation.Request);

        }

        public async Task<AlternativeTimeRequestResult> SubmitSuggestionAsync(
            int requestId,
            int currentLecturerUserId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            int durationMinutes,
            TimeSpan workingHoursStart,
            TimeSpan workingHoursEnd,
            string? lecturerNotes = null,
            byte[]? clientRowVersion = null)
        {
            // B3.5.1: Authentication must run before any database access.
            var authorizationError =
                await ValidateCurrentExaminerAsync(currentLecturerUserId);

            if (authorizationError != null)
            {
                return authorizationError;
            }

            // B3.5.1: Resolve the authoritative IAF Defense duration.
            // Match the existing IAFDefenseBookingService rule exactly.

            var durationSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Key == "IAFDefenseDurationMinutes");

            int authoritativeDurationMinutes = 45;

            if (durationSetting != null &&
                int.TryParse(durationSetting.Value, out int configuredDuration) &&
                configuredDuration >= 15 &&
                configuredDuration <= 180)
            {
                authoritativeDurationMinutes = configuredDuration;
            }

            // Reject a stale or caller-supplied duration that differs
            // from the actual IAF Defense booking configuration.
            if (durationMinutes != authoritativeDurationMinutes)
            {
                return AlternativeTimeRequestResult.Fail(
                    "DURATION_MISMATCH",
                    $"The IAF Defense duration is configured as " +
                    $"{authoritativeDurationMinutes} minutes. " +
                    "Please refresh the schedule before suggesting a time.");
            }

            var request = await _context.IAFAlternativeTimeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
            if (request == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "REQUEST_NOT_FOUND",
                    $"Alternative time request ID {requestId} was not found.");
            }


            // B3.5.1 Step 6.9:
            // Revalidate the current authoritative IAF milestone and attempt.

            var milestone = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == request.MilestoneId);

            if (milestone == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "MILESTONE_NOT_FOUND",
                    "The IAF milestone associated with this request no longer exists.");
            }

            if (milestone.Status != "DefenseRequired" &&
                milestone.Status != "DefenseScheduled")
            {
                return AlternativeTimeRequestResult.Fail(
                    "INELIGIBLE_MILESTONE",
                    "This IAF milestone is no longer eligible for Defense scheduling.");
            }

            var attemptResult =
                IAFDefenseBookingService.ResolveAuthoritativeAttemptNumber(
                    milestone.Remarks);

            if (!attemptResult.Success)
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_ATTEMPT_HISTORY",
                    attemptResult.ErrorMessage ??
                    "Unable to determine the current IAF attempt.");
            }

            if (request.AttemptNumber != attemptResult.AttemptNumber)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ATTEMPT_MISMATCH",
                    "This Alternative Time Request belongs to an outdated IAF attempt.");
            }


            // B3.5.1 Step 6.9.2A:
            // Revalidate the authoritative Academic Session.

            if (milestone.Project == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "PROJECT_NOT_FOUND",
                    "The IAF milestone has no associated project.");
            }

            string? sessionName = milestone.Project.AcademicSession;

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                sessionName = await _context.ApplicationUsers
                    .AsNoTracking()
                    .Where(u => u.Id == milestone.Project.StudentId)
                    .Select(u => u.AcademicSession)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_NOT_FOUND",
                    "The project's academic session cannot be resolved.");
            }

            sessionName = sessionName.Trim();

            int? authoritativeSessionId = await _context.AcademicSessions
                .AsNoTracking()
                .Where(s => s.Name == sessionName)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();

            if (!authoritativeSessionId.HasValue)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_NOT_FOUND",
                    "The academic session does not exist.");
            }

            if (request.AcademicSessionId != authoritativeSessionId.Value)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SESSION_MISMATCH",
                    "This Alternative Time Request belongs to an outdated academic session.");
            }


            // B3.5.1 Step 6.10:
            // Require a valid RowVersion for optimistic concurrency.
            // Missing or stale versions must fail closed.

            if (clientRowVersion == null ||
                clientRowVersion.Length == 0 ||
                request.RowVersion == null ||
                request.RowVersion.Length == 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ROW_VERSION_REQUIRED",
                    "A valid request version is required. Please refresh and try again.");
            }

            if (!request.RowVersion.SequenceEqual(clientRowVersion))
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "This request has changed. Please refresh before suggesting a time.");
            }


            // B3.5.1: Resolve working hours from authoritative server settings.
            // Never trust workingHoursStart / workingHoursEnd supplied by callers.

            var startSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Key == "LecturerWorkingHoursStart");

            var endSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Key == "LecturerWorkingHoursEnd");

            if (!FYPTracker.Services.LecturerAvailabilityService.TryParseWorkingHours(
                startSetting?.Value,
                endSetting?.Value,
                out TimeSpan authoritativeWorkStart,
                out TimeSpan authoritativeWorkEnd))
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_WORKING_HOURS",
                    "Lecturer working hours are unconfigured or invalid.");
            }

            // B3.5.1: Revalidate Examiner independence
            // against the project's current Supervisor.

            int currentSupervisorId = milestone.Project.SupervisorId ?? 0;

            if (currentSupervisorId > 0 &&
                request.RequestedLecturerId == currentSupervisorId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "SUPERVISOR_CANNOT_EXAMINE",
                    "The requested Examiner is now the project's original Supervisor and cannot independently examine this defense.");
            }

            // Authoritative pure validation
            var validation = IAFAlternativeTimeValidationEngine.ValidateLecturerSuggestion(
                request,
                currentLecturerUserId,
                suggestedDate,
                suggestedStartTime,
                authoritativeDurationMinutes,
                authoritativeWorkStart,
                authoritativeWorkEnd,
                lecturerNotes);

            if (!validation.Success)
            {
                return validation;
            }

            // Save modifications (concurrency token handled by EF Core RowVersion)
            try
            {
                await _context.SaveChangesAsync();
                return AlternativeTimeRequestResult.Ok(request);
            }
            catch (DbUpdateConcurrencyException)
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "The request status was changed concurrently by an administrator. Suggestion could not be saved.");
            }

        }


        public Task<AlternativeTimeRequestResult> FinalizeAcceptedAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            int confirmedBookingId,
            byte[]? clientRowVersion = null)
        {
            // B3.5.1 SECURITY HARDENING
            //
            // Direct acceptance is disabled.
            // A positive Booking ID does not prove that a matching,
            // authoritative booking has been successfully committed.
            //
            // Future integration must validate the authoritative booking
            // and update the request within the existing booking transaction.

            return Task.FromResult(
                AlternativeTimeRequestResult.Fail(
                    "ACCEPTANCE_DISABLED",
                    "Direct acceptance is disabled until the alternative time request is integrated with the authoritative IAF Defense booking transaction."));
        }


        private async Task<AlternativeTimeRequestResult?> ValidateCurrentAdminAsync(
            int currentAdminUserId)
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            var principal = httpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true ||
                !int.TryParse(
                    principal.FindFirst("Id")?.Value,
                    out int authenticatedUserId) ||
                authenticatedUserId <= 0 ||
                authenticatedUserId != currentAdminUserId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "UNAUTHORIZED_ACCESS",
                    "A valid authenticated administrator identity is required.");
            }

            string? activeRole =
                httpContext!.Session.GetString("ActiveRole");

            if (!string.Equals(activeRole, "Admin",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(activeRole, "SuperAdmin",
                    StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_WORKSPACE",
                    "Switch to the Admin workspace before performing this action.");
            }

            bool hasAdminClaim = principal.Claims.Any(c =>
                c.Type == ClaimTypes.Role &&
                (string.Equals(c.Value, "Admin",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Value, "SuperAdmin",
                    StringComparison.OrdinalIgnoreCase)));

            if (!hasAdminClaim)
            {
                return AlternativeTimeRequestResult.Fail(
                    "UNAUTHORIZED_ROLE",
                    "The authenticated user does not own an administrative role.");
            }

            bool isAuthorizedAdmin = await _context.ApplicationUsers
                .AsNoTracking()
                .AnyAsync(u =>
                    u.Id == authenticatedUserId &&
                    !u.IsResign &&
                    u.UserRoles.Any(ur =>
                        ur.Role.Name == "Admin" ||
                        ur.Role.Name == "SuperAdmin"));

            if (!isAuthorizedAdmin)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ADMIN_NOT_AUTHORIZED",
                    "The account is inactive or no longer has administrative privileges.");
            }

            return null;
        }


        private async Task<AlternativeTimeRequestResult?> ValidateCurrentExaminerAsync(
            int currentLecturerUserId)
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            var principal = httpContext?.User;

            // Verify authenticated identity and User ID.
            if (principal?.Identity?.IsAuthenticated != true ||
                !int.TryParse(
                    principal.FindFirst("Id")?.Value,
                    out int authenticatedUserId) ||
                authenticatedUserId <= 0 ||
                authenticatedUserId != currentLecturerUserId)
            {
                return AlternativeTimeRequestResult.Fail(
                    "UNAUTHORIZED_ACCESS",
                    "A valid authenticated Examiner identity is required.");
            }

            // Verify the selected workspace.
            string? activeRole =
                httpContext!.Session.GetString("ActiveRole");

            if (!string.Equals(
                activeRole,
                "Examiner",
                StringComparison.OrdinalIgnoreCase))
            {
                return AlternativeTimeRequestResult.Fail(
                    "INVALID_WORKSPACE",
                    "Switch to the Examiner workspace before suggesting a time.");
            }

            // Verify authenticated role ownership.
            bool hasExaminerClaim = principal.Claims.Any(c =>
                c.Type == ClaimTypes.Role &&
                string.Equals(
                    c.Value,
                    "Examiner",
                    StringComparison.OrdinalIgnoreCase));

            if (!hasExaminerClaim)
            {
                return AlternativeTimeRequestResult.Fail(
                    "UNAUTHORIZED_ROLE",
                    "The authenticated user does not own the Examiner role.");
            }

            // Check the current database role and account status.
            bool isAuthorizedExaminer = await _context.ApplicationUsers
                .AsNoTracking()
                .AnyAsync(u =>
                    u.Id == authenticatedUserId &&
                    !u.IsResign &&
                    u.UserRoles.Any(ur =>
                        ur.Role.Name == "Examiner"));

            if (!isAuthorizedExaminer)
            {
                return AlternativeTimeRequestResult.Fail(
                    "EXAMINER_NOT_AUTHORIZED",
                    "The account is inactive or no longer has Examiner privileges.");
            }

            return null;
        }



        public async Task<AlternativeTimeRequestResult> RejectSuggestionAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            string reason,
            byte[]? clientRowVersion = null)
        {
            var authorizationError = await ValidateCurrentAdminAsync(currentAdminUserId);
            if (authorizationError != null)
            {
                return authorizationError;
            }

            var request = await _context.IAFAlternativeTimeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
            if (request == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "REQUEST_NOT_FOUND",
                    $"Alternative time request ID {requestId} was not found.");
            }


            // B3.5.1 Step 6.10.1:
            // Reject requests without a valid RowVersion.

            if (clientRowVersion == null ||
                clientRowVersion.Length == 0 ||
                request.RowVersion == null ||
                request.RowVersion.Length == 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ROW_VERSION_REQUIRED",
                    "A valid request version is required. Please refresh and try again.");
            }

            if (!request.RowVersion.SequenceEqual(clientRowVersion))
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "This request has changed. Please refresh before rejecting the suggestion.");
            }


            var validation = IAFAlternativeTimeValidationEngine.ValidateRejectSuggestion(
                request,
                currentAdminUserId,
                true, // Admin identity already verified by ValidateCurrentAdminAsync.
                reason);

            if (!validation.Success)
            {
                return validation;
            }

            try
            {
                await _context.SaveChangesAsync();
                return AlternativeTimeRequestResult.Ok(request);
            }
            catch (DbUpdateConcurrencyException)
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "The request was modified concurrently.");
            }
        }

        public async Task<AlternativeTimeRequestResult> CancelRequestAsync(
            int requestId,
            int currentAdminUserId,
            bool isAdminUser,
            string reason,
            byte[]? clientRowVersion = null)
        {
            var authorizationError =
                await ValidateCurrentAdminAsync(currentAdminUserId);

                        if (authorizationError != null)
                        {
                            return authorizationError;
                        }
            var request = await _context.IAFAlternativeTimeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
            if (request == null)
            {
                return AlternativeTimeRequestResult.Fail(
                    "REQUEST_NOT_FOUND",
                    $"Alternative time request ID {requestId} was not found.");
            }


            // B3.5.1 Step 6.10.2:
            // Require RowVersion before cancelling a request.

            if (clientRowVersion == null ||
                clientRowVersion.Length == 0 ||
                request.RowVersion == null ||
                request.RowVersion.Length == 0)
            {
                return AlternativeTimeRequestResult.Fail(
                    "ROW_VERSION_REQUIRED",
                    "A valid request version is required. Please refresh and try again.");
            }

            if (!request.RowVersion.SequenceEqual(clientRowVersion))
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "This request has changed. Please refresh before cancelling.");
            }


            var validation = IAFAlternativeTimeValidationEngine.ValidateCancelRequest(
                request,
                currentAdminUserId,
                true, // Admin identity already verified by ValidateCurrentAdminAsync.
                reason);

            if (!validation.Success)
            {
                return validation;
            }

            try
            {
                await _context.SaveChangesAsync();
                return AlternativeTimeRequestResult.Ok(request);
            }
            catch (DbUpdateConcurrencyException)
            {
                return AlternativeTimeRequestResult.Fail(
                    "CONCURRENCY_CONFLICT",
                    "The request was modified concurrently.");
            }
        }
    }
}
