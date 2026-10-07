using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class BackfillPendingToDraft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET Status = 'Draft'
                WHERE MilestoneSource = 'Supervisor' AND Stage = 1 AND Status = 'Pending';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET Status = 'Pending'
                WHERE MilestoneSource = 'Supervisor' AND Stage = 1 AND Status = 'Draft';
            ");
        }
    }
}
