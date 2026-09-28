using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobSuites.Api.Models;

namespace JobSuites.Api.Services;

/// <summary>
/// Identifies the profile inputs a match score actually depends on.
///
/// RoleMatch.ProfileHash was originally the CV text hash, which broke the moment
/// profiles became editable: changing a skill would change every future match
/// while the reuse check kept "proving" the old scores were still valid. The hash
/// here is a digest of exactly the fields MatchEngine reads, so an edit that
/// changes a score invalidates the score, and a cosmetic edit (a photo, a link)
/// is correctly free.
/// </summary>
public static class ProfileMatchHash
{
    // Serialization must be deterministic across calls, so the property
    // ordering is fixed by the anonymous type and casing by the policy.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Compute(CandidateProfile p)
    {
        var payload = JsonSerializer.Serialize(new
        {
            skills = p.Skills,
            experiences = p.Experiences,
            yearsExperience = p.YearsExperience,
            location = p.Location,
            headline = p.Headline,
        }, Json);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}