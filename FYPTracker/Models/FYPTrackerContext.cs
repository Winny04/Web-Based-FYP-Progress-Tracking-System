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
        public virtual DbSet<EmployeeInfo>      EmployeeInfos      { get; set; } = null!;
        public virtual DbSet<Department>        Department         { get; set; } = null!;
        public virtual DbSet<AuditTrail>        AuditTrail         { get; set; } = null!;
        public virtual DbSet<AuditTag>          AuditTag           { get; set; } = null!;
        public virtual DbSet<EmailTemplateConfig> EmailTemplateConfig { get; set; } = null!;

        // ── FYP Core ───────────────────────────────────────────────
        public virtual DbSet<FYPProject>        FYPProjects        { get; set; } = null!;
        public virtual DbSet<FYPMilestone>      FYPMilestones      { get; set; } = null!;
        public virtual DbSet<FYPFeedback>       FYPFeedbacks       { get; set; } = null!;
        public virtual DbSet<FYPSubmissionFile> FYPSubmissionFiles { get; set; } = null!;

        // ── Student Module ─────────────────────────────────────────
        public virtual DbSet<SupervisorRequest> SupervisorRequests { get; set; } = null!;
        public virtual DbSet<MeetingBooking>    MeetingBookings    { get; set; } = null!;
        public virtual DbSet<FYPNotification>   FYPNotifications   { get; set; } = null!;
        public virtual DbSet<FYPActivity>       FYPActivities      { get; set; } = null!;

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

            // EmployeeInfo → Department FK
            modelBuilder.Entity<EmployeeInfo>(entity =>
            {
                entity.HasOne(e => e.EmpDepartment)
                      .WithMany(d => d.EmployeeInfo)
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

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
