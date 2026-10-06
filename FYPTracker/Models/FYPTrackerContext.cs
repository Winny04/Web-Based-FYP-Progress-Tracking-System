using System;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Models
{
    public partial class FYPTrackerContext : DbContext
    {
        public FYPTrackerContext()
        {
        }

        public FYPTrackerContext(DbContextOptions<FYPTrackerContext> options)
            : base(options)
        {
        }

        // ── Shared / Auth ──────────────────────────────────────────
        public virtual DbSet<ApplicationUser>   ApplicationUsers   { get; set; } = null!;
        public virtual DbSet<Department>        Department         { get; set; } = null!;
        public virtual DbSet<Programme>         Programme          { get; set; } = null!;
        public virtual DbSet<Specialization>    Specialization     { get; set; } = null!;
        public virtual DbSet<AcademicSession>   AcademicSessions   { get; set; } = null!;
        public virtual DbSet<SupervisorAvailability> SupervisorAvailabilities { get; set; } = null!;
        public virtual DbSet<AppRole>           AppRoles           { get; set; } = null!;
        public virtual DbSet<UserRoleMapping>   UserRoleMappings   { get; set; } = null!;

        // ── FYP Core ───────────────────────────────────────────────
        public virtual DbSet<FYPProject>        FYPProjects        { get; set; } = null!;
        public virtual DbSet<FYPMilestone>      FYPMilestones      { get; set; } = null!;
        public virtual DbSet<FYPFeedback>       FYPFeedbacks       { get; set; } = null!;
        public virtual DbSet<FYPSubmissionFile> FYPSubmissionFiles { get; set; } = null!;
        public virtual DbSet<FYPProjectExaminer> FYPProjectExaminers { get; set; } = null!;
        public virtual DbSet<FYPProjectStatusHistory> FYPProjectStatusHistories { get; set; } = null!;

        // ── Student Module ─────────────────────────────────────────
        public virtual DbSet<SupervisorRequest> SupervisorRequests { get; set; } = null!;
        public virtual DbSet<MeetingBooking>    MeetingBookings    { get; set; } = null!;
        public virtual DbSet<MeetingLog>        MeetingLogs        { get; set; } = null!;
        public virtual DbSet<FYPNotification>   FYPNotifications   { get; set; } = null!;
        public virtual DbSet<FYPActivity>       FYPActivities      { get; set; } = null!;
        public virtual DbSet<ChatMessage>       ChatMessages       { get; set; } = null!;
        public virtual DbSet<ChatGroup>         ChatGroups         { get; set; } = null!;
        public virtual DbSet<ChatGroupMember>   ChatGroupMembers   { get; set; } = null!;
        public virtual DbSet<InvitationLog>     InvitationLogs     { get; set; } = null!;
        public virtual DbSet<FYPSetting>        FYPSettings        { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false);
                IConfiguration config = builder.Build();

                string dbConnString = config.GetValue<string>("ConnectionStrings:Entities")!;
                optionsBuilder.UseSqlServer(dbConnString, options =>
                {
                    options.CommandTimeout(80);
                });
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Global Query Filter for Soft Deletes
            modelBuilder.Entity<FYPSubmissionFile>().HasQueryFilter(f => !f.IsDeleted);
            // FYPProject: one student → many projects; one supervisor → many projects
            modelBuilder.Entity<FYPProject>(entity =>
            {
                entity.ToTable("FYPProject");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Supervisor)
                      .WithMany()
                      .HasForeignKey(e => e.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // FYPMilestone: belongs to a FYPProject
            modelBuilder.Entity<FYPMilestone>(entity =>
            {
                entity.ToTable("FYPMilestone");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Project)
                      .WithMany(p => p.Milestones)
                      .HasForeignKey(e => e.FYPProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // FYPProjectExaminer
            modelBuilder.Entity<FYPProjectExaminer>(entity =>
            {
                entity.ToTable("FYPProjectExaminer");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Project)
                      .WithMany(p => p.AssignedExaminers)
                      .HasForeignKey(e => e.FYPProjectId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Examiner)
                      .WithMany()
                      .HasForeignKey(e => e.ExaminerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // FYPFeedback: belongs to a milestone + written by a supervisor
            modelBuilder.Entity<FYPFeedback>(entity =>
            {
                entity.ToTable("FYPFeedback");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Milestone)
                      .WithMany(m => m.Feedbacks)
                      .HasForeignKey(e => e.MilestoneId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Supervisor)
                      .WithMany()
                      .HasForeignKey(e => e.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // FYPSubmissionFile: belongs to a milestone
            modelBuilder.Entity<FYPSubmissionFile>(entity =>
            {
                entity.ToTable("FYPSubmissionFile");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Milestone)
                      .WithMany(m => m.SubmissionFiles)
                      .HasForeignKey(e => e.MilestoneId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Uploader)
                      .WithMany()
                      .HasForeignKey(e => e.UploadedBy)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ApplicationUser → Department FK
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.ToTable("ApplicationUser");
                entity.HasOne(e => e.EmpDepartment)
                      .WithMany(d => d.ApplicationUsers)
                      .HasForeignKey(e => e.DepartmentId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // SupervisorRequest
            modelBuilder.Entity<SupervisorRequest>(entity =>
            {
                entity.ToTable("SupervisorRequest");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Supervisor)
                      .WithMany()
                      .HasForeignKey(e => e.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // MeetingBooking
            modelBuilder.Entity<MeetingBooking>(entity =>
            {
                entity.ToTable("MeetingBooking");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Supervisor)
                      .WithMany()
                      .HasForeignKey(e => e.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // MeetingLog
            modelBuilder.Entity<MeetingLog>(entity =>
            {
                entity.ToTable("MeetingLog");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.MeetingBooking)
                      .WithOne(m => m.MeetingLog)
                      .HasForeignKey<MeetingLog>(e => e.MeetingBookingId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // FYPNotification
            modelBuilder.Entity<FYPNotification>(entity =>
            {
                entity.ToTable("FYPNotification");
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Recipient)
                      .WithMany()
                      .HasForeignKey(e => e.RecipientId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // FYPActivity
            modelBuilder.Entity<FYPActivity>(entity =>
            {
                entity.ToTable("FYPActivity");
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Cascade);
            });


            // ChatMessage
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.ToTable("ChatMessage");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Sender)
                      .WithMany()
                      .HasForeignKey(e => e.SenderId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Receiver)
                      .WithMany()
                      .HasForeignKey(e => e.ReceiverId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Group)
                      .WithMany(g => g.Messages)
                      .HasForeignKey(e => e.GroupId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ChatGroup
            modelBuilder.Entity<ChatGroup>(entity =>
            {
                entity.ToTable("ChatGroup");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Supervisor)
                      .WithMany()
                      .HasForeignKey(e => e.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ChatGroupMember
            modelBuilder.Entity<ChatGroupMember>(entity =>
            {
                entity.ToTable("ChatGroupMember");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Group)
                      .WithMany(g => g.Members)
                      .HasForeignKey(e => e.GroupId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // InvitationLog
            modelBuilder.Entity<InvitationLog>(entity =>
            {
                entity.ToTable("InvitationLog");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Admin)
                      .WithMany()
                      .HasForeignKey(e => e.AdminId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Student)
                      .WithMany()
                      .HasForeignKey(e => e.StudentId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}

