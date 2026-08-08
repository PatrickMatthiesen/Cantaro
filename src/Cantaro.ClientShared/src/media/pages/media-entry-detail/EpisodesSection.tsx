import { ExternalLink, Play, RefreshCcw } from 'lucide-react';
import type {
  MediaEpisodeCatalogDto,
  MediaEpisodeDestinationDto,
  MediaLibraryEntryDetailDto,
} from '../../services/mediaApi';
import { getEpisodeRows } from './episodeRows';
import type { EpisodeCatalogState } from './mediaEntryDetailTypes';
function getEpisodeDestinationLabel(destination?: MediaEpisodeDestinationDto) {
  if (destination?.hasConflict) return 'Needs review';
  if (destination?.url) return 'Link available';
  return 'Not observed yet';
}

function getEpisodeProgressLabel(episodeNumber: number, watchedThrough: number) {
  if (episodeNumber <= watchedThrough) return 'Watched';
  if (episodeNumber === watchedThrough + 1) return 'Up next';
  return 'Not watched';
}

function getEpisodeDestinationAction(
  episodeNumber: number,
  destination?: MediaEpisodeDestinationDto,
  fallbackUrl?: string,
) {
  if (destination?.url && !destination.hasConflict) {
    return {
      url: destination.url,
      label: 'Watch',
      ariaLabel: `Open episode ${episodeNumber} on Crunchyroll`,
      icon: <Play aria-hidden />,
    };
  }
  if (!fallbackUrl) return null;
  return {
    url: fallbackUrl,
    label: 'Open',
    ariaLabel: `Open the Crunchyroll series page for episode ${episodeNumber}`,
    icon: <ExternalLink aria-hidden />,
  };
}

function EpisodeDestinationAction({
  episodeNumber,
  destination,
  stateLabel,
  fallbackUrl,
}: {
  episodeNumber: number;
  destination?: MediaEpisodeDestinationDto;
  stateLabel: string;
  fallbackUrl?: string;
}) {
  const action = getEpisodeDestinationAction(episodeNumber, destination, fallbackUrl);
  if (!action) {
    return <span className="media-detail-episode-state">{stateLabel}</span>;
  }

  return (
    <a
      href={action.url}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={action.ariaLabel}
    >
      {action.icon}
      {action.label}
    </a>
  );
}

function EpisodeRow({
  episodeNumber,
  destination,
  watchedThrough,
  fallbackUrl,
}: {
  episodeNumber: number;
  destination?: MediaEpisodeDestinationDto;
  watchedThrough: number;
  fallbackUrl?: string;
}) {
  const isNext = episodeNumber === watchedThrough + 1;
  const stateLabel = getEpisodeDestinationLabel(destination);
  const progressLabel = getEpisodeProgressLabel(episodeNumber, watchedThrough);

  return (
    <li className={`media-detail-episode-row ${isNext ? 'is-next' : ''}`}>
      <span className="media-detail-episode-number" aria-hidden>{episodeNumber}</span>
      <div className="media-detail-episode-copy">
        <strong>{destination?.title || `Episode ${episodeNumber}`}</strong>
        <span>{progressLabel}</span>
      </div>
      <EpisodeDestinationAction
        episodeNumber={episodeNumber}
        destination={destination}
        stateLabel={stateLabel}
        fallbackUrl={fallbackUrl}
      />
    </li>
  );
}

function EpisodeSectionHeading({
  state,
  catalog,
  availableCount,
  onRefresh,
}: {
  state: EpisodeCatalogState;
  catalog: MediaEpisodeCatalogDto | null;
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
        {catalog?.seriesUrl ? (
          <a href={catalog.seriesUrl} target="_blank" rel="noopener noreferrer">
            <ExternalLink aria-hidden />
            Open series on Crunchyroll
          </a>
        ) : null}
      </div>
    </div>
  );
}

function EpisodeSectionContent({
  entry,
  state,
  rows,
  fallbackUrl,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  rows: ReturnType<typeof getEpisodeRows>;
  fallbackUrl?: string;
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
        <span>Visit its Crunchyroll series page with the Cantaro extension enabled, then refresh this tab.</span>
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
          fallbackUrl={fallbackUrl}
        />
      ))}
    </ol>
  );
}

function getEpisodeSectionData(
  entry: MediaLibraryEntryDetailDto,
  state: EpisodeCatalogState,
  fallbackSeriesUrl: string | null,
) {
  if (state.status !== 'loaded') {
    return { catalog: null, rows: [], availableCount: 0 };
  }

  const catalog = {
    ...state.value,
    seriesUrl: state.value.seriesUrl ?? fallbackSeriesUrl ?? undefined,
  };
  return {
    catalog,
    rows: getEpisodeRows(entry, catalog.episodes),
    availableCount: catalog.episodes.filter((episode) => episode.url && !episode.hasConflict).length,
  };
}

export function EpisodesSection({
  entry,
  state,
  seriesUrl,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  seriesUrl: string | null;
  onRefresh: () => void;
}) {
  const { catalog, rows, availableCount } = getEpisodeSectionData(entry, state, seriesUrl);

  return (
    <section
      className="media-detail-section media-detail-episodes"
      role="tabpanel"
      id="media-detail-panel-episodes"
      aria-labelledby="media-detail-tab-episodes"
    >
      <EpisodeSectionHeading
        state={state}
        catalog={catalog}
        availableCount={availableCount}
        onRefresh={onRefresh}
      />
      <EpisodeSectionContent
        entry={entry}
        state={state}
        rows={rows}
        fallbackUrl={catalog?.seriesUrl}
        onRefresh={onRefresh}
      />
    </section>
  );
}
