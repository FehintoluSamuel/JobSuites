using System.Text.RegularExpressions;
using JobSuites.Api.Contracts;

namespace JobSuites.Api.Auth;

/// <summary>Field-level validation, run explicitly rather than relying on
/// DataAnnotations being inferred for minimal APIs.
///
/// Two reasons this is hand-rolled:
///  1. Password strength is a security control. It must not depend on framework
///     attribute inference staying enabled across SDK versions.
///  2. The frontend needs per-field messages to build a keyboard-navigable error
///     summary (see docs/UX-AUTH.md).</summary>
public static partial class AuthValidation
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    public static Dictionary<string, string[]> Validate(RegisterRequest r) => new()
    {
        ["email"] = ValidateEmail(r.Email),
        ["fullName"] = ValidateName(r.FullName),
        ["password"] = ValidatePassword(r.Password),
    };

    public static Dictionary<string, string[]> Validate(LoginRequest r) => new()
    {
        ["email"] = ValidateEmail(r.Email),
        ["password"] = ValidatePassword(r.Password, login: true),
    };

    private static string[] ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return ["Enter your email address."];

        var trimmed = email.Trim();
        if (trimmed.Length > 320)
            return ["Email address is too long."];

        return EmailPattern().IsMatch(trimmed) ? [] : ["Enter a valid email address."];
    }

    private static string[] ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ["Enter your full name."];

        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > 200)
            return ["Full name must be between 2 and 200 characters."];

        return [];
    }

    private static string[] ValidatePassword(string? password, bool login = false)
    {
        // On login we only reject values that cannot possibly be valid. We must
        // not leak the password policy here — that would tell an attacker the
        // current policy during enumeration.
        if (string.IsNullOrEmpty(password))
            return ["Enter your password."];

        if (password.Length > MaxPasswordLength)
            return ["Password is too long."];

        if (login)
            return [];

        if (password.Length < MinPasswordLength)
            return [$"Use at least {MinPasswordLength} characters."];

        if (password.All(char.IsLetterOrDigit))
            return ["Add a symbol or a space — letters and digits alone are easy to guess."];

        return [];
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

public static class ValidationResults
{
    /// <summary>Returns a 400 ProblemDetails carrying per-field messages, or
    /// null when everything passed.</summary>
    public static IResult? Invalid(Dictionary<string, string[]> errors)
    {
        if (errors.Values.All(e => e.Length == 0))
        {
            return null;
        }

        var onlyEmpty = errors.Where(e => e.Value.Length > 0).ToArray();

        return TypedResults.Problem(
            title: "Check the form",
            detail: "Some fields need attention before you can continue.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = onlyEmpty.ToDictionary(e => e.Key, e => e.Value),
            });
    }
}
