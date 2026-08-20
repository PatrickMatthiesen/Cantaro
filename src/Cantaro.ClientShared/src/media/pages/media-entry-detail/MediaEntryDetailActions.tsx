import {
  Clock3,
  ExternalLink,
  Minus,
  Play,
  Plus,
  RotateCw,
  Save,
  Star,
} from "lucide-react";
import {
  ActionButton,
  IconButton,
  SelectField,
  actionClassName,
} from "../../../ui";
import { formatNextReleaseDisplay } from "../../services/mediaFormatting";
import type { MediaContinueWatchingDto } from "../../services/mediaApi";
import type { StreamingDestination } from "../../services/streamingDestinations";
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from "../../services/streamingServices";
import { StreamingServiceIcon } from "../../components/StreamingServiceIcon";
import {
  clampProgressValue,
  getEntryStatusChanged,
  getProgressCapabilities,
  getPrimaryProgressSummary,
  getStatusSaveLabel,
  isStatusRefreshDisabled,
} from "./mediaEntryDetailModel";
import {
  getContinueLinkActions,
  type ContinueLinkAction,
} from "./continueWatchingAction";
import type {
  ContinueWatchingState,
  MediaEntryDetailContentProps,
} from "./mediaEntryDetailTypes";

const NORMALIZED_STATUSES = [
  { value: "current", label: "Watching / Reading" },
  { value: "completed", label: "Completed" },
  { value: "planned", label: "Planning" },
  { value: "paused", label: "Paused" },
  { value: "dropped", label: "Dropped" },
  { value: "repeating", label: "Rewatching / Rereading" },
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
  const progressPercent = Math.min((currentValue / sliderMax) * 100, 100);
  const setProgressValue = (nextValue: number) =>
    onChange(clampProgressValue(nextValue, max));

  return (
    <div className="flex min-w-0 flex-1 items-center gap-2 sm:gap-3">
      <IconButton
        label={`Decrease ${label.toLowerCase()}`}
        onClick={() => setProgressValue(currentValue - 1)}
        disabled={!canDecrease}
        className="hover:bg-danger-surface hover:text-danger-content"
      >
        <Minus size={18} aria-hidden />
      </IconButton>
      <input
        type="range"
        min={0}
        max={sliderMax}
        step={1}
        value={currentValue}
        onChange={(event) => setProgressValue(Number(event.target.value))}
        aria-label={`${label} progress`}
        className="h-1.5 min-w-0 flex-1 cursor-pointer appearance-none bg-surface-subtle accent-personal-accent outline-none focus-visible:ring-2 focus-visible:ring-focus [&::-moz-range-thumb]:size-4 [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:border-0 [&::-moz-range-thumb]:bg-personal-accent [&::-webkit-slider-thumb]:size-4 [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-personal-accent"
        style={{
          background: `linear-gradient(to right, var(--color-personal-accent) ${progressPercent}%, var(--color-surface-subtle) ${progressPercent}%)`,
        }}
      />
      <IconButton
        label={`Increase ${label.toLowerCase()}`}
        onClick={() => setProgressValue(currentValue + 1)}
        disabled={!canIncrease}
        className="hover:bg-success-surface hover:text-success-content"
      >
        <Plus size={18} aria-hidden />
      </IconButton>
    </div>
  );
}

type ProgressCockpitProps = Pick<
  MediaEntryDetailContentProps,
  | "entry"
  | "selectedStatus"
  | "progressEpisodes"
  | "progressChapters"
  | "progressVolumes"
  | "isRefreshingProgress"
  | "isSavingStatus"
  | "isAddingToLibrary"
  | "onSetSelectedStatus"
  | "onSetProgressEpisodes"
  | "onSetProgressChapters"
  | "onSetProgressVolumes"
  | "onRefreshProgress"
  | "onAddToLibrary"
>;

