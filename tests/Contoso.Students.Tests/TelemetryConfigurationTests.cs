using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class TelemetryConfigurationTests
{
    [TestMethod]
    public async Task ProductionRequiresApplicationInsightsConnectionString()
    {
        await using var app = new ProductionFactory();
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => _ = app.Services);
        StringAssert.Contains(exception.Message, "APPLICATIONINSIGHTS_CONNECTION_STRING");
    }

    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:Audience"] = "api://test",
                    ["ConnectionStrings:Students"] = "Server=unused;Database=unused",
                    ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = ""
                }));
        }
    }
}
