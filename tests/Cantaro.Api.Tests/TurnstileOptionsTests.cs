using System.ComponentModel.DataAnnotations;
using Cantaro.Api.Configuration;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TurnstileOptionsTests
{
    [Fact]
    public void AutoModeWithoutConfigurationIsDisabledAndValid()
    {
        var options = new TurnstileOptions();

        Assert.False(options.IsEnabled);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void AutoModeWithPartialConfigurationFailsValidation()
    {
        var options = new TurnstileOptions { SiteKey = "site-key" };

        var errors = Validate(options);

        Assert.Contains(errors, error => error.ErrorMessage!.Contains(nameof(TurnstileOptions.Secret), StringComparison.Ordinal));
        Assert.Contains(errors, error => error.ErrorMessage!.Contains(nameof(TurnstileOptions.AllowedHostnames), StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledModeRequiresAllValues()
    {
        var options = new TurnstileOptions { Mode = "Enabled" };

        Assert.NotEmpty(Validate(options));
        // Invalid enabled configuration must not silently become an opt-out.
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void DisabledModeIsExplicitOptOut()
    {
        var options = new TurnstileOptions
        {
            Mode = "Disabled",
            SiteKey = "site-key",
        };

        Assert.False(options.IsEnabled);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void CompleteAutoConfigurationEnablesTurnstile()
    {
        var options = new TurnstileOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = ["cantaro.example"],
        };

        Assert.True(options.IsEnabled);
        Assert.Empty(Validate(options));
    }

    private static List<ValidationResult> Validate(TurnstileOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        results.AddRange(options.Validate(new ValidationContext(options)));
        return results;
    }
}
