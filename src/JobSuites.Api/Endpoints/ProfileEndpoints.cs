using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Cv;
using JobSuites.Api.Data;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using JobSuites.Api.Services;
using JobSuites.Api.Services.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").WithTags("Profile").RequireAuthorization();

        group.MapPost("/cv", UploadCv).WithName("UploadCv");
        group.MapPut("/", Update).WithName("UpdateProfile");
        group.MapGet("/", Get).WithName("GetProfile");
        group.MapDelete("/", Delete).WithName("DeleteProfile");

        // Media is addressed by its random file name and served only to the
        // owner — see UploadStore. There is deliberately no public media route.
        group.MapPost("/media", UploadMedia).WithName("UploadMedia");
        group.MapGet("/media/{file}", GetMedia).WithName("GetMedia");
        group.MapDelete("/media/{file}", DeleteMedia).WithName("DeleteMedia");
    }

    private static async Task<IResult> UploadCv(
        HttpRequest request,
        AppDbContext db,
        MatchService matches,
        LlmCvParser llmParser,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        if (!request.HasFormContentType)
        {
            return Problem("Send the CV as multipart/form-data with a 'file' field.");
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files["file"];

        if (file is null || file.Length == 0)
        {
            return Problem("No file was uploaded. Choose your CV and try again.", "file");
        }

        string rawText;
        try
        {
            await using var stream = file.OpenReadStream();
            rawText = CvTextExtractor.Extract(stream, file.FileName);
        }
        catch (InvalidCvException ex)
        {
            return Problem(ex.Message, "file");
        }

        // Both parsers run and their results are merged. The deterministic regex
        // parser is the baseline that can never lose a fact; the LLM pass may only
        // add what it truthfully extracted. A model that truncates therefore
        // cannot make the profile thinner than the regex parse would (ParsedCv.Merge).
        ParsedCv? parsed;
        try
        {
            parsed = CvParser.Parse(rawText);
        }
        catch (InvalidCvException)
        {
            // The regex parser is strict about minimum structure. The LLM is not
            // configured to be bug-for-bug compatible with it — a parse that
            // throws here may still be readable by the LLM, so don't give up yet.
            parsed = null;
        }

        var aiParse = await llmParser.TryParseAsync(rawText, ct);
        parsed = parsed is null ? aiParse : ParsedCv.Merge(parsed, aiParse);

        var hash = CvParser.Hash(rawText);

        // A CV we could not read anything from is not a usable profile. Rejecting
        // here is better than showing the user an empty dashboard.
        if (parsed is null || (parsed.Skills.Count == 0 && parsed.Experiences.Count == 0))
        {
            return Problem(
                "We could not find any skills or work history in that CV. " +
                "If it is a scan or image, upload a text-based PDF or a DOCX instead.",
                "file");
        }

        var existing = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var profile = existing ?? new CandidateProfile { UserId = userId.Value };

        profile.FileName = SanitizeFileName(file.FileName);
        profile.RawText = rawText;
        profile.ContentHash = hash;
        profile.FullName = parsed.FullName;
        profile.Email = parsed.Email;
        profile.Phone = parsed.Phone;
        profile.Location = parsed.Location;
        profile.Headline = parsed.Headline;
        profile.YearsExperience = parsed.YearsExperience;
        profile.Summary = parsed.Summary;

        // Re-uploading replaces CV-derived facts but never a user's manual
        // correction. Source is "cv" on everything the parser produced; anything
        // the user added or edited through the profile editor has Source "manual"
        // and survives.
        profile.Skills = MergeSkills(existing?.Skills, parsed.Skills);
        profile.Experiences = MergeExperiences(existing?.Experiences, parsed.Experiences);
        profile.Education = MergeEducation(existing?.Education, parsed.Education);
        profile.Certifications = MergeCertifications(existing?.Certifications, parsed.Certifications);
        profile.Languages = MergeLanguages(existing?.Languages, parsed.Languages);
        profile.Links = MergeLinks(existing?.Links, parsed.Links);
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing is null) db.Profiles.Add(profile);

        await db.SaveChangesAsync(ct);

        var result = await matches.RecomputeAsync(userId.Value, ct);

        return TypedResults.Ok(new
        {
            profile = CandidateProfileResponse.From(profile),
            matchesComputed = result.Scored,
            skillsFound = profile.Skills.Count,
            experiencesFound = profile.Experiences.Count,
            unchanged = existing?.ContentHash == hash,
        });
    }

    private static async Task<IResult> Update(
        UpdateProfileRequest request,
        AppDbContext db,
        MatchService matches,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        if (!ProfileValidation.ValidateUpdate(request, out var errors))
        {
            return TypedResults.Problem(
                title: "Check the profile",
                detail: "Some fields need attention.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errors"] = errors });
        }

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? await NewProfileAsync(db, userId.Value, ct);

        Apply(request, profile);
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var result = await matches.RecomputeAsync(userId.Value, ct);

        return TypedResults.Ok(new
        {
            profile = CandidateProfileResponse.From(profile),
            matchesComputed = result.Scored,
        });
    }

    private static async Task<IResult> Get(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            return TypedResults.Problem(
                title: "No CV yet",
                detail: "Upload your CV to get matched roles.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(CandidateProfileResponse.From(profile));
    }

    private static async Task<IResult> Delete(AppDbContext db, UploadStore media, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return TypedResults.NoContent();

        // Deleting the record must not leave its photos orphaned on disk.
        foreach (var name in new[] { profile.ProfilePicture, profile.BannerPicture })
        {
            if (!string.IsNullOrWhiteSpace(name)) media.Delete(userId.Value, name);
        }

        db.Profiles.Remove(profile);

        // Matches are meaningless without the profile they were computed from.
        var matches = await db.Matches.Where(m => m.UserId == userId).ToListAsync(ct);
        db.Matches.RemoveRange(matches);

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UploadMedia(
        HttpRequest request,
        AppDbContext db,
        UploadStore media,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        if (!request.HasFormContentType)
        {
            return Problem("Send media as multipart/form-data with a 'kind' and a 'file' field.");
        }

        var form = await request.ReadFormAsync(ct);
        var kind = form["kind"].ToString();
        var canUseKind = kind is "picture" or "banner";
        if (!canUseKind)
        {
            return Problem("Kind must be 'picture' or 'banner'.", "kind");
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Problem("No image was uploaded. Choose a file and try again.", "file");
        }

        if (file.Length > UploadContentTypes.MaxFileBytes)
        {
            return Problem("Images must be under 5 MB.", "file");
        }

        var contentType = file.ContentType;
        if (!UploadContentTypes.ByMediaType.TryGetValue(contentType, out var extension))
        {
            return Problem("Profile photos must be JPEG, PNG or WebP.", "file");
        }

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? await NewProfileAsync(db, userId.Value, ct);

        var oldName = kind == "picture" ? profile.ProfilePicture : profile.BannerPicture;

        await using var stream = file.OpenReadStream();
        var newName = await media.SaveAsync(userId.Value, stream, extension, ct);

        if (kind == "picture") profile.ProfilePicture = newName;
        else profile.BannerPicture = newName;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(oldName) && oldName != newName)
        {
            media.Delete(userId.Value, oldName);
        }

        return TypedResults.Ok(new ProfileMediaResponse(profile.ProfilePicture, profile.BannerPicture));
    }

    private static async Task<IResult> GetMedia(
        string file,
        AppDbContext db,
        UploadStore media,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        // Photos are the owner's private data: an entry only resolves if this
        // account actually references it as a photo. Guessing another user's
        // file name still 404s.
        var owns = profile is not null
            && (profile.ProfilePicture == file || profile.BannerPicture == file);

        if (!owns || !UploadStore.IsSafeName(file) || !media.Exists(userId.Value, file))
        {
            return TypedResults.NotFound();
        }

        var path = Path.Combine(media.UserDirectory(userId.Value), file);
        var mediaType = FileExtensionToMediaType(Path.GetExtension(file));

        return Results.File(path, mediaType);
    }

    private static async Task<IResult> DeleteMedia(
        string file,
        AppDbContext db,
        UploadStore media,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var references = profile is not null
            && (profile.ProfilePicture == file || profile.BannerPicture == file);

        if (profile is null || !references || !UploadStore.IsSafeName(file))
        {
            return TypedResults.NoContent();
        }

        if (profile.ProfilePicture == file) profile.ProfilePicture = null;
        if (profile.BannerPicture == file) profile.BannerPicture = null;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        media.Delete(userId.Value, file);

        return TypedResults.Ok(new ProfileMediaResponse(profile.ProfilePicture, profile.BannerPicture));
    }

    private static async Task<CandidateProfile> NewProfileAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        var profile = new CandidateProfile
        {
            UserId = userId,
            FullName = user?.FullName,
            Email = user?.Email,
        };
        db.Profiles.Add(profile);
        return profile;
    }

    private static void Apply(UpdateProfileRequest r, CandidateProfile p)
    {
        p.FullName = TrimOrNull(r.FullName) ?? p.FullName;
        p.Email = string.IsNullOrWhiteSpace(r.Email) ? p.Email : r.Email.Trim().ToLowerInvariant();
        p.Phone = TrimOrNull(r.Phone) ?? p.Phone;
        p.Location = TrimOrNull(r.Location) ?? p.Location;
        p.Headline = TrimOrNull(r.Headline) ?? p.Headline;
        p.Summary = TrimOrNull(r.Summary) ?? p.Summary;
        p.DesiredSalary = TrimOrNull(r.DesiredSalary) ?? p.DesiredSalary;
        p.Availability = TrimOrNull(r.Availability) ?? p.Availability;
        p.YearsExperience = r.YearsExperience ?? p.YearsExperience;
        p.PhotoConsentGiven = r.PhotoConsentGiven ?? p.PhotoConsentGiven;

        if (r.TargetRoles is not null)
            p.TargetRoles = NormalizeStrings(r.TargetRoles, cap: 12);
        if (r.PreferredStates is not null)
            p.PreferredStates = NormalizeStates(r.PreferredStates);
        if (r.DesiredJobTypes is not null)
            p.DesiredJobTypes = NormalizeStrings(r.DesiredJobTypes, cap: 4)
                .Select(j => j.ToLowerInvariant().Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (r.Skills is not null)
            p.Skills = SanitizeSkills(r.Skills);
        if (r.Experiences is not null)
            p.Experiences = SanitizeExperiences(r.Experiences);
        if (r.Education is not null)
            p.Education = SanitizeEducation(r.Education);
        if (r.Certifications is not null)
            p.Certifications = SanitizeCertifications(r.Certifications);
        if (r.Languages is not null)
            p.Languages = SanitizeLanguages(r.Languages);
        if (r.Links is not null)
            p.Links = SanitizeLinks(r.Links);
    }

    /// <summary>Manual facts first (their order is user-chosen), then CV facts
    /// that are not already covered by a same-named manual skill.</summary>
    private static List<ProfileSkill> MergeSkills(
        IReadOnlyList<ProfileSkill>? existing,
        IReadOnlyList<ProfileSkill> derived)
    {
        var manual = (existing ?? []).Where(s => s.Source != "cv").ToList();
        var names = new HashSet<string>(manual.Select(s => ProfileTaxonomy.Normalize(s.Name)), StringComparer.Ordinal);

        var result = new List<ProfileSkill>(manual);
        foreach (var skill in derived)
        {
            var cvSkill = skill with { Source = "cv" };
            if (names.Add(ProfileTaxonomy.Normalize(cvSkill.Name)))
            {
                result.Add(cvSkill);
            }
        }

        return result;
    }

    private static List<ProfileExperience> MergeExperiences(
        IReadOnlyList<ProfileExperience>? existing,
        IReadOnlyList<ProfileExperience> derived)
    {
        var manual = (existing ?? []).Where(e => e.Source != "cv").ToList();
        var keys = new HashSet<string>(
            manual.Select(e => NormalizeExperienceKey(e.Company, e.Title, e.StartDate)),
            StringComparer.Ordinal);

        var result = new List<ProfileExperience>(manual);
        foreach (var experience in derived)
        {
            var cvExperience = experience with { Source = "cv" };
            if (keys.Add(NormalizeExperienceKey(cvExperience.Company, cvExperience.Title, cvExperience.StartDate)))
            {
                result.Add(cvExperience);
            }
        }

        return result;
    }

    private static string NormalizeExperienceKey(string company, string title, string? start) =>
        ProfileTaxonomy.Normalize(company) + "|" + ProfileTaxonomy.Normalize(title) + "|" +
        (string.IsNullOrWhiteSpace(start) ? "" : ProfileTaxonomy.Normalize(start));

    private static List<ProfileEducation> MergeEducation(
        IReadOnlyList<ProfileEducation>? existing,
        IReadOnlyList<ProfileEducation> derived)
    {
        var manual = (existing ?? []).Where(e => e.Source != "cv").ToList();
        var keys = new HashSet<string>(
            manual.Select(e => NormalizeEducationKey(e.School, e.Degree, e.StartYear)),
            StringComparer.Ordinal);

        var result = new List<ProfileEducation>(manual);
        foreach (var entry in derived)
        {
            var cv = entry with { Source = "cv" };
            if (keys.Add(NormalizeEducationKey(cv.School, cv.Degree, cv.StartYear)))
            {
                result.Add(cv);
            }
        }

        return result;
    }

    private static string NormalizeEducationKey(string? school, string? degree, int? start) =>
        ProfileTaxonomy.Normalize(school ?? "") + "|" + ProfileTaxonomy.Normalize(degree ?? "") +
        (start is null ? "" : "|" + start.Value.ToString());

    private static List<ProfileCertification> MergeCertifications(
        IReadOnlyList<ProfileCertification>? existing,
        IReadOnlyList<ProfileCertification> derived)
    {
        var manual = (existing ?? []).Where(c => c.Source != "cv").ToList();
        var keys = new HashSet<string>(
            manual.Select(c => NormalizeCertKey(c.Name, c.Year)),
            StringComparer.Ordinal);

        var result = new List<ProfileCertification>(manual);
        foreach (var cert in derived)
        {
            var cv = cert with { Source = "cv" };
            if (keys.Add(NormalizeCertKey(cv.Name, cv.Year)))
            {
                result.Add(cv);
            }
        }

        return result;
    }

    private static string NormalizeCertKey(string name, int? year) =>
        ProfileTaxonomy.Normalize(name) + (year is null ? "" : "|" + year.Value.ToString());

    private static List<ProfileLanguage> MergeLanguages(
        IReadOnlyList<ProfileLanguage>? existing,
        IReadOnlyList<ProfileLanguage> derived)
    {
        var manual = (existing ?? []).Where(l => l.Source != "cv").ToList();
        var keys = new HashSet<string>(
            manual.Select(l => ProfileTaxonomy.Normalize(l.Name)),
            StringComparer.Ordinal);

        var result = new List<ProfileLanguage>(manual);
        foreach (var language in derived)
        {
            var cv = language with { Source = "cv" };
            if (keys.Add(ProfileTaxonomy.Normalize(cv.Name)))
            {
                result.Add(cv);
            }
        }

        return result;
    }

    private static List<ProfileLink> MergeLinks(
        IReadOnlyList<ProfileLink>? existing,
        IReadOnlyList<ProfileLink> derived)
    {
        var manual = (existing ?? []).Where(l => l.Source != "cv").ToList();
        var keys = new HashSet<string>(
            manual.Select(l => l.Url.ToLowerInvariant()),
            StringComparer.Ordinal);

        var result = new List<ProfileLink>(manual);
        foreach (var link in derived)
        {
            var cv = link with { Source = "cv" };
            if (keys.Add(cv.Url.ToLowerInvariant()))
            {
                result.Add(cv);
            }
        }

        return result;
    }

    private static List<ProfileSkill> SanitizeSkills(IReadOnlyList<ProfileSkill> skills)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ProfileSkill>();
        foreach (var s in skills)
        {
            var name = s.Name.Trim();
            if (name.Length == 0) continue;
            if (!seen.Add(name)) continue;
            result.Add(s with
            {
                Name = name,
                Normalized = name.ToLowerInvariant(),
                Years = Math.Clamp(s.Years, 0, 60),
                Source = s.Source == "manual" ? "manual" : "cv",
            });
        }
        return result;
    }

    private static List<ProfileExperience> SanitizeExperiences(IReadOnlyList<ProfileExperience> experiences)
    {
        var result = new List<ProfileExperience>();
        foreach (var e in experiences)
        {
            var title = TrimOrNull(e.Title);
            var company = TrimOrNull(e.Company);
            if (title is null && company is null) continue;
            result.Add(e with
            {
                Title = title ?? "",
                Company = company ?? "",
                StartDate = TrimOrNull(e.StartDate),
                EndDate = TrimOrNull(e.EndDate),
                Source = e.Source == "manual" ? "manual" : "cv",
            });
        }
        return result;
    }

    private static readonly string[] AllowedProficiencies = ["native", "professional", "conversational", "basic"];

    private static List<ProfileEducation> SanitizeEducation(IReadOnlyList<ProfileEducation> education)
    {
        var result = new List<ProfileEducation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in education)
        {
            var school = TrimOrNull(e.School);
            var degree = TrimOrNull(e.Degree);
            if (school is null && degree is null) continue;
            if (school is not null && school.Length > 120) school = school[..120];
            if (degree is not null && degree.Length > 120) degree = degree[..120];

            var key = $"{school ?? ""}|{degree ?? ""}|{e.StartYear?.ToString() ?? ""}";
            if (!seen.Add(key)) continue;

            var details = TrimOrNull(e.Details);
            if (details is not null && details.Length > 300) details = details[..300];

            result.Add(new ProfileEducation(
                School: school,
                Degree: degree,
                FieldOfStudy: TrimOrNull(e.FieldOfStudy) is { } field && field.Length > 120 ? field[..120] : TrimOrNull(e.FieldOfStudy),
                StartYear: e.StartYear is >= 1900 and <= 2100 ? e.StartYear : null,
                EndYear: e.EndYear is >= 1900 and <= 2100 ? e.EndYear : null,
                Details: details,
                Source: e.Source == "manual" ? "manual" : "cv"));
            if (result.Count == 12) break;
        }
        return result;
    }

    private static List<ProfileCertification> SanitizeCertifications(IReadOnlyList<ProfileCertification> certs)
    {
        var result = new List<ProfileCertification>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in certs)
        {
            var name = TrimOrNull(c.Name);
            if (name is null || name.Length == 0) continue;
            if (name.Length > 120) name = name[..120];
            if (!seen.Add(name)) continue;

            var issuer = TrimOrNull(c.Issuer);
            if (issuer is not null && issuer.Length > 120) issuer = issuer[..120];

            result.Add(new ProfileCertification(
                Name: name,
                Issuer: issuer,
                Year: c.Year is >= 1990 and <= 2100 ? c.Year : null,
                Source: c.Source == "manual" ? "manual" : "cv"));
            if (result.Count == 12) break;
        }
        return result;
    }

    private static List<ProfileLanguage> SanitizeLanguages(IReadOnlyList<ProfileLanguage> languages)
    {
        var result = new List<ProfileLanguage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in languages)
        {
            var name = TrimOrNull(l.Name);
            if (name is null || name.Length == 0) continue;
            if (name.Length > 60) name = name[..60];
            if (!seen.Add(name)) continue;

            var proficiency = TrimOrNull(l.Proficiency) ?? "professional";
            if (!AllowedProficiencies.Contains(proficiency)) proficiency = "professional";

            result.Add(new ProfileLanguage(name, proficiency, l.Source == "manual" ? "manual" : "cv"));
            if (result.Count == 12) break;
        }
        return result;
    }

    private static List<ProfileLink> SanitizeLinks(IReadOnlyList<ProfileLink> links)
    {
        var result = new List<ProfileLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in links)
        {
            var url = TrimOrNull(l.Url);
            if (url is null) continue;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (url.Length > 300) url = url[..300];
            if (!seen.Add(url)) continue;

            var label = TrimOrNull(l.Label);
            if (label is null || label.Length == 0) label = "Website";
            if (label.Length > 30) label = label[..30];

            result.Add(new ProfileLink(label, url, l.Source == "manual" ? "manual" : "cv"));
            if (result.Count == 12) break;
        }
        return result;
    }

    private static List<string> NormalizeStrings(IReadOnlyList<string>? values, int cap)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? [])
        {
            var value = string.Join(' ', raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (value.Length > 100) value = value[..100];
            if (value.Length == 0 || !seen.Add(value)) continue;
            result.Add(value);
            if (result.Count == cap) break;
        }
        return result;
    }

    private static List<string> NormalizeStates(IReadOnlyList<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values)
        {
            var canonical = ProfileTaxonomy.States.FirstOrDefault(
                s => ProfileTaxonomy.Normalize(s) == ProfileTaxonomy.Normalize(raw));
            if (canonical is null) continue;
            if (!seen.Add(ProfileTaxonomy.Normalize(canonical))) continue;
            result.Add(canonical);
        }
        return result;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Strips any path component. A browser sends the full path on some
    /// platforms, and we store this as a display name.</summary>
    private static string SanitizeFileName(string name)
    {
        var leaf = name.Split(['/', '\\']).LastOrDefault()?.Trim() ?? "cv";
        return leaf.Length > 260 ? leaf[..260] : leaf;
    }

    private static string FileExtensionToMediaType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    internal static IResult Problem(string detail, string? field = null)
    {
        return TypedResults.Problem(
            title: "Check the CV",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: field is null
                ? null
                : new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { [field] = [detail] } });
    }
}