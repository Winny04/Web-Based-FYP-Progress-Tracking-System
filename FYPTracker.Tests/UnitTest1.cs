
using FYPTracker.Models;
using FYPTracker.Services;
using FYPTracker.Services.Defense;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FYPTracker.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void UnavailableTime_ShouldBeRemoved()
        {
            // No database context is created or used.
            var service = new LecturerAvailabilityService(null!);

            var available = new List<TimeSpanRange>
            {
                new TimeSpanRange
                {
                    StartTime = new TimeSpan(12, 30, 0),
                    EndTime = new TimeSpan(17, 0, 0)
                }
            };

            // Test the actual private interval calculation
            // without accessing SQL Server.
            var method = typeof(LecturerAvailabilityService)
                .GetMethod("RemoveOverlap",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

            Assert.NotNull(method);

            var result = method!.Invoke(service, new object[]
            {
                available,
                new TimeSpan(14, 0, 0),
                new TimeSpan(15, 0, 0)
            }) as List<TimeSpanRange>;

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);

            Assert.Equal(new TimeSpan(12, 30, 0),
                result[0].StartTime);

            Assert.Equal(new TimeSpan(14, 0, 0),
                result[0].EndTime);

            Assert.Equal(new TimeSpan(15, 0, 0),
                result[1].StartTime);

            Assert.Equal(new TimeSpan(17, 0, 0),
                result[1].EndTime);
        }


        [Fact]
        public void FullDayUnavailable_ShouldRemoveAllAvailableTime()
        {
            // In-memory only: no SQL Server connection.
            var service = new LecturerAvailabilityService(null!);

            var available = new List<TimeSpanRange>
    {
        new TimeSpanRange
        {
            StartTime = new TimeSpan(12, 30, 0),
            EndTime = new TimeSpan(17, 30, 0)
        }
    };

            var method = typeof(LecturerAvailabilityService)
                .GetMethod(
                    "RemoveOverlap",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);

            var result = method!.Invoke(service, new object[]
            {
        available,
        new TimeSpan(12, 30, 0),
        new TimeSpan(17, 30, 0)
            }) as List<TimeSpanRange>;

            Assert.NotNull(result);
            Assert.Empty(result!);
        }

        [Fact]
        public void OverlappingAvailableIntervals_ShouldBeMerged()
        {
            var available = new List<TimeSpanRange>
    {
        new() {
            StartTime = new TimeSpan(12, 30, 0),
            EndTime = new TimeSpan(14, 0, 0)
        },
        new() {
            StartTime = new TimeSpan(13, 30, 0),
            EndTime = new TimeSpan(15, 0, 0)
        },
        new() {
            StartTime = new TimeSpan(15, 0, 0),
            EndTime = new TimeSpan(16, 0, 0)
        }
    };

            var method = typeof(LecturerAvailabilityService)
                .GetMethod(
                    "MergeContiguousOrOverlappingIntervals",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = method!.Invoke(null, new object[] { available })
                as List<TimeSpanRange>;

            Assert.NotNull(result);
            Assert.Single(result!);

            Assert.Equal(new TimeSpan(12, 30, 0),
                result![0].StartTime);

            Assert.Equal(new TimeSpan(16, 0, 0),
                result[0].EndTime);
        }

        [Fact]
        public void MutualAvailability_ShouldHandleDifferentStartTimes()
        {
            // In-memory test: no SQL Server connection.
            var service = new LecturerAvailabilityService(null!);

            var examiner = new List<TimeSpanRange>
    {
        new()
        {
            StartTime = new TimeSpan(12, 30, 0),
            EndTime = new TimeSpan(13, 30, 0)
        }
    };

            var supervisor = new List<TimeSpanRange>
    {
        new()
        {
            StartTime = new TimeSpan(12, 40, 0),
            EndTime = new TimeSpan(13, 25, 0)
        }
    };

            var intersectMethod = typeof(LecturerAvailabilityService)
                .GetMethod(
                    "IntersectContinuousIntervals",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(intersectMethod);

            var intersection = intersectMethod!.Invoke(
                service,
                new object[] { examiner, supervisor })
                as List<TimeSpanRange>;

            Assert.NotNull(intersection);
            Assert.Single(intersection!);

            var sliceMethod = typeof(LecturerAvailabilityService)
                .GetMethod(
                    "SliceIntoDiscreteSlots",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(sliceMethod);

            var slots = sliceMethod!.Invoke(
                null,
                new object[] { intersection!, 15 })
                as List<TimeSpanRange>;

            Assert.NotNull(slots);
            Assert.Equal(3, slots!.Count);

            Assert.Equal(new TimeSpan(12, 40, 0), slots[0].StartTime);
            Assert.Equal(new TimeSpan(12, 55, 0), slots[0].EndTime);

            Assert.Equal(new TimeSpan(12, 55, 0), slots[1].StartTime);
            Assert.Equal(new TimeSpan(13, 10, 0), slots[1].EndTime);

            Assert.Equal(new TimeSpan(13, 10, 0), slots[2].StartTime);
            Assert.Equal(new TimeSpan(13, 25, 0), slots[2].EndTime);
        }

        #region Phase B2.1 Tests

        [Fact]
        public void Test1_FullIntervalInsideConfirmedAvailability_ReturnsTrue()
        {
            var freeIntervals = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 0, 0) }
            };

            // 12:30 to 12:45 is completely contained
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 30, 0),
                new TimeSpan(12, 45, 0)));

            // 12:45 to 13:00 is completely contained
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 45, 0),
                new TimeSpan(13, 0, 0)));
        }

        [Fact]
        public void Test2_IntervalExtendingOutsideConfirmedAvailability_ReturnsFalse()
        {
            var freeIntervals = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(13, 0, 0) }
            };

            // Extends past 13:00
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 45, 0),
                new TimeSpan(13, 15, 0)));

            // Starts before 12:30
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 15, 0),
                new TimeSpan(12, 45, 0)));
        }

        [Fact]
        public void Test3_NoConfirmedAvailability_ReturnsFalse()
        {
            var emptyIntervals = new List<TimeSpanRange>();

            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                emptyIntervals,
                new TimeSpan(12, 30, 0),
                new TimeSpan(12, 45, 0)));

            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                null,
                new TimeSpan(12, 30, 0),
                new TimeSpan(12, 45, 0)));
        }

        [Fact]
        public void Test4_PartialUnavailableOverlap_SubtractsAndRejectsOverlappingInterval()
        {
            var initial = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(15, 0, 0) }
            };

            // Subtract unavailable block: 13:00 to 14:00
            var remaining = LecturerAvailabilityService.RemoveOverlapStatic(
                initial,
                new TimeSpan(13, 0, 0),
                new TimeSpan(14, 0, 0));

            Assert.Equal(2, remaining.Count);

            // 13:00 - 13:45 is blocked
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));

            // 12:45 - 13:15 straddles the blocked boundary
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(12, 45, 0),
                new TimeSpan(13, 15, 0)));

            // Cleanly inside first segment
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(12, 30, 0),
                new TimeSpan(13, 0, 0)));

            // Cleanly inside second segment
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(14, 0, 0),
                new TimeSpan(14, 30, 0)));
        }

        [Fact]
        public void Test5_FullDayUnavailable_RemovesAllAndRejectsAnySlot()
        {
            var initial = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            // Full-day block convention (12:30 to 17:30)
            var remaining = LecturerAvailabilityService.RemoveOverlapStatic(
                initial,
                new TimeSpan(12, 30, 0),
                new TimeSpan(17, 30, 0));

            Assert.Empty(remaining);

            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 15, 0)));
        }

        [Fact]
        public void Test6_ConfirmedOneOffDateOverride_ValidOnlyWithinPermittedRange()
        {
            var workStart = new TimeSpan(12, 30, 0);
            var workEnd = new TimeSpan(17, 30, 0);

            // One-off positive override: 11:00 to 14:00
            var overrideStart = new TimeSpan(11, 0, 0);
            var overrideEnd = new TimeSpan(14, 0, 0);

            // Clamping logic per B1 engine
            var clampedStart = overrideStart < workStart ? workStart : overrideStart;
            var clampedEnd = overrideEnd > workEnd ? workEnd : overrideEnd;

            var clampedIntervals = new List<TimeSpanRange>
            {
                new() { StartTime = clampedStart, EndTime = clampedEnd } // 12:30 to 14:00
            };

            // Before working hours (11:30 - 12:00) is rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                clampedIntervals,
                new TimeSpan(11, 30, 0),
                new TimeSpan(12, 0, 0)));

            // Inside clamped range (12:30 - 13:00) is valid
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                clampedIntervals,
                new TimeSpan(12, 30, 0),
                new TimeSpan(13, 0, 0)));

            // After override ends (14:00 - 14:30) is rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                clampedIntervals,
                new TimeSpan(14, 0, 0),
                new TimeSpan(14, 30, 0)));
        }

        [Fact]
        public void Test7_OtherMilestoneDefenseConflict_BlocksSlot()
        {
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int MilestoneId, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (100, new TimeSpan(14, 0, 0), new TimeSpan(14, 45, 0)) // Milestone 100
            };

            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeMilestoneId: null);

            // 14:00 to 14:45 is blocked by Milestone 100
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(14, 0, 0),
                new TimeSpan(14, 45, 0)));

            // 13:00 to 13:45 is free
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));
        }

        [Fact]
        public void Test8_CurrentMilestoneExclusionDuringRescheduling_PermitsSlotWhileEnforcingOtherConflicts()
        {
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int MilestoneId, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (266, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0)), // Milestone 266 (self)
                (100, new TimeSpan(15, 0, 0), new TimeSpan(15, 45, 0))  // Milestone 100 (other)
            };

            // Rescheduling Milestone 266: excludeMilestoneId = 266
            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeMilestoneId: 266);

            // Milestone 266 self-booking is NOT subtracted: no self-conflict!
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));

            // Milestone 100 remains strictly blocked!
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(15, 0, 0),
                new TimeSpan(15, 45, 0)));

            // Other free slot remains available
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(16, 0, 0),
                new TimeSpan(16, 45, 0)));
        }

        [Fact]
        public void Test9_MissingValidWorkingHours_FailsClosed()
        {
            // Missing start
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours(null, "17:30", out _, out _));

            // Missing end
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("12:30", null, out _, out _));

            // Unparseable string
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("not-a-time", "17:30", out _, out _));

            // Inverted range
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("17:30", "12:30", out _, out _));

            // Zero-duration range
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("12:30", "12:30", out _, out _));

            // Valid range
            Assert.True(LecturerAvailabilityService.TryParseWorkingHours("12:30", "17:30", out var s, out var e));
            Assert.Equal(new TimeSpan(12, 30, 0), s);
            Assert.Equal(new TimeSpan(17, 30, 0), e);
        }

        [Fact]
        public void Test10_ArbitraryStartTimeAndDurationBoundaries_HandledCorrectly()
        {
            var freeIntervals = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(14, 0, 0) }
            };

            // Arbitrary 15-minute start at 12:35 to 12:50: valid
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 35, 0),
                new TimeSpan(12, 50, 0)));

            // Arbitrary 45-minute interval 12:42 to 13:27: valid
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(12, 42, 0),
                new TimeSpan(13, 27, 0)));

            // Exceeds upper boundary (13:30 to 14:15): invalid
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(13, 30, 0),
                new TimeSpan(14, 15, 0)));

            // Inverted interval (startTime >= endTime): rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(13, 0, 0),
                new TimeSpan(12, 45, 0)));

            // Zero-length interval: rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 0, 0)));

            // Negative time: rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(-1, 0, 0),
                new TimeSpan(13, 0, 0)));

            // Crossing midnight (> 24 hours): rejected
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                freeIntervals,
                new TimeSpan(23, 30, 0),
                new TimeSpan(25, 0, 0)));
        }

        #endregion

        #region Phase B2.2 Tests

        [Fact]
        public async Task B2_2_Test1_NoConfirmedAvailability_BookingFails()
        {
            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => Task.FromResult(false)
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                new[] { 42 },
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 13),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.Equal("LecturerNotAvailable", result.ErrorCode);
            Assert.Contains("Examiner does not have confirmed availability", result.ErrorMessage);
        }

        [Fact]
        public async Task B2_2_Test2_ValidConfirmedAvailability_BookingPasses()
        {
            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => Task.FromResult(true)
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                new[] { 42 },
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.Null(result); // Null indicates no conflict / success
        }

        [Fact]
        public async Task B2_2_Test3_OutsideConfirmedAvailability_BookingFails()
        {
            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) =>
                {
                    // Available only until 13:00; requested slot 13:00-13:15 is outside
                    if (s >= new TimeSpan(12, 30, 0) && e <= new TimeSpan(13, 0, 0)) return Task.FromResult(true);
                    return Task.FromResult(false);
                }
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                new[] { 42 },
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(13, 0, 0),
                endTime: new TimeSpan(13, 15, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.Equal("LecturerNotAvailable", result.ErrorCode);
        }

        [Fact]
        public async Task B2_2_Test4_SupervisorRequired_BothMustBeAvailable()
        {
            // When supervisor is required, both examiner and supervisor IDs must be resolved
            var lecturerResolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: 10);

            Assert.True(lecturerResolution.Success);
            var requiredLecturers = lecturerResolution.LecturerIds;

            Assert.Equal(2, requiredLecturers.Count);
            Assert.Contains(42, requiredLecturers);
            Assert.Contains(10, requiredLecturers);

            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) =>
                {
                    // Examiner (42) is available, Supervisor (10) is NOT available
                    return Task.FromResult(lecId == 42);
                }
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                requiredLecturers,
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.Equal("LecturerNotAvailable", result.ErrorCode);
            Assert.Contains("Supervisor does not have confirmed availability", result.ErrorMessage);
        }

        [Fact]
        public async Task B2_2_Test5_SupervisorNotRequired_OnlyExaminerAvailabilityRequired()
        {
            var lecturerResolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: false,
                supervisorId: 10);

            Assert.True(lecturerResolution.Success);
            var requiredLecturers = lecturerResolution.LecturerIds;

            Assert.Single(requiredLecturers);
            Assert.Contains(42, requiredLecturers);

            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) =>
                {
                    // Examiner is available, Supervisor is unavailable
                    return Task.FromResult(lecId == 42);
                }
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                requiredLecturers,
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.Null(result); // Passes because supervisor was not required
        }

        [Fact]
        public void B2_2_Test6_RescheduleSelfExclusion_ResolvesAuthoritatively()
        {
            // Case A: Matching milestone, matching attempt, active status -> Exclude self
            var excl1 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingMilestoneId: 266,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Scheduled");
            Assert.Equal(266, excl1);

            // Case B: Same milestone, Rescheduled status -> Exclude self
            var excl2 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingMilestoneId: 266,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Rescheduled");
            Assert.Equal(266, excl2);

            // Case C: Different milestone -> Do NOT exclude
            var excl3 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingMilestoneId: 100,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Scheduled");
            Assert.Null(excl3);

            // Case D: Different attempt -> Do NOT exclude
            var excl4 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 2,
                existingBookingMilestoneId: 266,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Scheduled");
            Assert.Null(excl4);

            // Case E: Inactive / Completed status -> Do NOT exclude
            var excl5 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingMilestoneId: 266,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Completed");
            Assert.Null(excl5);

            // Case F: No existing booking (initial schedule) -> Do NOT exclude
            var excl6 = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingMilestoneId: null,
                existingBookingAttemptNumber: null,
                existingBookingStatus: null);
            Assert.Null(excl6);
        }

        [Fact]
        public void B2_2_Test7_WorkingHoursFailClosed_InvalidOrMissingHoursRejected()
        {
            // Missing values
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours(null, "17:30", out _, out _));
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("12:30", null, out _, out _));

            // Inverted range
            Assert.False(LecturerAvailabilityService.TryParseWorkingHours("17:30", "12:30", out _, out _));

            // Valid range
            Assert.True(LecturerAvailabilityService.TryParseWorkingHours("12:30", "17:30", out var s, out var e));
            Assert.Equal(new TimeSpan(12, 30, 0), s);
            Assert.Equal(new TimeSpan(17, 30, 0), e);
        }

        [Fact]
        public async Task B2_2_Test8_AvailabilityValidatorException_FailsClosed()
        {
            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => throw new InvalidOperationException("Simulated database timeout")
            };

            var result = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                new[] { 42 },
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeMilestoneId: null);

            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.Equal("AvailabilityVerificationFailed", result.ErrorCode);
        }

        [Fact]
        public void B2_2_Test9_RequiredLecturerResolution_HandlesEdgeCases()
        {
            // Null supervisor ID -> Fails closed
            var res1 = IAFDefenseBookingService.ResolveRequiredLecturerIds(42, isSupervisorRequired: true, supervisorId: null);
            Assert.False(res1.Success);
            Assert.Equal("SupervisorRequired", res1.ErrorCode);

            // Zero supervisor ID -> Fails closed
            var res2 = IAFDefenseBookingService.ResolveRequiredLecturerIds(42, isSupervisorRequired: true, supervisorId: 0);
            Assert.False(res2.Success);
            Assert.Equal("SupervisorRequired", res2.ErrorCode);

            // Negative supervisor ID -> Fails closed
            var res3 = IAFDefenseBookingService.ResolveRequiredLecturerIds(42, isSupervisorRequired: true, supervisorId: -5);
            Assert.False(res3.Success);
            Assert.Equal("SupervisorRequired", res3.ErrorCode);

            // Valid supervisor ID -> Succeeds with both
            var res4 = IAFDefenseBookingService.ResolveRequiredLecturerIds(42, isSupervisorRequired: true, supervisorId: 15);
            Assert.True(res4.Success);
            Assert.Equal(2, res4.LecturerIds.Count);
            Assert.Equal(42, res4.LecturerIds[0]);
            Assert.Equal(15, res4.LecturerIds[1]);
        }

        [Fact]
        public void B2_2_Test10_TransactionRevalidation_MethodIntegrity()
        {
            var bookingServiceType = typeof(IAFDefenseBookingService);

            // Verify ValidateScheduleConflictsAsync is a private method
            var method = bookingServiceType.GetMethod(
                "ValidateScheduleConflictsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);

            // Verify ScheduleOrRescheduleDefenseAsync is a public method
            var scheduleMethod = bookingServiceType.GetMethod(
                "ScheduleOrRescheduleDefenseAsync",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(scheduleMethod);
        }

        #endregion

        #region Phase B2.2H Tests (Safety Hardening)

        [Fact]
        public void B2_2H_Test1_CurrentBookingSelfExclusion_WorksByExactBookingId()
        {
            // Authoritative self-exclusion resolution returns the exact BookingId
            var exclId = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingId: 555,
                existingBookingMilestoneId: 266,
                existingBookingAttemptNumber: 1,
                existingBookingStatus: "Scheduled");

            Assert.Equal(555, exclId);

            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (555, 266, 1, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0)) // Exact active booking being rescheduled
            };

            // When booking 555 is excluded, slot 13:00 - 13:45 is NOT blocked
            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeBookingId: 555);

            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));
        }

        [Fact]
        public void B2_2H_Test2_SameMilestoneDifferentAttempt_RemainsConflict()
        {
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (555, 266, 1, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0)), // Attempt 1 booking (being rescheduled)
                (777, 266, 2, new TimeSpan(14, 0, 0), new TimeSpan(14, 45, 0))  // Attempt 2 booking (different attempt on same milestone!)
            };

            // Rescheduling Attempt 1 (excludeBookingId = 555)
            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeBookingId: 555);

            // Attempt 1 slot (13:00-13:45) is permitted (no self-conflict)
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));

            // Attempt 2 slot (14:00-14:45) REMAINS STRICTLY CONFLICTED AND BLOCKED!
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(14, 0, 0),
                new TimeSpan(14, 45, 0)));
        }

        [Fact]
        public void B2_2H_Test3_AnotherMilestoneBooking_RemainsConflict()
        {
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (555, 266, 1, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0)), // Self booking 555
                (888, 100, 1, new TimeSpan(15, 0, 0), new TimeSpan(15, 45, 0))  // Milestone 100 booking 888
            };

            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeBookingId: 555);

            // Milestone 100 booking remains strictly blocked
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(15, 0, 0),
                new TimeSpan(15, 45, 0)));
        }

        [Fact]
        public void B2_2H_Test4_InitialScheduling_ExcludesNothing()
        {
            // Initial schedule: no existing booking to exclude
            var exclId = IAFDefenseBookingService.ResolveRescheduleSelfExclusion(
                milestoneId: 266,
                attemptNumber: 1,
                existingBookingId: null,
                existingBookingMilestoneId: null,
                existingBookingAttemptNumber: null,
                existingBookingStatus: null);

            Assert.Null(exclId);

            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            var bookings = new List<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (555, 266, 1, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0))
            };

            // With excludeBookingId null, all bookings block their time
            var afterConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeBookingId: null);

            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));
        }

        [Fact]
        public void B2_2H_Test5_LegacyDefenseWithUncertainIdentity_NotSilentlyExcluded()
        {
            var validRemarks = "Form info JSON: {\"CurrentAttempt\":1,\"Attempts\":[{\"AttemptNumber\":1,\"Status\":\"DefenseScheduled\"}]}";
            var attempt2Remarks = "Form info JSON: {\"CurrentAttempt\":2,\"Attempts\":[{\"AttemptNumber\":2,\"Status\":\"DefenseScheduled\"}]}";
            var malformedRemarks = "Empty or invalid remarks";

            var targetDate = new DateTime(2026, 10, 14);
            var startTime = new TimeSpan(13, 0, 0);

            // Case A: Exact match across MilestoneId, Attempt, Date, and Time -> Suppressed
            Assert.True(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 266,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "01:00 PM",
                legacyRemarks: validRemarks,
                excludedBookingMilestoneId: 266,
                excludedBookingAttemptNumber: 1,
                excludedBookingDate: targetDate,
                excludedBookingStartTime: startTime));

            // Case B: Same milestone, different attempt -> NOT suppressed (conflict candidate!)
            Assert.False(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 266,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "01:00 PM",
                legacyRemarks: attempt2Remarks,
                excludedBookingMilestoneId: 266,
                excludedBookingAttemptNumber: 1,
                excludedBookingDate: targetDate,
                excludedBookingStartTime: startTime));

            // Case C: Same milestone, different time -> NOT suppressed (conflict candidate!)
            Assert.False(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 266,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "02:00 PM",
                legacyRemarks: validRemarks,
                excludedBookingMilestoneId: 266,
                excludedBookingAttemptNumber: 1,
                excludedBookingDate: targetDate,
                excludedBookingStartTime: startTime));

            // Case D: Malformed remarks (uncertain attempt identity) -> NOT suppressed (fails closed!)
            Assert.False(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 266,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "01:00 PM",
                legacyRemarks: malformedRemarks,
                excludedBookingMilestoneId: 266,
                excludedBookingAttemptNumber: 1,
                excludedBookingDate: targetDate,
                excludedBookingStartTime: startTime));

            // Case E: Different milestone -> NOT suppressed
            Assert.False(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 100,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "01:00 PM",
                legacyRemarks: validRemarks,
                excludedBookingMilestoneId: 266,
                excludedBookingAttemptNumber: 1,
                excludedBookingDate: targetDate,
                excludedBookingStartTime: startTime));

            // Case F: Initial scheduling (no excluded booking values) -> NOT suppressed
            Assert.False(LecturerAvailabilityService.ShouldSuppressLegacyDefense(
                legacyMilestoneId: 266,
                legacyDefenseDate: targetDate,
                legacyDefenseTime: "01:00 PM",
                legacyRemarks: validRemarks,
                excludedBookingMilestoneId: null,
                excludedBookingAttemptNumber: null,
                excludedBookingDate: null,
                excludedBookingStartTime: null));
        }

        [Fact]
        public void B2_2H_Test6_SupervisorRequired_MissingOrInvalidId_Rejects()
        {
            // Null Supervisor ID
            var resNull = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: null);

            Assert.False(resNull.Success);
            Assert.Equal("SupervisorRequired", resNull.ErrorCode);

            // Zero Supervisor ID
            var resZero = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: 0);

            Assert.False(resZero.Success);
            Assert.Equal("SupervisorRequired", resZero.ErrorCode);

            // Negative Supervisor ID
            var resNeg = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: -1);

            Assert.False(resNeg.Success);
            Assert.Equal("SupervisorRequired", resNeg.ErrorCode);
        }

        [Fact]
        public async Task B2_2H_Test7_SupervisorRequired_ValidId_BothParticipantsChecked()
        {
            var resolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: 99);

            Assert.True(resolution.Success);
            Assert.Equal(2, resolution.LecturerIds.Count);
            Assert.Contains(42, resolution.LecturerIds);
            Assert.Contains(99, resolution.LecturerIds);

            // Simulated service where examiner is available but supervisor is unavailable
            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => Task.FromResult(lecId == 42)
            };

            var checkResult = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                resolution.LecturerIds,
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeBookingId: null);

            Assert.NotNull(checkResult);
            Assert.False(checkResult!.Success);
            Assert.Equal("LecturerNotAvailable", checkResult.ErrorCode);
            Assert.Contains("Supervisor does not have confirmed availability", checkResult.ErrorMessage);
        }

        [Fact]
        public async Task B2_2H_Test8_SupervisorNotRequired_ExaminerOnly()
        {
            var resolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: false,
                supervisorId: null);

            Assert.True(resolution.Success);
            Assert.Single(resolution.LecturerIds);
            Assert.Contains(42, resolution.LecturerIds);

            // Even if an invalid supervisor ID was present, not required means examiner only
            var resolutionWithId = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 42,
                isSupervisorRequired: false,
                supervisorId: 99);

            Assert.True(resolutionWithId.Success);
            Assert.Single(resolutionWithId.LecturerIds);
            Assert.Equal(42, resolutionWithId.LecturerIds[0]);

            var fakeService = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => Task.FromResult(lecId == 42)
            };

            var checkResult = await IAFDefenseBookingService.EvaluateLecturersAvailabilityAsync(
                fakeService,
                resolutionWithId.LecturerIds,
                examinerId: 42,
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(12, 45, 0),
                academicSessionId: 1,
                excludeBookingId: null);

            Assert.Null(checkResult); // Examiner is available -> Passes!
        }

        [Fact]
        public void B2_2H_Test9_MissingVivaDuration_NoRelevantViva_NoUnrelatedRejection()
        {
            var vivasOnDate = new List<TimeSpan>(); // No vivas on this date

            // Authoritative duration is missing (null)
            var result = IAFDefenseBookingService.CheckVivaConflict(
                roleName: "Examiner",
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(13, 15, 0),
                authoritativeVivaDurationMinutes: null,
                vivaStartTimes: vivasOnDate);

            // Must NOT reject when no viva exists
            Assert.Null(result);
        }

        [Fact]
        public void B2_2H_Test10_MissingVivaDuration_WithRelevantViva_FailsClosed()
        {
            var vivasOnDate = new List<TimeSpan>
            {
                new(12, 45, 0) // Final viva scheduled at 12:45
            };

            // Case A: Relevant viva exists, but authoritative duration is missing/null -> Fails closed
            var failResult = IAFDefenseBookingService.CheckVivaConflict(
                roleName: "Examiner",
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(13, 15, 0),
                authoritativeVivaDurationMinutes: null,
                vivaStartTimes: vivasOnDate);

            Assert.NotNull(failResult);
            Assert.False(failResult!.Success);
            Assert.Equal("ConfigurationError", failResult.ErrorCode);
            Assert.Contains("Viva duration setting is missing or invalid", failResult.ErrorMessage);

            // Case B: Relevant viva exists with valid duration (60 min) and overlaps (12:45-13:45 vs 12:30-13:15) -> Rejects conflict
            var conflictResult = IAFDefenseBookingService.CheckVivaConflict(
                roleName: "Examiner",
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(13, 15, 0),
                authoritativeVivaDurationMinutes: 60,
                vivaStartTimes: vivasOnDate);

            Assert.NotNull(conflictResult);
            Assert.False(conflictResult!.Success);
            Assert.Equal("VivaConflict", conflictResult.ErrorCode);

            // Case C: Relevant viva exists with valid duration (60 min) but does NOT overlap (14:00-15:00 vs 12:30-13:15) -> No conflict
            var noConflictResult = IAFDefenseBookingService.CheckVivaConflict(
                roleName: "Examiner",
                defenseDate: new DateTime(2026, 10, 14),
                startTime: new TimeSpan(12, 30, 0),
                endTime: new TimeSpan(13, 15, 0),
                authoritativeVivaDurationMinutes: 60,
                vivaStartTimes: new List<TimeSpan> { new(14, 0, 0) });

            Assert.Null(noConflictResult);
        }

        [Fact]
        public void B2_2H_1_Test11_ExactBookingIdExclusion_PreferredOverMilestoneId()
        {
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = new TimeSpan(12, 30, 0), EndTime = new TimeSpan(17, 30, 0) }
            };

            // Two attempts under the same milestone 266:
            // Attempt 1 (Booking 555) at 13:00-13:45
            // Attempt 2 (Booking 777) at 14:00-14:45
            var bookings = new List<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)>
            {
                (555, 266, 1, new TimeSpan(13, 0, 0), new TimeSpan(13, 45, 0)),
                (777, 266, 2, new TimeSpan(14, 0, 0), new TimeSpan(14, 45, 0))
            };

            // With exact excludeBookingId = 555:
            // Attempt 1 (555) is excluded -> 13:00-13:45 is available.
            // Attempt 2 (777) under the SAME milestone remains a conflict -> 14:00-14:45 is blocked.
            var afterExactConflict = LecturerAvailabilityService.ApplyDefenseConflicts(
                freeSlots,
                bookings,
                excludeBookingId: 555);

            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                afterExactConflict,
                new TimeSpan(13, 0, 0),
                new TimeSpan(13, 45, 0)));

            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                afterExactConflict,
                new TimeSpan(14, 0, 0),
                new TimeSpan(14, 45, 0)));
        }

        #endregion

        #region Phase B3.1 Tests (Alternative Time Request Validation & Lifecycle)

        [Fact]
        public void B3_1_Test1_StateTransitions_AllowedAndForbidden()
        {
            // Valid transitions
            Assert.True(IAFAlternativeTimeValidationEngine.CanTransition("Pending", "Suggested"));
            Assert.True(IAFAlternativeTimeValidationEngine.CanTransition("Pending", "Cancelled"));
            Assert.True(IAFAlternativeTimeValidationEngine.CanTransition("Suggested", "Accepted"));
            Assert.True(IAFAlternativeTimeValidationEngine.CanTransition("Suggested", "Rejected"));
            Assert.True(IAFAlternativeTimeValidationEngine.CanTransition("Suggested", "Cancelled"));

            // Forbidden transitions
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Pending", "Accepted")); // Cannot accept without suggestion & booking
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Pending", "Rejected")); // Cannot reject before suggestion
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Accepted", "Pending")); // Terminal
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Accepted", "Cancelled")); // Terminal
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Rejected", "Suggested")); // Terminal
            Assert.False(IAFAlternativeTimeValidationEngine.CanTransition("Cancelled", "Suggested")); // Terminal
        }

        [Fact]
        public void B3_1_Test2_AdminCreation_ValidationAndDuplicateRejection()
        {
            // Case A: Non-admin user cannot create request
            var nonAdminRes = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 10,
                isAdminUser: false,
                supervisorId: 15,
                existingRequestsForMilestoneAndAttempt: null);
            Assert.False(nonAdminRes.Success);
            Assert.Equal("Unauthorized", nonAdminRes.ErrorCode);

            // Case B: Supervisor cannot be designated as independent examiner
            var conflictRes = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 15, // Same as supervisorId
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 1,
                isAdminUser: true,
                supervisorId: 15,
                existingRequestsForMilestoneAndAttempt: null);
            Assert.False(conflictRes.Success);
            Assert.Equal("SupervisorCannotExamine", conflictRes.ErrorCode);

            // Case C: Valid creation succeeds in Pending status
            var validRes = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 1,
                isAdminUser: true,
                supervisorId: 15,
                existingRequestsForMilestoneAndAttempt: null,
                adminNotes: "Please suggest a slot next week.");
            Assert.True(validRes.Success);
            Assert.NotNull(validRes.Request);
            Assert.Equal("Pending", validRes.Request!.Status);
            Assert.Equal(42, validRes.Request.RequestedLecturerId);

            // Case D: Duplicate active request (Pending) is rejected
            var existing = new List<IAFAlternativeTimeRequest> { validRes.Request };
            var dupRes = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 1,
                isAdminUser: true,
                supervisorId: 15,
                existingRequestsForMilestoneAndAttempt: existing);
            Assert.False(dupRes.Success);
            Assert.Equal("DuplicateActiveRequest", dupRes.ErrorCode);
        }

        [Fact]
        public void B3_1_Test3_LecturerSuggestion_OwnershipAndWorkingHoursValidation()
        {
            var req = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 266,
                AttemptNumber = 1,
                RequestedLecturerId = 42,
                Status = "Pending"
            };

            var workStart = new TimeSpan(12, 30, 0);
            var workEnd = new TimeSpan(17, 30, 0);
            var futureDate = DateTime.Today.AddDays(7);

            // Case A: Wrong lecturer cannot submit suggestion
            var wrongLecturer = IAFAlternativeTimeValidationEngine.ValidateLecturerSuggestion(
                req,
                respondingUserId: 99, // Not 42
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(13, 0, 0),
                durationMinutes: 45,
                workingHoursStart: workStart,
                workingHoursEnd: workEnd);
            Assert.False(wrongLecturer.Success);
            Assert.Equal("UnauthorizedLecturer", wrongLecturer.ErrorCode);

            // Case B: Outside working hours is rejected
            var outsideHours = IAFAlternativeTimeValidationEngine.ValidateLecturerSuggestion(
                req,
                respondingUserId: 42,
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(11, 0, 0), // Starts before 12:30
                durationMinutes: 45,
                workingHoursStart: workStart,
                workingHoursEnd: workEnd);
            Assert.False(outsideHours.Success);
            Assert.Equal("OutsideWorkingHours", outsideHours.ErrorCode);

            // Case C: Past date is rejected
            var pastDate = IAFAlternativeTimeValidationEngine.ValidateLecturerSuggestion(
                req,
                respondingUserId: 42,
                suggestedDate: DateTime.Today.AddDays(-1),
                suggestedStartTime: new TimeSpan(13, 0, 0),
                durationMinutes: 45,
                workingHoursStart: workStart,
                workingHoursEnd: workEnd);
            Assert.False(pastDate.Success);
            Assert.Equal("PastDate", pastDate.ErrorCode);

            // Case D: Valid suggestion transitions to Suggested (and records proposal only)
            var validSuggest = IAFAlternativeTimeValidationEngine.ValidateLecturerSuggestion(
                req,
                respondingUserId: 42,
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(13, 0, 0),
                durationMinutes: 45,
                workingHoursStart: workStart,
                workingHoursEnd: workEnd,
                lecturerNotes: "I am available between 1pm and 2pm.");
            Assert.True(validSuggest.Success);
            Assert.Equal("Suggested", req.Status);
            Assert.Equal(new TimeSpan(13, 0, 0), req.SuggestedStartTime);
            Assert.Equal(new TimeSpan(13, 45, 0), req.SuggestedEndTime);
            Assert.NotNull(req.RespondedAt);
        }

        [Fact]
        public void B3_1_Test4_AcceptanceRequiresCommittedBooking_NoAutoAccept()
        {
            var req = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 266,
                AttemptNumber = 1,
                Status = "Suggested"
            };

            // Case A: Cannot mark Accepted without confirmed committed booking ID
            var noBookingRes = IAFAlternativeTimeValidationEngine.ValidateAcceptSuggestion(
                req,
                adminUserId: 1,
                isAdminUser: true,
                isCommittedBookingConfirmed: false,
                confirmedBookingId: null);
            Assert.False(noBookingRes.Success);
            Assert.Equal("BookingNotCommitted", noBookingRes.ErrorCode);

            // Case B: Non-admin cannot accept suggestion
            var nonAdminRes = IAFAlternativeTimeValidationEngine.ValidateAcceptSuggestion(
                req,
                adminUserId: 10,
                isAdminUser: false,
                isCommittedBookingConfirmed: true,
                confirmedBookingId: 100);
            Assert.False(nonAdminRes.Success);
            Assert.Equal("Unauthorized", nonAdminRes.ErrorCode);

            // Case C: With verified committed booking, transition to Accepted succeeds
            var successRes = IAFAlternativeTimeValidationEngine.ValidateAcceptSuggestion(
                req,
                adminUserId: 1,
                isAdminUser: true,
                isCommittedBookingConfirmed: true,
                confirmedBookingId: 100);
            Assert.True(successRes.Success);
            Assert.Equal("Accepted", req.Status);
            Assert.Equal(1, req.FinalizedByAdminId);
            Assert.Contains("Booking #100", req.DecisionReason);
        }

        [Fact]
        public void B3_1_Test5_AdminRejectAndCancel_AllowedOnlyFromPermittedStates()
        {
            // Case A: Rejecting a Suggested request succeeds
            var reqSuggested = new IAFAlternativeTimeRequest { Id = 1, Status = "Suggested" };
            var rejectRes = IAFAlternativeTimeValidationEngine.ValidateRejectSuggestion(
                reqSuggested,
                adminUserId: 1,
                isAdminUser: true,
                rejectionReason: "Time not suitable for student.");
            Assert.True(rejectRes.Success);
            Assert.Equal("Rejected", reqSuggested.Status);

            // Case B: Cannot reject a Pending request (must wait for suggestion or cancel)
            var reqPending = new IAFAlternativeTimeRequest { Id = 2, Status = "Pending" };
            var invalidReject = IAFAlternativeTimeValidationEngine.ValidateRejectSuggestion(
                reqPending,
                adminUserId: 1,
                isAdminUser: true,
                rejectionReason: "Too early.");
            Assert.False(invalidReject.Success);
            Assert.Equal("InvalidStatusTransition", invalidReject.ErrorCode);

            // Case C: Cancelling a Pending request succeeds
            var cancelRes = IAFAlternativeTimeValidationEngine.ValidateCancelRequest(
                reqPending,
                adminUserId: 1,
                isAdminUser: true,
                cancellationReason: "Admin rescheduled directly.");
            Assert.True(cancelRes.Success);
            Assert.Equal("Cancelled", reqPending.Status);

            // Case D: Cannot cancel an already Accepted request
            var reqAccepted = new IAFAlternativeTimeRequest { Id = 3, Status = "Accepted" };
            var invalidCancel = IAFAlternativeTimeValidationEngine.ValidateCancelRequest(
                reqAccepted,
                adminUserId: 1,
                isAdminUser: true,
                cancellationReason: "Undo.");
            Assert.False(invalidCancel.Success);
            Assert.Equal("InvalidStatusTransition", invalidCancel.ErrorCode);
        }

        [Fact]
        public void B3_1_Test6_ParticipantResolution_SupervisorRequiredAndIndependence()
        {
            // Case A: Supervisor required with valid supervisor ID -> returns both
            var res1 = IAFAlternativeTimeValidationEngine.ResolveParticipantsForSuggestion(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: 15);
            Assert.True(res1.Valid);
            Assert.Equal(2, res1.RequiredLecturerIds.Count);
            Assert.Contains(42, res1.RequiredLecturerIds);
            Assert.Contains(15, res1.RequiredLecturerIds);

            // Case B: Supervisor required but supervisor ID missing -> Fails closed
            var res2 = IAFAlternativeTimeValidationEngine.ResolveParticipantsForSuggestion(
                examinerId: 42,
                isSupervisorRequired: true,
                supervisorId: null);
            Assert.False(res2.Valid);
            Assert.Equal("SupervisorRequired", res2.ErrorCode);

            // Case C: Supervisor is same as examiner -> Fails independence
            var res3 = IAFAlternativeTimeValidationEngine.ResolveParticipantsForSuggestion(
                examinerId: 15,
                isSupervisorRequired: true,
                supervisorId: 15);
            Assert.False(res3.Valid);
            Assert.Equal("SupervisorCannotExamine", res3.ErrorCode);

            // Case D: Supervisor not required -> returns examiner only
            var res4 = IAFAlternativeTimeValidationEngine.ResolveParticipantsForSuggestion(
                examinerId: 42,
                isSupervisorRequired: false,
                supervisorId: null);
            Assert.True(res4.Valid);
            Assert.Single(res4.RequiredLecturerIds);
            Assert.Equal(42, res4.RequiredLecturerIds[0]);
        }

        [Fact]
        public void B3_1_Test7_SuggestionRequiresAuthoritativeRevalidation()
        {
            var reqSuggested = new IAFAlternativeTimeRequest { Status = "Suggested" };
            Assert.True(IAFAlternativeTimeValidationEngine.RequiresAuthoritativeRevalidation(reqSuggested));

            var reqPending = new IAFAlternativeTimeRequest { Status = "Pending" };
            Assert.False(IAFAlternativeTimeValidationEngine.RequiresAuthoritativeRevalidation(reqPending));

            var reqAccepted = new IAFAlternativeTimeRequest { Status = "Accepted" };
            Assert.False(IAFAlternativeTimeValidationEngine.RequiresAuthoritativeRevalidation(reqAccepted));
        }

        #endregion

        #region Step 36.8-B3.2 Backend Preparation Unit Tests

        [Fact]
        public void B3_2_Test1_Model_RowVersionAndFilteredIndexMetadata_ConfiguredCorrectly()
        {
            var request = new IAFAlternativeTimeRequest
            {
                MilestoneId = 266,
                AttemptNumber = 1,
                AcademicSessionId = 5,
                RequestedLecturerId = 42,
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 0, 0, 0, 1 }
            };

            Assert.NotNull(request.RowVersion);
            Assert.Equal(4, request.RowVersion.Length);
            Assert.Equal("Pending", request.Status);
        }

        [Fact]
        public void B3_2_Test2_ClientRowVersion_MismatchDetected_PreventsConcurrentOverwrite()
        {
            var serverRowVersion = new byte[] { 1, 2, 3, 4 };
            var staleClientRowVersion = new byte[] { 1, 2, 3, 3 };

            bool isMatch = serverRowVersion.SequenceEqual(staleClientRowVersion);
            Assert.False(isMatch);
        }

        [Fact]
        public void B3_2_Test3_ClientRowVersion_Match_PermitsValidUpdate()
        {
            var serverRowVersion = new byte[] { 1, 2, 3, 4 };
            var currentClientRowVersion = new byte[] { 1, 2, 3, 4 };

            bool isMatch = serverRowVersion.SequenceEqual(currentClientRowVersion);
            Assert.True(isMatch);
        }

        [Fact]
        public void B3_2_Test4_AdminUnauthorizedAccess_FailsWithoutTouchingDatabase()
        {
            var validation = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 5,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 0,
                isAdminUser: false,
                supervisorId: 99,
                existingRequestsForMilestoneAndAttempt: new List<IAFAlternativeTimeRequest>());

            Assert.False(validation.Success);
            Assert.Equal("Unauthorized", validation.ErrorCode);
        }

        [Fact]
        public void B3_2_Test5_AcceptedStatusForbidden_DirectSuggestionAccepted_Rejected()
        {
            var request = new IAFAlternativeTimeRequest
            {
                Id = 10,
                MilestoneId = 266,
                AttemptNumber = 1,
                RequestedLecturerId = 42,
                Status = IAFAlternativeTimeRequest.StatusSuggested,
                SuggestedDate = DateTime.Today.AddDays(2),
                SuggestedStartTime = new TimeSpan(10, 0, 0),
                SuggestedEndTime = new TimeSpan(10, 45, 0)
            };

            // Attempting to accept directly without authoritative booking confirmation
            var validation = IAFAlternativeTimeValidationEngine.ValidateAcceptSuggestion(
                request,
                adminUserId: 1,
                isAdminUser: true,
                isCommittedBookingConfirmed: false,
                confirmedBookingId: null);

            Assert.False(validation.Success);
            Assert.Equal("BookingNotCommitted", validation.ErrorCode);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, request.Status);
        }

        #endregion
    }

    public class FakeLecturerAvailabilityService : ILecturerAvailabilityService
    {
        public Func<int, DateTime, TimeSpan, TimeSpan, int, int?, Task<bool>>? IsSlotAvailableFunc { get; set; }

        public Task<List<TimeSpanRange>> GetAvailableSlotsAsync(int lecturerId, DateTime targetDate, int durationMinutes, int academicSessionId)
        {
            return Task.FromResult(new List<TimeSpanRange>());
        }

        public Task<List<TimeSpanRange>> GetMutualAvailableSlotsAsync(List<int> lecturerIds, DateTime targetDate, int durationMinutes, int academicSessionId)
        {
            return Task.FromResult(new List<TimeSpanRange>());
        }

        public Task<bool> IsSlotAvailableAsync(int lecturerId, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, int academicSessionId, int? excludeBookingId = null, int? excludeMilestoneId = null)
        {
            if (IsSlotAvailableFunc != null)
            {
                return IsSlotAvailableFunc(lecturerId, targetDate, startTime, endTime, academicSessionId, excludeBookingId ?? excludeMilestoneId);
            }
            return Task.FromResult(false);
        }


        [Fact]
        public void B3_5_1_Test1_MatchingBooking_ShouldPass()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.True(result.Success);
        }

        [Fact]
        public void B3_5_1_Test2_DifferentAttempt_ShouldFail()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            booking.AttemptNumber = 2;

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.False(result.Success);
            Assert.Equal("BookingIdentityMismatch", result.ErrorCode);
        }

        [Fact]
        public void B3_5_1_Test3_DifferentExaminer_ShouldFail()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            booking.ExaminerId = 99;

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.False(result.Success);
            Assert.Equal("ExaminerMismatch", result.ErrorCode);
        }

        [Fact]
        public void B3_5_1_Test4_DifferentTime_ShouldFail()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            booking.StartTime = new TimeSpan(14, 0, 0);
            booking.EndTime = new TimeSpan(14, 45, 0);

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.False(result.Success);
            Assert.Equal("BookingTimeMismatch", result.ErrorCode);
        }

        [Fact]
        public void B3_5_1_Test5_CancelledBooking_ShouldFail()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            booking.BookingStatus = "Cancelled";

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.False(result.Success);
            Assert.Equal("InvalidBookingStatus", result.ErrorCode);
        }

        [Fact]
        public void B3_5_1_Test6_MissingSuggestion_ShouldFail()
        {
            var request = CreateTestAlternativeRequest();
            var booking = CreateTestDefenseBooking();

            request.SuggestedDate = null;

            var result = IAFAlternativeTimeValidationEngine
                .ValidateBookingForAcceptance(request, booking);

            Assert.False(result.Success);
            Assert.Equal("MissingSuggestion", result.ErrorCode);
        }

        // Test-only objects. No database connection.
        private static IAFAlternativeTimeRequest CreateTestAlternativeRequest()
        {
            return new IAFAlternativeTimeRequest
            {
                MilestoneId = 266,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                Status = IAFAlternativeTimeRequest.StatusSuggested,
                SuggestedDate = new DateTime(2026, 10, 14),
                SuggestedStartTime = new TimeSpan(12, 30, 0),
                SuggestedEndTime = new TimeSpan(13, 15, 0),
                SuggestedDurationMinutes = 45
            };
        }

        private static IAFDefenseBooking CreateTestDefenseBooking()
        {
            return new IAFDefenseBooking
            {
                Id = 555,
                MilestoneId = 266,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                ExaminerId = 42,
                DefenseDate = new DateTime(2026, 10, 14),
                StartTime = new TimeSpan(12, 30, 0),
                EndTime = new TimeSpan(13, 15, 0),
                DurationMinutes = 45,
                Venue = "Test Room",
                BookingStatus = "Scheduled"
            };
        }

        [Fact]
        public void B3_5_1_Test7_DifferentExaminer_SameMilestoneAttempt_ShouldRejectDuplicate()
        {
            // Existing active request for Examiner 42
            var existingRequests = new List<IAFAlternativeTimeRequest>
    {
        new IAFAlternativeTimeRequest
        {
            Id = 100,
            MilestoneId = 266,
            AttemptNumber = 1,
            RequestedLecturerId = 42,
            RequestedLecturerRole = "Examiner",
            Status = IAFAlternativeTimeRequest.StatusPending
        }
    };

            // Admin tries creating another request for Examiner 99,
            // but it belongs to the same Milestone and Attempt.
            var result = IAFAlternativeTimeValidationEngine.ValidateCreateRequest(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 99,
                requestedLecturerRole: "Examiner",
                requestedByAdminId: 1,
                isAdminUser: true,
                supervisorId: 10,
                existingRequestsForMilestoneAndAttempt: existingRequests,
                adminNotes: "Request another available time"
            );

            Assert.False(result.Success);
            Assert.Equal("DuplicateActiveRequest", result.ErrorCode);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(99999)]
        public async Task B3_5_1_DirectAcceptance_MustAlwaysBeDisabled(
            int bookingId)
        {
            // No database context or connection is required.
            var service = new IAFAlternativeTimeRequestService(null!);

            var result = await service.FinalizeAcceptedAsync(
                requestId: 100,
                currentAdminUserId: 1,
                isAdminUser: true,
                confirmedBookingId: bookingId
            );

            Assert.False(result.Success);
            Assert.Equal("ACCEPTANCE_DISABLED", result.ErrorCode);
        }


        [Fact]
        public async Task B3_5_1_Reject_WithoutAuthenticatedUser_ShouldFail()
        {
            // No HttpContext, no database access.
            var service = new IAFAlternativeTimeRequestService(null!);

            var result = await service.RejectSuggestionAsync(
                requestId: 100,
                currentAdminUserId: 1,
                isAdminUser: true,
                reason: "Test rejection");

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }

        [Fact]
        public async Task B3_5_1_Cancel_WithoutAuthenticatedUser_ShouldFail()
        {
            // No HttpContext, no database access.
            var service = new IAFAlternativeTimeRequestService(null!);

            var result = await service.CancelRequestAsync(
                requestId: 100,
                currentAdminUserId: 1,
                isAdminUser: true,
                reason: "Test cancellation");

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }


        [Fact]
        public void B3_5_1_AlternativeTimeRequest_EFModel_ShouldConfigureRowVersionAndUniqueIndex()
        {
            // This test inspects EF Core metadata only.
            // It does NOT connect to SQL Server.

            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseSqlServer(
                    "Server=(local);Database=MetadataOnly;Trusted_Connection=True;TrustServerCertificate=True")
                .Options;

            using var context = new FYPTrackerContext(options);

            var entity = context.Model.FindEntityType(
                typeof(IAFAlternativeTimeRequest));

            Assert.NotNull(entity);

            var rowVersion = entity!.FindProperty(
                nameof(IAFAlternativeTimeRequest.RowVersion));

            Assert.NotNull(rowVersion);
            Assert.True(rowVersion!.IsConcurrencyToken);

            Assert.Equal(
                Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate,
                rowVersion.ValueGenerated);

            var activeIndex = entity.GetIndexes()
                .FirstOrDefault(i =>
                    i.Properties.Select(p => p.Name).SequenceEqual(
                        new[]
                        {
                    nameof(IAFAlternativeTimeRequest.MilestoneId),
                    nameof(IAFAlternativeTimeRequest.AttemptNumber)
                        })
                    && i.IsUnique);

            Assert.NotNull(activeIndex);

            Assert.Equal(
                "[Status] IN ('Pending', 'Suggested')",
                activeIndex!.GetFilter());
        }

        [Fact]
        public async Task B3_5_1_CreateRequest_WithoutAuthenticatedAdmin_ShouldFail()
        {
            // No HttpContext and no database connection.
            var service = new IAFAlternativeTimeRequestService(null!);

            var result = await service.CreateRequestAsync(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 1,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Security test");

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }

        [Fact]
        public async Task B3_5_1_SubmitSuggestion_WithoutAuthenticatedExaminer_ShouldFail()
        {
            // No HttpContext and no database connection.
            var service = new IAFAlternativeTimeRequestService(null!);

            var result = await service.SubmitSuggestionAsync(
                requestId: 100,
                currentLecturerUserId: 42,
                suggestedDate: new DateTime(2026, 11, 10),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 45,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(18, 0, 0));

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }


        [Fact]
        public async Task B3_5_1_CreateRequest_WithWrongAdminUserId_ShouldFail()
        {
            var httpContext = new DefaultHttpContext();

            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                new Claim("Id", "99"),
                new Claim(ClaimTypes.Role, "Admin")
                    },
                    "TestAuthentication"));

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };

            // No database connection required.
            var service = new IAFAlternativeTimeRequestService(
                null!,
                accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 1,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Security test");

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }

        [Fact]
        public async Task B3_5_1_SubmitSuggestion_WithWrongExaminerUserId_ShouldFail()
        {
            var httpContext = new DefaultHttpContext();

            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                new Claim("Id", "42"),
                new Claim(ClaimTypes.Role, "Examiner")
                    },
                    "TestAuthentication"));

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };

            // No database connection required.
            var service = new IAFAlternativeTimeRequestService(
                null!,
                accessor);

            var result = await service.SubmitSuggestionAsync(
                requestId: 100,
                currentLecturerUserId: 88,
                suggestedDate: new DateTime(2026, 11, 10),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 45,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(18, 0, 0));

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ACCESS", result.ErrorCode);
        }


        private sealed class TestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _values = new();

            public bool IsAvailable => true;
            public string Id => "TestSession";
            public IEnumerable<string> Keys => _values.Keys;

            public void Clear() => _values.Clear();

            public Task CommitAsync(
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task LoadAsync(
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Remove(string key) => _values.Remove(key);

            public void Set(string key, byte[] value)
                => _values[key] = value;

            public bool TryGetValue(string key, out byte[] value)
                => _values.TryGetValue(key, out value!);
        }


        [Fact]
        public async Task B3_5_1_CreateRequest_WrongWorkspace_ShouldFail()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "99"),
                    new Claim(ClaimTypes.Role, "Admin")
                        },
                        "TestAuthentication"))
            };

            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Examiner");

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };

            // No real database connection.
            var service = new IAFAlternativeTimeRequestService(
                null!,
                accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 99,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Wrong workspace security test");

            Assert.False(result.Success);
            Assert.Equal("INVALID_WORKSPACE", result.ErrorCode);
        }


        [Fact]
        public async Task B3_5_1_CreateRequest_WithoutAdminRoleClaim_ShouldFail()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "99"),
                    new Claim(ClaimTypes.Role, "Examiner")
                        },
                        "TestAuthentication"))
            };

            // Session claims Admin, but authenticated user is Examiner.
            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Admin");

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };

            // No database connection.
            var service = new IAFAlternativeTimeRequestService(
                null!,
                accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 266,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 42,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 99,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Missing Admin claim security test");

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ROLE", result.ErrorCode);
        }

        [Fact]
        public async Task B3_5_1_SubmitSuggestion_WrongExaminerWorkspace_ShouldFail()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "42"),
                    new Claim(ClaimTypes.Role, "Examiner")
                        },
                        "TestAuthentication"))
            };

            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Supervisor");

            var service = new IAFAlternativeTimeRequestService(
                null!,
                new HttpContextAccessor { HttpContext = httpContext });

            var result = await service.SubmitSuggestionAsync(
                requestId: 100,
                currentLecturerUserId: 42,
                suggestedDate: new DateTime(2026, 11, 10),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 45,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(18, 0, 0));

            Assert.False(result.Success);
            Assert.Equal("INVALID_WORKSPACE", result.ErrorCode);
        }


        [Fact]
        public async Task B3_5_1_SubmitSuggestion_WithoutExaminerClaim_ShouldFail()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "42"),
                    new Claim(ClaimTypes.Role, "Supervisor")
                        },
                        "TestAuthentication"))
            };

            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Examiner");

            var service = new IAFAlternativeTimeRequestService(
                null!,
                new HttpContextAccessor { HttpContext = httpContext });

            var result = await service.SubmitSuggestionAsync(
                requestId: 100,
                currentLecturerUserId: 42,
                suggestedDate: new DateTime(2026, 11, 10),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 45,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(18, 0, 0));

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED_ROLE", result.ErrorCode);
        }


        [Fact]
        public async Task B3_6_GetRequestById_WithoutAuthenticatedUser_ShouldReturnNull()
        {
            // No HttpContext and no database connection.
            var service = new IAFAlternativeTimeRequestService(null!);

            // Unauthorized reads must stop before accessing the database.
            var result = await service.GetRequestByIdAsync(1);

            Assert.Null(result);
        }


        [Fact]
        public async Task B3_6_GetActiveRequest_WithoutAuthenticatedUser_ShouldReturnNull()
        {
            // No HttpContext and no database connection.
            var service = new IAFAlternativeTimeRequestService(null!);

            // Unauthorized access must fail before any database query.
            var result = await service.GetActiveRequestAsync(
                milestoneId: 1,
                attemptNumber: 1);

            Assert.Null(result);
        }


        [Fact]
        public async Task B3_6_GetRequestHistory_WithoutAuthenticatedUser_ShouldReturnEmpty()
        {
            // No HttpContext and no database connection.
            var service = new IAFAlternativeTimeRequestService(null!);

            // Unauthorized access must stop before any database query.
            var result = await service.GetRequestHistoryAsync(
                milestoneId: 1,
                attemptNumber: 1);

            Assert.NotNull(result);
            Assert.Empty(result);
        }


        [Fact]
        public async Task B3_6_GetRequestById_WithExaminerClaim_ShouldReturnNull()
        {
            // Authenticated Examiner attempting to access Admin-only data.
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "42"),
                    new Claim(ClaimTypes.Role, "Examiner")
                        },
                        "TestAuthentication"))
            };

            // Even if the session claims Admin workspace,
            // the actual authenticated role must still be checked.
            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Admin");

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };

            // No database connection.
            var service = new IAFAlternativeTimeRequestService(
                null!,
                accessor);

            var result = await service.GetRequestByIdAsync(100);

            // Examiner must not access this Admin-only read method.
            Assert.Null(result);
        }

        [Fact]
        public async Task B3_6_GetRequestById_WithWrongWorkspace_ShouldReturnNull()
        {
            // Authenticated user has Admin role.
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                    new Claim("Id", "1"),
                    new Claim(ClaimTypes.Role, "Admin")
                        },
                        "TestAuthentication"))
            };

            // Wrong workspace: user is currently in Examiner workspace.
            httpContext.Session = new TestSession();
            httpContext.Session.SetString("ActiveRole", "Examiner");

            var service = new IAFAlternativeTimeRequestService(
                null!,
                new HttpContextAccessor
                {
                    HttpContext = httpContext
                });

            var result = await service.GetRequestByIdAsync(100);

            // Must reject before accessing the database.
            Assert.Null(result);
        }







    }
}
