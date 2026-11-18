using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

// Use a separate tracker so recording an auth failure cannot commit caller work.
internal static class ProviderReconnectState
{
    internal static async Task<bool> MarkAsync(ApplicationDbContext caller,
        ConnectedServiceAccount account, string reason, CancellationToken ct)
    {
        await using var db = PlaylistReadIngestion.CreateContext(caller);
        var now = DateTime.UtcNow;
        return await db.ConnectedServiceAccounts.Where(item => item.Id == account.Id
            && item.UserId == account.UserId && item.Service == account.Service
            && item.ExternalAccountId == account.ExternalAccountId && item.TokenVersion == account.TokenVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.ConnectionState, "reconnect_required")
                .SetProperty(item => item.ReconnectReason, reason)
                .SetProperty(item => item.ReconnectRequiredAt, now)
                .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1)
                .SetProperty(item => item.UpdatedAt, now), ct) == 1;
    }

    internal static void RequireUsable(ConnectedServiceAccount account)
    {
        if (account.ConnectionState != "connected")
            throw new PlatformReconnectRequiredException($"Reconnect {account.Service} to continue.");
    }

    internal static async Task<bool> MarkOrAlreadyRequiredAsync(ApplicationDbContext caller,
        ConnectedServiceAccount account, string reason, CancellationToken ct)
    {
        if (await MarkAsync(caller, account, reason, ct)) return true;
        await using var db = PlaylistReadIngestion.CreateContext(caller);
        return await db.ConnectedServiceAccounts.AnyAsync(item => item.Id == account.Id
            && item.UserId == account.UserId && item.Service == account.Service
            && item.ExternalAccountId == account.ExternalAccountId && item.ConnectionState == "reconnect_required", ct);
    }
}
