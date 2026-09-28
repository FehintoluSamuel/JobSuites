namespace JobSuites.Api.Models;

/// <summary>
/// Closed vocabularies for the editable profile. Mirrors docs/PRODUCT.md §4:
/// preferred states are a multi-select over the 36 states + FCT, and job types
/// are full-time / contract / internship / part-time. The server validates
/// against these lists so a typo in the client cannot persist bad data.
/// </summary>
public static class ProfileTaxonomy
{
    public static readonly IReadOnlyList<string> States =
    [
        "Abia", "Adamawa", "Akwa Ibom", "Anambra", "Bauchi", "Bayelsa", "Benue",
        "Borno", "Cross River", "Delta", "Ebonyi", "Edo", "Ekiti", "Enugu",
        "FCT Abuja", "Gombe", "Imo", "Jigawa", "Kaduna", "Kano", "Katsina",
        "Kebbi", "Kogi", "Kwara", "Lagos", "Nasarawa", "Niger", "Ogun", "Ondo",
        "Osun", "Oyo", "Plateau", "Rivers", "Sokoto", "Taraba", "Yobe", "Zamfara",
    ];

    public static readonly IReadOnlyList<string> JobTypes =
    [
        "full-time", "contract", "internship", "part-time",
    ];

    public static readonly string[] ValidLinkSchemes = ["https://", "http://"];

    /// <summary>Case/space-insensitive match helper. The CJK-free alphabet means
    /// a simple fold is enough: "fct abuja", "FCT", "Abuja" all resolve.</summary>
    public static bool IsState(string value) =>
        States.Any(s => Normalize(s) == Normalize(value));

    public static bool IsJobType(string value) =>
        JobTypes.Any(t => t == value.Trim().ToLowerInvariant());

    public static string Normalize(string value) =>
        string.Concat(value.Where(c => char.IsLetterOrDigit(c)))
            .Trim()
            .ToLowerInvariant();
}