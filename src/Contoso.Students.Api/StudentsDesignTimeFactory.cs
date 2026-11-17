using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contoso.Students.Api;

public sealed class StudentsDesignTimeFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StudentsDbContext>()
            .UseSqlServer("Server=localhost;Database=ContosoStudentsDesignTime;Integrated Security=true")
            .Options;
        return new StudentsDbContext(options);
    }
}
