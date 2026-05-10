using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace eForm_dotnetCore.Models
{
    public partial class EformSophicDemoContext : DbContext
    {
        public EformSophicDemoContext()
        {
        }

        public EformSophicDemoContext(DbContextOptions<EformSophicDemoContext> options)
            : base(options)
        {
        }
        public virtual DbSet<vBOMSupportFile> vBOMSupportFile { get; set; } = null!;
        public virtual DbSet<Bom> Boms { get; set; } = null!;
        public virtual DbSet<vBOM> vBOM { get; set; } = null!;
        public virtual DbSet<Category> Category { get; set; } = null!;
        public virtual DbSet<EmployeeInfo> EmployeeInfos { get; set; } = null!;
        public virtual DbSet<Form> Forms { get; set; } = null!;
        public virtual DbSet<Po> Pos { get; set; } = null!;
        public virtual DbSet<Pr> Prs { get; set; } = null!;
        public virtual DbSet<Project> Projects { get; set; } = null!;
        public virtual DbSet<vProject> vProject { get; set; } = null!;
        public virtual DbSet<Rfq> Rfqs { get; set; } = null!;
        public virtual DbSet<EmailTemplateConfig> EmailTemplateConfig { get; set; } = null!;
        public virtual DbSet<SupportFile> SupportFile { get; set; } = null!;
        public virtual DbSet<SupplierSupportFile> SupplierSupportFile { get; set; } = null!;
        public virtual DbSet<BomSupportFile> BomSupportFile { get; set; } = null!;
        public virtual DbSet<BomsendEmail> BomSendEmail { get; set; } = null!;
        public virtual DbSet<PRApprover> PRApprover { get; set; } = null!;
        public virtual DbSet<AuditTrail> AuditTrail { get; set; } = null!;
        public virtual DbSet<AuditTag> AuditTag { get; set; } = null!;
        public virtual DbSet<PurchaseHistory> PurchaseHistory { get; set; } = null!;
        public virtual DbSet<SupplierAttachment> SupplierAttachment { get; set; } = null!;
        //public virtual DbSet<POC> POC { get; set; } = null!;
        public virtual DbSet<SupplierInfo> SupplierInfo { get; set; } = null!;
        public virtual DbSet<Department> Department { get; set; } = null!;
        public virtual DbSet<DeliveryLocation> DeliveryLocation { get; set; } = null!;
        public virtual DbSet<BomInventory> BomInventory { get; set; } = null!;
        public virtual DbSet<TradeTracker> TradeTracker { get; set; } = null!;
        public virtual DbSet<RFQSharing> RFQSharing { get; set; } = null!;
        public virtual DbSet<Brand> Brand { get; set; } = null!;
        public virtual DbSet<BrandSupplier> BrandSupplier { get; set; } = null!;
        public virtual DbSet<Sourcing> Sourcing { get; set; } = null!;
        public virtual DbSet<Invoice> Invoices { get; set; } = null!;
        public virtual DbSet<ProjectPOFile> ProjectPOFiles { get; set; }

        public virtual DbSet<ProjectQFFile> ProjectQFFiles { get; set; }
        public virtual DbSet<AdditionalSupplier> AdditionalSuppliers { get; set; }



        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false);
                IConfiguration config = builder.Build();

                string dbConnString = config.GetValue<string>("ConnectionStrings:Entities");
                //string dbConn2 = _configuration.GetValue<string>("ConnectionStrings:Entities");
                //optionsBuilder.UseSqlServer(@"Server=sophicauto.ddns.net;Database=Eform-Sophic-Demo;User Id=izzati;Password=izzati");
                optionsBuilder.UseSqlServer(dbConnString,options =>
                {
                    options.CommandTimeout(80);
                });
                
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            OnModelCreatingPartial(modelBuilder);

            modelBuilder.Entity<Invoice>().ToTable("Invoice"); // Ensure correct table mapping


            modelBuilder.Entity<vBOM>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vBOM");
            });

            modelBuilder.Entity<vProject>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vProject");
            });

            modelBuilder.Entity<vBOMSupportFile>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vBOMSupportFile");
            });

            modelBuilder.Entity<ProjectPOFile>(entity =>
            {
                entity.ToTable("ProjectPOFile");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).HasMaxLength(255);
                entity.Property(e => e.FileData).HasMaxLength(255);
                entity.Property(e => e.CreatedDate);
                entity.Property(e => e.LastUpdatedDate);
                entity.Property(e => e.ProjectId);
                entity.Property(e => e.RequestorId).IsRequired();
                entity.HasOne(p => p.Project)
                      .WithMany(p => p.ProjectPOFiles)
                      .HasForeignKey(p => p.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProjectQFFile>(entity =>
            {
                entity.ToTable("ProjectQFFile");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).HasMaxLength(255);  
                entity.Property(e => e.FileData).HasMaxLength(255);
                entity.Property(e => e.CreatedDate);
                entity.Property(e => e.LastUpdatedDate);
                entity.Property(e => e.ProjectId);
                entity.Property(e => e.RequestorId).IsRequired();
                entity.HasOne(p => p.Project)
                      .WithMany(p => p.ProjectQFFiles)
                      .HasForeignKey(p => p.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
