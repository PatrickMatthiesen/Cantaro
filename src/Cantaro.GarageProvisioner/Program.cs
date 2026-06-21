using Cantaro.GarageProvisioner;

var options = GarageProvisioningOptions.FromEnvironment();
using var httpClient = GarageAdminClient.CreateHttpClient(options);
var garage = new GarageAdminClient(httpClient);

await garage.WaitUntilReachableAsync(CancellationToken.None);
await garage.EnsureSingleNodeLayoutAsync(options.CapacityBytes, CancellationToken.None);
await garage.WaitUntilReachableAsync(CancellationToken.None);
await garage.EnsureKeyAsync(options.AccessKeyId, options.SecretAccessKey, CancellationToken.None);
var bucketId = await garage.EnsureBucketAsync(options.BucketName, CancellationToken.None);
await garage.EnsureBucketPermissionsAsync(bucketId, options.AccessKeyId, CancellationToken.None);

Console.WriteLine("Garage layout, application key, bucket, and permissions are ready.");
