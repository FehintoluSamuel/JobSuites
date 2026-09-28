using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace JobSuites.Api.Services;

/// <summary>
/// Local-disk media store for profile photos.
///
/// Photos are personal data, so the serving contract is: files are addressed by
/// a random name inside a per-user directory, never by an owner-predictable
/// path, and every read goes through an owner-guarded endpoint. A static folder
/// would leak every photo to anyone who guessed a name.
///
/// Production can swap Root for object storage; the endpoint contract does not
/// change.
/// </summary>
public sealed class UploadStore(IConfiguration config, IHostEnvironment env)
{
    public string Root { get; } = ResolveRoot(config, env);

    public string UserDirectory(Guid userId) => Path.Combine(Root, userId.ToString("N"));

    /// <summary>Writes a stream under the user's directory and returns the
    /// random file name. The extension is server-derived from the content type,
    /// never from the client filename.</summary>
    public async Task<string> SaveAsync(Guid userId, Stream stream, string extension, CancellationToken ct)
    {
        var dir = UserDirectory(userId);
        Directory.CreateDirectory(dir);

        var name = $"{Guid.NewGuid():N}.{extension}";
        var path = Path.Combine(dir, name);

        await using var fs = File.Create(path);
        await stream.CopyToAsync(fs, ct);

        return name;
    }

    public bool Exists(Guid userId, string name) =>
        IsSafeName(name) && File.Exists(Path.Combine(UserDirectory(userId), name));

    public void Delete(Guid userId, string name)
    {
        if (!IsSafeName(name)) return;
        try
        {
            File.Delete(Path.Combine(UserDirectory(userId), name));
        }
        catch (IOException)
        {
            // A missing file is not an error worth failing a profile save over.
        }
    }

    /// <summary>Names are random hex + a whitelisted extension, so only
    /// "letters, digits, dot, hyphen, underscore" survive. This kills path
    /// traversal before it touches the filesystem.</summary>
    public static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 120
        && !name.Contains("..", StringComparison.Ordinal)
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static string ResolveRoot(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Uploads:Path"];
        var root = configured is { Length: > 0 }
            ? Path.GetFullPath(configured)
            : Path.Combine(env.ContentRootPath, "uploads");

        Directory.CreateDirectory(root);
        return root;
    }
}

public static class UploadContentTypes
{
    public static readonly Dictionary<string, string> ByMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    public const long MaxFileBytes = 5 * 1024 * 1024;
}