using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;

namespace FYPTracker.Services.Defense
{
    public class IAFDefenseBookingService : IIAFDefenseBookingService
    {
        private readonly FYPTrackerContext _context;
        private readonly ILecturerAvailabilityService _availabilityService;

        public IAFDefenseBookingService(
            FYPTrackerContext context,
            ILecturerAvailabilityService availabilityService)
        {
            _context = context;
            _availabilityService = availabilityService;
        }

        public async Task<DefenseBookingResult> ValidateDefenseScheduleAsync(
            int milestoneId,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            string venue)
        {
            var preflight = await BuildValidationContextAsync(
                milestoneId,
                examinerId,
                defenseDate,
                startTime,
                venue);

            if (!preflight.Success)
            {
                return preflight;
            }

            return await ValidateScheduleConflictsAsync(preflight.Context!);
        }

        public async Task<DefenseBookingResult> ScheduleOrRescheduleDefenseAsync(
            int milestoneId,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            string venue,
            int currentUserId,
            string? changeReason = null)
        {
            if (!await IsSchemaAvailableAsync())
            {
                return DefenseBookingResult.Fail("SchemaNotMigrated", "IAF Defense Booking schema (migration 20261011194333_AddIAFDefenseBookingAndAudit) has not been applied to this database.");
            }

            // Initial pre-check to fail fast on validation errors
            var preflight = await BuildValidationContextAsync(
                milestoneId,
                examinerId,
                defenseDate,
                startTime,
                venue);

            if (!preflight.Success)
            {
                return preflight;
            }

            var ctx = preflight.Context!;

            // Deterministic resource locking list:
            // 1. Milestone Attempt lock: "FYP_DEFENSE:MILESTONE:{milestoneId}:ATTEMPT:{attemptNumber}"
            // 2. Student lock: "FYP_DEFENSE:STUDENT:{studentId}:{yyyyMMdd}"
            // 3. Lecturer locks (unified participant key): "FYP_DEFENSE:LECTURER:{lecturerId}:{yyyyMMdd}"
            // 4. Venue lock: "FYP_DEFENSE:VENUE:{normalizedVenue}:{yyyyMMdd}"
            // 5. If rescheduling: Lock old resources as well to prevent concurrent races across moves
            int studentId = ctx.Milestone.Project!.StudentId;
            var lockResources = new HashSet<string>(StringComparer.Ordinal)
            {
                $"FYP_DEFENSE:MILESTONE:{ctx.Milestone.Id}:ATTEMPT:{ctx.AttemptNumber}",
                $"FYP_DEFENSE:STUDENT:{studentId}:{defenseDate:yyyyMMdd}"
            };

            var participantLecturerIds = new HashSet<int> { ctx.Examiner.Id };
            if (ctx.IsSupervisorRequired && ctx.OriginalSupervisorId.HasValue && ctx.OriginalSupervisorId.Value > 0)
            {
                participantLecturerIds.Add(ctx.OriginalSupervisorId.Value);
            }

            foreach (var lecId in participantLecturerIds)
            {
                lockResources.Add($"FYP_DEFENSE:LECTURER:{lecId}:{defenseDate:yyyyMMdd}");
            }

            string normalizedVenue = NormalizeVenue(venue);
            if (!string.IsNullOrWhiteSpace(normalizedVenue))
            {
                lockResources.Add($"FYP_DEFENSE:VENUE:{normalizedVenue}:{defenseDate:yyyyMMdd}");
            }

            // Rescheduling protection: If an existing booking exists, also lock old resource set
            var existingBookingPre = await _context.IAFDefenseBookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.MilestoneId == ctx.Milestone.Id && b.AttemptNumber == ctx.AttemptNumber);

            if (existingBookingPre != null)
            {
                lockResources.Add($"FYP_DEFENSE:STUDENT:{studentId}:{existingBookingPre.DefenseDate:yyyyMMdd}");
                lockResources.Add($"FYP_DEFENSE:LECTURER:{existingBookingPre.ExaminerId}:{existingBookingPre.DefenseDate:yyyyMMdd}");
                if (existingBookingPre.IsSupervisorRequired && existingBookingPre.OriginalSupervisorId.HasValue && existingBookingPre.OriginalSupervisorId.Value > 0)
                {
                    lockResources.Add($"FYP_DEFENSE:LECTURER:{existingBookingPre.OriginalSupervisorId.Value}:{existingBookingPre.DefenseDate:yyyyMMdd}");
                }
                string oldNormVenue = NormalizeVenue(existingBookingPre.Venue);
                if (!string.IsNullOrWhiteSpace(oldNormVenue))
                {
                    lockResources.Add($"FYP_DEFENSE:VENUE:{oldNormVenue}:{existingBookingPre.DefenseDate:yyyyMMdd}");
                }
            }

            // Ensure deterministic acquisition order to eliminate distributed deadlocks
            var sortedLockResources = lockResources.OrderBy(r => r, StringComparer.Ordinal).ToList();

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                IAFDefenseBooking? booking = null;
                IAFDefenseHistory? history = null;
                FYPMilestone? milestoneToUpdate = null;
                bool isCommitStage = false;

