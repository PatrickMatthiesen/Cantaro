using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace Cantaro.Api.Services;

public sealed record StoredAvatar(byte[] Content, string ContentType, string ETag);

public interface IAvatarStore
{
    Task<string> PutAsync(string objectKey, Stream content, long length, string contentType, CancellationToken cancellationToken);
    Task<StoredAvatar?> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class S3AvatarStore(IAmazonS3 client, S3ObjectStorageOptions options, ILogger<S3AvatarStore> logger) : IAvatarStore
{
    public async Task<string> PutAsync(
        string objectKey,
        Stream content,
        long length,
        string contentType,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            UseChunkEncoding = false
        };
        request.Headers.ContentLength = length;
        var response = await client.PutObjectAsync(request, cancellationToken);
        return NormalizeETag(response.ETag);
    }

    public async Task<StoredAvatar?> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(options.BucketName, objectKey, cancellationToken);
            await using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return new StoredAvatar(
                buffer.ToArray(),
                response.Headers.ContentType ?? "image/webp",
                NormalizeETag(response.ETag));
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteObjectAsync(options.BucketName, objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            logger.LogDebug("Avatar object {ObjectKey} was already absent.", objectKey);
        }
    }

    private static bool IsNotFound(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound
        && string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal);

    private static string NormalizeETag(string? etag) => etag?.Trim('"') ?? string.Empty;
}
