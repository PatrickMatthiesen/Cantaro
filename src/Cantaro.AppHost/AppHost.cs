using Aspire.Hosting.JavaScript;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var db = postgres.AddDatabase("cantaro-db");

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithReference(db);

// Add frontend
var frontend = builder.AddViteApp("web", "../Cantaro.Web")
    .WithReference(api);

var browserExtension = builder.AddWXTBrowserExtension("browser-extension", "npm", "../Cantaro.BrowserExtension")
    .WithReference(api);

builder.Build().Run();

#pragma warning disable ASPIREDOCKERFILEBUILDER001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

public static class BuilderExtensions
{

    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<JavaScriptAppResource> AddWXTBrowserExtension(
            string name,
            string command,
            string appDirectory,
            Action<CommandLineArgsCallbackContext>? argsCallback = null)
        {
            var runScriptName = "dev";
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(appDirectory);
            ArgumentException.ThrowIfNullOrEmpty(runScriptName);

            static string NormalizePathForCurrentPlatform(string path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return path;
                }

                // Fix slashes
                path = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

                return Path.GetFullPath(path);
            }

            appDirectory = NormalizePathForCurrentPlatform(Path.Combine(builder.AppHostDirectory, appDirectory));
            var resource = new JavaScriptAppResource(name, command, appDirectory);

            static string GetDefaultBaseImage(string defaultSuffix)
            {
                return $"oven/bun:{defaultSuffix}";
            }

            static void AddInstallCommand(Aspire.Hosting.ApplicationModel.Docker.DockerfileStage builderStage, JavaScriptPackageManagerAnnotation packageManager, JavaScriptInstallCommandAnnotation installCommand)
            {
                // Use BuildKit cache mount for package manager cache if available
                var installCmd = $"{packageManager.ExecutableName} {string.Join(' ', installCommand.Args)}";
                if (!string.IsNullOrEmpty(packageManager.CacheMount))
                {
                    builderStage.Run($"--mount=type=cache,target={packageManager.CacheMount} {installCmd}");
                }
                else
                {
                    builderStage.Run(installCmd);
                }
            }

            var resourceBuilder = builder.AddResource(resource)
                .ExcludeFromMcp()
                .WithOtlpExporter()
                .WithCertificateTrustConfiguration((ctx) =>
                {
                    if (ctx.Scope == CertificateTrustScope.Append)
                    {
                        ctx.EnvironmentVariables["NODE_EXTRA_CA_CERTS"] = ctx.CertificateBundlePath;
                    }
                    else
                    {
                        ctx.Arguments.Add("--use-openssl-ca");
                    }

                    return Task.CompletedTask;
                })
                .WithArgs(c =>
                {
                    if (c.Resource.TryGetLastAnnotation<JavaScriptRunScriptAnnotation>(out var runCommand))
                    {
                        if (c.Resource.TryGetLastAnnotation<JavaScriptPackageManagerAnnotation>(out var packageManager) &&
                            !string.IsNullOrEmpty(packageManager.ScriptCommand))
                        {
                            c.Args.Add(packageManager.ScriptCommand);
                        }

                        c.Args.Add(runCommand.ScriptName);

                        foreach (var arg in runCommand.Args)
                        {
                            c.Args.Add(arg);
                        }
                    }

                    Console.WriteLine(runCommand);

                    argsCallback?.Invoke(c);
                })
                .WithIconName("CodeJsRectangle")
                .WithBun()
                .PublishAsDockerFile(c =>
                {
                    // Only generate a Dockerfile if one doesn't already exist in the app directory
                    if (File.Exists(Path.Combine(appDirectory, "Dockerfile")))
                    {
                        return;
                    }

                    c.WithDockerfileBuilder(appDirectory, dockerfileContext =>
                    {
                        if (c.Resource.TryGetLastAnnotation<JavaScriptPackageManagerAnnotation>(out var packageManager))
                        {
                            // Get custom base image from annotation, if present
                            dockerfileContext.Resource.TryGetLastAnnotation<DockerfileBaseImageAnnotation>(out var baseImageAnnotation);
                            var baseImage = baseImageAnnotation?.BuildImage ?? GetDefaultBaseImage("slim");

                            var dockerBuilder = dockerfileContext.Builder
                                .From(baseImage)
                                .WorkDir("/app");

                            var copiedAllSource = false;

                            // Copy package files first for better layer caching
                            if (packageManager.PackageFilesPatterns.Count > 0)
                            {
                                foreach (var packageFilePattern in packageManager.PackageFilesPatterns)
                                {
                                    dockerBuilder.Copy(packageFilePattern.Source, packageFilePattern.Destination);
                                }
                            }
                            else
                            {
                                dockerBuilder.Copy(".", ".");
                                copiedAllSource = true;
                            }

                            if (c.Resource.TryGetLastAnnotation<JavaScriptInstallCommandAnnotation>(out var installCommand))
                            {
                                AddInstallCommand(dockerBuilder, packageManager, installCommand);
                            }

                            if (!copiedAllSource)
                            {
                                // Copy application source code after dependencies are installed
                                dockerBuilder.Copy(".", ".");
                            }

                            if (c.Resource.TryGetLastAnnotation<JavaScriptBuildScriptAnnotation>(out var buildCommand))
                            {
                                var commandArgs = new List<string>() { packageManager.ExecutableName };
                                if (!string.IsNullOrEmpty(packageManager.ScriptCommand))
                                {
                                    commandArgs.Add(packageManager.ScriptCommand);
                                }
                                commandArgs.Add(buildCommand.ScriptName);
                                commandArgs.AddRange(buildCommand.Args);

                                dockerBuilder.Run(string.Join(' ', commandArgs));
                            }
                        }
                    });

                    // Javascript apps don't have an entrypoint
                    if (resource.TryGetLastAnnotation<DockerfileBuildAnnotation>(out var dockerFileAnnotation))
                    {
                        dockerFileAnnotation.HasEntrypoint = false;
                    }
                    else
                    {
                        throw new InvalidOperationException("DockerfileBuildAnnotation should exist after calling PublishAsDockerFile.");
                    }
                })
            .WithRunScript("dev")
            .WithArgs(ctx =>
            {
                ctx.Args.Add("--port");
                ctx.Args.Add("5174");
                return Task.CompletedTask;
            });
            // .WithUrl("http://localhost:5174")
            // .WithExternalHttpEndpoints();