function StatusSelect({
  value,
  onChange,
  visuallyHiddenLabel = false,
}: {
  value: string;
  onChange: (value: string) => void;
  visuallyHiddenLabel?: boolean;
}) {
  return (
    <SelectField
      label="Library status"
      visuallyHiddenLabel={visuallyHiddenLabel}
      name="status"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      containerClassName="min-w-44"
    >
      {NORMALIZED_STATUSES.map((status) => (
        <option key={status.value} value={status.value}>
          {status.label}
        </option>
      ))}
    </SelectField>
  );
}

export function AddToLibraryActions({
  selectedStatus,
  isAddingToLibrary,
  onSetSelectedStatus,
  onAddToLibrary,
}: Pick<
  ProgressCockpitProps,
  | "selectedStatus"
  | "isAddingToLibrary"
  | "onSetSelectedStatus"
  | "onAddToLibrary"
>) {
  return (
    <>
      <StatusSelect
        value={selectedStatus}
        onChange={onSetSelectedStatus}
        visuallyHiddenLabel
      />
      <ActionButton
        tone="personal"
        onClick={onAddToLibrary}
        disabled={isAddingToLibrary}
        aria-busy={isAddingToLibrary}
        busyLabel="Adding…"
      >
        <Plus size={18} aria-hidden /> Add to library
      </ActionButton>
    </>
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
    <div className="grid min-w-0 flex-1 gap-2">
      {capabilities.supportsEpisodes ? (
        <ProgressStepper
          label="Episodes"
          value={props.progressEpisodes}
          max={props.entry.title.episodeCount}
          onChange={props.onSetProgressEpisodes}
        />
      ) : null}
      {capabilities.supportsChapters ? (
        <ProgressStepper
          label="Chapters"
          value={props.progressChapters}
          max={props.entry.title.chapterCount}
          onChange={props.onSetProgressChapters}
        />
      ) : null}
      {capabilities.supportsVolumes ? (
        <ProgressStepper
          label="Volumes"
          value={props.progressVolumes}
          max={props.entry.title.volumeCount}
          onChange={props.onSetProgressVolumes}
        />
      ) : null}
    </div>
  );
}

function EmptyScore() {
  return (
    <div className="flex gap-1 text-content-subtle" aria-label="No score set">
      {[1, 2, 3, 4, 5].map((star) => (
        <button
          key={star}
          type="button"
          aria-label={`Set score to ${star} stars`}
          className="inline-flex size-8 items-center justify-center hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
        >
          <Star size={18} aria-hidden />
        </button>
      ))}
    </div>
  );
}

function ProgressStateMessage({ status }: { status: "loading" | "error" }) {
  return (
    <section
      className="border-b border-border-subtle px-4 py-7 sm:px-7 xl:px-9"
      aria-busy={status === "loading"}
    >
      <h2 className="font-bold text-content">
        {status === "loading"
          ? "Loading your progress…"
          : "Couldn't load your progress"}
      </h2>
      <p className="mt-1 max-w-2xl text-sm text-content-muted">
        Public title information stays available while Cantaro loads your
        private library state.
      </p>
    </section>
  );
}

function NotInLibraryProgress({ total }: { total?: number }) {
  return (
    <section className="grid gap-6 border-b border-border-subtle px-4 py-7 sm:grid-cols-[auto_minmax(12rem,1fr)_auto] sm:items-center sm:px-7 xl:px-9">
      <div className="min-w-36">
        <h2 className="text-sm font-semibold text-content-muted">
          Your progress
        </h2>
        <p className="mt-2 text-2xl font-black tabular-nums text-content">
          <span className="text-personal-accent-strong">0</span>
          <span className="text-content-subtle"> / {total ?? "?"}</span>
        </p>
      </div>
      <p className="text-sm text-content-muted sm:text-center">
        Add this title to track progress.
      </p>
      <div>
        <p className="mb-1.5 text-sm text-content-muted">Your score</p>
        <EmptyScore />
      </div>
    </section>
  );
}

