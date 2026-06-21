using System.Data.Common;
using Amazon.Runtime;
using Amazon.S3;

namespace Cantaro.Api.Services;

public sealed record S3ObjectStorageOptions(string ServiceUrl, string AccessKeyId, string SecretAccessKey, string Region, string BucketName);

public static class S3ObjectStorageExtensions
{
    public static WebApplicationBuilder AddS3ObjectStorage(this WebApplicationBuilder builder, string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is required.");
        var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var serviceUrl = Required(values, "ServiceUrl");
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var endpoint)
            || (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The S3 ServiceUrl must be an absolute HTTP or HTTPS endpoint.");
        }

        var options = new S3ObjectStorageOptions(
            endpoint.ToString().TrimEnd('/'),
            Required(values, "AccessKeyId"),
            Required(values, "SecretAccessKey"),
            Required(values, "Region"),
            Required(values, "BucketName"));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = options.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            }));
        return builder;
    }

    private static string Required(DbConnectionStringBuilder values, string key) =>
        values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"The S3 connection string is missing '{key}'.");
}
