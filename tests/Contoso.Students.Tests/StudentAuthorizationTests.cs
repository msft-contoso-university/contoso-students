using System.Security.Claims;
using Contoso.Students.Api;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class StudentAuthorizationTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [TestMethod]
    public void DelegatedUserNeedsBothReadScopeAndReaderOrRegistrarRole()
    {
        Assert.IsTrue(StudentAuthorization.CanRead(Principal(
            new Claim("scp", "openid students.read"), new Claim("roles", "Reader"))));
        Assert.IsTrue(StudentAuthorization.CanRead(Principal(
            new Claim("scp", "students.read"), new Claim("roles", "Registrar"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(new Claim("roles", "Reader"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(new Claim("scp", "students.read"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(
            new Claim("scp", "students.read.all"), new Claim("roles", "Reader"))));
    }

    [TestMethod]
    public void AppOnlyLookupRequiresExplicitAppIdentityAndNarrowRole()
    {
        Assert.IsTrue(StudentAuthorization.CanRead(Principal(
            new Claim("idtyp", "app"), new Claim("roles", "Students.Lookup"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(new Claim("roles", "Students.Lookup"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(
            new Claim("idtyp", "app"), new Claim("roles", "Students.Lookup"), new Claim("scp", "other"))));
        Assert.IsFalse(StudentAuthorization.CanRead(Principal(
            new Claim("idtyp", "app"), new Claim("roles", "Registrar"))));
    }
}
