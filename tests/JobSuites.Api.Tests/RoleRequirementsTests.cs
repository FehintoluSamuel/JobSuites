using JobSuites.Api.Matching;
using JobSuites.Api.Models;

namespace JobSuites.Api.Tests;

/// <summary>
/// The requirement extractor, against a description shaped like the real thing.
///
/// docs/PRODUCT.md §3 makes this the join target for matching, tailoring and
/// prep, so what it emits — and what it refuses to emit — is load-bearing. The
/// tests here are mostly about refusals: a requirement with no evidence, or a
/// boilerplate line read as a demand, becomes a gap shown to a real candidate,
/// and that is worse than a missed requirement.
/// </summary>
public class RoleRequirementsTests
{
    /// <summary>A myjobmag-shaped description: headed blocks, bullets, years,
    /// a preferred block, and the closing "send your CV" boilerplate every one of
    /// them carries.</summary>
    private const string SampleJd = """
        Instrumentation Manager

        The role

        You will be responsible for the instrumentation team across our Lagos
        plant, reporting to the Operations Director.

        Key Skills and Experience

        - Degree in Engineering or a related discipline
        - Minimum 5 years experience in a manufacturing environment
        - Must have hands-on experience with DCS and SCADA systems
        - Familiarity with PLC programming
        - NEBOSH certified is an added advantage

        Added Advantage

        - Exposure to the oil and gas sector
        - French language proficiency

        How to Apply

        Send your CV and cover letter to the HR unit. Only shortlisted candidates
        will be contacted.
        """;

    private static Role RoleWith(string description, params string[] skills) => new()
    {
        Company = "PZ Cussons",
        Title = "Instrumentation Manager",
        Description = description,
        Skills = skills.ToList(),
    };

    [Fact]
    public void Reads_requirements_out_of_the_description_body()
    {
        var requirements = RoleRequirements.FromDescription(RoleWith(SampleJd));

        Assert.NotEmpty(requirements);

        Assert.Contains(requirements, r => r.Category == "education" && r.MustHave);
        Assert.Contains(requirements, r => r.Category == "certification" && !r.MustHave);
        Assert.Contains(requirements, r => r.Category == "experience" && r.YearsMin == 5);
    }

    [Fact]
    public void Every_requirement_carries_the_jd_text_it_came_from()
    {
        var requirements = RoleRequirements.FromDescription(RoleWith(SampleJd));

        Assert.All(requirements, r => Assert.False(string.IsNullOrWhiteSpace(r.Span)));

        // The span must be the posting's own words, not our paraphrase of them.
        var degree = requirements.Single(r => r.Category == "education");
        Assert.Contains("Degree in Engineering", degree.Span!, StringComparison.Ordinal);
    }

