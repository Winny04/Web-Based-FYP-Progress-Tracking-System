using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddIsRequiredToMilestone : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "FYPMilestone",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // Backfill legacy optional / supervisor milestones to false
            migrationBuilder.Sql("UPDATE FYPMilestone SET IsRequired = 0 WHERE MilestoneSource = 'Supervisor' OR Title LIKE '%Ethic Form%' OR Title LIKE '%Turnitin Checking - DPP%';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "FYPMilestone");
        }
    }
}
 