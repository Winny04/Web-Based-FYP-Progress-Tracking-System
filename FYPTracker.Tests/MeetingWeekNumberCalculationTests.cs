using System;
using System.Linq;
using System.Threading.Tasks;
using FYPTracker.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FYPTracker.Tests
{
    public class MeetingWeekNumberCalculationTests
    {
        private FYPTrackerContext CreateInMemoryContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new FYPTrackerContext(options);
        }

        [Fact]
        public async Task MaxExistingWeekInStage_WhenNoMeetingsExist_ReturnsZeroAndCalculatesWeekOne()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryContext(dbName);

            int studentId = 1001;
            int currentStage = 1;

            var maxExistingWeekInStage = (await context.MeetingBookings
                .Where(m => m.StudentId == studentId && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled")
                .MaxAsync(m => (int?)m.WeekNumber)) ?? 0;

            int calculatedWeekNumber = 1;
            if (maxExistingWeekInStage > 0)
            {
                calculatedWeekNumber = Math.Min(14, maxExistingWeekInStage + 1);
            }

            Assert.Equal(0, maxExistingWeekInStage);
            Assert.Equal(1, calculatedWeekNumber);
        }

        [Fact]
        public async Task MaxExistingWeekInStage_WhenExistingMeetingsPresent_CalculatesNextWeekNumber()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryContext(dbName);

            int studentId = 1002;
            int currentStage = 1;

            context.MeetingBookings.AddRange(
                new MeetingBooking
                {
                    StudentId = studentId,
                    Stage = 1,
                    WeekNumber = 1,
                    Status = "Approved",
                    MeetingDate = new DateTime(2026, 1, 14),
                    MeetingDay = "Wednesday",
                    MeetingTime = "10:00"
                },
                new MeetingBooking
                {
                    StudentId = studentId,
                    Stage = 1,
                    WeekNumber = 2,
                    Status = "Approved",
                    MeetingDate = new DateTime(2026, 1, 21),
                    MeetingDay = "Wednesday",
                    MeetingTime = "10:00"
                },
                // Rejected meeting with higher week number should be ignored
                new MeetingBooking
                {
                    StudentId = studentId,
                    Stage = 1,
                    WeekNumber = 5,
                    Status = "Rejected",
                    MeetingDate = new DateTime(2026, 2, 11),
                    MeetingDay = "Wednesday",
                    MeetingTime = "10:00"
                },
                // Meeting for another student should be ignored
                new MeetingBooking
                {
                    StudentId = 9999,
                    Stage = 1,
                    WeekNumber = 8,
                    Status = "Approved",
                    MeetingDate = new DateTime(2026, 3, 4),
                    MeetingDay = "Wednesday",
                    MeetingTime = "10:00"
                },
                // Meeting for Stage 2 should be ignored
                new MeetingBooking
                {
                    StudentId = studentId,
                    Stage = 2,
                    WeekNumber = 4,
                    Status = "Approved",
                    MeetingDate = new DateTime(2026, 6, 10),
                    MeetingDay = "Wednesday",
                    MeetingTime = "10:00"
                }
            );
            await context.SaveChangesAsync();

            var maxExistingWeekInStage = (await context.MeetingBookings
                .Where(m => m.StudentId == studentId && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled")
                .MaxAsync(m => (int?)m.WeekNumber)) ?? 0;

            int calculatedWeekNumber = 1;
            if (maxExistingWeekInStage > 0)
            {
                calculatedWeekNumber = Math.Min(14, maxExistingWeekInStage + 1);
            }

            Assert.Equal(2, maxExistingWeekInStage);
            Assert.Equal(3, calculatedWeekNumber);
        }

        [Fact]
        public async Task MaxExistingWeekInStage_CapsAtWeek14()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateInMemoryContext(dbName);

            int studentId = 1003;
            int currentStage = 1;

            context.MeetingBookings.Add(new MeetingBooking
            {
                StudentId = studentId,
                Stage = 1,
                WeekNumber = 14,
                Status = "Approved",
                MeetingDate = new DateTime(2026, 4, 15),
                MeetingDay = "Wednesday",
                MeetingTime = "10:00"
            });
            await context.SaveChangesAsync();

            var maxExistingWeekInStage = (await context.MeetingBookings
                .Where(m => m.StudentId == studentId && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled")
                .MaxAsync(m => (int?)m.WeekNumber)) ?? 0;

            int calculatedWeekNumber = 1;
            if (maxExistingWeekInStage > 0)
            {
                calculatedWeekNumber = Math.Min(14, maxExistingWeekInStage + 1);
            }

            Assert.Equal(14, maxExistingWeekInStage);
            Assert.Equal(14, calculatedWeekNumber);
        }
    }
}
