using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElevateX.Core.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            // Auto-provisions the SQLite database file and tables on first run (FR-02)
            await db.Database.EnsureCreatedAsync();

            // Enable SQLite Write-Ahead Logging (WAL mode) to reduce write contention
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");

            logger.LogInformation("Database self-provisioned and WAL mode enabled successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while self-provisioning the SQLite database.");
            throw;
        }
    }
}
