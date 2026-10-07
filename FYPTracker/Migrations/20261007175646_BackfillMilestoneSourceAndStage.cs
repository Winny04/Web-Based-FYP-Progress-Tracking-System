using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class BackfillMilestoneSourceAndStage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set Stage 2 Default
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET MilestoneSource = 'Default', Stage = 2
                WHERE Title LIKE 'Step %' OR Title LIKE 'Stage 2:%';
            ");

            // Set Stage 1 Default
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET MilestoneSource = 'Default', Stage = 1
                WHERE Title LIKE '%Initial Assessment Form%' 
                   OR Title LIKE '%Stage 1 Final Submission%' 
                   OR Title LIKE '%Submission of Ethic Form%' 
                   OR Title LIKE '%Turnitin Checking - DPP%';
            ");

            // Set Stage 1 Supervisor
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET MilestoneSource = 'Supervisor', Stage = 1
                WHERE (MilestoneSource = 'Default' OR MilestoneSource IS NULL OR MilestoneSource = '')
                  AND Title NOT LIKE 'Step %' 
                  AND Title NOT LIKE 'Stage 2:%'
                  AND Title NOT LIKE '%Initial Assessment Form%' 
                  AND Title NOT LIKE '%Stage 1 Final Submission%' 
                  AND Title NOT LIKE '%Submission of Ethic Form%' 
                  AND Title NOT LIKE '%Turnitin Checking - DPP%'
                  AND Title NOT LIKE 'FYP 2:%';
            ");

            // Set Stage 2 Supervisor
            migrationBuilder.Sql(@"
                UPDATE FYPMilestone 
                SET MilestoneSource = 'Supervisor', Stage = 2
                WHERE Title LIKE 'FYP 2:%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
