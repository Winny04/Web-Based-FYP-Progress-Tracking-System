using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddDefaultMilestoneTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultMilestoneTemplateId",
                table: "FYPMilestone",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MilestoneCode",
                table: "FYPMilestone",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DefaultMilestoneTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcademicSessionId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MilestoneCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefaultMilestoneTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefaultMilestoneTemplates_AcademicSession_AcademicSessionId",
                        column: x => x.AcademicSessionId,
                        principalTable: "AcademicSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FYPMilestone_DefaultMilestoneTemplateId",
                table: "FYPMilestone",
                column: "DefaultMilestoneTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DefaultMilestoneTemplates_AcademicSessionId",
                table: "DefaultMilestoneTemplates",
                column: "AcademicSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_FYPMilestone_DefaultMilestoneTemplates_DefaultMilestoneTemplateId",
                table: "FYPMilestone",
                column: "DefaultMilestoneTemplateId",
                principalTable: "DefaultMilestoneTemplates",
                principalColumn: "Id");

            // Backfill existing Stage 1 default milestones with MilestoneCode based on legacy titles
            migrationBuilder.Sql(@"
                -- Create Templates for IAF
                INSERT INTO DefaultMilestoneTemplates (AcademicSessionId, Title, Description, MilestoneCode, Stage, IsRequired, IsActive, DisplayOrder, DueDate)
                SELECT s.Id, 'FYP 1: Initial Assessment Form (IAF)', NULL, 'IAF', 1, 1, 1, 1, 
                    (SELECT MAX(m.DueDate) FROM FYPMilestone m INNER JOIN FYPProject p ON m.FYPProjectId = p.Id WHERE p.AcademicSession = s.SessionName AND m.MilestoneSource = 'Default' AND m.Stage = 1 AND (m.Title LIKE '%IAF%' OR m.Title LIKE '%Initial Project Allocation Form%') AND m.Title NOT LIKE '%Industrial%')
                FROM AcademicSession s;

                -- Create Templates for ETHICS
                INSERT INTO DefaultMilestoneTemplates (AcademicSessionId, Title, Description, MilestoneCode, Stage, IsRequired, IsActive, DisplayOrder, DueDate)
                SELECT s.Id, 'Submission of Ethic Form', NULL, 'GENERIC', 1, 0, 1, 2, 
                    (SELECT MAX(m.DueDate) FROM FYPMilestone m INNER JOIN FYPProject p ON m.FYPProjectId = p.Id WHERE p.AcademicSession = s.SessionName AND m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Ethic%')
                FROM AcademicSession s;

                -- Create Templates for TURNITIN_DPP
                INSERT INTO DefaultMilestoneTemplates (AcademicSessionId, Title, Description, MilestoneCode, Stage, IsRequired, IsActive, DisplayOrder, DueDate)
                SELECT s.Id, 'Turnitin Checking - DPP', NULL, 'GENERIC', 1, 0, 1, 3, 
                    (SELECT MAX(m.DueDate) FROM FYPMilestone m INNER JOIN FYPProject p ON m.FYPProjectId = p.Id WHERE p.AcademicSession = s.SessionName AND m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Turnitin%')
                FROM AcademicSession s;

                -- Create Templates for STAGE1_FINAL
                INSERT INTO DefaultMilestoneTemplates (AcademicSessionId, Title, Description, MilestoneCode, Stage, IsRequired, IsActive, DisplayOrder, DueDate)
                SELECT s.Id, 'FYP 1: Stage 1 Final Submission (DPP)', NULL, 'STAGE1_FINAL', 1, 1, 1, 4, 
                    (SELECT MAX(m.DueDate) FROM FYPMilestone m INNER JOIN FYPProject p ON m.FYPProjectId = p.Id WHERE p.AcademicSession = s.SessionName AND m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Stage 1 Final Submission%')
                FROM AcademicSession s;

                -- Map IAF milestones to the new templates
                UPDATE m
                SET m.DefaultMilestoneTemplateId = t.Id, m.MilestoneCode = 'IAF'
                FROM FYPMilestone m
                INNER JOIN FYPProject p ON m.FYPProjectId = p.Id
                INNER JOIN AcademicSession s ON p.AcademicSession = s.SessionName
                INNER JOIN DefaultMilestoneTemplates t ON t.AcademicSessionId = s.Id AND t.MilestoneCode = 'IAF'
                WHERE m.MilestoneSource = 'Default' AND m.Stage = 1 AND (m.Title LIKE '%IAF%' OR m.Title LIKE '%Initial Project Allocation Form%') AND m.Title NOT LIKE '%Industrial%';

                -- Map Ethics milestones to the new templates
                UPDATE m
                SET m.DefaultMilestoneTemplateId = t.Id, m.MilestoneCode = 'GENERIC'
                FROM FYPMilestone m
                INNER JOIN FYPProject p ON m.FYPProjectId = p.Id
                INNER JOIN AcademicSession s ON p.AcademicSession = s.SessionName
                INNER JOIN DefaultMilestoneTemplates t ON t.AcademicSessionId = s.Id AND t.Title = 'Submission of Ethic Form'
                WHERE m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Ethic%';

                -- Map Turnitin DPP milestones to the new templates
                UPDATE m
                SET m.DefaultMilestoneTemplateId = t.Id, m.MilestoneCode = 'GENERIC'
                FROM FYPMilestone m
                INNER JOIN FYPProject p ON m.FYPProjectId = p.Id
                INNER JOIN AcademicSession s ON p.AcademicSession = s.SessionName
                INNER JOIN DefaultMilestoneTemplates t ON t.AcademicSessionId = s.Id AND t.Title = 'Turnitin Checking - DPP'
                WHERE m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Turnitin%';

                -- Map Stage 1 Final milestones to the new templates
                UPDATE m
                SET m.DefaultMilestoneTemplateId = t.Id, m.MilestoneCode = 'STAGE1_FINAL'
                FROM FYPMilestone m
                INNER JOIN FYPProject p ON m.FYPProjectId = p.Id
                INNER JOIN AcademicSession s ON p.AcademicSession = s.SessionName
                INNER JOIN DefaultMilestoneTemplates t ON t.AcademicSessionId = s.Id AND t.MilestoneCode = 'STAGE1_FINAL'
                WHERE m.MilestoneSource = 'Default' AND m.Stage = 1 AND m.Title LIKE '%Stage 1 Final Submission%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FYPMilestone_DefaultMilestoneTemplates_DefaultMilestoneTemplateId",
                table: "FYPMilestone");

            migrationBuilder.DropTable(
                name: "DefaultMilestoneTemplates");

            migrationBuilder.DropIndex(
                name: "IX_FYPMilestone_DefaultMilestoneTemplateId",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "DefaultMilestoneTemplateId",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "MilestoneCode",
                table: "FYPMilestone");
        }
    }
}