function TrackedProgressCockpit(props: ProgressCockpitProps) {
  const capabilities = getProgressCapabilities(props.entry.title);
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);

  return (
    <section
      aria-labelledby="media-progress-heading"
      className="flex flex-col gap-6 border-b border-border-subtle px-4 py-7 sm:px-7 xl:flex-row xl:items-center xl:px-9"
    >
      <div className="min-w-36">
        <h2
          id="media-progress-heading"
          className="text-sm font-semibold text-content-muted"
        >
          Your progress
        </h2>
        <p className="mt-2 text-2xl font-black tabular-nums text-content">
          <span className="text-personal-accent-strong">
            {progressSummary.value ?? 0}
          </span>
          <span className="text-content-subtle">
            {" "}
            / {progressSummary.total ?? "?"}
          </span>
        </p>
        {hasStatusChanged ? (
          <p className="mt-1 text-xs font-semibold text-warning-content">
            Unsaved changes
          </p>
        ) : null}
      </div>

      <ProgressControls props={props} capabilities={capabilities} />

      <div className="flex flex-wrap items-end gap-4 xl:ml-auto xl:justify-end">
        <div>
          <p className="mb-1.5 text-sm text-content-muted">Your score</p>
          <EmptyScore />
        </div>
        <StatusSelect
          value={props.selectedStatus}
          onChange={props.onSetSelectedStatus}
        />
        <IconButton
          label="Refresh progress from provider"
          onClick={props.onRefreshProgress}
          disabled={isStatusRefreshDisabled(
            props.entry.isConnected,
            props.isSavingStatus,
            props.isRefreshingProgress,
          )}
          aria-busy={props.isRefreshingProgress}
        >
          <RotateCw
            size={18}
            className={props.isRefreshingProgress ? "animate-spin" : ""}
            aria-hidden
          />
        </IconButton>
      </div>
    </section>
  );
}

export function ProgressCockpit(props: ProgressCockpitProps) {
  if (props.entry.viewerStateStatus !== "loaded") {
    return <ProgressStateMessage status={props.entry.viewerStateStatus} />;
  }
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  if (!props.entry.isInLibrary) {
    return <NotInLibraryProgress total={progressSummary.total} />;
  }
  return <TrackedProgressCockpit {...props} />;
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
    <ActionButton
      tone="personal"
      onClick={onSaveStatus}
      disabled={isSavingStatus || isRefreshingProgress}
      aria-busy={isSavingStatus}
      busyLabel="Saving…"
    >
      <Save size={18} aria-hidden /> {getStatusSaveLabel(isSavingStatus)}
    </ActionButton>
  );
}

function getContinueWatchingLabel(state: ContinueWatchingState): string {
  if (state.status === "loading") return "Finding episode…";
  if (state.status === "error") return "Couldn't load streaming links";

  const labels: Record<
    Exclude<MediaContinueWatchingDto["outcome"], "direct">,
    string
  > = {
    series_fallback: "Open streaming service",
    completed: "Completed",
    conflict: "Episode link needs review",
    unavailable: "Streaming link not available",
  };
  return state.value.outcome === "direct"
    ? `Play episode ${state.value.episodeNumber ?? ""}`.trim()
    : labels[state.value.outcome];
}

function ContinueDestinationLink({
  action,
  primary,
  onSelect,
}: {
  action: ContinueLinkAction;
  primary: boolean;
  onSelect: (serviceId: StreamingServiceId) => void;
}) {
  const service = STREAMING_SERVICES[action.serviceId];
  const ActionIcon = action.kind === "episode" ? Play : ExternalLink;
  return (
    <a
      className={
        primary
          ? actionClassName({ tone: "personal" })
          : actionClassName({
              tone: "secondary",
              className:
                "border-white/15 bg-black/30 text-white hover:bg-black/55",
            })
      }
      href={action.url}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={action.label}
      onClick={() => onSelect(action.serviceId)}
    >
      {primary ? (
        <ActionIcon size={18} aria-hidden />
      ) : (
        <StreamingServiceIcon
          serviceId={action.serviceId}
          className="h-5 w-5"
          aria-hidden
        />
      )}
      <span>{primary ? action.label : service.displayName}</span>
    </a>
  );
}

