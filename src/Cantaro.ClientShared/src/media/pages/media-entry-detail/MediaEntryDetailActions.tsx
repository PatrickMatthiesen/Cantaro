import {
  CheckCircle2,
  Clock3,
  ExternalLink,
  Minus,
  MoreVertical,
  Play,
  Plus,
  RefreshCcw,
  Save,
  Star,
} from 'lucide-react';
import { type ProviderAvailabilityMap } from '../../components/media-entry-detail/providerAvailability';
import { formatNextReleaseDisplay } from '../../services/mediaFormatting';
import type { MediaContinueWatchingDto } from '../../services/mediaApi';
import {
  clampProgressValue,
  getEntryStatusChanged,
  getProgressCapabilities,
  getProgressPercent,
  getPrimaryProgressSummary,
  getRemainingLabel,
  getStatusSaveLabel,
  isStatusRefreshDisabled,
} from './mediaEntryDetailModel';
import { ProgressRing } from './MediaEntryDetailHero';
import { getContinueLinkAction } from './continueWatchingAction';
import type {
  ContinueWatchingState,
  MediaEntryDetailContentProps,
} from './mediaEntryDetailTypes';

const NORMALIZED_STATUSES = [
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planned', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

function ProgressStepper({
  label,
  value,
  max,
  onChange,
}: {
  label: string;
  value: number | undefined;
  max?: number;
  onChange: (value: number) => void;
}) {
  const currentValue = clampProgressValue(value ?? 0, max);
  const sliderMax = Math.max(max ?? 100, currentValue, 1);
  const canDecrease = currentValue > 0;
  const canIncrease = max ? currentValue < max : true;
  const setProgressValue = (nextValue: number) => onChange(clampProgressValue(nextValue, max));

  return (
    <div className="media-detail-stepper">
      <div className="media-detail-stepper-row">
        <button
          type="button"
          onClick={() => setProgressValue(currentValue - 1)}
          disabled={!canDecrease}
          aria-label={`Decrease ${label.toLowerCase()}`}
        >
          <Minus aria-hidden />
        </button>
        <input
          type="range"
          min={0}
          max={sliderMax}
          step={1}
          value={currentValue}
          onChange={(event) => setProgressValue(Number(event.target.value))}
          aria-label={`${label} progress`}
        />
        <button
          type="button"
          onClick={() => setProgressValue(currentValue + 1)}
          disabled={!canIncrease}
          aria-label={`Increase ${label.toLowerCase()}`}
        >
          <Plus aria-hidden />
        </button>
      </div>
      <p>{currentValue}{max ? ` / ${max}` : ''}</p>
    </div>
  );
}

type ProgressCockpitProps = Pick<
  MediaEntryDetailContentProps,
  'entry'
  | 'selectedStatus'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'isRefreshingProgress'
  | 'isSavingStatus'
  | 'onSetSelectedStatus'
  | 'onSetProgressEpisodes'
  | 'onSetProgressChapters'
  | 'onSetProgressVolumes'
  | 'onRefreshProgress'
  | 'onSaveStatus'
>;

function ProgressSummaryBlock({
  progressSummary,
  percent,
}: {
  progressSummary: ReturnType<typeof getPrimaryProgressSummary>;
  percent: number;
}) {
  return (
    <div className="media-detail-progress-main">
      <ProgressRing percent={percent} />
      <div>
        <p>{progressSummary.label}</p>
        <strong>{progressSummary.value ?? 0}{progressSummary.total ? ` / ${progressSummary.total}` : ''}</strong>
        <span>{getRemainingLabel(progressSummary)}</span>
      </div>
    </div>
  );
}

function ProgressSyncStatus({ hasStatusChanged }: { hasStatusChanged: boolean }) {
  return (
    <span className={`media-detail-sync-chip ${hasStatusChanged ? 'media-detail-sync-chip--pending' : 'media-detail-sync-chip--ok'}`}>
      {hasStatusChanged ? <Clock3 aria-hidden /> : <CheckCircle2 aria-hidden />}
      {hasStatusChanged ? 'Unsaved' : 'Synced'}
    </span>
  );
}

function ProgressControls({
  props,
  capabilities,
}: {
  props: ProgressCockpitProps;
  capabilities: ReturnType<typeof getProgressCapabilities>;
}) {
  return (
    <>
      {capabilities.supportsEpisodes ? (
        <ProgressStepper label="Episodes" value={props.progressEpisodes} max={props.entry.title.episodeCount} onChange={props.onSetProgressEpisodes} />
      ) : null}
      {capabilities.supportsChapters ? (
        <ProgressStepper label="Chapters" value={props.progressChapters} max={props.entry.title.chapterCount} onChange={props.onSetProgressChapters} />
      ) : null}
      {capabilities.supportsVolumes ? (
        <ProgressStepper label="Volumes" value={props.progressVolumes} max={props.entry.title.volumeCount} onChange={props.onSetProgressVolumes} />
      ) : null}
    </>
  );
}

function ProgressScoreRow({ props }: { props: ProgressCockpitProps }) {
  return (
    <div className="media-detail-score-row">
      <div>
        <p>Your score</p>
        <div aria-label="User score unavailable">
          {[1, 2, 3, 4, 5].map((star) => <Star key={star} aria-hidden />)}
        </div>
      </div>
      <select value={props.selectedStatus} onChange={(event) => props.onSetSelectedStatus(event.target.value)}>
        {NORMALIZED_STATUSES.map((status) => (
          <option key={status.value} value={status.value}>{status.label}</option>
        ))}
      </select>
      <button
        type="button"
        onClick={props.onRefreshProgress}
        disabled={isStatusRefreshDisabled(props.entry.isConnected, props.isSavingStatus, props.isRefreshingProgress)}
        aria-busy={props.isRefreshingProgress}
      >
        <RefreshCcw className={props.isRefreshingProgress ? 'media-detail-spin' : ''} aria-hidden />
        Refresh
      </button>
    </div>
  );
}

export function ProgressCockpit(props: ProgressCockpitProps) {
  const capabilities = getProgressCapabilities(props.entry.title);
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const percent = getProgressPercent(progressSummary);
  const hasStatusChanged = getEntryStatusChanged(props);

  return (
    <section className="media-detail-progress-card">
      <ProgressSummaryBlock progressSummary={progressSummary} percent={percent} />
      <div className="media-detail-progress-next">
        <div className="media-detail-progress-next-head">
          <div>
            <p>{progressSummary.nextLabel}</p>
            <span>Update progress and sync when the change is ready.</span>
          </div>
          <ProgressSyncStatus hasStatusChanged={hasStatusChanged} />
        </div>
        <ProgressControls props={props} capabilities={capabilities} />
      </div>
      <ProgressScoreRow props={props} />
    </section>
  );
}

function SaveProgressAction({
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
}: {
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
}) {
  return (
    <button
      type="button"
      className="media-detail-primary-action"
      onClick={onSaveStatus}
      disabled={isSavingStatus || isRefreshingProgress}
      aria-busy={isSavingStatus}
    >
      <Save aria-hidden />
      <span>{getStatusSaveLabel(isSavingStatus)}</span>
    </button>
  );
}

function getContinueWatchingLabel(state: ContinueWatchingState): string {
  if (state.status === 'loading') return 'Finding episode...';
  if (state.status === 'error') return "Couldn't load Crunchyroll link";

  const labels: Record<Exclude<MediaContinueWatchingDto['outcome'], 'direct'>, string> = {
    series_fallback: 'Open series on Crunchyroll',
    completed: 'Completed',
    conflict: 'Episode link needs review',
    unavailable: 'Search Crunchyroll',
  };
  return state.value.outcome === 'direct'
    ? `Continue episode ${state.value.episodeNumber ?? ''}`.trim()
    : labels[state.value.outcome];
}

function parseAbsoluteUrl(value: string): URL | null {
  try {
    return new URL(value);
  } catch {
    return null;
  }
}

const CRUNCHYROLL_HOSTS = new Set(['crunchyroll.com', 'www.crunchyroll.com']);
const CRUNCHYROLL_SERIES_PATH = /^\/series\/[A-Z0-9]+(?:\/[^/?#]+)?\/?$/i;

function isCrunchyrollSeriesUrl(url: URL): boolean {
  return url.protocol === 'https:'
    && CRUNCHYROLL_HOSTS.has(url.hostname)
    && CRUNCHYROLL_SERIES_PATH.test(url.pathname);
}

function normalizeCrunchyrollSeriesUrl(value?: string): string | null {
  const url = value ? parseAbsoluteUrl(value) : null;
  return url && isCrunchyrollSeriesUrl(url)
    ? `https://www.crunchyroll.com${url.pathname}`
    : null;
}

export function getCrunchyrollSeriesUrl(availabilityByProviderLink: ProviderAvailabilityMap): string | null {
  for (const state of Object.values(availabilityByProviderLink)) {
    if (state.status !== 'loaded') continue;
    const link = state.links.find((candidate) => candidate.serviceId.toLowerCase() === 'crunchyroll');
    const url = link ? normalizeCrunchyrollSeriesUrl(link.url) : null;
    if (url) return url;
  }

  return null;
}

function ContinueDestinationLink({
  label,
  isEpisodeLink,
  url,
}: {
  label: string;
  isEpisodeLink: boolean;
  url: string;
}) {
  return (
    <a
      className="media-detail-primary-action"
      href={url}
      target="_blank"
      rel="noopener noreferrer"
    >
      {isEpisodeLink ? <Play aria-hidden /> : <ExternalLink aria-hidden />}
      <span>{label}</span>
    </a>
  );
}

function ContinueUnavailableAction({ state }: { state: ContinueWatchingState }) {
  return (
    <button type="button" className="media-detail-primary-action" disabled>
      <Play aria-hidden />
      <span>{getContinueWatchingLabel(state)}</span>
    </button>
  );
}

const UPCOMING_RELEASE_OUTCOMES = new Set<MediaContinueWatchingDto['outcome']>([
  'series_fallback',
  'unavailable',
]);

function isFutureRelease(timestamp?: string): timestamp is string {
  return Boolean(timestamp && Date.parse(timestamp) > Date.now());
}

function readReleaseEpisodeNumber(label?: string): number | null {
  const value = /\b(?:episode|ep|e)\s*(\d+)\b/i.exec(label ?? '')?.[1];
  return value ? Number(value) : null;
}

function getUpcomingRelease(
  state: ContinueWatchingState,
  nextReleaseAt?: string,
  nextReleaseLabel?: string,
) {
  if (state.status !== 'loaded') return null;
  if (!UPCOMING_RELEASE_OUTCOMES.has(state.value.outcome)) return null;
  if (!isFutureRelease(nextReleaseAt)) return null;

  const releaseEpisodeNumber = readReleaseEpisodeNumber(nextReleaseLabel);
  if (releaseEpisodeNumber !== null && releaseEpisodeNumber !== state.value.episodeNumber) return null;

  return formatNextReleaseDisplay(nextReleaseAt);
}

function ContinueWatchingAction({
  state,
  seriesUrl,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  state: ContinueWatchingState;
  seriesUrl: string | null;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  const upcomingRelease = getUpcomingRelease(state, nextReleaseAt, nextReleaseLabel);
  if (upcomingRelease) {
    return (
      <button
        type="button"
        className="media-detail-primary-action"
        disabled
        title={`${nextReleaseLabel ?? 'Next episode'} expected ${upcomingRelease.absolute}`}
      >
        <Clock3 aria-hidden />
        <span>Come back {upcomingRelease.relative}</span>
      </button>
    );
  }

  const linkAction = getContinueLinkAction(state, seriesUrl, canonicalTitle);
  if (linkAction) {
    return (
      <ContinueDestinationLink
        label={linkAction.label}
        isEpisodeLink={linkAction.isEpisodeLink}
        url={linkAction.url}
      />
    );
  }

  return <ContinueUnavailableAction state={state} />;
}

export function ActionRail({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
  onLinkProvider,
  continueWatching,
  crunchyrollSeriesUrl,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
  onLinkProvider: () => void;
  continueWatching: ContinueWatchingState;
  crunchyrollSeriesUrl: string | null;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  return (
    <div className="media-detail-action-rail">
      {hasStatusChanged ? (
        <SaveProgressAction
          isSavingStatus={isSavingStatus}
          isRefreshingProgress={isRefreshingProgress}
          onSaveStatus={onSaveStatus}
        />
      ) : (
        <ContinueWatchingAction
          state={continueWatching}
          seriesUrl={crunchyrollSeriesUrl}
          canonicalTitle={canonicalTitle}
          nextReleaseAt={nextReleaseAt}
          nextReleaseLabel={nextReleaseLabel}
        />
      )}
      <button type="button" className="media-detail-secondary-action" onClick={onLinkProvider}>
        <Plus aria-hidden />
        Add to Library
      </button>
      <button type="button" className="media-detail-more-action" aria-label="More actions">
        <MoreVertical aria-hidden />
      </button>
    </div>
  );
}
