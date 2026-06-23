using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Cantaro.Aspire.Hosting.Garage;

public static class GarageBuilderExtensions
{
    public static IResourceBuilder<GarageResource> AddGarage(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<ParameterResource>? rpcSecret = null,
        IResourceBuilder<ParameterResource>? adminToken = null,
        IResourceBuilder<ParameterResource>? accessKeyId = null,
        IResourceBuilder<ParameterResource>? secretAccessKey = null,
        string? configPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        rpcSecret ??= AddGeneratedSecret(builder, $"{name}-rpc-secret", 64);
        adminToken ??= AddGeneratedSecret(builder, $"{name}-admin-token", 64);
        accessKeyId ??= AddGeneratedSecret(builder, $"{name}-access-key-id", 26);
        secretAccessKey ??= AddGeneratedSecret(builder, $"{name}-secret-access-key", 64);

        configPath ??= Path.Combine(builder.AppHostDirectory, "Garage", "garage.toml");
        var resource = new GarageResource(
            name,
            rpcSecret.Resource,
            adminToken.Resource,
            accessKeyId.Resource,
            secretAccessKey.Resource);

        return builder.AddResource(resource)
            .WithImage(GarageContainerImageTags.Image)
            .WithImageTag(GarageContainerImageTags.Tag)
            .WithImageSHA256(GarageContainerImageTags.Digest)
            .WithContainerFiles("/etc",
            [
                new ContainerFile
                {
                    Name = Path.GetFileName(GarageResource.ConfigPath),
                    Contents = File.ReadAllText(configPath)
                }
            ])
            .WithEnvironment("GARAGE_CONFIG_FILE", GarageResource.ConfigPath)
            .WithEnvironment("GARAGE_RPC_SECRET", rpcSecret)
            .WithEnvironment("GARAGE_ADMIN_TOKEN", adminToken)
            .WithArgs("/garage", "server")
            .WithEndpoint(targetPort: 3900, scheme: "http", name: GarageResource.S3EndpointName)
            .WithEndpoint(targetPort: 3903, scheme: "http", name: GarageResource.AdminEndpointName)
            .WithEndpoint(targetPort: 3901, name: GarageResource.RpcEndpointName)
            .WithEndpoint(GarageResource.S3EndpointName, endpoint => endpoint.TargetHost = "127.0.0.1")
            .WithEndpoint(GarageResource.AdminEndpointName, endpoint => endpoint.TargetHost = "127.0.0.1")
            .WithHttpHealthCheck("/health", endpointName: GarageResource.AdminEndpointName);
    }

    public static IResourceBuilder<GarageBucketResource> AddBucket(
        this IResourceBuilder<GarageResource> garage,
        string name,
        string bucketName)
    {
        ArgumentNullException.ThrowIfNull(garage);
        var bucket = new GarageBucketResource(name, bucketName, garage.Resource);
        return garage.ApplicationBuilder.AddResource(bucket);
    }

    private static IResourceBuilder<ParameterResource> AddGeneratedSecret(
        IDistributedApplicationBuilder builder,
        string name,
        int length) => builder.AddParameter(
            name,
            new GenerateParameterDefault
            {
                MinLength = length,
                Numeric = true,
                MinNumeric = length,
                Lower = false,
                Upper = false,
                Special = false
            },
            secret: true,
            persist: true);
}
