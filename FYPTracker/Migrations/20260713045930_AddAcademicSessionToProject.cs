using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddAcademicSessionToProject : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcademicSession",
                table: "FYPProject",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill existing FYPProject records with the Student's AcademicSession
            migrationBuilder.Sql("UPDATE FYPProject SET AcademicSession = (SELECT AcademicSession FROM ApplicationUser WHERE ApplicationUser.Id = FYPProject.StudentId)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcademicSession",
                table: "FYPProject");
        }
    }
}
