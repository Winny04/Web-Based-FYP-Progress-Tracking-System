using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class RenameEmployeeInfoToApplicationUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditTag");

            migrationBuilder.DropTable(
                name: "EmailTemplateConfig");

            migrationBuilder.DropTable(
                name: "FYPActivity");

            migrationBuilder.DropTable(
                name: "FYPFeedback");

            migrationBuilder.DropTable(
                name: "FYPIAForm");

            migrationBuilder.DropTable(
                name: "FYPNotification");

            migrationBuilder.DropTable(
                name: "FYPSubmissionFile");

            migrationBuilder.DropTable(
                name: "MeetingBooking");

            migrationBuilder.DropTable(
                name: "SupervisorRequest");

            migrationBuilder.DropTable(
                name: "AuditTrail");

            migrationBuilder.DropTable(
                name: "FYPMilestone");

            migrationBuilder.DropTable(
                name: "FYPProject");

            migrationBuilder.DropTable(
                name: "ApplicationUser");

            migrationBuilder.DropTable(
                name: "Department");
        }
    }
}
