using System.Data.Common;

namespace Cantaro.GarageProvisioner;

public sealed record GarageProvisioningOptions(
    Uri AdminUrl,
    string AdminToken,
    string AccessKeyId,
    string SecretAccessKey,
    string BucketName,
    long CapacityBytes = 1_000_000_000)
{
    public static GarageProvisioningOptions FromEnvironment()
    {
        var garage = Parse(Environment.GetEnvironmentVariable("ConnectionStrings__garage"), "garage");
        var bucket = Parse(Environment.GetEnvironmentVariable("ConnectionStrings__avatars"), "avatars");
        return new GarageProvisioningOptions(
            new Uri(Required(garage, "AdminUrl"), UriKind.Absolute),
            Required(garage, "AdminToken"),
            Required(bucket, "AccessKeyId"),
            Required(bucket, "SecretAccessKey"),
            Required(bucket, "BucketName"));
    }

    private static DbConnectionStringBuilder Parse(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The '{name}' Aspire connection string is required.");
        }

        return new DbConnectionStringBuilder { ConnectionString = value };
    }

    private static string Required(DbConnectionStringBuilder values, string key) =>
        values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"The Garage connection string is missing '{key}'.");
}
