using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobSuites.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobSuites.Api.Tests;

/// <summary>Sprint 1 acceptance criteria, verified against the real pipeline.</summary>
public class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string GoodPassword = "correct-horse-battery";

    [Fact]
    public async Task Register_returns_token_and_normalises_email()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var (status, body) = await client.PostJsonAsync("/api/auth/register", new
        {
            email = "  Samuel@Example.COM ",
            fullName = "Samuel Fehintolu",
            password = GoodPassword,
        });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
        // Whitespace and casing must not create two distinct accounts.
        Assert.Equal("samuel@example.com", body.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Register_never_returns_the_password_hash()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var (_, body) = await client.PostJsonAsync("/api/auth/register", new
        {
            email = "hash-check@example.com",
            fullName = "Hash Check",
            password = GoodPassword,
        });

        Assert.False(body.TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain(
            "passwordHash",
            body.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("alllettersonly")]
    [InlineData("1234567890")]
    public async Task Register_rejects_weak_passwords(string password)
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var (status, body) = await client.PostJsonAsync("/api/auth/register", new
        {
            email = "weak@example.com",
            fullName = "Weak Password",
            password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    [InlineData("")]
    public async Task Register_rejects_malformed_email(string email)
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var (status, body) = await client.PostJsonAsync("/api/auth/register", new
        {
            email,
            fullName = "Bad Email",
            password = GoodPassword,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_by_the_database_index()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();
        var payload = new { email = "dupe@example.com", fullName = "First", password = GoodPassword };

        var (first, _) = await client.PostJsonAsync("/api/auth/register", payload);
        var (second, body) = await client.PostJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.Created, first);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Contains("already exists", body.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_registration_of_the_same_email_yields_exactly_one_account()
    {
        await factory.ResetAsync();

        // The pre-check alone cannot prevent this race; only the unique index can.
        var tasks = Enumerable.Range(0, 8).Select(async i =>
        {
            var client = factory.CreateClient();
            return await client.PostJsonAsync("/api/auth/register", new
            {
                email = "race@example.com",
                fullName = $"Racer {i}",
                password = GoodPassword,
            });
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.Created));
        Assert.Equal(7, results.Count(r => r.Status == HttpStatusCode.Conflict));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "race@example.com"));
    }

    [Fact]
    public async Task Login_issues_a_token_that_authenticates_subsequent_requests()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        await client.PostJsonAsync("/api/auth/register", new
        {
            email = "login@example.com",
            fullName = "Login User",
            password = GoodPassword,
        });

        var (status, body) = await client.PostJsonAsync("/api/auth/login", new
        {
            email = "LOGIN@example.com",
            password = GoodPassword,
        });

        Assert.Equal(HttpStatusCode.OK, status);
        var token = body.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/auth/me");
        var meBody = await me.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Login User", meBody.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Login_uses_one_message_for_unknown_email_and_wrong_password()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var (unknownStatus, unknown) = await client.PostJsonAsync("/api/auth/login", new
        {
            email = "ghost@example.com",
            password = GoodPassword,
        });

        var (wrongStatus, wrong) = await client.PostJsonAsync("/api/auth/login", new
        {
            email = "login@example.com",
            password = "wrong-password-here",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, unknownStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongStatus);
        Assert.Equal(
            unknown.GetProperty("detail").GetString(),
            wrong.GetProperty("detail").GetString());

        // Explicitly guard against enumeration wording creeping back in.
        var text = unknown.GetProperty("detail").GetString()!.ToLowerInvariant();
        Assert.DoesNotContain("no such user", text);
        Assert.DoesNotContain("not registered", text);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_rejects_a_forged_token()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJoYWNrZXIifQ.bad-signature");

        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Health_is_public()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
