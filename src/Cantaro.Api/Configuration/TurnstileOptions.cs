using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class TurnstileOptions : IValidatableObject
{
    public const string SectionName = "Turnstile";

    /// <summary>
    /// Auto enables Turnstile when all required values are present. Disabled is an explicit
    /// opt-out for self-hosted deployments. Enabled requires all values at startup.
    /// </summary>
    public string Mode { get; set; } = "Auto";

    public string? SiteKey { get; set; }

    public string? Secret { get; set; }

    public string[] AllowedHostnames { get; set; } = [];

    public bool IsEnabled => !string.Equals(Mode, "Disabled", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(Mode, "Enabled", StringComparison.OrdinalIgnoreCase) || HasAnyConfiguration);

    private bool HasAnyConfiguration => !string.IsNullOrWhiteSpace(SiteKey)
        || !string.IsNullOrWhiteSpace(Secret)
        || (AllowedHostnames ?? []).Any(hostname => !string.IsNullOrWhiteSpace(hostname));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.Equals(Mode, "Auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Mode, "Enabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Turnstile:Mode must be Auto, Enabled, or Disabled.", [nameof(Mode)]);
            yield break;
        }

        if (string.Equals(Mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(SiteKey))
        {
            missing.Add(nameof(SiteKey));
        }

        if (string.IsNullOrWhiteSpace(Secret))
        {
            missing.Add(nameof(Secret));
        }

        if (!(AllowedHostnames ?? []).Any(hostname => !string.IsNullOrWhiteSpace(hostname)))
        {
            missing.Add(nameof(AllowedHostnames));
        }

        if (missing.Count > 0 && (string.Equals(Mode, "Enabled", StringComparison.OrdinalIgnoreCase) || HasAnyConfiguration))
        {
            yield return new ValidationResult(
                $"Turnstile is enabled or partially configured. Set {string.Join(", ", missing)} or set Turnstile:Mode to Disabled.",
                missing.Select(name => $"Turnstile:{name}"));
        }
    }
}
