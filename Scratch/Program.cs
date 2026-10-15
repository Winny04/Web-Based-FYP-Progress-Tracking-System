using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FYPTracker.Common;
using FYPTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace Scratch
{
    public class SeedRunner
    {
        private const string ConnectionString = "server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        public static async Task Main(string[] args)
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine(" FYPTracker — Idempotent Synthetic Dummy Data Seeder");
            Console.WriteLine(" Target: Boon\\SQLEXPRESS / FYPTracker_Development");
            Console.WriteLine("==========================================================");

            var optionsBuilder = new DbContextOptionsBuilder<FYPTrackerContext>();
            optionsBuilder.UseSqlServer(ConnectionString);

            using var context = new FYPTrackerContext(optionsBuilder.Options);

            // Safety Checks
            var dbName = context.Database.GetDbConnection().Database;
            if (!string.Equals(dbName, "FYPTracker_Development", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CRITICAL SAFETY ERROR] Unexpected database: {dbName}. Execution aborted!");
                Console.ResetColor();
                return;
            }

            // Master session check
            var activeSession = await context.AcademicSessions.FirstOrDefaultAsync(s => s.IsActive && s.IsCurrent == true)
                                ?? await context.AcademicSessions.FirstOrDefaultAsync();

            if (activeSession == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No active AcademicSession found in FYPTracker_Development.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"[INFO] Using AcademicSession: '{activeSession.Name}' (Id={activeSession.Id})");

            var depts = await context.Department.Take(6).ToListAsync();
            var programmes = await context.Programme.Take(6).ToListAsync();
            var specializations = await context.Specialization.Take(6).ToListAsync();
            var templates = await context.DefaultMilestoneTemplates.Where(t => t.AcademicSessionId == activeSession.Id).ToListAsync();

            // Prepare Synthetic File Directory (Safe dedicated folder)
            var baseDir = AppContext.BaseDirectory;
            // Go up to solution root -> FYPTracker -> wwwroot -> uploads -> dummy_synthetic
            var webRootUploads = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\FYPTracker\wwwroot\uploads\dummy_synthetic"));
            Directory.CreateDirectory(webRootUploads);

            var sampleProposalPath = Path.Combine(webRootUploads, "Dummy_Proposal_Template.pdf");
            var sampleInterimPath = Path.Combine(webRootUploads, "Dummy_Interim_Report.pdf");
            var sampleFinalPath = Path.Combine(webRootUploads, "Dummy_Final_Report.pdf");

            // Harmless synthetic placeholder PDF files (Valid standard minimal PDF)
            byte[] minimalPdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R/Resources<<>>>>endobj\nxref\n0 4\n0000000000 65535 f \n0000000010 00000 n \n0000000060 00000 n \n0000000117 00000 n \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n200\n%%EOF\n");
            if (!File.Exists(sampleProposalPath)) File.WriteAllBytes(sampleProposalPath, minimalPdfBytes);
            if (!File.Exists(sampleInterimPath)) File.WriteAllBytes(sampleInterimPath, minimalPdfBytes);
            if (!File.Exists(sampleFinalPath)) File.WriteAllBytes(sampleFinalPath, minimalPdfBytes);

            Console.WriteLine($"[INFO] Synthetic physical files verified in: {webRootUploads}");

            var manifest = new SeedManifest();

            // Standard test password hash: "Password123!"
            string passwordHash = Encryption.GetHash("Password123!");

            // 1. SEED 10 DUAL-ROLE LECTURERS
            Console.WriteLine("[STEP 1/5] Checking / Seeding 10 dual-role lecturers...");
            var lecturers = new List<ApplicationUser>();
            var lecturerNames = new[]
            {
                "Dr. Alice Tan", "Dr. Bernard Lim", "Ts. Catherine Wong", "Dr. Daniel Raj", "Ir. Evelyn Chong",
                "Dr. Farooq Ahmad", "Ms. Grace Teo", "Dr. Haris bin Omar", "Ts. Irene Lee", "Dr. Jeremy Ko"
            };

            for (int i = 1; i <= 10; i++)
            {
                string uname = $"l260000{i:D2}";
                string email = $"dummy.lecturer{i:D2}@newinti.edu.my";

                var existing = await context.ApplicationUsers
                    .Include(u => u.UserRoles)
                    .FirstOrDefaultAsync(u => u.UserName == uname);

                if (existing == null)
                {
                    var lect = new ApplicationUser
                    {
                        UserName = uname,
                        FullName = lecturerNames[i - 1],
                        Email = email,
                        PassCode = passwordHash,
                        RequirePasswordChange = false,
                        IsResign = false,
                        EmployeeId = $"EMP2600{i:D2}",
                        DepartmentId = depts[(i - 1) % depts.Count].Id,
                        MaxStudentSlots = 10,
                        DateCreated = DateTime.Now,
                        DateJoin = DateTime.Now.AddYears(-3),
                        IsNotificationOn = true,
                        Title = lecturerNames[i - 1].Split(' ')[0]
                    };
                    context.ApplicationUsers.Add(lect);
                    await context.SaveChangesAsync();
                    existing = lect;
                    manifest.CreatedLecturerIds.Add(lect.Id);
                }

                // Ensure Dual-Role (Supervisor: 10, Examiner: 11)
                if (!await context.UserRoleMappings.AnyAsync(r => r.UserId == existing.Id && r.RoleId == 10))
                {
                    context.UserRoleMappings.Add(new UserRoleMapping { UserId = existing.Id, RoleId = 10 });
                }
                if (!await context.UserRoleMappings.AnyAsync(r => r.UserId == existing.Id && r.RoleId == 11))
                {
                    context.UserRoleMappings.Add(new UserRoleMapping { UserId = existing.Id, RoleId = 11 });
                }
                await context.SaveChangesAsync();
                lecturers.Add(existing);
            }

            // 2. SEED 50 STUDENTS
            Console.WriteLine("[STEP 2/5] Checking / Seeding 50 students (p26010001 - p26010050)...");
            var students = new List<ApplicationUser>();
            for (int i = 1; i <= 50; i++)
            {
                string uname = $"p260100{i:D2}";
                string email = $"p260100{i:D2}@student.newinti.edu.my";

                var existing = await context.ApplicationUsers
                    .Include(u => u.UserRoles)
                    .FirstOrDefaultAsync(u => u.UserName == uname);

                if (existing == null)
                {
                    var stu = new ApplicationUser
                    {
                        UserName = uname,
                        FullName = $"Student Dummy {i:D2}",
                        Email = email,
                        PassCode = passwordHash,
                        RequirePasswordChange = false,
                        IsResign = false,
                        EmployeeId = uname, // INTI ID
                        CoventryId = $"cov260100{i:D2}",
                        DepartmentId = depts[(i - 1) % depts.Count].Id,
                        Programme = programmes[(i - 1) % programmes.Count].Name,
                        Specialization = specializations[(i - 1) % specializations.Count].Name,
                        AcademicSession = activeSession.Name,
                        DateCreated = DateTime.Now,
                        DateJoin = DateTime.Now.AddMonths(-6),
                        IsNotificationOn = true
                    };
                    context.ApplicationUsers.Add(stu);
                    await context.SaveChangesAsync();
                    existing = stu;
                    manifest.CreatedStudentIds.Add(stu.Id);
                }

                // Student Role (RoleId = 12)
                if (!await context.UserRoleMappings.AnyAsync(r => r.UserId == existing.Id && r.RoleId == 12))
                {
                    context.UserRoleMappings.Add(new UserRoleMapping { UserId = existing.Id, RoleId = 12 });
                    await context.SaveChangesAsync();
                }
                students.Add(existing);
            }

            // 3. SEED 50 PROJECTS & ALLOCATIONS (Supervisor & Examiner)
            // Exactly 5 supervised per lecturer:
            // S(i) supervised by L((i - 1) / 5)
            // S(i) examined by L(((i - 1) / 5 + 5) % 10)
            Console.WriteLine("[STEP 3/5] Seeding 50 projects, supervisor requests, and examiner allocations...");

            for (int i = 1; i <= 50; i++)
            {
                var student = students[i - 1];
                int supIndex = (i - 1) / 5;               // 0..9 (5 students each)
                int exIndex = (supIndex + 5) % 10;        // cyclic offset by 5 -> STRICT ZERO SELF-EXAMINATION
                var supervisor = lecturers[supIndex];
                var examiner = lecturers[exIndex];

                string projectTopic = $"Design and Implementation of Synthetic FYP Artifact {i:D2}";

                // Create or verify SupervisorRequest
                var supReq = await context.SupervisorRequests.FirstOrDefaultAsync(r => r.StudentId == student.Id && r.SupervisorId == supervisor.Id);
                if (supReq == null)
                {
                    supReq = new SupervisorRequest
                    {
                        StudentId = student.Id,
                        SupervisorId = supervisor.Id,
                        Status = (i <= 5) ? "Pending" : "Approved", // S01-S05 pending, S06-S50 approved
                        Priority = 1,
                        MatricNumber = student.UserName,
                        Program = student.Programme,
                        PhoneNumber = "0123456789",
                        SessionSemester = activeSession.Name,
                        YearOfStudy = "Year 3",
                        ProjectTopic = projectTopic,
                        RequestMessage = $"Requesting supervision for project topic #{i}.",
                        RequestedDate = DateTime.Now.AddDays(-60),
                        ExpiryDate = DateTime.Now.AddDays(30),
                        RespondedDate = (i <= 5) ? null : DateTime.Now.AddDays(-55),
                        ResponseNote = (i <= 5) ? null : "Topic accepted."
                    };
                    context.SupervisorRequests.Add(supReq);
                    await context.SaveChangesAsync();
                    manifest.CreatedSupervisorRequestIds.Add(supReq.Id);
                }

                // If S01-S05, requests are still pending approval, so project is NOT created prematurely
                // (Unless manual testing requires project, but user specifically asked to respect supervisor approval)
                // However, user stated "Exactly 50 Projects, one per Student".
                // To satisfy BOTH: S01-S05 have project with Status = 'Proposed', S06-S50 have Status = 'Approved'/'InProgress'/'Completed'.
                var project = await context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == student.Id);
                if (project == null)
                {
                    string initialStatus = (i <= 5) ? "Proposed" :
                                          (i <= 20) ? "Approved" :
                                          (i <= 46) ? "InProgress" : "Completed";

                    project = new FYPProject
                    {
                        Title = projectTopic,
                        Description = $"Comprehensive investigation and software development for FYP dummy track #{i}.",
                        ProblemStatement = "Traditional manual tracking lacks real-time milestone synchronisation.",
                        Objective1 = "Design an automated tracking architecture.",
                        Objective2 = "Implement role-based milestone and viva evaluation.",
                        Objective3 = "Validate accuracy using synthetic testing fixtures.",
                        Scope = "Undergraduate final year project cohort.",
                        ExpectedOutcome = "Fully documented and tested application suite.",
                        StudentId = student.Id,
                        SupervisorId = supervisor.Id,
                        Status = initialStatus,
                        StartDate = DateTime.Now.AddMonths(-5),
                        CreatedDate = DateTime.Now.AddMonths(-5),
                        AcademicSession = activeSession.Name
                    };

                    if (initialStatus == "Completed")
                    {
                        project.EndDate = DateTime.Now.AddDays(-2);
                        project.VivaDate = DateTime.Now.AddDays(-7);
                        project.VivaVenue = "Viva Room 101";
                        project.FinalGrade = (i % 2 == 0) ? "Excellent" : "Good";
                        project.IsGradingLocked = true;
                    }
                    else if (i >= 41)
                    {
                        project.VivaDate = DateTime.Now.AddDays(14);
                        project.VivaVenue = "Main Conference Hall";
                    }

                    context.FYPProjects.Add(project);
                    await context.SaveChangesAsync();
                    manifest.CreatedProjectIds.Add(project.Id);

                    context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                    {
                        ProjectId = project.Id,
                        OldStatus = "None",
                        NewStatus = initialStatus,
                        ChangedById = supervisor.Id,
                        Remarks = "Project seeded via synthetic data generator.",
                        ChangedDate = DateTime.Now.AddMonths(-5)
                    });
                    await context.SaveChangesAsync();
                }

                // Assigned Examiner (FYPProjectExaminer)
                if (!await context.FYPProjectExaminers.AnyAsync(pe => pe.FYPProjectId == project.Id && pe.ExaminerId == examiner.Id))
                {
                    var projExaminer = new FYPProjectExaminer
                    {
                        FYPProjectId = project.Id,
                        ExaminerId = examiner.Id,
                        AssignedDate = DateTime.Now.AddMonths(-4)
                    };
                    context.FYPProjectExaminers.Add(projExaminer);
                    await context.SaveChangesAsync();
                    manifest.CreatedProjectExaminerIds.Add(projExaminer.Id);
                }

                // 4. SEED MILESTONES & LIFECYCLE
                await SeedProjectMilestonesAndLifecycle(context, project, student, supervisor, examiner, templates, activeSession, manifest, i);
            }

            // Write Manifest
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestPath = Path.Combine(baseDir, "dummy_seed_manifest.json");
            File.WriteAllText(manifestPath, manifestJson);

            Console.WriteLine("==========================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" DUMMY SEED EXECUTION COMPLETED SUCCESSFULLY!");
            Console.ResetColor();
            Console.WriteLine($" Manifest written to: {manifestPath}");
            Console.WriteLine($" Summary: 50 Students, 10 Lecturers, 50 Projects, 50 Examiner Allocations.");
            Console.WriteLine("==========================================================");
        }

        private static async Task SeedProjectMilestonesAndLifecycle(
            FYPTrackerContext context,
            FYPProject project,
            ApplicationUser student,
            ApplicationUser supervisor,
            ApplicationUser examiner,
            List<DefaultMilestoneTemplate> templates,
            AcademicSession session,
            SeedManifest manifest,
            int projectIndex)
        {
            // Seed Stage 1 Default Milestones
            var existingMilestones = await context.FYPMilestones.Where(m => m.FYPProjectId == project.Id).ToListAsync();
            if (!existingMilestones.Any())
            {
                foreach (var t in templates)
                {
                    string mStatus = "Pending";
                    DateTime? subDate = null;

                    // Progressively advance milestone states according to cohort group
                    if (t.MilestoneCode == "IAF")
                    {
                        if (projectIndex > 10) { mStatus = "AdminApproved"; subDate = DateTime.Now.AddMonths(-4); }
                        else if (projectIndex > 5) { mStatus = "Submitted"; subDate = DateTime.Now.AddDays(-10); }
                    }
                    else if (t.MilestoneCode == "STAGE1_FINAL")
                    {
                        if (projectIndex >= 31) { mStatus = "Completed"; subDate = DateTime.Now.AddMonths(-2); }
                        else if (projectIndex >= 21) { mStatus = "Submitted"; subDate = DateTime.Now.AddDays(-5); }
                    }

                    var ms = new FYPMilestone
                    {
                        FYPProjectId = project.Id,
                        Title = t.Title,
                        Description = t.Description,
                        DueDate = DateTime.Now.AddMonths(1),
                        SubmittedDate = subDate,
                        Status = mStatus,
                        MilestoneSource = "Default",
                        MilestoneCode = t.MilestoneCode,
                        Stage = t.Stage,
                        IsRequired = t.IsRequired,
                        DefaultMilestoneTemplateId = t.Id,
                        CreatedDate = DateTime.Now.AddMonths(-5),
                        Remarks = (t.MilestoneCode == "IAF" && projectIndex > 10) ? "[Attempt 1: Approved - Defense passed]" : null
                    };

                    // Stage 1 Rubrics for group D & E (projectIndex >= 31)
                    if (t.MilestoneCode == "STAGE1_FINAL" && projectIndex >= 31)
                    {
                        // 6-Criterion Stage 1 JSON format matching GradingHelper
                        ms.SupervisorMarksheetData = "{\"Crit1\":3.5,\"Crit2\":3.5,\"Crit3\":3.0,\"Crit4\":3.5,\"Crit5\":3.0,\"Crit6\":4.0,\"IsDraft\":false}";
                        ms.ExaminerMarksheetData = "{\"Crit1\":3.0,\"Crit2\":3.5,\"Crit3\":3.5,\"Crit4\":3.0,\"Crit5\":3.5,\"Crit6\":3.5,\"IsDraft\":false}";
                        ms.IsReadyForJointEvaluation = true;
                    }

                    context.FYPMilestones.Add(ms);
                    await context.SaveChangesAsync();
                    manifest.CreatedMilestoneIds.Add(ms.Id);
                    existingMilestones.Add(ms);

                    // Add synthetic submission file for submitted milestones
                    if (subDate.HasValue)
                    {
                        var subFile = new FYPSubmissionFile
                        {
                            MilestoneId = ms.Id,
                            FileName = (t.MilestoneCode == "IAF") ? "IAF_Proposal.pdf" : "Stage1_Report.pdf",
                            FilePath = "/uploads/dummy_synthetic/" + ((t.MilestoneCode == "IAF") ? "Dummy_Proposal_Template.pdf" : "Dummy_Interim_Report.pdf"),
                            FileSize = 1024,
                            AttemptNumber = 1,
                            UploadedBy = student.Id,
                            UploadedDate = subDate.Value,
                            Remarks = "Synthetic submission file fixture."
                        };
                        context.FYPSubmissionFiles.Add(subFile);
                        await context.SaveChangesAsync();
                        manifest.CreatedSubmissionFileIds.Add(subFile.Id);
                    }
                }

                // Add Stage 2 Milestones for projects in Stage 2 (projectIndex >= 31)
                if (projectIndex >= 31)
                {
                    var stage2Titles = new[]
                    {
                        "Step 1: Turnitin Checking",
                        "Step 2: Final Report Submission",
                        "Step 3: Marksheet Submission",
                        "Step 4: Record of Supervisor Meeting Form Submission",
                        "Step 5: Presentation Slides Submission"
                    };

                    foreach (var s2Title in stage2Titles)
                    {
                        var ms2 = new FYPMilestone
                        {
                            FYPProjectId = project.Id,
                            Title = s2Title,
                            DueDate = DateTime.Now.AddMonths(4),
                            Status = (projectIndex >= 47) ? "Completed" : "Pending",
                            Stage = 2,
                            IsRequired = true,
                            MilestoneSource = "Default",
                            CreatedDate = DateTime.Now.AddMonths(-2)
                        };
                        context.FYPMilestones.Add(ms2);
                        await context.SaveChangesAsync();
                        manifest.CreatedMilestoneIds.Add(ms2.Id);
                    }

                    // Stage 2 Rubric Version 2 standardization
                    if (projectIndex >= 41)
                    {
                        project.Stage2SupervisorMarksheetData = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"Crit1\":3.5,\"Crit2\":3.5,\"Crit3\":3.0,\"Crit4\":3.5,\"Crit5\":3.0,\"Crit6\":3.0,\"Crit7\":3.5}";
                        project.Stage2ExaminerMarksheetData = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"Crit1\":3.0,\"Crit2\":3.5,\"Crit3\":3.0,\"Crit4\":3.5,\"Crit5\":3.5,\"Crit6\":3.0,\"Crit7\":3.5}";
                        await context.SaveChangesAsync();
                    }
                }
            }

            // 5. SEED IAF DEFENSE BOOKINGS (For Group B: projectIndex 11..20)
            if (projectIndex >= 11 && projectIndex <= 20)
            {
                var iafMilestone = existingMilestones.FirstOrDefault(m => m.MilestoneCode == "IAF");
                if (iafMilestone != null && !await context.IAFDefenseBookings.AnyAsync(b => b.MilestoneId == iafMilestone.Id))
                {
                    bool isPassed = (projectIndex <= 18);
                    var booking = new IAFDefenseBooking
                    {
                        MilestoneId = iafMilestone.Id,
                        AttemptNumber = 1,
                        AcademicSessionId = session.Id,
                        ExaminerId = examiner.Id,
                        OriginalSupervisorId = supervisor.Id,
                        IsSupervisorRequired = true,
                        DefenseDate = DateTime.Now.AddDays(-15),
                        StartTime = new TimeSpan(10, 0, 0),
                        EndTime = new TimeSpan(10, 30, 0),
                        DurationMinutes = 30,
                        Venue = "Meeting Room B-2",
                        BookingStatus = "Completed",
                        AcademicOutcome = isPassed ? "AdminApproved" : "ChangeOfTopicRequired",
                        OutcomeComments = isPassed ? "Project scope clear and well justified." : "Scope insufficient. Please submit revision.",
                        CreatedAt = DateTime.Now.AddDays(-20)
                    };
                    context.IAFDefenseBookings.Add(booking);
                    await context.SaveChangesAsync();
                    manifest.CreatedDefenseBookingIds.Add(booking.Id);

                    var history = new IAFDefenseHistory
                    {
                        BookingId = booking.Id,
                        MilestoneId = booking.MilestoneId,
                        AttemptNumber = 1,
                        AcademicSessionId = session.Id,
                        ActionType = "OutcomeRecorded",
                        ExaminerId = examiner.Id,
                        OriginalSupervisorId = supervisor.Id,
                        DefenseDate = booking.DefenseDate,
                        StartTime = booking.StartTime,
                        EndTime = booking.EndTime,
                        DurationMinutes = 30,
                        Venue = booking.Venue,
                        BookingStatus = "Completed",
                        AcademicOutcome = booking.AcademicOutcome,
                        OutcomeComments = booking.OutcomeComments,
                        ReasonForChange = "Defense outcome recorded.",
                        Timestamp = DateTime.Now.AddDays(-15),
                        ActionByUserId = supervisor.Id
                    };
                    context.IAFDefenseHistories.Add(history);
                    await context.SaveChangesAsync();
                    manifest.CreatedDefenseHistoryIds.Add(history.Id);
                }
            }

            // 6. SEED WEEKLY MEETINGS & SIGNED LOGS (For Groups C, D, E: projectIndex >= 21)
            if (projectIndex >= 21)
            {
                int meetingCount = (projectIndex <= 30) ? 5 : 10;
                var existingBookings = await context.MeetingBookings.Where(b => b.StudentId == student.Id).CountAsync();
                if (existingBookings == 0)
                {
                    for (int m = 1; m <= meetingCount; m++)
                    {
                        var booking = new MeetingBooking
                        {
                            StudentId = student.Id,
                            SupervisorId = supervisor.Id,
                            FYPProjectId = project.Id,
                            MeetingDate = DateTime.Now.AddDays(-7 * (meetingCount - m + 1)).Date,
                            MeetingDay = DateTime.Now.AddDays(-7 * (meetingCount - m + 1)).DayOfWeek.ToString(),
                            MeetingTime = "14:00",
                            StartDateTime = DateTime.Now.AddDays(-7 * (meetingCount - m + 1)).Date.AddHours(14),
                            EndDateTime = DateTime.Now.AddDays(-7 * (meetingCount - m + 1)).Date.AddHours(15),
                            Status = "Approved",
                            Stage = (m <= 5) ? 1 : 2,
                            AcademicSessionId = session.Id,
                            CreatedDate = DateTime.Now.AddDays(-7 * (meetingCount - m + 1)).AddDays(-2)
                        };
                        context.MeetingBookings.Add(booking);
                        await context.SaveChangesAsync();
                        manifest.CreatedMeetingBookingIds.Add(booking.Id);

                        var log = new MeetingLog
                        {
                            MeetingBookingId = booking.Id,
                            KeyTopicsDiscussed = $"Discussion on Milestone Progress and Task #{m} Implementation.",
                            CompletedActionsAndNotes = "Completed literature review tasks and updated architectural diagram.",
                            ActionPointsForNextMeeting = "Prepare test fixtures and verify experimental metrics.",
                            SupervisorComments = "Progress on track. Keep up the good work.|||SIG|||SupervisorSignatureDummy",
                            LogStatus = "Signed",
                            SignatureDate = booking.MeetingDate.AddHours(2),
                            CreatedDate = booking.MeetingDate
                        };
                        context.MeetingLogs.Add(log);
                        await context.SaveChangesAsync();
                        manifest.CreatedMeetingLogIds.Add(log.Id);
                    }
                }
            }
        }
    }

    public class SeedManifest
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<int> CreatedStudentIds { get; set; } = new();
        public List<int> CreatedLecturerIds { get; set; } = new();
        public List<int> CreatedSupervisorRequestIds { get; set; } = new();
        public List<int> CreatedProjectIds { get; set; } = new();
        public List<int> CreatedProjectExaminerIds { get; set; } = new();
        public List<int> CreatedMilestoneIds { get; set; } = new();
        public List<int> CreatedSubmissionFileIds { get; set; } = new();
        public List<int> CreatedDefenseBookingIds { get; set; } = new();
        public List<int> CreatedDefenseHistoryIds { get; set; } = new();
        public List<int> CreatedMeetingBookingIds { get; set; } = new();
        public List<int> CreatedMeetingLogIds { get; set; } = new();
    }
}
