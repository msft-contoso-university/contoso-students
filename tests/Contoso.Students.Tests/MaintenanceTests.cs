using System.Diagnostics;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class MaintenanceTests
{
    [TestMethod]
    public async Task SeedCannotRunWithoutExplicitMigration()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--seed-demo");
        start.Environment["ConnectionStrings__Students"] = "";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start maintenance process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreNotEqual(0, process.ExitCode);
        StringAssert.Contains($"{await stdout}\n{await stderr}", "Use --migrate");
    }
}
