using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Students.Api;

public sealed class Student
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateTime EnrollmentDate { get; set; }
}

public sealed record StudentResponse(int Id, string FirstName, string LastName, DateTime EnrollmentDate)
{
    public DateTime EnrollmentDate { get; init; } = DateTime.SpecifyKind(EnrollmentDate, DateTimeKind.Utc);
}
public sealed record StudentPage(IReadOnlyList<StudentResponse> Students, int Count, int Pages, int CurrentPage);
public sealed record CreateStudentRequest(string? FirstName, string? LastName, DateTimeOffset? EnrollmentDate);
public sealed record UpdateStudentRequest(int? Id, string? FirstName, string? LastName, DateTimeOffset? EnrollmentDate);

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>(student =>
        {
            student.ToTable("Students", table => table.HasCheckConstraint(
                "CK_Students_EnrollmentDate_NotDefault", "[EnrollmentDate] > '0001-01-01T00:00:00'"));
            student.HasKey(s => s.Id);
            student.Property(s => s.FirstName).IsRequired();
            student.Property(s => s.LastName).IsRequired();
        });
    }
}

public static class StudentAuthorization
{
    public static bool CanRead(ClaimsPrincipal principal) =>
        !principal.HasClaim("idtyp", "app")
            && HasScope(principal, "students.read")
            && (principal.HasClaim("roles", "Reader") || principal.HasClaim("roles", "Registrar"));

    public static bool CanLookup(ClaimsPrincipal principal) =>
        CanRead(principal) || (!principal.HasClaim(claim => claim.Type == "scp")
            && principal.HasClaim("idtyp", "app")
            && principal.HasClaim("roles", "Students.Lookup"));

    public static bool CanWrite(ClaimsPrincipal principal) =>
        !principal.HasClaim("idtyp", "app")
        && HasScope(principal, "students.write")
        && principal.HasClaim("roles", "Registrar");

    private static bool HasScope(ClaimsPrincipal principal, string scope) =>
        principal.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