function ContinueDestinationMenu({
  actions,
  onSelect,
}: {
  actions: ContinueLinkAction[];
  onSelect: (serviceId: StreamingServiceId) => void;
}) {
  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((action, index) => (
        <ContinueDestinationLink
          key={`${action.serviceId}:${action.kind}`}
          action={action}
          primary={index === 0}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}

function ContinueUnavailableAction({
  state,
}: {
  state: ContinueWatchingState;
}) {
  return (
    <ActionButton tone="personal" disabled>
      <Play size={18} aria-hidden /> {getContinueWatchingLabel(state)}
    </ActionButton>
  );
}

const UPCOMING_RELEASE_OUTCOMES = new Set<MediaContinueWatchingDto["outcome"]>([
  "series_fallback",
  "unavailable",
]);

function isFutureRelease(timestamp?: string): timestamp is string {
  return Boolean(timestamp && Date.parse(timestamp) > Date.now());
}

function readReleaseEpisodeNumber(label?: string): number | null {
  const value = /\b(?:episode|ep|e)\s*(\d+)\b/i.exec(label ?? "")?.[1];
  return value ? Number(value) : null;
}

function getUpcomingRelease(
  state: ContinueWatchingState,
  nextReleaseAt?: string,
  nextReleaseLabel?: string,
) {
  if (state.status !== "loaded") return null;
  if (!UPCOMING_RELEASE_OUTCOMES.has(state.value.outcome)) return null;
  if (!isFutureRelease(nextReleaseAt)) return null;
  const releaseEpisodeNumber = readReleaseEpisodeNumber(nextReleaseLabel);
  if (
    releaseEpisodeNumber !== null &&
    releaseEpisodeNumber !== state.value.episodeNumber
  ) {
    return null;
  }
  return formatNextReleaseDisplay(nextReleaseAt);
}

function ContinueWatchingAction({
  state,
  seriesDestinations,
  episodeDestinations,
  preferredServiceId,
  onSelectStreamingService,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  state: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  const upcomingRelease = getUpcomingRelease(
    state,
    nextReleaseAt,
    nextReleaseLabel,
  );
  if (upcomingRelease) {
    return (
      <ActionButton
        tone="personal"
        disabled
        title={`${nextReleaseLabel ?? "Next episode"} expected ${upcomingRelease.absolute}`}
      >
        <Clock3 size={18} aria-hidden /> Come back {upcomingRelease.relative}
      </ActionButton>
    );
  }

  const linkActions = getContinueLinkActions(
    state,
    seriesDestinations,
    episodeDestinations,
    preferredServiceId,
    canonicalTitle,
  );
  return linkActions.length > 0 ? (
    <ContinueDestinationMenu
      actions={linkActions}
      onSelect={onSelectStreamingService}
    />
  ) : (
    <ContinueUnavailableAction state={state} />
  );
}

export function ActionRail({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
  continueWatching,
  seriesDestinations,
  episodeDestinations,
  preferredServiceId,
  onSelectStreamingService,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
  continueWatching: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      {hasStatusChanged ? (
        <SaveProgressAction
          isSavingStatus={isSavingStatus}
          isRefreshingProgress={isRefreshingProgress}
          onSaveStatus={onSaveStatus}
        />
      ) : (
        <ContinueWatchingAction
          state={continueWatching}
          seriesDestinations={seriesDestinations}
          episodeDestinations={episodeDestinations}
          preferredServiceId={preferredServiceId}
          onSelectStreamingService={onSelectStreamingService}
          canonicalTitle={canonicalTitle}
          nextReleaseAt={nextReleaseAt}
          nextReleaseLabel={nextReleaseLabel}
        />
      )}
    </div>
  );
}
