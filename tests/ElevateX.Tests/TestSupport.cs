using ElevateX.Core.Data;
using ElevateX.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElevateX.Tests;

internal static class TestSupport
{
    /// <summary>
    /// A fresh in-memory SQLite database with the real schema. The caller owns the
    /// returned connection and must keep it open for the lifetime of the context.
    /// </summary>
    public static AppDbContext NewDb(out SqliteConnection connection)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static IOptions<VirusTotalOptions> Opt(Action<VirusTotalOptions>? configure = null)
    {
        var options = new VirusTotalOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}
