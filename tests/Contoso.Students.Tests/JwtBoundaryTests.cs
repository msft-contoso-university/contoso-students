using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Contoso.Students.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Contoso.Students.Tests;

[TestClass]
public sealed class JwtBoundaryTests
{
    [TestMethod]
    public async Task ValidatedJwtMustHaveCorrectAudienceAndPermissions()
    {
        await using var app = new JwtFactory();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
            db.Students.Add(new Student { Id = 1, FirstName = "Ana", LastName = "Example", EnrollmentDate = new DateTime(2020, 9, 1) });
            await db.SaveChangesAsync();
        }
        using var client = app.CreateClient();
        async Task<HttpStatusCode> Request(string audience, params Claim[] claims)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Token(audience, claims));
            return (await client.GetAsync("/api/v1/students/1")).StatusCode;
        }

        Assert.AreEqual(HttpStatusCode.OK, await Request("api://test", new Claim("scp", "students.read"), new Claim("roles", "Reader")));
        Assert.AreEqual(HttpStatusCode.OK, await Request("api://test", new Claim("idtyp", "app"), new Claim("roles", "Students.Lookup")));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/students")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/students/search?name=Ana")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, await Request("api://test", new Claim("roles", "Reader")));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Request("api://other", new Claim("scp", "students.read"), new Claim("roles", "Reader")));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ForgedToken());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/students/1")).StatusCode);
    }

    private sealed class JwtFactory : WebApplicationFactory<Program>
    {
        private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));

        public string Token(string audience, Claim[] claims)
        {
            var token = new JwtSecurityToken(
                issuer: "https://issuer.example",
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string ForgedToken()
        {
            var token = new JwtSecurityToken(
                issuer: "https://issuer.example",
                audience: "api://test",
                claims: [new Claim("scp", "students.read"), new Claim("roles", "Reader")],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

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
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example", SigningKeys = { key } };
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = "https://issuer.example",
                        ValidateAudience = true,
                        ValidAudience = "api://test",
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });
            });
        }
    }
}
