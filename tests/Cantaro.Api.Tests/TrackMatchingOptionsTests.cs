using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Cantaro.Api.Configuration;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchingOptionsTests
{
    [Fact]
    public void DataAnnotationsValidateIndependentlyOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
            var options = new TrackMatchingOptions();
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(
                options,
                new ValidationContext(options),
                results,
                validateAllProperties: true);

            Assert.True(isValid, string.Join(Environment.NewLine, results.Select(result => result.ErrorMessage)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
