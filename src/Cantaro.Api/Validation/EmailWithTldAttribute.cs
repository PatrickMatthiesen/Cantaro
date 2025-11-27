using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Cantaro.Api.Validation;

/// <summary>
/// Validates that an email address contains a valid TLD (top-level domain).
/// The standard EmailAddress attribute allows emails like "a@b" without a TLD.
/// This attribute requires at least a two-character TLD (e.g., "a@b.dk").
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public partial class EmailWithTldAttribute : ValidationAttribute
{
    public EmailWithTldAttribute() : base("Invalid email address. Email must include a valid domain with a TLD (e.g., example@domain.com).")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string email)
        {
            return true; // Let [Required] handle null/empty
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return true; // Let [Required] handle empty strings
        }

        // Use a regex that requires:
        // 1. Valid email format
        // 2. A domain with at least one dot
        // 3. A TLD of at least 2 characters
        return EmailWithTldRegex().IsMatch(email);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailWithTldRegex();
}
