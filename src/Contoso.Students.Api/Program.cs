using Contoso.Students.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<StudentsDbContext>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<IConfiguration>().GetConnectionString("Students")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["Entra:TenantId"]}/v2.0";
    options.Audience = builder.Configuration["Entra:Audience"];
    options.MapInboundClaims = false;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("students-read", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => StudentAuthorization.CanRead(context.User)));

var app = builder.Build();
if (!Guid.TryParse(app.Configuration["Entra:TenantId"], out _)
    || string.IsNullOrWhiteSpace(app.Configuration["Entra:Audience"]))
    throw new InvalidOperationException("Set Entra:TenantId and Entra:Audience to the configured API registration.");
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Students")))
    throw new InvalidOperationException("Set ConnectionStrings:Students to the service-owned SQL database.");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/api/v1/students/{id:int}", async (int id, StudentsDbContext db, CancellationToken cancellationToken) =>
{
    var student = await db.Students.AsNoTracking()
        .Where(s => s.Id == id)
        .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.EnrollmentDate))
        .SingleOrDefaultAsync(cancellationToken);
    return student is null ? Results.NotFound() : Results.Ok(student);
}).RequireAuthorization("students-read");

app.Run();

public partial class Program;
