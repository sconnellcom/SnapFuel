using FuelImport.Core.Models;
using FuelImport.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FuelImport.Tests;

public class PersistenceIdempotencyTests
{
    [Fact]
    public async Task SourceImage_FileHashIsUnique()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FuelImportDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var setup = new FuelImportDbContext(options);
        await setup.Database.EnsureCreatedAsync();

        await using var db = new FuelImportDbContext(options);
        db.SourceImages.Add(new SourceImage { FilePath = "/tmp/a.jpg", FileName = "a.jpg", FileHash = "abc" });
        await db.SaveChangesAsync();

        db.SourceImages.Add(new SourceImage { FilePath = "/tmp/b.jpg", FileName = "b.jpg", FileHash = "abc" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
