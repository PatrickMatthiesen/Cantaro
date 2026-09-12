using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Cantaro.Api.Configuration;

internal enum ApiRateLimitBucket
{
    None,
    Identity,
    ExtensionAuthentication,
    Costly,
    GeneralApi
}

public static class ApiRateLimitingConfiguration
{
    private static readonly TimeSpan FixedWindow = TimeSpan.FromMinutes(1);

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = HandleRejectedAsync;

        var generalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var bucket = Classify(context);
            if (bucket is ApiRateLimitBucket.None or ApiRateLimitBucket.Costly)
            {
                return RateLimitPartition.GetNoLimiter("general-excluded");
            }

            var partitionKey = $"{bucket}:{GetClientKey(context, bucket)}";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = bucket switch
                    {
                        ApiRateLimitBucket.Identity => 10,
                        ApiRateLimitBucket.ExtensionAuthentication => 30,
                        _ => 120
                    },
                    Window = FixedWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });

        var costlyWindowLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (Classify(context) is not ApiRateLimitBucket.Costly)
            {
                return RateLimitPartition.GetNoLimiter("costly-window-excluded");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"costly-window:{GetClientKey(context, ApiRateLimitBucket.Costly)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = FixedWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });

        var costlyConcurrencyLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (Classify(context) is not ApiRateLimitBucket.Costly)
            {
                return RateLimitPartition.GetNoLimiter("costly-concurrency-excluded");
            }

            return RateLimitPartition.GetConcurrencyLimiter(
                $"costly-concurrency:{GetClientKey(context, ApiRateLimitBucket.Costly)}",
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 2,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });

        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            generalLimiter,
            costlyConcurrencyLimiter,
            costlyWindowLimiter);
    }

    internal static ApiRateLimitBucket Classify(HttpContext context)
    {
        var path = NormalizePath(context.Request.Path);
        if (!path.StartsWithSegments("/api"))
        {
            return ApiRateLimitBucket.None;
        }

        if (IsIdentityEndpoint(path))
        {
            return ApiRateLimitBucket.Identity;
        }

        if (path.StartsWithSegments("/api/auth/extension"))
        {
            return ApiRateLimitBucket.ExtensionAuthentication;
        }

        if (IsCostlyEndpoint(context, path))
        {
            return ApiRateLimitBucket.Costly;
        }

        return ApiRateLimitBucket.GeneralApi;
    }

    private static PathString NormalizePath(PathString path)
    {
        var value = path.Value?.TrimEnd('/');
        return new PathString(string.IsNullOrEmpty(value) ? "/" : value);
    }

    private static bool IsIdentityEndpoint(PathString path) =>
        string.Equals(path.Value, "/api/register", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/login", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/refresh", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/confirmEmail", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/forgotPassword", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/resetPassword", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/resendConfirmationEmail", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/auth/google/login", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/auth/google/link", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/auth/google/link/authorize", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/auth/google/reauth", StringComparison.OrdinalIgnoreCase);

    private static bool IsCostlyEndpoint(HttpContext context, PathString path)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return false;
        }

        return path.StartsWithSegments("/api/sync")
            || path.StartsWithSegments("/api/matching")
            || path.StartsWithSegments("/api/media/providers")
                && (path.Value?.Contains("/import", StringComparison.OrdinalIgnoreCase) == true
                    || path.Value?.Contains("/initial-sync", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string GetClientKey(HttpContext context, ApiRateLimitBucket bucket)
    {
        if (bucket is not ApiRateLimitBucket.Identity and not ApiRateLimitBucket.ExtensionAuthentication)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"user:{userId}";
            }
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static ValueTask HandleRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfterSeconds = 1;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata)
            && retryAfterMetadata is TimeSpan retryAfter)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    }
}
