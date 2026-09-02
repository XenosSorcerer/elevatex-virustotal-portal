using ElevateX.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElevateX.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FileAnalysis> FileAnalyses => Set<FileAnalysis>();
    public DbSet<Submission> Submissions => Set<Submission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FileAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Sha256).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastScannedAtUtc);

            entity.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Md5).HasMaxLength(32);
            entity.Property(e => e.Sha1).HasMaxLength(40);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(260);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.CurrentStage).HasConversion<string>().HasMaxLength(30);

            entity.HasMany(e => e.Submissions)
                  .WithOne(s => s.FileAnalysis)
                  .HasForeignKey(s => s.FileAnalysisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Submission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SubmittedAtUtc);
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.ThreatPriority);

            entity.Property(e => e.Source).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ThreatPriority).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.ReasonForSuspicion).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.TargetDepartment).HasMaxLength(100);
        });
    }
}
