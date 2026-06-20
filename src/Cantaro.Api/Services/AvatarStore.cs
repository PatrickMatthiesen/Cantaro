using Minio;
using Minio.DataModel.Args;

namespace Cantaro.Api.Services;

public sealed record StoredAvatar(byte[] Content, string ContentType, string ETag);

public interface IAvatarStore
{
    Task<string> PutAsync(string objectKey, Stream content, long length, string contentType, CancellationToken cancellationToken);
    Task<StoredAvatar?> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class MinioAvatarStore(IMinioClient client, ILogger<MinioAvatarStore> logger) : IAvatarStore
{
    private const string BucketName = "cantaro-avatars";
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public async Task<string> PutAsync(string objectKey, Stream content, long length, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var response = await client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectKey)
                .WithStreamData(content)
                .WithObjectSize(length)
                .WithContentType(contentType),
            cancellationToken);
        return response.Etag;
    }

    public async Task<StoredAvatar?> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        try
        {
            var stat = await client.StatObjectAsync(
                new StatObjectArgs().WithBucket(BucketName).WithObject(objectKey),
                cancellationToken);
            await using var buffer = new MemoryStream((int)stat.Size);
            await client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(BucketName)
                    .WithObject(objectKey)
                    .WithCallbackStream(stream => stream.CopyTo(buffer)),
                cancellationToken);
            return new StoredAvatar(buffer.ToArray(), stat.ContentType ?? "image/webp", stat.ETag);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        try
        {
            await client.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(BucketName).WithObject(objectKey),
                cancellationToken);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            logger.LogDebug("Avatar object {ObjectKey} was already absent.", objectKey);
        }
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady) return;
        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketReady) return;
            var exists = await client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(BucketName),
                cancellationToken);
            if (!exists)
            {
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(BucketName), cancellationToken);
            }
            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }
}
