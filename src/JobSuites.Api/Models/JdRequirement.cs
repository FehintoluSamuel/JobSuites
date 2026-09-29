namespace JobSuites.Api.Models;

/// <summary>
/// One thing a job description asks for, extracted once and stored per role.
///
/// This is the join target for the whole product (docs/PRODUCT.md §3): matching
/// scores CV facts against it, tailoring selects facts to cover it, and prep asks
/// about the ones left uncovered. It lives on the <em>role</em>, not the
/// posting, because one role posted across ten states is one row of work and its
/// description is the same body in all ten.
///
/// Stored rather than recomputed per request for the same reason prep is cached
/// (§7.1): the extraction is the expensive step, and every user of a role would
/// otherwise pay for it again. A role's requirements change only when its
/// description changes, and `JdHash` is what detects that.
/// </summary>
public class JdRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Stable identity within the role, e.g. "skill:sql" or
    /// "experience:5". Deduplication key and the join key — re-extracting the
    /// same role replaces rows rather than accumulating near-duplicates.</summary>
    public string Key { get; set; } = "";

    /// <summary>What we show the user: the requirement as written.</summary>
    public string Text { get; set; } = "";

    /// <summary>The verbatim JD text this was read from. This is the evidence.
    ///
    /// A verdict that cannot point at the posting's own words is an assertion,
    /// and PRODUCT.md §5.3 requires every verdict to cite the JD span that
    /// produced it. Kept separate from <see cref="Text"/> because the display
    /// form is trimmed for reading while this must stay exactly as scraped.</summary>
    public string Span { get; set; } = "";

    /// <summary>skill | experience | education | certification | language |
    /// domain, or null when the posting's sentence named a demand we could not
    /// classify.
    ///
    /// Null is a real state, not a gap in the data. A posting that says "must
    /// have hands-on experience with DCS and SCADA" is making a demand; labelling
    /// it "skill" because that is probably right would be us inventing a
    /// requirement the employer never wrote, and a role that starts asking for a
    /// certification nobody mentioned is how a user learns to distrust the gap
    /// list. The text and its span are kept either way — only the kind is
    /// unknown.</summary>
    public string? Category { get; set; }

    /// <summary>Disqualifying if absent, or merely wanted. Read from the JD's own
    /// wording — "must have", "required", "should have", "nice to have" — rather
    /// than guessed from position or count.</summary>
    public bool MustHave { get; set; }

    public int? YearsMin { get; set; }

    /// <summary>Where this requirement came from: "deterministic" today, and
    /// "llm" for anything a configured model contributed. Kept so a requirement
    /// can always be traced to the engine that asserted it.</summary>
    public string Origin { get; set; } = "deterministic";

    /// <summary>Hash of the JD body this set was extracted from. A role whose
    /// description changes gets re-extracted; one that does not keeps its rows
    /// and costs nothing.</summary>
    public string JdHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
