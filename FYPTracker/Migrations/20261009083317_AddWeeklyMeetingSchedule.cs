using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyMeetingSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyMeetingSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: false),
                    FYPProjectId = table.Column<int>(type: "int", nullable: true),
                    AcademicSessionId = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    FirstMeetingBookingId = table.Column<int>(type: "int", nullable: false),
                    FirstMeetingDate = table.Column<DateTime>(type: "date", nullable: false),
                    LockedDayOfWeek = table.Column<int>(type: "int", nullable: false),
                    LockedTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    NextMeetingDate = table.Column<DateTime>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastConflictDate = table.Column<DateTime>(type: "date", nullable: true),
                    LastConflictReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyMeetingSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyMeetingSchedules_StudentId_AcademicSessionId_Stage",
                table: "WeeklyMeetingSchedules",
                columns: new[] { "StudentId", "AcademicSessionId", "Stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyMeetingSchedules");
        }
    }
}
