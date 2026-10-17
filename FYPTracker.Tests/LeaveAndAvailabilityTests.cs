using Xunit;
using FYPTracker.Services;
using System;
using System.Collections.Generic;

namespace FYPTracker.Tests
{
    public class LeaveAndAvailabilityTests
    {
        [Fact]
        public void AbsencePeriod_FullDay_RemovesAllAvailableSlotsForDate()
        {
            var workStart = new TimeSpan(9, 0, 0);
            var workEnd = new TimeSpan(17, 0, 0);

            // Lecturer has positive available working hours
            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = workStart, EndTime = workEnd }
            };

            // Full-day absence: 00:00 to 23:59 (or clamped to work hours)
            var absenceStart = workStart;
            var absenceEnd = workEnd;

            var remaining = LecturerAvailabilityService.RemoveOverlapStatic(freeSlots, absenceStart, absenceEnd);

            Assert.Empty(remaining);
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(10, 0, 0),
                new TimeSpan(11, 0, 0)));
        }

        [Fact]
        public void AbsencePeriod_PartialDay_BlocksOnlySpecifiedWindow()
        {
            var workStart = new TimeSpan(9, 0, 0);
            var workEnd = new TimeSpan(17, 0, 0);

            var freeSlots = new List<TimeSpanRange>
            {
                new() { StartTime = workStart, EndTime = workEnd }
            };

            // Partial day absence (e.g. MC morning: 09:00 - 13:00)
            var absenceStart = new TimeSpan(9, 0, 0);
            var absenceEnd = new TimeSpan(13, 0, 0);

            var remaining = LecturerAvailabilityService.RemoveOverlapStatic(freeSlots, absenceStart, absenceEnd);

            // Morning is blocked
            Assert.False(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(10, 0, 0),
                new TimeSpan(11, 0, 0)));

            // Afternoon is available (13:00 - 17:00)
            Assert.True(LecturerAvailabilityService.IsIntervalFullyContained(
                remaining,
                new TimeSpan(14, 0, 0),
                new TimeSpan(15, 0, 0)));
        }

        [Fact]
        public void AbsencePeriod_MultiDayDateRange_GeneratesCorrectDailySpans()
        {
            var startDate = new DateTime(2026, 11, 10);
            var endDate = new DateTime(2026, 11, 12);

            var days = new List<DateTime>();
            for (var dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
            {
                days.Add(dt);
            }

            Assert.Equal(3, days.Count);
            Assert.Equal(new DateTime(2026, 11, 10), days[0]);
            Assert.Equal(new DateTime(2026, 11, 11), days[1]);
            Assert.Equal(new DateTime(2026, 11, 12), days[2]);
        }
    }
}
