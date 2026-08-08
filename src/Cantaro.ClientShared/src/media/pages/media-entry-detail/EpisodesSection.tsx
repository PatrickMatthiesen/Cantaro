import { ExternalLink, Play, RefreshCcw } from 'lucide-react';
import type { CSSProperties } from 'react';
import type {
  MediaLibraryEntryDetailDto,
} from '../../services/mediaApi';
import {
  type MediaStreamingDestinations,
  type StreamingDestination,
} from '../../services/streamingDestinations';
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from '../../services/streamingServices';
import { StreamingServiceIcon } from '../../components/StreamingServiceIcon';
import { getEpisodeRows } from './episodeRows';
import type { EpisodeCatalogState } from './mediaEntryDetailTypes';
function getEpisodeProgressLabel(episodeNumber: number, watchedThrough: number) {
  if (episodeNumber <= watchedThrough) return 'Watched';
  if (episodeNumber === watchedThrough + 1) return 'Up next';
  return 'Not watched';
}

function getEpisodeServiceDestinations(
  episodeDestinations: readonly StreamingDestination[],
  seriesDestinations: readonly StreamingDestination[],
) {
  const serviceIds = new Set([
    ...episodeDestinations.map(destination => destination.serviceId),
    ...seriesDestinations.map(destination => destination.serviceId),
  ]);
  return [...serviceIds].map(serviceId => (
    episodeDestinations.find(destination => destination.serviceId === serviceId)
    ?? seriesDestinations.find(destination => destination.serviceId === serviceId)
  )).filter((destination): destination is StreamingDestination => Boolean(destination));
}

function EpisodeDestinationActions({
  episodeNumber,
  episodeDestinations,
  seriesDestinations,
  onSelectStreamingService,
}: {
  episodeNumber: number;
  episodeDestinations: readonly StreamingDestination[];
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
}) {
  const destinations = getEpisodeServiceDestinations(episodeDestinations, seriesDestinations);
  if (destinations.length === 0) return <span className="media-detail-episode-state">No streaming link</span>;

  return (
    <div className="media-detail-episode-destinations">
      {destinations.map(destination => {
        const service = STREAMING_SERVICES[destination.serviceId];
        const opensEpisode = destination.kind === 'episode';
        return (
          <a
            key={destination.serviceId}
            className="media-detail-streaming-destination"
            href={destination.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`${opensEpisode ? `Open episode ${episodeNumber}` : 'Open series'} on ${service.displayName}`}
            title={opensEpisode ? `Watch episode ${episodeNumber}` : 'Open series page'}
            style={{ '--streaming-service-color': service.brandColor } as CSSProperties}
            onClick={() => onSelectStreamingService(destination.serviceId)}
          >
            <StreamingServiceIcon serviceId={destination.serviceId} aria-hidden />
            <span>{service.displayName}</span>
            {opensEpisode ? <Play aria-hidden /> : <ExternalLink aria-hidden />}
          </a>
        );
      })}
    </div>
  );
}

function EpisodeRow({
  episodeNumber,
  destination,
  watchedThrough,
  seriesDestinations,
  onSelectStreamingService,
}: {
  episodeNumber: number;
  destination?: MediaStreamingDestinations['episodes'][number];
  watchedThrough: number;
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
}) {
  const isNext = episodeNumber === watchedThrough + 1;
  const progressLabel = getEpisodeProgressLabel(episodeNumber, watchedThrough);

  return (
    <li className={`media-detail-episode-row ${isNext ? 'is-next' : ''}`}>
      <span className="media-detail-episode-number" aria-hidden>{episodeNumber}</span>
      <div className="media-detail-episode-copy">
        <strong>{destination?.title || `Episode ${episodeNumber}`}</strong>
        <span>{progressLabel}</span>
      </div>
      <EpisodeDestinationActions
        episodeNumber={episodeNumber}
        episodeDestinations={destination?.destinations ?? []}
        seriesDestinations={seriesDestinations}
        onSelectStreamingService={onSelectStreamingService}
      />
    </li>
  );
}

function EpisodeSectionHeading({
  state,
  availableCount,
  onRefresh,
}: {
  state: EpisodeCatalogState;
  availableCount: number;
  onRefresh: () => void;
}) {
  const isLoading = state.status === 'loading';
  const summary = state.status === 'loaded'
    ? `${availableCount} episode links collected`
    : 'Loading collected episode links…';

  return (
    <div className="media-detail-episodes-heading">
      <div>
        <h3>Episodes</h3>
        <p>{summary}</p>
      </div>
      <div className="media-detail-episodes-actions">
        <button type="button" onClick={onRefresh} disabled={isLoading}>
          <RefreshCcw className={isLoading ? 'media-detail-spin' : ''} aria-hidden />
          Refresh links
        </button>
      </div>
    </div>
  );
}

function EpisodeSectionContent({
  entry,
  state,
  rows,
  seriesDestinations,
  onSelectStreamingService,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  rows: ReturnType<typeof getEpisodeRows>;
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
}) {
  if (state.status === 'error') {
    return (
      <div className="media-detail-episode-empty" role="alert">
        <p>Episode links could not be loaded.</p>
        <button type="button" onClick={onRefresh}>Try again</button>
      </div>
    );
  }

  if (state.status === 'loaded' && rows.length === 0) {
    return (
      <div className="media-detail-episode-empty">
        <p>No episode URLs have been observed for this title yet.</p>
        <span>Visit a supported streaming series page with the Cantaro extension enabled, then refresh this tab.</span>
      </div>
    );
  }

  if (rows.length === 0) return null;

  return (
    <ol className="media-detail-episode-list">
      {rows.map((row) => (
        <EpisodeRow
          key={row.episodeNumber}
          episodeNumber={row.episodeNumber}
          destination={row.destination}
          watchedThrough={entry.progressEpisodes ?? 0}
          seriesDestinations={seriesDestinations}
          onSelectStreamingService={onSelectStreamingService}
        />
      ))}
    </ol>
  );
}

function getEpisodeSectionData(
  entry: MediaLibraryEntryDetailDto,
  state: EpisodeCatalogState,
  streamingDestinations: MediaStreamingDestinations,
) {
  if (state.status !== 'loaded') {
    return { rows: [], availableCount: 0 };
  }
  return {
    rows: getEpisodeRows(entry, streamingDestinations.episodes),
    availableCount: streamingDestinations.episodes
      .filter(episode => episode.destinations.length > 0).length,
  };
}

export function EpisodesSection({
  entry,
  state,
  streamingDestinations,
  preferredServiceId,
  onSelectStreamingService,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  streamingDestinations: MediaStreamingDestinations;
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
}) {
  const { rows, availableCount } = getEpisodeSectionData(entry, state, streamingDestinations);
  const orderedSeriesDestinations = preferredServiceId
    ? [...streamingDestinations.seriesDestinations].sort((left, right) => {
      if (left.serviceId === preferredServiceId) return -1;
      if (right.serviceId === preferredServiceId) return 1;
      return 0;
    })
    : streamingDestinations.seriesDestinations;

  return (
    <section
      className="media-detail-section media-detail-episodes"
      role="tabpanel"
      id="media-detail-panel-episodes"
      aria-labelledby="media-detail-tab-episodes"
    >
      <EpisodeSectionHeading
        state={state}
        availableCount={availableCount}
        onRefresh={onRefresh}
      />
      <EpisodeSectionContent
        entry={entry}
        state={state}
        rows={rows}
        seriesDestinations={orderedSeriesDestinations}
        onSelectStreamingService={onSelectStreamingService}
        onRefresh={onRefresh}
      />
    </section>
  );
}
