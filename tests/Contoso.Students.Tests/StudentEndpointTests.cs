using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Contoso.Students.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class StudentEndpointTests
{
    [TestMethod]
    public async Task StudentLookupEnforcesAuthorizationAndReturnsStoredRecord()
    {
        await using var app = new StudentsFactory();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
            db.Students.Add(new Student { Id = 1, FirstName = "Ana", LastName = "Example" });
            await db.SaveChangesAsync();
        }
        using var client = app.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/students/1")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Identity", "no-role");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/students/1")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Identity");
        client.DefaultRequestHeaders.Add("X-Test-Identity", "reader");
        var response = await client.GetAsync("/api/v1/students/1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var student = await response.Content.ReadFromJsonAsync<StudentResponse>();
        Assert.IsNotNull(student);
        Assert.AreEqual("Ana", student.FirstName);
        await CaptureResponseAsync(response, "Student", "student-lookup.json");
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/students/2")).StatusCode);
    }

    [TestMethod]
    public async Task RegistrarCanCreateAndUpdateButReaderCannotWrite()
    {
        await using var app = new StudentsFactory();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Identity", "reader");
        var input = new CreateStudentRequest("Ana", "Example", new DateTime(2020, 1, 1));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/students", input)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Identity");
        client.DefaultRequestHeaders.Add("X-Test-Identity", "registrar");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/students", input with { FirstName = "" })).StatusCode);
        var created = await client.PostAsJsonAsync("/api/v1/students", input);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var student = await created.Content.ReadFromJsonAsync<StudentResponse>();
        Assert.IsNotNull(student);
        await CaptureResponseAsync(created, "Student", "student-created.json");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            $"/api/v1/students/{student.Id}", new UpdateStudentRequest(student.Id + 1, "Ana", "Example", input.EnrollmentDate))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            "/api/v1/students/99", new UpdateStudentRequest(99, "Ana", "Example", input.EnrollmentDate))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/v1/students/{student.Id}", new UpdateStudentRequest(student.Id, "Anita", "Example", input.EnrollmentDate))).StatusCode);
        var pageResponse = await client.GetAsync("/api/v1/students?page=0");
        Assert.AreEqual(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<StudentPage>();
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.CurrentPage);
        Assert.AreEqual("Anita", page.Students[0].FirstName);
        await CaptureResponseAsync(pageResponse, "StudentPage", "students-page.json");
        var matches = await client.GetFromJsonAsync<List<StudentResponse>>("/api/v1/students/search?name=An");
        Assert.IsNotNull(matches);
        Assert.HasCount(1, matches);
        var farPageResponse = await client.GetAsync("/api/v1/students?page=2147483647");
        Assert.AreEqual(HttpStatusCode.OK, farPageResponse.StatusCode);
        var farPage = await farPageResponse.Content.ReadFromJsonAsync<StudentPage>();
        Assert.IsNotNull(farPage);
        Assert.IsEmpty(farPage.Students);
        await CaptureResponseAsync(farPageResponse, "StudentPage", "students-empty-page.json");
    }

    private static async Task CaptureResponseAsync(HttpResponseMessage response, string schema, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("CONTOSO_PRODUCER_SAMPLES");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Directory.CreateDirectory(directory);
        var envelope = new { schema, body = body.RootElement };
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), JsonSerializer.Serialize(envelope));
    }

    private sealed class StudentsFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var databaseName = Guid.NewGuid().ToString();
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:Audience"] = "api://test",
                    ["ConnectionStrings:Students"] = "Server=unused;Database=unused"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<StudentsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<StudentsDbContext>>();
                services.RemoveAll<StudentsDbContext>();
                services.AddDbContext<StudentsDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "test";
                    options.DefaultChallengeScheme = "test";
                    options.DefaultForbidScheme = "test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrEmpty(identity))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("scp", identity == "registrar" ? "students.read students.write" : "students.read") };
            if (identity == "reader") claims.Add(new Claim("roles", "Reader"));
            if (identity == "registrar") claims.Add(new Claim("roles", "Registrar"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