    [Fact]
    public void Must_have_is_read_from_the_posting_not_from_position()
    {
        var requirements = RoleRequirements.FromDescription(RoleWith(SampleJd, "DCS", "PLC"));

        // "Must have hands-on experience with DCS" states it outright.
        Assert.Contains(
            requirements,
            r => r.MustHave && r.Text.Contains("DCS", StringComparison.OrdinalIgnoreCase));

        // "Familiarity with PLC programming" sits in the same block but is not
        // stated as a requirement. The old heuristic would have called the first
        // three skills must-haves; this reads the posting instead.
        var plc = requirements.SingleOrDefault(r => r.Text.Contains("PLC", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(plc);
        Assert.False(plc!.MustHave);

        // "Exposure to the oil and gas sector" sits under Added Advantage and
        // must never reach the gap surface as something the candidate lacks.
        var sector = requirements.SingleOrDefault(r => r.Text.Contains("oil and gas", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sector);
        Assert.False(sector!.MustHave);
    }

    /// <summary>A posting with no structured skill list at all is the case this
    /// extractor exists for: the demand is in the prose, and if it is only read
    /// when myjobmag also filled in the taxonomy, it is read almost never.</summary>
    [Fact]
    public void A_prose_demand_is_a_requirement_even_with_no_skill_list_and_no_category()
    {
        var requirements = RoleRequirements.FromDescription(
            RoleWith("Applicants must have hands-on experience with DCS and SCADA systems."));

        var dcs = requirements.Single();
        Assert.True(dcs.MustHave);
        Assert.Contains("DCS and SCADA", dcs.Text);

        // The obligation frame is not part of what the candidate has to satisfy,
        // so it is not what they are shown. The posting's own words are kept in
        // the span.
        Assert.DoesNotContain("Applicants must have", dcs.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Applicants must have", dcs.Span!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(dcs.Category);
    }

    [Fact]
    public void Boilerplate_is_not_read_as_a_demand()
    {
        var requirements = RoleRequirements.FromDescription(RoleWith(SampleJd));

        Assert.DoesNotContain(
            requirements,
            r => r.Text.Contains("Send your CV", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            requirements,
            r => r.Text.Contains("shortlisted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_preferred_block_does_not_inherit_the_obligations_of_a_requirements_block()
    {
        var requirements = RoleRequirements.FromDescription(RoleWith(SampleJd));

        var french = requirements.SingleOrDefault(r => r.Category == "language");
        Assert.NotNull(french);
        Assert.False(french!.MustHave);
    }

    [Fact]
    public void A_description_with_nothing_readable_yields_nothing_rather_than_a_guess()
    {
        var requirements = RoleRequirements.FromDescription(
            RoleWith("Lorem ipsum dolor sit amet. Consectetur adipiscing elit."));

        Assert.Empty(requirements);
        Assert.False(RoleRequirements.IsUsable(requirements));
    }

    /// <summary>The floor that keeps a malformed JD from making a role vanish:
    /// the structured fields the board itself publishes are requirements too.</summary>
    [Fact]
    public void Structured_fields_are_requirements_even_when_the_body_is_unreadable()
    {
        var role = RoleWith("Lorem ipsum dolor sit amet.");
        role.Qualification = "HND Mechanical Engineering";
        role.MinYears = 7;

        var requirements = RoleRequirements.FromDescription(role);

        Assert.Contains(requirements, r => r.Category == "education" && r.MustHave);
        Assert.Contains(requirements, r => r.YearsMin == 7 && r.MustHave);
    }

    [Fact]
    public void Extracted_keys_are_unique_and_stable_across_runs()
    {
        var first = RoleRequirements.FromDescription(RoleWith(SampleJd));
        var second = RoleRequirements.FromDescription(RoleWith(SampleJd));

        Assert.Equal(
            first.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal),
            second.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal(first.Count, first.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void Skills_become_requirements_that_cite_the_line_they_were_named_on()
    {
        var requirements = RoleRequirements.FromDescription(
            RoleWith(SampleJd, "DCS", "SCADA", "PLC"));

        var dcs = requirements.Single(r => r.Key == "skill:dcs");
        Assert.Equal("skill", dcs.Category);
        Assert.True(dcs.MustHave);
        Assert.Contains("DCS", dcs.Span!, StringComparison.Ordinal);
    }

    /// <summary>Normalisation is shared with the matcher. If the two disagreed,
    /// a supported fact would fail to join its own requirement and tailoring
    /// would report a gap the candidate does not have.</summary>
    [Theory]
    [InlineData("Power BI", "power-bi")]
    [InlineData("Node.js", "nodejs")]
    [InlineData("C#", "c#")]
    public void Normalisation_is_identical_on_both_sides_of_the_join(string a, string b)
    {
        Assert.Equal(RoleRequirements.Normalize(a), RoleRequirements.Normalize(b));
    }

    [Fact]
    public void The_cap_keeps_must_haves_and_drops_optional_ones()
    {
        // 40 optional requirements and one required one.
        var lines = new List<string> { "Key Skills" };
        for (var i = 0; i < 40; i++) lines.Add($"- Familiarity with Tool{i} in industry settings");
        lines.Add("- Must have certification in NEBOSH safety practice");

        var requirements = RoleRequirements.FromDescription(RoleWith(string.Join('\n', lines)));

        Assert.True(requirements.Count <= 24);
        Assert.Contains(requirements, r => r.MustHave && r.Text.Contains("NEBOSH", StringComparison.Ordinal));
    }
}