                try
                {
                    // 1. Acquire transaction-owned application locks sequentially
                    foreach (var resource in sortedLockResources)
                    {
                        var lockResult = await AcquireTransactionAppLockAsync(resource, timeoutSeconds: 5);
                        if (!lockResult.Success)
                        {
                            await transaction.RollbackAsync();
                            return DefenseBookingResult.Fail(
                                "ConcurrencyConflict",
                                $"Unable to acquire schedule lock for '{resource}'. A concurrent defense scheduling operation is in progress. Please retry.");
                        }
                    }

                    // 2. Re-validate ALL constraints INSIDE the protected transaction
                    var recheckPreflight = await BuildValidationContextAsync(
                        milestoneId,
                        examinerId,
                        defenseDate,
                        startTime,
                        venue);

                    if (!recheckPreflight.Success)
                    {
                        await transaction.RollbackAsync();
                        return recheckPreflight;
                    }

                    var insideCtx = recheckPreflight.Context!;
                    var conflictCheck = await ValidateScheduleConflictsAsync(insideCtx);
                    if (!conflictCheck.Success)
                    {
                        await transaction.RollbackAsync();
                        return conflictCheck;
                    }

                    // 3. Upsert IAFDefenseBooking
                    var existingBooking = await _context.IAFDefenseBookings
                        .FirstOrDefaultAsync(b => b.MilestoneId == insideCtx.Milestone.Id && b.AttemptNumber == insideCtx.AttemptNumber);

                    bool isReschedule = existingBooking != null;

                    if (isReschedule)
                    {
                        booking = existingBooking!;
                        booking.ExaminerId = insideCtx.Examiner.Id;
                        booking.OriginalSupervisorId = insideCtx.OriginalSupervisorId;
                        booking.IsSupervisorRequired = insideCtx.IsSupervisorRequired;
                        booking.DefenseDate = insideCtx.DefenseDate.Date;
                        booking.StartTime = insideCtx.StartTime;
                        booking.EndTime = insideCtx.EndTime;
                        booking.DurationMinutes = insideCtx.DurationMinutes;
                        booking.Venue = insideCtx.Venue;
                        booking.BookingStatus = "Rescheduled";
                        booking.UpdatedAt = DateTime.Now;
                        booking.UpdatedByUserId = currentUserId;
                    }
                    else
                    {
                        booking = new IAFDefenseBooking
                        {
                            MilestoneId = insideCtx.Milestone.Id,
                            AttemptNumber = insideCtx.AttemptNumber,
                            AcademicSessionId = insideCtx.AcademicSessionId,
                            ExaminerId = insideCtx.Examiner.Id,
                            OriginalSupervisorId = insideCtx.OriginalSupervisorId,
                            IsSupervisorRequired = insideCtx.IsSupervisorRequired,
                            DefenseDate = insideCtx.DefenseDate.Date,
                            StartTime = insideCtx.StartTime,
                            EndTime = insideCtx.EndTime,
                            DurationMinutes = insideCtx.DurationMinutes,
                            Venue = insideCtx.Venue,
                            BookingStatus = "Scheduled",
                            AcademicOutcome = "Pending",
                            CreatedAt = DateTime.Now,
                            CreatedByUserId = currentUserId
                        };
                        _context.IAFDefenseBookings.Add(booking);
                    }

                    // Must save changes so that booking.Id is generated for the history record
                    await _context.SaveChangesAsync();

                    // 4. Create immutable IAFDefenseHistory entry
                    history = new IAFDefenseHistory
                    {
                        BookingId = booking.Id,
                        MilestoneId = booking.MilestoneId,
                        AttemptNumber = booking.AttemptNumber,
                        AcademicSessionId = booking.AcademicSessionId,
                        ActionType = isReschedule ? "Rescheduled" : "InitialScheduled",
                        ExaminerId = booking.ExaminerId,
                        OriginalSupervisorId = booking.OriginalSupervisorId,
                        IsSupervisorRequired = booking.IsSupervisorRequired,
                        DefenseDate = booking.DefenseDate,
                        StartTime = booking.StartTime,
                        EndTime = booking.EndTime,
                        DurationMinutes = booking.DurationMinutes,
                        Venue = booking.Venue,
                        BookingStatus = booking.BookingStatus,
                        AcademicOutcome = booking.AcademicOutcome,
                        OutcomeComments = booking.OutcomeComments,
                        ReasonForChange = changeReason ?? (isReschedule ? "Defense rescheduled by administrator." : "Initial defense session scheduled."),
                        Timestamp = DateTime.Now,
                        ActionByUserId = currentUserId
                    };
                    _context.IAFDefenseHistories.Add(history);

                    // 5. Synchronize FYPMilestone fields
                    milestoneToUpdate = await _context.FYPMilestones
                        .FirstOrDefaultAsync(m => m.Id == insideCtx.Milestone.Id);

                    if (milestoneToUpdate == null)
                    {
                        await transaction.RollbackAsync();
                        if (history != null) _context.Entry(history).State = EntityState.Detached;
                        if (booking != null) _context.Entry(booking).State = EntityState.Detached;
                        return DefenseBookingResult.Fail(
                            "MilestoneNotFound",
                            $"Milestone #{insideCtx.Milestone.Id} could not be found for synchronization.");
                    }

                    milestoneToUpdate.Status = "DefenseScheduled";
                    milestoneToUpdate.DefenseDate = insideCtx.DefenseDate.Date;
                    milestoneToUpdate.DefenseTime = FormatDisplayTime(insideCtx.StartTime);
                    milestoneToUpdate.DefenseVenue = insideCtx.Venue;

                    // Synchronize attempt history JSON inside Remarks
                    var syncResult = SyncMilestoneRemarksAttemptStatus(milestoneToUpdate, insideCtx.AttemptNumber, "DefenseScheduled");
                    if (!syncResult.Success)
                    {
                        await transaction.RollbackAsync();
                        if (milestoneToUpdate != null) _context.Entry(milestoneToUpdate).State = EntityState.Detached;
                        if (history != null) _context.Entry(history).State = EntityState.Detached;
                        if (booking != null) _context.Entry(booking).State = EntityState.Detached;
                        return DefenseBookingResult.Fail(
                            "RemarksSyncFailed",
                            $"Failed to synchronize milestone remarks: {syncResult.ErrorMessage}");
                    }

                    await _context.SaveChangesAsync();
                    isCommitStage = true;
                    await transaction.CommitAsync();

                    return DefenseBookingResult.Ok(booking, history);
                }
                catch (DbUpdateConcurrencyException)
                {
                    bool rollbackConfirmed = false;
                    try
                    {
                        await transaction.RollbackAsync();
                        rollbackConfirmed = true;
                    }
                    catch
                    {
                        rollbackConfirmed = false;
                    }

                    if (milestoneToUpdate != null) _context.Entry(milestoneToUpdate).State = EntityState.Detached;
                    if (history != null) _context.Entry(history).State = EntityState.Detached;
                    if (booking != null) _context.Entry(booking).State = EntityState.Detached;

                    if (!rollbackConfirmed)
                    {
                        return DefenseBookingResult.Fail(
                            "ConcurrencyError",
                            "A concurrency conflict occurred and the transaction rollback could not be confirmed. Please refresh the page to verify the current schedule status.");
                    }

                    return DefenseBookingResult.Fail(
                        "ConcurrencyError",
                        "The defense record was modified by another user concurrently. Please reload the schedule and retry.");
                }
                catch (Exception)
                {
                    bool rollbackConfirmed = false;
                    try
                    {
                        await transaction.RollbackAsync();
                        rollbackConfirmed = true;
                    }
                    catch
                    {
                        rollbackConfirmed = false;
                    }

                    if (milestoneToUpdate != null) _context.Entry(milestoneToUpdate).State = EntityState.Detached;
                    if (history != null) _context.Entry(history).State = EntityState.Detached;
                    if (booking != null) _context.Entry(booking).State = EntityState.Detached;

                    if (isCommitStage)
                    {
                        return DefenseBookingResult.Fail(
                            "ExecutionError",
                            "An error occurred while confirming the schedule transaction. Please refresh and verify the current schedule before retrying.");
                    }

                    if (!rollbackConfirmed)
                    {
                        return DefenseBookingResult.Fail(
                            "ExecutionError",
                            "An unexpected error occurred and transaction rollback could not be confirmed. Please refresh and verify the schedule before taking further action.");
                    }

                    return DefenseBookingResult.Fail(
                        "ExecutionError",
                        "An unexpected error occurred while scheduling the defense. Please retry.");
                }
            });
        }

        public async Task<DefenseOutcomeResult> RecordDefenseOutcomeAsync(
            int milestoneId,
            string outcome,
            string? outcomeComments,
            int currentUserId,
            bool isEarlyOutcomeTestOverride = false)
        {
            if (outcome != "AdminApproved" && outcome != "ChangeOfTopicRequired")
            {
                return DefenseOutcomeResult.Fail(
                    "InvalidOutcome",
                    "Outcome must be either 'AdminApproved' (Pass) or 'ChangeOfTopicRequired' (Fail).");
            }

            if (!await IsSchemaAvailableAsync())
            {
                return DefenseOutcomeResult.Fail("SchemaNotMigrated", "IAF Defense Booking schema (migration 20261011194333_AddIAFDefenseBookingAndAudit) has not been applied to this database.");
            }

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                FYPMilestone? milestone = null;
                IAFDefenseBooking? booking = null;
                IAFDefenseHistory? history = null;
                bool isCommitStage = false;

                try
                {
                    milestone = await _context.FYPMilestones
                        .Include(m => m.Project)
                        .FirstOrDefaultAsync(m => m.Id == milestoneId);

                    if (milestone == null)
                    {
                        return DefenseOutcomeResult.Fail("MilestoneNotFound", "Milestone was not found.");
                    }

                    if (milestone.Status != "DefenseScheduled")
                    {
                        return DefenseOutcomeResult.Fail(
                            "InvalidMilestoneStatus",
                            $"Cannot record defense outcome because the milestone status is '{milestone.Status}' (expected 'DefenseScheduled').");
                    }

                    var attemptResult = ResolveAuthoritativeAttemptNumber(milestone.Remarks);
                    if (!attemptResult.Success)
                    {
                        return DefenseOutcomeResult.Fail(
                            "MalformedAttemptHistory",
                            attemptResult.ErrorMessage!);
                    }
                    int attemptNumber = attemptResult.AttemptNumber;

                    booking = await _context.IAFDefenseBookings
                        .FirstOrDefaultAsync(b => b.MilestoneId == milestoneId && b.AttemptNumber == attemptNumber);

                    if (booking == null)
                    {
                        return DefenseOutcomeResult.Fail(
                            "BookingNotFound",
                            $"No active defense booking found for Milestone #{milestoneId} Attempt {attemptNumber}.");
                    }

                    if (booking.BookingStatus != "Scheduled" && booking.BookingStatus != "Rescheduled")
                    {
                        return DefenseOutcomeResult.Fail(
                            "InvalidBookingStatus",
                            $"Cannot record defense outcome because the defense booking status is '{booking.BookingStatus}' (expected 'Scheduled' or 'Rescheduled').");
                    }

                    if (booking.AcademicOutcome != "Pending")
                    {
                        return DefenseOutcomeResult.Fail(
                            "OutcomeAlreadyRecorded",
                            $"Academic outcome '{booking.AcademicOutcome}' has already been recorded for this defense attempt.");
                    }

                    // Timing safeguard: Ensure the scheduled defense session has actually concluded (unless explicitly authorized for controlled Development testing)
                    var defenseEndDateTime = booking.DefenseDate.Date.Add(booking.EndTime);
                    if (defenseEndDateTime > DateTime.Now && !isEarlyOutcomeTestOverride)
                    {
                        return DefenseOutcomeResult.Fail(
                            "DefenseNotConcludedYet",
                            $"Cannot record defense outcome before the defense session has concluded. Scheduled for {booking.DefenseDate:yyyy-MM-dd} from {FormatDisplayTime(booking.StartTime)} to {FormatDisplayTime(booking.EndTime)}.");
                    }

                    // Update Booking
                    booking.AcademicOutcome = outcome;
                    booking.BookingStatus = "Completed";
                    booking.OutcomeComments = outcomeComments;
                    booking.UpdatedAt = DateTime.Now;
                    booking.UpdatedByUserId = currentUserId;

                    // Append to History
                    string reasonForChange = isEarlyOutcomeTestOverride
                        ? "Controlled Development early-outcome test — defense not actually conducted."
                        : $"Defense outcome recorded: {outcome}";

                    history = new IAFDefenseHistory
                    {
                        BookingId = booking.Id,
                        MilestoneId = booking.MilestoneId,
                        AttemptNumber = booking.AttemptNumber,
                        AcademicSessionId = booking.AcademicSessionId,
                        ActionType = "OutcomeRecorded",
                        ExaminerId = booking.ExaminerId,
                        OriginalSupervisorId = booking.OriginalSupervisorId,
                        IsSupervisorRequired = booking.IsSupervisorRequired,
                        DefenseDate = booking.DefenseDate,
                        StartTime = booking.StartTime,
                        EndTime = booking.EndTime,
                        DurationMinutes = booking.DurationMinutes,
                        Venue = booking.Venue,
                        BookingStatus = booking.BookingStatus,
                        AcademicOutcome = outcome,
                        OutcomeComments = outcomeComments,
                        ReasonForChange = reasonForChange,
                        Timestamp = DateTime.Now,
                        ActionByUserId = currentUserId
                    };
                    _context.IAFDefenseHistories.Add(history);

                    // Synchronize Milestone
                    milestone.Status = outcome;
                    string panelComment = outcome == "AdminApproved"
                        ? "Passed Defense Session. Title accepted."
                        : "Defense Failed. Please submit a new Initial Allocation Form with a change of topic.";

                    var syncResult = SyncMilestoneRemarksAttemptStatus(milestone, attemptNumber, outcome, outcomeComments ?? panelComment);
                    if (!syncResult.Success)
                    {
                        await transaction.RollbackAsync();
                        if (history != null) _context.Entry(history).State = EntityState.Detached;
                        if (booking != null) _context.Entry(booking).State = EntityState.Detached;
                        if (milestone != null) _context.Entry(milestone).State = EntityState.Detached;
                        return DefenseOutcomeResult.Fail(
                            "RemarksSyncFailed",
                            $"Failed to synchronize milestone remarks: {syncResult.ErrorMessage}");
                    }

                    await _context.SaveChangesAsync();
                    isCommitStage = true;
                    await transaction.CommitAsync();

                    return DefenseOutcomeResult.Ok(booking, history);
                }
                catch (DbUpdateConcurrencyException)
                {
                    bool rollbackConfirmed = false;
                    try
                    {
                        await transaction.RollbackAsync();
                        rollbackConfirmed = true;
                    }
                    catch
                    {
                        rollbackConfirmed = false;
                    }

                    if (history != null) _context.Entry(history).State = EntityState.Detached;
                    if (booking != null) _context.Entry(booking).State = EntityState.Detached;
                    if (milestone != null) _context.Entry(milestone).State = EntityState.Detached;

                    if (!rollbackConfirmed)
                    {
                        return DefenseOutcomeResult.Fail(
                            "ConcurrencyError",
                            "A concurrency conflict occurred and the transaction rollback could not be confirmed. Please refresh the page to verify the recorded outcome.");
                    }

                    return DefenseOutcomeResult.Fail(
                        "ConcurrencyError",
                        "The defense record was modified by another user. Please refresh and verify its current status.");
                }
                catch (Exception)
                {
                    bool rollbackConfirmed = false;
                    try
                    {
                        await transaction.RollbackAsync();
                        rollbackConfirmed = true;
                    }
                    catch
                    {
                        rollbackConfirmed = false;
                    }

                    if (history != null) _context.Entry(history).State = EntityState.Detached;
                    if (booking != null) _context.Entry(booking).State = EntityState.Detached;
                    if (milestone != null) _context.Entry(milestone).State = EntityState.Detached;

                    if (isCommitStage)
                    {
                        return DefenseOutcomeResult.Fail(
                            "ExecutionError",
                            "An error occurred while confirming the defense outcome. Please refresh and verify the recorded outcome before taking further action.");
                    }

                    if (!rollbackConfirmed)
                    {
                        return DefenseOutcomeResult.Fail(
                            "ExecutionError",
                            "An unexpected error occurred and transaction rollback could not be confirmed. Please refresh and verify the recorded outcome before taking further action.");
                    }

                    return DefenseOutcomeResult.Fail(
                        "ExecutionError",
                        "An unexpected error occurred while recording defense outcome. Please retry.");
                }
            });
        }

        public async Task<IAFDefenseBooking?> GetActiveBookingAsync(int milestoneId, int? attemptNumber = null)
        {
            if (!await IsSchemaAvailableAsync())
            {
                return null;
            }

            if (attemptNumber.HasValue)
            {
                return await _context.IAFDefenseBookings
                    .Include(b => b.Examiner)
                    .Include(b => b.OriginalSupervisor)
                    .Include(b => b.AcademicSession)
                    .FirstOrDefaultAsync(b => b.MilestoneId == milestoneId && b.AttemptNumber == attemptNumber.Value);
            }

            var milestone = await _context.FYPMilestones
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone == null) return null;

            var attemptResult = ResolveAuthoritativeAttemptNumber(milestone.Remarks);
            if (!attemptResult.Success) return null;

            return await _context.IAFDefenseBookings
                .Include(b => b.Examiner)
                .Include(b => b.OriginalSupervisor)
                .Include(b => b.AcademicSession)
                .FirstOrDefaultAsync(b => b.MilestoneId == milestoneId && b.AttemptNumber == attemptResult.AttemptNumber);
        }

        private async Task<bool> IsSchemaAvailableAsync()
        {
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IAFDefenseBooking';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        #region Internal Validation Helpers

        private class PreflightResult : DefenseBookingResult
        {
            public DefenseValidationContext? Context { get; set; }
        }

        private async Task<PreflightResult> BuildValidationContextAsync(
            int milestoneId,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            string venue)
        {
            if (milestoneId <= 0)
                return new PreflightResult { Success = false, ErrorCode = "InvalidMilestone", ErrorMessage = "Invalid milestone ID." };

            if (examinerId <= 0)
                return new PreflightResult { Success = false, ErrorCode = "InvalidExaminer", ErrorMessage = "An independent examiner must be selected." };

            if (string.IsNullOrWhiteSpace(venue))
                return new PreflightResult { Success = false, ErrorCode = "InvalidVenue", ErrorMessage = "Venue or meeting link is required." };

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project!)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project!)
                    .ThenInclude(p => p.Supervisor)
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone == null)
                return new PreflightResult { Success = false, ErrorCode = "MilestoneNotFound", ErrorMessage = "Target milestone does not exist." };

            if (milestone.Project == null)
                return new PreflightResult { Success = false, ErrorCode = "ProjectNotFound", ErrorMessage = "Milestone has no associated project." };

            // 1. Eligibility Check: Status must be DefenseRequired or DefenseScheduled
            if (milestone.Status != "DefenseRequired" && milestone.Status != "DefenseScheduled")
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "IneligibleStatus",
                    ErrorMessage = $"Milestone status '{milestone.Status}' is not eligible for defense scheduling. Must be 'DefenseRequired' or 'DefenseScheduled'."
                };
            }

            // 2. Resolve Authoritative Attempt Number (Fail Closed)
            var attemptResult = ResolveAuthoritativeAttemptNumber(milestone.Remarks);
            if (!attemptResult.Success)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "MalformedAttemptHistory",
                    ErrorMessage = attemptResult.ErrorMessage!
                };
            }
            int attemptNumber = attemptResult.AttemptNumber;

            // 3. Resolve Academic Session ID
            int? sessionId = await ResolveAcademicSessionIdAsync(milestone);
            if (!sessionId.HasValue)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "SessionNotFound",
                    ErrorMessage = "Could not resolve a valid academic session for this milestone project."
                };
            }

            // 4. Resolve Examiner and Validate Independence
            var examiner = await _context.ApplicationUsers
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == examinerId && !u.IsResign);

            if (examiner == null || !examiner.UserRoles.Any(r => r.Role.Name == "Examiner"))
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "InvalidExaminerRole",
                    ErrorMessage = "Selected examiner is inactive or does not possess the active 'Examiner' role."
                };
            }

            int supervisorId = milestone.Project.SupervisorId ?? 0;
            if (supervisorId > 0 && examiner.Id == supervisorId)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "SupervisorCannotExamine",
                    ErrorMessage = "The student's original supervisor cannot serve as the independent examiner for this defense."
                };
            }

            // 5. Read Configured Duration & Supervisor Requirement from FYPSettings
            int durationMinutes = await GetConfiguredDurationMinutesAsync();
            bool isSupervisorRequired = await GetConfiguredSupervisorRequiredAsync();

            if (isSupervisorRequired && supervisorId <= 0)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "SupervisorRequired",
                    ErrorMessage = "Supervisor attendance is required for this defense, but no valid original supervisor is assigned to the project."
                };
            }

            TimeSpan endTime = startTime.Add(TimeSpan.FromMinutes(durationMinutes));

            // Validate date and time boundaries
            if (startTime < TimeSpan.Zero || endTime > TimeSpan.FromHours(24) || endTime <= startTime)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "InvalidTimeRange",
                    ErrorMessage = "Defense time must fit within a single calendar day (00:00 to 24:00)."
                };
            }

            // Working hours window check (fail-closed, no hardcoded fallbacks)
            var (hasWorkingHours, workStart, workEnd) = await GetLecturerWorkingHoursWindowAsync();
            if (!hasWorkingHours)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "InvalidWorkingHours",
                    ErrorMessage = "Authoritative lecturer working hours are unconfigured or invalid. Scheduling fails closed."
                };
            }

            if (startTime < workStart || endTime > workEnd)
            {
                return new PreflightResult
                {
                    Success = false,
                    ErrorCode = "OutsideWorkingHours",
                    ErrorMessage = $"Defense session ({startTime:hh\\:mm} - {endTime:hh\\:mm}) falls outside configured working hours ({workStart:hh\\:mm} - {workEnd:hh\\:mm})."
                };
            }

            var context = new DefenseValidationContext
            {
                Milestone = milestone,
                AttemptNumber = attemptNumber,
                AcademicSessionId = sessionId.Value,
                Examiner = examiner,
                OriginalSupervisorId = supervisorId > 0 ? supervisorId : null,
                IsSupervisorRequired = isSupervisorRequired,
                DurationMinutes = durationMinutes,
                DefenseDate = defenseDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                Venue = venue.Trim()
            };

            return new PreflightResult { Success = true, Context = context };
        }

        private async Task<DefenseBookingResult> ValidateScheduleConflictsAsync(DefenseValidationContext ctx)
        {
            var date = ctx.DefenseDate;
            var start = ctx.StartTime;
            var end = ctx.EndTime;
            string dayOfWeekName = date.DayOfWeek.ToString();

            // 1. Authoritative Rescheduling Self-Exclusion Resolution
            var existingActiveBooking = await _context.IAFDefenseBookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.MilestoneId == ctx.Milestone.Id && 
                                          b.AttemptNumber == ctx.AttemptNumber &&
                                          (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled"));

            int? excludeBookingId = ResolveRescheduleSelfExclusion(
                ctx.Milestone.Id,
                ctx.AttemptNumber,
                existingActiveBooking?.Id,
                existingActiveBooking?.MilestoneId,
                existingActiveBooking?.AttemptNumber,
                existingActiveBooking?.BookingStatus);

            // 2. Check Student Conflict across active IAF Defense bookings (excluding ONLY the exact rescheduled booking)
            int studentId = ctx.Milestone.Project!.StudentId;
            var studentDefenseConflict = await _context.IAFDefenseBookings
                .AsNoTracking()
                .Include(b => b.Milestone!)
                    .ThenInclude(m => m.Project)
                .Where(b => b.DefenseDate == date && 
                            (!excludeBookingId.HasValue || b.Id != excludeBookingId.Value) && 
                            (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled") &&
                            b.Milestone!.Project!.StudentId == studentId &&
                            b.StartTime < end && start < b.EndTime)
                .FirstOrDefaultAsync();

            if (studentDefenseConflict != null)
            {
                return DefenseBookingResult.Fail(
                    "StudentConflict",
                    $"The student has another defense session scheduled on {date:yyyy-MM-dd} from {studentDefenseConflict.StartTime:hh\\:mm} to {studentDefenseConflict.EndTime:hh\\:mm}.");
            }

            // 3. Resolve Required Lecturer Participants (Fail-Closed if supervisor is required but missing)
            var lecturerResolution = ResolveRequiredLecturerIds(
                ctx.Examiner.Id,
                ctx.IsSupervisorRequired,
                ctx.OriginalSupervisorId);

            if (!lecturerResolution.Success)
            {
                return DefenseBookingResult.Fail(
                    lecturerResolution.ErrorCode!,
                    lecturerResolution.ErrorMessage!);
            }

            var requiredLecturerIds = lecturerResolution.LecturerIds;

            // 4. Authoritative Confirmed Availability Check (Shared B1/B2.1 Engine)
            // Enforces positive WeeklyDefault / DateOverride, working hours, and exact exclusions fail-closed.
            var availabilityResult = await EvaluateLecturersAvailabilityAsync(
                _availabilityService,
                requiredLecturerIds,
                ctx.Examiner.Id,
                date,
                start,
                end,
                ctx.AcademicSessionId,
                excludeBookingId: excludeBookingId);

            if (availabilityResult != null)
            {
                return availabilityResult;
            }

            // 5. Defense-in-depth: Retain specific conflict diagnostics for lecturer participants
            foreach (var lecId in requiredLecturerIds)
            {
                string roleName = (lecId == ctx.Examiner.Id) ? "Examiner" : "Supervisor";

                // A. Check Leave / Unavailability Date Overrides
                var leaveOverrides = await _context.SupervisorAvailabilities
                    .AsNoTracking()
                    .Where(a => a.SupervisorId == lecId && a.ScheduleType == "DateOverride" && a.SpecificDate == date && !a.IsAvailable)
                    .ToListAsync();

                foreach (var leave in leaveOverrides)
                {
                    TimeSpan bStart = (leave.StartTime == TimeSpan.Zero && leave.EndTime == TimeSpan.Zero) ? TimeSpan.Zero : leave.StartTime;
                    TimeSpan bEnd = (leave.StartTime == TimeSpan.Zero && leave.EndTime == TimeSpan.Zero) ? TimeSpan.FromHours(24) : leave.EndTime;

                    if (bStart < end && start < bEnd)
                    {
                        return DefenseBookingResult.Fail(
                            "LecturerUnavailable",
                            $"{roleName} is marked unavailable on {date:yyyy-MM-dd} during this time.");
                    }
                }

                // B. Check Teaching Timetable Entries
                var teachingConflict = await _context.SupervisorTimetableEntries
                    .AsNoTracking()
                    .Where(t => t.SupervisorId == lecId && t.AcademicSessionId == ctx.AcademicSessionId && t.DayOfWeek == dayOfWeekName &&
                                t.StartTime < end && start < t.EndTime)
                    .FirstOrDefaultAsync();

                if (teachingConflict != null)
                {
                    string className = !string.IsNullOrWhiteSpace(teachingConflict.Details) ? $" '{teachingConflict.Details}'" : "";
                    return DefenseBookingResult.Fail(
                        "TimetableConflict",
                        $"{roleName} has a scheduled teaching class{className} from {teachingConflict.StartTime:hh\\:mm} to {teachingConflict.EndTime:hh\\:mm}.");
                }

                // C. Check Meeting Bookings (Approved or Pending)
                var meetingConflicts = await _context.MeetingBookings
                    .AsNoTracking()
                    .Where(m => m.SupervisorId == lecId && m.MeetingDate == date &&
                                (m.Status == "Approved" || m.Status == "Pending"))
                    .ToListAsync();

                foreach (var m in meetingConflicts)
                {
                    TimeSpan mStart;
                    TimeSpan mEnd;
                    if (m.StartDateTime.HasValue && m.EndDateTime.HasValue && m.EndDateTime.Value > m.StartDateTime.Value)
                    {
                        mStart = m.StartDateTime.Value.TimeOfDay;
                        mEnd = m.EndDateTime.Value.TimeOfDay;
                    }
                    else if (!string.IsNullOrWhiteSpace(m.MeetingTime) && TimeSpan.TryParse(m.MeetingTime, out TimeSpan pt))
                    {
                        mStart = pt;
                        mEnd = pt.Add(TimeSpan.FromMinutes(60));
                    }
                    else
                    {
                        // Unparseable meeting time: fail closed to prevent unsafe double-booking
                        return DefenseBookingResult.Fail(
                            "MeetingConflict",
                            $"{roleName} has a scheduled student meeting with unverified time on this date.");
                    }

                    if (mStart < end && start < mEnd)
                    {
                        return DefenseBookingResult.Fail(
                            "MeetingConflict",
                            $"{roleName} has a student meeting booked from {mStart:hh\\:mm} to {mEnd:hh\\:mm}.");
                    }
                }

                // D. Check Final Viva Conflicts (fail closed if relevant viva exists and duration is invalid; consistent with GET)
                var vivas = await _context.FYPProjects
                    .AsNoTracking()
                    .Include(p => p.AssignedExaminers)
                    .Where(p => p.VivaDate.HasValue && p.VivaDate.Value.Date == date &&
                                (p.SupervisorId == lecId || (p.AssignedExaminers != null && p.AssignedExaminers.Any(ae => ae.ExaminerId == lecId))))
                    .ToListAsync();

                if (vivas.Any())
                {
                    var vivaDurationSetting = await _context.FYPSettings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Key == "VivaDurationMinutes");

                    int? vivaDuration = null;
                    if (vivaDurationSetting != null && int.TryParse(vivaDurationSetting.Value, out int vd) && vd > 0)
                    {
                        vivaDuration = vd;
                    }

                    var vivaConflictResult = CheckVivaConflict(
                        roleName,
                        date,
                        start,
                        end,
                        vivaDuration,
                        vivas.Select(v => v.VivaDate!.Value.TimeOfDay));

                    if (vivaConflictResult != null)
                    {
                        return vivaConflictResult;
                    }
                }

                // E. Check Existing IAF Defense Bookings (across Examiner AND Supervisor roles, excluding ONLY the exact rescheduled booking)
                var defenseConflict = await _context.IAFDefenseBookings
                    .AsNoTracking()
                    .Where(b => b.DefenseDate == date && 
                                (!excludeBookingId.HasValue || b.Id != excludeBookingId.Value) &&
                                (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled") &&
                                (b.ExaminerId == lecId || (b.IsSupervisorRequired && b.OriginalSupervisorId == lecId)) &&
                                b.StartTime < end && start < b.EndTime)
                    .FirstOrDefaultAsync();

                if (defenseConflict != null)
                {
                    return DefenseBookingResult.Fail(
                        "DefenseConflict",
                        $"{roleName} is already assigned to another IAF Defense session on {date:yyyy-MM-dd} from {defenseConflict.StartTime:hh\\:mm} to {defenseConflict.EndTime:hh\\:mm}.");
                }
            }

            // 3. Check Physical Venue Overlap (excluding ONLY the exact rescheduled booking)
            string normVenue = NormalizeVenue(ctx.Venue);
            if (!string.IsNullOrWhiteSpace(normVenue) && !normVenue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var venueConflict = await _context.IAFDefenseBookings
                    .AsNoTracking()
                    .Where(b => b.DefenseDate == date &&
                                (!excludeBookingId.HasValue || b.Id != excludeBookingId.Value) &&
                                (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled") &&
                                b.StartTime < end && start < b.EndTime)
                    .ToListAsync();

                if (venueConflict.Any(b => NormalizeVenue(b.Venue) == normVenue))
                {
                    return DefenseBookingResult.Fail(
                        "VenueConflict",
                        $"Venue '{ctx.Venue}' is already booked for another defense session on {date:yyyy-MM-dd} during this time.");
                }
            }

            return DefenseBookingResult.Ok(null!, null!);
        }

        private async Task<(bool Success, int ReturnCode)> AcquireTransactionAppLockAsync(string resource, int timeoutSeconds)
        {
            var returnParam = new SqlParameter
            {
                ParameterName = "@ReturnCode",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output
            };

            var resourceParam = new SqlParameter("@Resource", resource);
            var modeParam = new SqlParameter("@LockMode", "Exclusive");
            var ownerParam = new SqlParameter("@LockOwner", "Transaction");
            var timeoutParam = new SqlParameter("@LockTimeout", timeoutSeconds * 1000);

            await _context.Database.ExecuteSqlRawAsync(
                "DECLARE @res int; EXEC @res = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout; SET @ReturnCode = @res;",
                returnParam, resourceParam, modeParam, ownerParam, timeoutParam);

            int returnCode = (returnParam.Value is int rc) ? rc : (int.TryParse(returnParam.Value?.ToString(), out var parsed) ? parsed : -999);

            // sp_getapplock Return values:
            // 0 = Lock was successfully granted synchronously.
            // 1 = Lock was granted successfully after waiting.
            // < 0 = Failed, timed out (-1), or cancelled (-2).
            bool granted = (returnCode >= 0);
            return (granted, returnCode);
        }

        private static string NormalizeVenue(string venue)
        {
            if (string.IsNullOrWhiteSpace(venue)) return string.Empty;
            return string.Concat(venue.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        }

        private static string FormatDisplayTime(TimeSpan time)
        {
            return new DateTime(time.Ticks).ToString("hh:mm tt");
        }

        public static (bool Success, int AttemptNumber, string? ErrorMessage) ResolveAuthoritativeAttemptNumber(string? remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks))
            {
                return (false, 0, "Milestone remarks are empty; no IAF attempt history found.");
            }

            if (!remarks.Contains("JSON: "))
            {
                return (false, 0, "Milestone remarks do not contain structured attempt data ('JSON: ' marker missing).");
            }

            try
            {
                var parts = remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                {
                    return (false, 0, "Milestone remarks contain empty JSON payload.");
                }

                var history = IafAttemptHistory.ParseOrMigrate(parts[1]);
                if (history == null || history.Attempts == null || history.Attempts.Count == 0)
                {
                    return (false, 0, "Failed to parse structured IAF attempt history or no attempts found.");
                }

                if (history.CurrentAttempt <= 0)
                {
                    return (false, 0, $"Structured IAF attempt history indicates an invalid CurrentAttempt: {history.CurrentAttempt}.");
                }

                var matchedAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                if (matchedAttempt == null)
                {
                    return (false, 0, $"Structured IAF attempt history has CurrentAttempt={history.CurrentAttempt}, but no matching attempt record was found in the Attempts list.");
                }

                return (true, history.CurrentAttempt, null);
            }
            catch (Exception ex)
            {
                return (false, 0, $"Error parsing authoritative IAF attempt history: {ex.Message}");
            }
        }

        private (bool Success, string? ErrorMessage) SyncMilestoneRemarksAttemptStatus(
            FYPMilestone milestone,
            int attemptNumber,
            string status,
            string? comments = null)
        {
            if (string.IsNullOrWhiteSpace(milestone.Remarks) || !milestone.Remarks.Contains("JSON: "))
            {
                return (false, "Milestone remarks do not contain structured attempt data ('JSON: ' marker missing).");
            }

            var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                return (false, "Milestone remarks contain empty JSON payload.");
            }

            var formInfo = parts[0].Trim();
            try
            {
                var history = IafAttemptHistory.ParseOrMigrate(parts[1]);
                if (history == null || history.Attempts == null || history.Attempts.Count == 0)
                {
                    return (false, "Failed to parse structured IAF attempt history or no attempts found.");
                }

                var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == attemptNumber);
                if (currentAttempt == null)
                {
                    return (false, $"Attempt #{attemptNumber} was not found in milestone structured remarks history.");
                }

                currentAttempt.Status = status;
                if (!string.IsNullOrWhiteSpace(comments))
                {
                    currentAttempt.PanelComments = comments;
                }

                milestone.Remarks = $"{formInfo} JSON: {JsonSerializer.Serialize(history)}";
                return (true, null);
            }
            catch (Exception)
            {
                return (false, "Unable to synchronize milestone attempt history.");
            }
        }

        private async Task<int?> ResolveAcademicSessionIdAsync(FYPMilestone milestone)
        {
            var project = milestone.Project;
            if (project == null) return null;

            string? sessionName = project.AcademicSession;
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                sessionName = await _context.ApplicationUsers
                    .AsNoTracking()
                    .Where(u => u.Id == project.StudentId)
                    .Select(u => u.AcademicSession)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(sessionName)) return null;

            sessionName = sessionName.Trim();
            return await _context.AcademicSessions
                .AsNoTracking()
                .Where(s => s.Name == sessionName)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
        }

        private async Task<int> GetConfiguredDurationMinutesAsync()
        {
            var durationSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "IAFDefenseDurationMinutes");

            if (durationSetting != null && int.TryParse(durationSetting.Value, out int dur) && dur >= 15 && dur <= 180)
            {
                return dur;
            }

            return 45; // Default fallback
        }

        private async Task<bool> GetConfiguredSupervisorRequiredAsync()
        {
            var supervisorSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "IAFDefenseSupervisorRequired");

            if (supervisorSetting != null && bool.TryParse(supervisorSetting.Value, out bool req))
            {
                return req;
            }

            return false;
        }

        private async Task<(bool Valid, TimeSpan Start, TimeSpan End)> GetLecturerWorkingHoursWindowAsync()
        {
            var startSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");
            var endSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");

            if (!LecturerAvailabilityService.TryParseWorkingHours(startSetting?.Value, endSetting?.Value, out TimeSpan workStart, out TimeSpan workEnd))
            {
                return (false, TimeSpan.Zero, TimeSpan.Zero);
            }

            return (true, workStart, workEnd);
        }

        public class LecturerResolutionResult
        {
            public bool Success { get; set; }
            public string? ErrorCode { get; set; }
            public string? ErrorMessage { get; set; }
            public List<int> LecturerIds { get; set; } = new();

            public static LecturerResolutionResult Ok(List<int> lecturerIds) =>
                new() { Success = true, LecturerIds = lecturerIds };

            public static LecturerResolutionResult Fail(string errorCode, string errorMessage) =>
                new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }

        public static LecturerResolutionResult ResolveRequiredLecturerIds(int examinerId, bool isSupervisorRequired, int? supervisorId)
        {
            if (isSupervisorRequired)
            {
                if (!supervisorId.HasValue || supervisorId.Value <= 0)
                {
                    return LecturerResolutionResult.Fail(
                        "SupervisorRequired",
                        "Supervisor attendance is required for this defense, but no valid original supervisor is assigned to the project.");
                }
                return LecturerResolutionResult.Ok(new List<int> { examinerId, supervisorId.Value });
            }

            return LecturerResolutionResult.Ok(new List<int> { examinerId });
        }

        public static DefenseBookingResult? CheckVivaConflict(
            string roleName,
            DateTime defenseDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int? authoritativeVivaDurationMinutes,
            IEnumerable<TimeSpan> vivaStartTimes)
        {
            if (vivaStartTimes == null || !vivaStartTimes.Any())
            {
                return null; // No relevant Viva on this date, so missing Viva duration alone must not reject
            }

            if (!authoritativeVivaDurationMinutes.HasValue || authoritativeVivaDurationMinutes.Value <= 0)
            {
                return DefenseBookingResult.Fail(
                    "ConfigurationError",
                    $"{roleName} has a scheduled Final Viva presentation on {defenseDate:yyyy-MM-dd}, but the authoritative Viva duration setting is missing or invalid. Defense scheduling cannot proceed.");
            }

            int duration = authoritativeVivaDurationMinutes.Value;
            foreach (var vStart in vivaStartTimes)
            {
                var vEnd = vStart.Add(TimeSpan.FromMinutes(duration));
                if (vStart < endTime && startTime < vEnd)
                {
                    return DefenseBookingResult.Fail(
                        "VivaConflict",
                        $"{roleName} has a Final Viva examination scheduled from {vStart:hh\\:mm} to {vEnd:hh\\:mm}.");
                }
            }

            return null;
        }

        public static async Task<DefenseBookingResult?> EvaluateLecturersAvailabilityAsync(
            ILecturerAvailabilityService availabilityService,
            IEnumerable<int> lecturerIds,
            int examinerId,
            DateTime defenseDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int academicSessionId,
            int? excludeBookingId = null,
            int? excludeMilestoneId = null)
        {
            foreach (var lecId in lecturerIds)
            {
                string roleName = (lecId == examinerId) ? "Examiner" : "Supervisor";
                bool isAvailable;
                try
                {
                    isAvailable = await availabilityService.IsSlotAvailableAsync(
                        lecId,
                        defenseDate,
                        startTime,
                        endTime,
                        academicSessionId,
                        excludeBookingId: excludeBookingId,
                        excludeMilestoneId: excludeMilestoneId);
                }
                catch
                {
                    return DefenseBookingResult.Fail(
                        "AvailabilityVerificationFailed",
                        $"Unable to verify {roleName} availability. Scheduling has been blocked for safety.");
                }

                if (!isAvailable)
                {
                    return DefenseBookingResult.Fail(
                        "LecturerNotAvailable",
                        $"{roleName} does not have confirmed availability for the selected defense time.");
                }
            }
            return null;
        }

        public static int? ResolveRescheduleSelfExclusion(
            int milestoneId,
            int attemptNumber,
            int? existingBookingId,
            int? existingBookingMilestoneId,
            int? existingBookingAttemptNumber,
            string? existingBookingStatus)
        {
            if (existingBookingId.HasValue && existingBookingId.Value > 0 &&
                existingBookingMilestoneId.HasValue && existingBookingMilestoneId.Value == milestoneId &&
                existingBookingAttemptNumber.HasValue && existingBookingAttemptNumber.Value == attemptNumber &&
                (existingBookingStatus == "Scheduled" || existingBookingStatus == "Rescheduled"))
            {
                return existingBookingId.Value;
            }
            return null;
        }

        public static int? ResolveRescheduleSelfExclusion(
            int milestoneId,
            int attemptNumber,
            int? existingBookingMilestoneId,
            int? existingBookingAttemptNumber,
            string? existingBookingStatus)
        {
            if (existingBookingMilestoneId.HasValue &&
                existingBookingMilestoneId.Value == milestoneId &&
                existingBookingAttemptNumber.HasValue &&
                existingBookingAttemptNumber.Value == attemptNumber &&
                (existingBookingStatus == "Scheduled" || existingBookingStatus == "Rescheduled"))
            {
                return milestoneId;
            }
            return null;
        }

        #endregion
    }
}
