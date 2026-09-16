using ElevateX.Core.Entities;
using ElevateX.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace ElevateX.Core.Data;

public class AppDbContext : DbContext
{
    // Optional so existing test helpers that construct AppDbContext directly (no DI) keep compiling
    // unchanged; the running app resolves the real singleton via DI.
    private readonly IScanNotifier? _notifier;

    public AppDbContext(DbContextOptions<AppDbContext> options, IScanNotifier? notifier = null) : base(options)
    {
        _notifier = notifier;
    }

    public DbSet<FileAnalysis> FileAnalyses => Set<FileAnalysis>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ApiCall> ApiCalls => Set<ApiCall>();

    /// <summary>
    /// Single choke point for FR-12 real-time updates: snapshot which FileAnalysis/Submission rows are
    /// about to be created or have a status change, save, and only publish once the save has actually
    /// succeeded. Keeps every pipeline/dispatcher/submission call site completely unaware of this.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var pending = _notifier is null ? null : CollectPendingScanEvents();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pending is { Count: > 0 })
        {
            foreach (var scanEvent in pending)
                _notifier!.Publish(scanEvent);
        }

        return result;
    }

    private List<ScanEvent> CollectPendingScanEvents()
    {
        var events = new List<ScanEvent>();

        foreach (var entry in ChangeTracker.Entries<FileAnalysis>())
        {
            var isNew = entry.State == EntityState.Added;
            var statusChanged = entry.State == EntityState.Modified &&
                                 entry.Property(nameof(FileAnalysis.Status)).IsModified;

            if (isNew || statusChanged)
                events.Add(new ScanEvent(entry.Entity.Id, entry.Entity.Status, IsNewSubmission: false));
        }

        foreach (var entry in ChangeTracker.Entries<Submission>())
        {
            if (entry.State == EntityState.Added)
                events.Add(new ScanEvent(entry.Entity.FileAnalysisId, AnalysisStatus.Queued, IsNewSubmission: true));
        }

        return events;
    }

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

        modelBuilder.Entity<ApiCall>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OccurredAtUtc);

            entity.Property(e => e.EndpointKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        });
    }
}
