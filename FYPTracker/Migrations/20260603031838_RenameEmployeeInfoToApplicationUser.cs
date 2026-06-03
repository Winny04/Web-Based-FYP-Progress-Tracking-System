using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class RenameEmployeeInfoToApplicationUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Department",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Department", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplateConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfigValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequireParams = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationUser",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShortName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime", nullable: true),
                    DateJoin = table.Column<DateTime>(type: "datetime", nullable: true),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    IsNotificationOn = table.Column<bool>(type: "bit", nullable: false),
                    IsResign = table.Column<bool>(type: "bit", nullable: false),
                    ResetToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResetTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Programme = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcademicSession = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OfficeRoom = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxStudentSlots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationUser_Department_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Department",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AuditTrail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShortMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmpId = table.Column<int>(type: "int", nullable: true),
                    LogLevelId = table.Column<int>(type: "int", nullable: false),
                    FullMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferrerUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TableChange = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataAfter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTrail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditTrail_ApplicationUser_EmpId",
                        column: x => x.EmpId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FYPActivity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPActivity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPActivity_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPIAForm",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanySupervisorName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanySupervisorEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanySupervisorPhone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProjectTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectScope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonthlyAllowance = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPIAForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPIAForm_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPNotification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    ReadDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPNotification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPNotification_ApplicationUser_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPProject",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProblemStatement = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Objective1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Objective2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Objective3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedOutcome = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    References = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPProject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPProject_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FYPProject_ApplicationUser_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeetingBooking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: false),
                    MeetingDate = table.Column<DateTime>(type: "date", nullable: false),
                    MeetingDay = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MeetingTime = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupervisorNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedNewDate = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    RespondedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingBooking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingBooking_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeetingBooking_ApplicationUser_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupervisorRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    MatricNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Program = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionSemester = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    YearOfStudy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProjectTopic = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    RespondedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ResponseNote = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupervisorRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupervisorRequest_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupervisorRequest_ApplicationUser_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditTag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuditId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreateAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditTag_ApplicationUser_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuditTag_AuditTrail_AuditId",
                        column: x => x.AuditId,
                        principalTable: "AuditTrail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPMilestone",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FYPProjectId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupervisorFeedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPMilestone", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPMilestone_FYPProject_FYPProjectId",
                        column: x => x.FYPProjectId,
                        principalTable: "FYPProject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPFeedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MilestoneId = table.Column<int>(type: "int", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPFeedback_ApplicationUser_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FYPFeedback_FYPMilestone_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "FYPMilestone",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FYPSubmissionFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MilestoneId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    UploadedBy = table.Column<int>(type: "int", nullable: true),
                    UploadedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPSubmissionFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPSubmissionFile_ApplicationUser_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FYPSubmissionFile_FYPMilestone_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "FYPMilestone",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUser_DepartmentId",
                table: "ApplicationUser",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTag_AuditId",
                table: "AuditTag",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTag_EmployeeId",
                table: "AuditTag",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrail_EmpId",
                table: "AuditTrail",
                column: "EmpId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPActivity_StudentId",
                table: "FYPActivity",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPFeedback_MilestoneId",
                table: "FYPFeedback",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPFeedback_SupervisorId",
                table: "FYPFeedback",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPIAForm_StudentId",
                table: "FYPIAForm",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPMilestone_FYPProjectId",
                table: "FYPMilestone",
                column: "FYPProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPNotification_RecipientId",
                table: "FYPNotification",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPProject_StudentId",
                table: "FYPProject",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPProject_SupervisorId",
                table: "FYPProject",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPSubmissionFile_MilestoneId",
                table: "FYPSubmissionFile",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPSubmissionFile_UploadedBy",
                table: "FYPSubmissionFile",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingBooking_StudentId",
                table: "MeetingBooking",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingBooking_SupervisorId",
                table: "MeetingBooking",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupervisorRequest_StudentId",
                table: "SupervisorRequest",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupervisorRequest_SupervisorId",
                table: "SupervisorRequest",
                column: "SupervisorId");
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
