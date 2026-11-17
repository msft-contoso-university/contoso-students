using Contoso.Students.Api;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;

var maintenance = args.Contains("--migrate", StringComparer.Ordinal) || args.Contains("--seed-demo", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(maintenance ? [] : args);
if (maintenance)
{
    if (args.Count(arg => arg == "--migrate") != 1
        || args.Count(arg => arg == "--seed-demo") > 1
        || args.Any(arg => arg is not "--migrate" and not "--seed-demo"))
        throw new InvalidOperationException("Use --migrate, optionally followed by --seed-demo.");
    var connectionString = builder.Configuration.GetConnectionString("Students");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Set ConnectionStrings:Students for the migration identity.");
    var options = new DbContextOptionsBuilder<StudentsDbContext>().UseSqlServer(connectionString).Options;
    await using var database = new StudentsDbContext(options);
    await database.Database.MigrateAsync();
    if (args.Contains("--seed-demo", StringComparer.Ordinal))
    {
        var seed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seed-students.sql"));
        await database.Database.ExecuteSqlRawAsync(seed);
    }
    return;
}
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    var insightsConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    if (string.IsNullOrWhiteSpace(insightsConnection))
        throw new InvalidOperationException("Set APPLICATIONINSIGHTS_CONNECTION_STRING for Azure Monitor.");
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("contoso-students"))
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = insightsConnection;
            options.Credential = new DefaultAzureCredential();
        });
}
builder.Services.AddDbContext<StudentsDbContext>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<IConfiguration>().GetConnectionString("Students")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
    options.Audience = builder.Configuration["AzureAd:Audience"];
    options.MapInboundClaims = false;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("students-read", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => StudentAuthorization.CanRead(context.User)))
    .AddPolicy("students-lookup", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => StudentAuthorization.CanLookup(context.User)))
    .AddPolicy("students-write", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => StudentAuthorization.CanWrite(context.User)));

var app = builder.Build();
if (!Guid.TryParse(app.Configuration["AzureAd:TenantId"], out _)
    || string.IsNullOrWhiteSpace(app.Configuration["AzureAd:Audience"]))
    throw new InvalidOperationException("Set AzureAd:TenantId and AzureAd:Audience to the configured API registration.");
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Students")))
    throw new InvalidOperationException("Set ConnectionStrings:Students to the service-owned SQL database.");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/api/v1/students", async (int? page, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    const int pageSize = 50;
    var currentPage = page is > 0 ? page.Value : 1;
    var count = await db.Students.CountAsync(cancellationToken);
    var offset = ((long)currentPage - 1) * pageSize;
    var students = offset > int.MaxValue
        ? []
        : await db.Students.AsNoTracking().OrderBy(s => s.Id).Skip((int)offset).Take(pageSize)
            .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.EnrollmentDate))
            .ToListAsync(cancellationToken);
    return Results.Ok(new StudentPage(students, count, (int)Math.Ceiling(count / (double)pageSize), currentPage));
}).RequireAuthorization("students-read");

app.MapGet("/api/v1/students/search", async (string name, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    var students = await db.Students.AsNoTracking()
        .Where(s => EF.Functions.Like(s.FirstName, name + "%") || EF.Functions.Like(s.LastName, name + "%"))
        .OrderBy(s => s.Id)
        .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.EnrollmentDate))
        .ToListAsync(cancellationToken);
    return Results.Ok(students);
}).RequireAuthorization("students-read");

app.MapGet("/api/v1/students/{id:int}", async (int id, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    var student = await db.Students.AsNoTracking()
        .Where(s => s.Id == id)
        .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.EnrollmentDate))
        .SingleOrDefaultAsync(cancellationToken);
    return student is null ? Results.NotFound() : Results.Ok(student);
}).RequireAuthorization("students-lookup");

app.MapPost("/api/v1/students", async (CreateStudentRequest request, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        return Results.BadRequest("FirstName and LastName are required.");
    var student = new Student { FirstName = request.FirstName, LastName = request.LastName, EnrollmentDate = request.EnrollmentDate };
    db.Students.Add(student);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/students/{student.Id}",
        new StudentResponse(student.Id, student.FirstName, student.LastName, student.EnrollmentDate));
}).RequireAuthorization("students-write");

app.MapPut("/api/v1/students/{id:int}", async (int id, UpdateStudentRequest request, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    if (id != request.Id || string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        return Results.BadRequest("Matching ID, FirstName and LastName are required.");
    var student = await db.Students.FindAsync([id], cancellationToken);
    if (student is null) return Results.NotFound();
    student.FirstName = request.FirstName;
    student.LastName = request.LastName;
    student.EnrollmentDate = request.EnrollmentDate;
    try
    {
        await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == id, cancellationToken))
            return Results.NotFound();
        throw;
    }
    return Results.NoContent();
}).RequireAuthorization("students-write");

app.Run();

public partial class Program;
