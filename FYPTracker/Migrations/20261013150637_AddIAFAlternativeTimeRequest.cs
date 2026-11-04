using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddIAFAlternativeTimeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IAFAlternativeTimeRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MilestoneId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    AcademicSessionId = table.Column<int>(type: "int", nullable: false),
                    RequestedLecturerId = table.Column<int>(type: "int", nullable: false),
                    RequestedLecturerRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestedByAdminId = table.Column<int>(type: "int", nullable: false),
                    AdminNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SuggestedDate = table.Column<DateTime>(type: "date", nullable: true),
                    SuggestedStartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    SuggestedEndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    SuggestedDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    LecturerNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    FinalizedByAdminId = table.Column<int>(type: "int", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IAFAlternativeTimeRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IAFAlternativeTimeRequest_AcademicSession_AcademicSessionId",
                        column: x => x.AcademicSessionId,
                        principalTable: "AcademicSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFAlternativeTimeRequest_ApplicationUser_FinalizedByAdminId",
                        column: x => x.FinalizedByAdminId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFAlternativeTimeRequest_ApplicationUser_RequestedByAdminId",
                        column: x => x.RequestedByAdminId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFAlternativeTimeRequest_ApplicationUser_RequestedLecturerId",
                        column: x => x.RequestedLecturerId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IAFAlternativeTimeRequest_FYPMilestone_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "FYPMilestone",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IAFAlternativeTimeRequest_AcademicSessionId",
                table: "IAFAlternativeTimeRequest",
                column: "AcademicSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFAlternativeTimeRequest_FinalizedByAdminId",
                table: "IAFAlternativeTimeRequest",
                column: "FinalizedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_IAFAlternativeTimeRequest_LecturerStatus",
                table: "IAFAlternativeTimeRequest",
                columns: new[] { "RequestedLecturerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IAFAlternativeTimeRequest_MilestoneAttemptStatus",
                table: "IAFAlternativeTimeRequest",
                columns: new[] { "MilestoneId", "AttemptNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IAFAlternativeTimeRequest_RequestedByAdminId",
                table: "IAFAlternativeTimeRequest",
                column: "RequestedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_IAFAlternativeTimeRequest_ActiveMilestoneAttempt",
                table: "IAFAlternativeTimeRequest",
                columns: new[] { "MilestoneId", "AttemptNumber" },
                unique: true,
                filter: "[Status] IN ('Pending', 'Suggested')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IAFAlternativeTimeRequest");
        }
    }
}
