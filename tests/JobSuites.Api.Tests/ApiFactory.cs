using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobSuites.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobSuites.Api.Tests;

/// <summary>
/// Boots the real API in-process against a throwaway database, so the tests
/// exercise routing, model binding, validation, the unique index and the JWT
/// pipeline exactly as production does. Nothing meaningful is mocked — a passing
/// suite means the slice genuinely works end to end.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbName = $"jobsuites_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Only runtime-read config belongs here. Anything Program.cs reads
            // at startup is already bound by the time this callback runs, so
            // overriding Jwt:Key here would sign with one key and verify with
            // another. The real dev key is used instead.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(svc =>
        {
            var descriptors = svc
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || (d.ServiceType.FullName?.StartsWith("Npgsql") ?? false))
                .ToList();

            foreach (var d in descriptors) svc.Remove(d);

            svc.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));
        });
    }

    /// <summary>
    /// Resolves the local Postgres connection, preferring an explicit env var and
    /// otherwise reading the API project's gitignored dev config. The credential
    /// itself is never committed — see appsettings.Development.json.example.
    /// </summary>
    private static string ResolveLocalConnectionString()
    {
        if (Environment.GetEnvironmentVariable("JOBSUITES_TEST_CONNECTION") is { Length: > 0 } fromEnv)
        {
            return fromEnv;
        }

        // Walk up from the test binary until we find the API project config.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "JobSuites.Api", "appsettings.Development.json");

            if (File.Exists(candidate))
            {
                return new ConfigurationBuilder()
                    .AddJsonFile(candidate, optional: false)
                    .Build()
                    .GetConnectionString("Default")
                    ?? throw new InvalidOperationException("No connection string found.");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the local Postgres connection. Set JOBSUITES_TEST_CONNECTION, " +
            "or copy appsettings.Development.json.example to appsettings.Development.json " +
            "in src/JobSuites.Api.");
    }

    public async Task InitializeAsync()
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(ResolveLocalConnectionString())
        {
            Database = _dbName,
        };
        _connectionString = csb.ConnectionString;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        catch
        {
            // A leftover test database must never fail a test run.
        }

        await base.DisposeAsync();
    }

    /// <summary>Empties user profiles and ingested roles between tests without
    /// recreating the schema. Roles and matches must go too, otherwise the order
    /// tests run in would leak scores into later tests.
    ///
    /// <c>sources</c> is in the list and is not per-user: its zero-run counters
    /// and health status accumulate across runs, so a test that leaves a degraded
    /// source behind would silently degrade every later test's dashboard.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE \"Users\", \"candidate_profiles\", \"roles\", \"role_postings\", " +
            "\"jd_requirements\", \"role_matches\", \"ingest_runs\", \"sources\", " +
            "\"crawl_targets\" CASCADE;");
    }
}

public static class HttpExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text)
            ? default
            : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> PostJsonAsync(
        this HttpClient c, string url, object payload)
    {
        var res = await c.PostAsJsonAsync(url, payload);
        return (res.StatusCode, await res.ReadJsonAsync());
    }
}
