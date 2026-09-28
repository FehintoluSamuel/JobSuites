using JobSuites.Api.Cv;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using Xunit;

namespace JobSuites.Api.Tests;

/// <summary>
/// The parser is where a wrong answer is most expensive and least visible: a
/// hallucinated skill shows up in the evidence list as though we had read it off
/// the CV. These tests pin the "extract, never invent" rule.
/// </summary>
public class CvParserTests
{
    private const string SampleCv = """
        Adaeze Okonkwo
        adaeze.okonkwo@example.com | +234 803 555 0142
        Lagos, Nigeria

        SENIOR BACKEND ENGINEER

        Summary
        Backend engineer with 8 years building payment systems in Python and
        PostgreSQL, with some exposure to Kubernetes.

        Experience

        Senior Backend Engineer, Kuda Technologies
        2021-03 - Present
        - Rebuilt the ledger service in Python and PostgreSQL, cutting settlement
          time by 60%.
        - Introduced Kubernetes and Docker to the platform team.

        Backend Engineer, Paystack
        2018-01 - 2021-02
        - Owned the reconciliation API.

        Education
        B.Sc Computer Science, University of Lagos, 2017

        Skills
        Python, PostgreSQL, Docker, Kubernetes, AWS, Kafka, Redis
        """;

    [Fact]
    public void Extracts_identity_and_headline()
    {
        var p = CvParser.Parse(SampleCv);

        Assert.Equal("Adaeze Okonkwo", p.FullName);
        Assert.Equal("adaeze.okonkwo@example.com", p.Email);
        Assert.Equal("Lagos", p.Location);
        // The candidate's own casing is preserved verbatim; the parser does not
        // silently re-word or re-case what was on the CV.
        Assert.Equal("SENIOR BACKEND ENGINEER", p.Headline);
    }

    [Fact]
    public void Extracts_skills_case_insensitively_and_dedupes()
    {
        var p = CvParser.Parse(SampleCv);

        var names = p.Skills.Select(x => x.Name).ToList();

        Assert.Contains("Python", names);
        Assert.Contains("PostgreSQL", names);
        Assert.Contains("Kubernetes", names);

        // A mention in the summary must not create a second entry.
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // "AWS" and "Amazon Web Services" are the same skill; both appear in the
        // catalog, so the parser must collapse them rather than double-count.
        Assert.Equal(1, names.Count(n => n is "AWS" or "Amazon Web Services"));
    }

    [Fact]
    public void Extracts_work_history_with_dates()
    {
        var p = CvParser.Parse(SampleCv);

        Assert.Equal(2, p.Experiences.Count);
        Assert.Contains(p.Experiences, e => e.Company.Contains("Kuda", StringComparison.OrdinalIgnoreCase));

        var mostRecent = p.Experiences.First(e => e.Company.Contains("Kuda", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mar 2021", mostRecent.StartDate);
        Assert.Equal("Senior Backend Engineer", mostRecent.Title);

        // "Present" must be reported as an open-ended current role, not as a job
        // that ended today.
        Assert.True(mostRecent.IsCurrent);
        Assert.Equal("Present", mostRecent.EndDate);

        var paystack = p.Experiences.First(e => e.Company.Contains("Paystack", StringComparison.OrdinalIgnoreCase));
        Assert.False(paystack.IsCurrent);
    }

    [Fact]
    public void Estimates_years_from_dated_roles()
    {
        var p = CvParser.Parse(SampleCv);

        // 2018-01 to now is a little over 8 years. Allow a month of slack for the
        // day the fixture was written versus the day the test runs.
        Assert.NotNull(p.YearsExperience);
        Assert.InRange(p.YearsExperience!.Value, 7, 9);
    }

    [Fact]
    public void Leaves_unknown_fields_null_rather_than_guessing()
    {
        var p = CvParser.Parse("Some notes about a hobby. Nothing structured here.");

        Assert.Null(p.FullName);
        Assert.Null(p.Email);
        Assert.Null(p.YearsExperience);
        Assert.Empty(p.Skills);
        Assert.Empty(p.Experiences);
    }

    [Fact]
    public void Does_not_treat_arbitrary_numbers_as_phone_numbers()
    {
        // "5 years" is a duration, not a phone number. Treating it as one would
        // put a wrong value on the user's profile header.
        var p = CvParser.Parse("5 years of experience with Python.");

        Assert.Null(p.Phone);
    }

    [Fact]
    public void Hash_is_stable_and_content_addressed()
    {
        var a = CvParser.Hash(SampleCv);
        var b = CvParser.Hash(SampleCv);
        var c = CvParser.Hash(SampleCv + " ");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Rejects_empty_text_before_parsing()
    {
        Assert.Throws<InvalidCvException>(() => CvParser.Parse("   \n  \t "));
    }
}

public class RoleSkillExtractorTests
{
    [Fact]
    public void Finds_requirements_in_free_text()
    {
        var skills = RoleSkillExtractor.Extract(new Role
        {
            Description = "We need a backend engineer strong in Python, PostgreSQL and Kafka. " +
                          "Experience with Docker required. GraphQL is a nice to have.",
        });

        Assert.Contains("Python", skills);
        Assert.Contains("PostgreSQL", skills);
        Assert.Contains("Kafka", skills);
    }

    [Fact]
    public void Returns_empty_for_an_unrelated_description()
    {
        var skills = RoleSkillExtractor.Extract(new Role
        {
            Description = "Seeking a confident hair stylist with salon experience.",
        });

        Assert.Empty(skills);
    }
}
