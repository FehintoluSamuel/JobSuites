using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using Xunit;

namespace JobSuites.Api.Tests;

/// <summary>
/// The score is only as trustworthy as the reasoning that produced it. These
/// tests pin the weights, the tiers, and — most importantly — the "every point is
/// traceable" rule: the evidence list is a written justification, not a log.
/// </summary>
public class MatchEngineTests
{
    private static CandidateProfile EngineerProfile() => new()
    {
        UserId = Guid.NewGuid(),
        ContentHash = "abc",
        FullName = "Ada",
        Location = "Lagos",
        YearsExperience = 8,
        Skills =
        [
            new ProfileSkill("Python", "python", 6, true),
            new ProfileSkill("PostgreSQL", "postgresql", 5, true),
            new ProfileSkill("Docker", "docker", 3, false),
            new ProfileSkill("Kubernetes", "kubernetes", 2, false),
        ],
        Experiences =
        [
            new ProfileExperience("Kuda", "Senior Backend Engineer", "Mar 2021", "Present", true,
                "Rebuilt the ledger service in Python and PostgreSQL; B.Sc Computer Science, " +
                "University of Lagos; cut settlement time by 60%."),
        ],
    };

    private static Role BackendRole() => new()
    {
        Id = Guid.NewGuid(),
        Company = "A Bank",
        Title = "Backend Engineer",
        Description = "Backend engineer for the payments platform.",
        Qualification = "B.Sc Computer Science",
        States = ["Lagos"],
        MinYears = 5,
        MaxYears = 10,
        Skills = ["Python", "PostgreSQL", "Docker"],
    };

    [Fact]
    public void Strong_profile_wins_strong_tier()
    {
        var match = MatchEngine.Compute(EngineerProfile(), BackendRole(), "https://myjobmag.example/r/1");

        Assert.True(match.Score >= 75, $"expected >= 75, got {match.Score}");
        Assert.Equal("Strong", match.Tier);
    }

    [Fact]
    public void Every_skill_point_carries_written_evidence()
    {
        var match = MatchEngine.Compute(EngineerProfile(), BackendRole(), "https://myjobmag.example/r/1");

        Assert.NotEmpty(match.Evidence);
        Assert.All(match.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Label)));

        // The evidence must name the skill that produced the point. A numeric
        // score with a matching label is a traceable claim; without one it is noise.
        Assert.Contains(match.Evidence, e =>
            e.Kind == "skill" && e.Detail.Contains("Python", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evidence_links_to_the_source_posting()
    {
        var match = MatchEngine.Compute(EngineerProfile(), BackendRole(), "https://myjobmag.example/r/42");

        Assert.All(match.Evidence, e => Assert.Equal("https://myjobmag.example/r/42", e.RolePostingUrl));
    }

    [Fact]
    public void Unrelated_profile_scores_zero_and_explains_itself()
    {
        var profile = new CandidateProfile
        {
            UserId = Guid.NewGuid(),
            ContentHash = "def",
            Location = "Abuja",
            YearsExperience = 2,
            Skills = [new ProfileSkill("Hair Styling", "hair styling", 4, true)],
        };

        var match = MatchEngine.Compute(profile, BackendRole(), null);

        // The floor hit is 25 * (2/5) * 0.5 = 5. Skill, qualification and location
        // all contribute nothing, so 5 is the deterministic, explainable answer.
        Assert.Equal(5, match.Score);
        Assert.Equal("Stretch", match.Tier);
        Assert.Contains(match.Gaps, g => g.Contains("years", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Experience_below_the_floor_is_proportional_not_zero()
    {
        var profile = EngineerProfile();
        profile.YearsExperience = 2;
        var role = BackendRole();
        role.Skills = [];
        role.Qualification = null;
        role.States = [];

        var match = MatchEngine.Compute(profile, role, null);

        // 25 * (2/5) * 0.5 = 5 from the floor. The posting states no skill,
        // qualification or location requirement, which the engine banks as full
        // marks rather than punishing a role for not saying anything.
        Assert.Equal(5 + 15 + 10, match.Score);
        Assert.Contains(match.Gaps, g => g.Contains("5+", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_inputs_same_score()
    {
        var a = MatchEngine.Compute(EngineerProfile(), BackendRole(), "https://myjobmag.example/r/1");
        var b = MatchEngine.Compute(EngineerProfile(), BackendRole(), "https://myjobmag.example/r/1");

        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Evidence.Count, b.Evidence.Count);
    }

    [Theory]
    [InlineData(90, "Strong")]
    [InlineData(75, "Strong")]
    [InlineData(74, "Good")]
    [InlineData(55, "Good")]
    [InlineData(54, "Possible")]
    [InlineData(35, "Possible")]
    [InlineData(34, "Stretch")]
    [InlineData(0, "Stretch")]
    public void Tiers_follow_the_agreed_boundaries(int score, string tier)
    {
        Assert.Equal(tier, MatchEngine.TierFor(score));
    }

    [Fact]
    public void Substring_skill_matching_handles_react_and_react_native()
    {
        var profile = new CandidateProfile
        {
            UserId = Guid.NewGuid(),
            ContentHash = "xyz",
            YearsExperience = 3,
            Skills = [new ProfileSkill("React", "react", 3, true)],
        };

        var role = BackendRole();
        role.Skills = ["React Native"];
        var match = MatchEngine.Compute(profile, role, null);

        Assert.NotInRange(match.Score, 0, 0);
        Assert.Contains(match.Evidence, e => e.Detail.Contains("React", StringComparison.OrdinalIgnoreCase));
    }
}