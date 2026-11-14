using Cantaro.Api.Models;
namespace Cantaro.Api.Tests;
internal static class TrackMatchFixtures
{
    public static ProviderCatalogItem Associate(ProviderCatalogItem item, Guid trackId, RecordingAssociationState state)
    {
        item.TrackId = trackId; item.AssociationState = state;
        if (state == RecordingAssociationState.Verified)
        {
            item.AssociationFactsRevision = item.Revision; item.AssociationRevision = 1;
            item.AssociationMethod = "provider_origin"; item.AssociationVerifiedAt = item.VerifiedAt;
            item.AssociationEvidenceJson = item.FactsJson;
        }
        return item;
    }
}
