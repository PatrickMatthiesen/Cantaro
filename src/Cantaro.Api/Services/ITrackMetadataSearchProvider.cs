using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface ITrackMetadataSearchProvider
{
    Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(ObservationInput observation, CancellationToken cancellationToken);
}
