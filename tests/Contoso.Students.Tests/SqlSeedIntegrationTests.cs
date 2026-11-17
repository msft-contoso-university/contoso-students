using Contoso.Students.Api;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class SqlSeedIntegrationTests
{
    [TestMethod]
    public async Task MigrationAndSeedAreRepeatableAndNeverOverwriteExistingRecords()
    {
        var connectionString = Environment.GetEnvironmentVariable("STUDENTS_SQL_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("Set STUDENTS_SQL_TEST_CONNECTION_STRING only for a disposable SQL test server.");

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false };
        var databaseName = $"StudentsCi_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(master.ConnectionString);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await connection.OpenAsync();
                break;
            }
            catch (SqlException) when (attempt < 59)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        await new SqlCommand($"CREATE DATABASE [{databaseName}]", connection).ExecuteNonQueryAsync();
        try
        {
            var database = new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = databaseName };
            var options = new DbContextOptionsBuilder<StudentsDbContext>().UseSqlServer(database.ConnectionString).Options;
            await using var db = new StudentsDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261117165342_InitialStudents");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO dbo.Students (FirstName, LastName, EnrollmentDate) VALUES ('Invalid', 'Date', '0001-01-01T00:00:00')");
            await Assert.ThrowsExactlyAsync<SqlException>(() => migrator.MigrateAsync());
            Assert.AreEqual(1, await db.Students.CountAsync());
            await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Students WHERE FirstName = 'Invalid'");
            await RunMaintenance(database.ConnectionString, "--migrate");
            await Assert.ThrowsExactlyAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO dbo.Students (FirstName, LastName, EnrollmentDate) VALUES ('Invalid', 'Date', '0001-01-01T00:00:00')"));
            var seed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seed-students.sql"));

            db.Students.Add(new Student { FirstName = "Existing", LastName = "Record", EnrollmentDate = new DateTime(2020, 9, 1) });
            await db.SaveChangesAsync();
            await Assert.ThrowsExactlyAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(seed));
            Assert.AreEqual(1, await db.Students.CountAsync());

            await db.Students.ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            await RunMaintenance(database.ConnectionString, "--migrate", "--seed-demo");
            Assert.AreEqual(3, await db.Students.CountAsync());
            var first = await db.Students.SingleAsync(student => student.Id == 1);
            first.FirstName = "Changed";
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(seed);
            Assert.AreEqual(3, await db.Students.CountAsync());
            Assert.AreEqual("Changed", (await db.Students.AsNoTracking().SingleAsync(student => student.Id == 1)).FirstName);
        }
        finally
        {
            await new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", connection)
                .ExecuteNonQueryAsync();
        }
    }

    private static async Task RunMaintenance(string connectionString, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ConnectionStrings__Students"] = connectionString;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start migration process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        Assert.AreEqual(0, process.ExitCode, $"Migration process failed: {await stdout}\n{await stderr}");
    }
}
