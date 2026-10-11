using BudgetApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BudgetApp.Tests;

/// <summary>Runs the real migrations: goals completed before CompletedAt existed get a sensible date.</summary>
public class GoalCompletedAtMigrationTests
{
    private const string BeforeCompletedAt = "20261007145639_AddCategoryIsFixed";

    [Fact]
    public void Migration_BackfillsCompletedAtForGoalsThatWereAlreadyCompleted()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();

        // The database as it was before the column existed, with three goals.
        migrator.Migrate(BeforeCompletedAt);
        db.Database.ExecuteSqlRaw(@"
            INSERT INTO Goals (Id, UserId, Name, TargetAmount, CreatedAt, IsCompleted) VALUES
              (1, 'u', 'done-with-contributions', 500, '2026-08-01 00:00:00', 1),
              (2, 'u', 'done-without-contributions', 500, '2026-08-05 00:00:00', 1),
              (3, 'u', 'still-open', 500, '2026-09-01 00:00:00', 0);
            INSERT INTO GoalContributions (GoalId, Amount, Date, Source) VALUES
              (1, 200, '2026-09-02 00:00:00', 0),
              (1, 300, '2026-09-20 00:00:00', 1),
              (3, 100, '2026-09-25 00:00:00', 1);");

        migrator.Migrate(); // up to the latest migration

        string? CompletedAt(string name) => db.Database
            .SqlQuery<string?>($"SELECT CompletedAt AS Value FROM Goals WHERE Name = {name}")
            .AsEnumerable().Single();

        Assert.StartsWith("2026-09-20", CompletedAt("done-with-contributions"));      // the last contribution completed it
        Assert.StartsWith("2026-08-05", CompletedAt("done-without-contributions"));   // falls back to the creation date
        Assert.Null(CompletedAt("still-open"));
    }

    [Fact]
    public void Migration_LeavesNewGoalsOpenWithoutACompletionDate()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        using var db = new ApplicationDbContext(options);

        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("INSERT INTO Goals (UserId, Name, TargetAmount, CreatedAt, IsCompleted) VALUES ('u', 'g', 10, '2026-10-01 00:00:00', 0)");

        var goal = db.Goals.AsNoTracking().Single();
        Assert.False(goal.IsCompleted);
        Assert.Null(goal.CompletedAt);
    }
}
