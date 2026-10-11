using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddIAFDefenseBookingAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IAFDefenseBooking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MilestoneId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    AcademicSessionId = table.Column<int>(type: "int", nullable: false),
                    ExaminerId = table.Column<int>(type: "int", nullable: false),
                    OriginalSupervisorId = table.Column<int>(type: "int", nullable: true),
                    IsSupervisorRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefenseDate = table.Column<DateTime>(type: "date", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    BookingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AcademicOutcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutcomeComments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IAFDefenseBooking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_AcademicSession_AcademicSessionId",
                        column: x => x.AcademicSessionId,
                        principalTable: "AcademicSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_ApplicationUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_ApplicationUser_ExaminerId",
                        column: x => x.ExaminerId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_ApplicationUser_OriginalSupervisorId",
                        column: x => x.OriginalSupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_ApplicationUser_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseBooking_FYPMilestone_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "FYPMilestone",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IAFDefenseHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    MilestoneId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    AcademicSessionId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExaminerId = table.Column<int>(type: "int", nullable: false),
                    OriginalSupervisorId = table.Column<int>(type: "int", nullable: true),
                    IsSupervisorRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefenseDate = table.Column<DateTime>(type: "date", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    BookingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AcademicOutcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutcomeComments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReasonForChange = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime", nullable: false),
                    ActionByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IAFDefenseHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_AcademicSession_AcademicSessionId",
                        column: x => x.AcademicSessionId,
                        principalTable: "AcademicSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_ApplicationUser_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_ApplicationUser_ExaminerId",
                        column: x => x.ExaminerId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_ApplicationUser_OriginalSupervisorId",
                        column: x => x.OriginalSupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_FYPMilestone_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "FYPMilestone",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFDefenseHistory_IAFDefenseBooking_BookingId",
                        column: x => x.BookingId,
                        principalTable: "IAFDefenseBooking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_AcademicSessionId",
                table: "IAFDefenseBooking",
                column: "AcademicSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_CreatedByUserId",
                table: "IAFDefenseBooking",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_ExaminerSchedule",
                table: "IAFDefenseBooking",
                columns: new[] { "ExaminerId", "DefenseDate", "StartTime", "EndTime", "BookingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_OriginalSupervisorId",
                table: "IAFDefenseBooking",
                column: "OriginalSupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_UpdatedByUserId",
                table: "IAFDefenseBooking",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseBooking_VenueSchedule",
                table: "IAFDefenseBooking",
                columns: new[] { "Venue", "DefenseDate", "StartTime", "EndTime", "BookingStatus" });

            migrationBuilder.CreateIndex(
                name: "UQ_IAFDefenseBooking_Milestone_Attempt",
                table: "IAFDefenseBooking",
                columns: new[] { "MilestoneId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_AcademicSessionId",
                table: "IAFDefenseHistory",
                column: "AcademicSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_ActionByUserId",
                table: "IAFDefenseHistory",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_BookingId",
                table: "IAFDefenseHistory",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_ExaminerId",
                table: "IAFDefenseHistory",
                column: "ExaminerId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_Milestone_Attempt",
                table: "IAFDefenseHistory",
                columns: new[] { "MilestoneId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_OriginalSupervisorId",
                table: "IAFDefenseHistory",
                column: "OriginalSupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFDefenseHistory_Timestamp",
                table: "IAFDefenseHistory",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IAFDefenseHistory");

            migrationBuilder.DropTable(
                name: "IAFDefenseBooking");
        }
    }
}
