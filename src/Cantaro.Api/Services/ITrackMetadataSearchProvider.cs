using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface ITrackMetadataSearchProvider
{
    Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken);
}
