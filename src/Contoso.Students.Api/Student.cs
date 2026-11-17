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

public sealed record StudentResponse(int Id, string FirstName, string LastName, DateTime EnrollmentDate);

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>(student =>
        {
            student.ToTable("Students");
            student.HasKey(s => s.Id);
            student.Property(s => s.FirstName).IsRequired();
            student.Property(s => s.LastName).IsRequired();
        });
    }
}

public static class StudentAuthorization
{
    public static bool CanRead(ClaimsPrincipal principal)
    {
        var scopes = principal.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var delegated = scopes.Contains("students.read", StringComparer.Ordinal)
            && (principal.HasClaim("roles", "Reader") || principal.HasClaim("roles", "Registrar"));
        var service = !principal.HasClaim(claim => claim.Type == "scp")
            && principal.HasClaim("idtyp", "app")
            && principal.HasClaim("roles", "Students.Lookup");
        return delegated || service;
    }
}
