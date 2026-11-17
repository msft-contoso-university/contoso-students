using ContosoUniversity.API.Controllers;
using ContosoUniversity.API.Data;
using ContosoUniversity.API.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentEntity = ContosoUniversity.API.Models.Student;

namespace Contoso.Students.LegacyCharacterization;

[TestClass]
public sealed class StudentControllerTests
{
    private static ContosoUniversityAPIContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ContosoUniversityAPIContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ContosoUniversityAPIContext(options);
    }

    [TestMethod]
    public async Task ListDefaultsToFirstPageAndOrdersById()
    {
        await using var db = CreateContext();
        for (var id = 55; id >= 1; id--)
            db.Student.Add(new StudentEntity { ID = id, FirstName = $"Student{id}", LastName = "Example" });
        await db.SaveChangesAsync();

        var action = await new StudentsController(db).GetStudent(page: 0);
        var result = (StudentCourseResult)((OkObjectResult)action).Value;

        Assert.AreEqual(55, result.Count);
        Assert.AreEqual(2, result.Pages);
        Assert.AreEqual(1, result.CurrentPage);
        Assert.HasCount(50, result.Students);
        Assert.AreEqual(1, result.Students[0].ID);
        Assert.AreEqual(50, result.Students[49].ID);
    }

    [TestMethod]
    public async Task SearchReturnsPrefixMatchesAndEmptyCollectionForNoMatches()
    {
        await using var db = CreateContext();
        db.Student.AddRange(
            new StudentEntity { ID = 1, FirstName = "Anna", LastName = "Lee" },
            new StudentEntity { ID = 2, FirstName = "Brian", LastName = "Anders" },
            new StudentEntity { ID = 3, FirstName = "Cara", LastName = "Doe" });
        await db.SaveChangesAsync();
        var controller = new StudentsController(db);

        var matching = (StudentCourseResult)((OkObjectResult)await controller.Search("An")).Value;
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, matching.Students.Select(s => s.ID).ToArray());
        var missing = (StudentCourseResult)((OkObjectResult)await controller.Search("Zed")).Value;
        Assert.IsEmpty(missing.Students);
    }

    [TestMethod]
    public async Task MissingRecordReturnsNotFoundAndMismatchedUpdateReturnsBadRequest()
    {
        await using var db = CreateContext();
        var controller = new StudentsController(db);

        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetStudent(42));
        Assert.IsInstanceOfType<NotFoundResult>(await controller.DeleteStudent(42));
        Assert.IsInstanceOfType<BadRequestResult>(await controller.PutStudent(42, new StudentEntity { ID = 43 }));
    }

    [TestMethod]
    public async Task DeleteReturnsDeletedStudentAndRemovesIt()
    {
        await using var db = CreateContext();
        db.Student.Add(new StudentEntity { ID = 7, FirstName = "Alex", LastName = "Example" });
        await db.SaveChangesAsync();

        var result = (OkObjectResult)await new StudentsController(db).DeleteStudent(7);

        Assert.AreEqual(7, ((StudentEntity)result.Value).ID);
        Assert.IsNull(await db.Student.FindAsync(7));
    }
}