            resourceBuilder.WithEndpoint(
                name: "http",
                scheme: "http",
                port: 5174,
                isProxied: false);
                
            // ADD THIS: Ensure the command is set before starting
            if (builder.ExecutionContext.IsRunMode)
            {
                builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
                {
                    if (resourceBuilder.Resource.TryGetLastAnnotation<JavaScriptPackageManagerAnnotation>(out var packageManager))
                    {
                        resourceBuilder.WithCommand(packageManager.ExecutableName);
                    }
                    return Task.CompletedTask;
                });
            }

            return resourceBuilder;
        }


    }

    extension<TResource>(IResourceBuilder<TResource> resource) where TResource : JavaScriptAppResource
    {
        public IResourceBuilder<TResource> WithBun(
            bool install = true,
            string? installCommand = null,
            string[]? installArgs = null)
        {
            ArgumentNullException.ThrowIfNull(resource);

            installCommand ??= "install";

            resource
                .WithAnnotation(new JavaScriptPackageManagerAnnotation(
                    executableName: "bun",
                    runScriptCommand: "run",
                    cacheMount: "/root/.bun")
                {
                    PackageFilesPatterns =
                    {
                        new CopyFilePattern("package*.json", "./"),
                        new CopyFilePattern("bun.lockb", "./"),
                    }
                })
                .WithAnnotation(new JavaScriptInstallCommandAnnotation(Combine(installCommand, installArgs)));

            AddBunInstaller(resource, install);
            return resource;

            static string[] Combine(string cmd, string[]? extra)
            {
                if (extra is { Length: > 0 })
                {
                    var result = new string[1 + extra.Length];
                    result[0] = cmd;
                    Array.Copy(extra, 0, result, 1, extra.Length);
                    return result;
                }
                return [cmd];
            }
        }








        public static void AddBunInstaller(IResourceBuilder<TResource> resourceBuilder, bool install)
        {
            // Only install packages if in run mode
            if (resourceBuilder.ApplicationBuilder.ExecutionContext.IsRunMode)
            {
                // Check if the installer resource already exists
                var installerName = $"{resourceBuilder.Resource.Name}-installer";
                resourceBuilder.ApplicationBuilder.TryCreateResourceBuilder<JavaScriptInstallerResource>(installerName, out var existingResource);

                if (!install)
                {
                    if (existingResource != null)
                    {
                        // Remove existing installer resource if install is false
                        resourceBuilder.ApplicationBuilder.Resources.Remove(existingResource.Resource);
                        resourceBuilder.Resource.Annotations.OfType<WaitAnnotation>()
                            .Where(w => w.Resource == existingResource.Resource)
                            .ToList()
                            .ForEach(w => resourceBuilder.Resource.Annotations.Remove(w));
                        resourceBuilder.Resource.Annotations.OfType<JavaScriptPackageInstallerAnnotation>()
                            .ToList()
                            .ForEach(a => resourceBuilder.Resource.Annotations.Remove(a));
                    }
                    else
                    {
                        // No installer needed
                    }
                    return;
                }

                if (existingResource is not null)
                {
                    // Installer already exists
                    return;
                }

                var installer = new JavaScriptInstallerResource(installerName, resourceBuilder.Resource.WorkingDirectory);
                var installerBuilder = resourceBuilder.ApplicationBuilder.AddResource(installer)
                    .WithParentRelationship(resourceBuilder.Resource)
                    .ExcludeFromManifest();

                resourceBuilder.ApplicationBuilder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
                {
                    // set the installer's working directory to match the resource's working directory
                    // and set the install command and args based on the resource's annotations
                    if (!resourceBuilder.Resource.TryGetLastAnnotation<JavaScriptPackageManagerAnnotation>(out var packageManager) ||
                        !resourceBuilder.Resource.TryGetLastAnnotation<JavaScriptInstallCommandAnnotation>(out var installCommand))
                    {
                        throw new InvalidOperationException("JavaScriptPackageManagerAnnotation and JavaScriptInstallCommandAnnotation are required when installing packages.");
                    }

                    installerBuilder
                        .WithCommand(packageManager.ExecutableName)
                        .WithWorkingDirectory(resourceBuilder.Resource.WorkingDirectory)
                        .WithArgs(installCommand.Args);

                    return Task.CompletedTask;
                });

                // Make the parent resource wait for the installer to complete
                resourceBuilder.WaitForCompletion(installerBuilder);

                resourceBuilder.WithAnnotation(new JavaScriptPackageInstallerAnnotation(installer));
            }
        }
    }
}