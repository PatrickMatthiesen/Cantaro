import {
  Clock3,
  ExternalLink,
  LoaderCircle,
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
import { mediaLibraryStatusLabel } from "../../components/media-library/mediaLibraryStatus";
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
import type {
  SeasonOption,
  SeasonSelection,
  SelectedSeasonProgress,
} from "./seasonEpisodes";

import type { ReactNode } from "react";
import { hasSeasonChoices, SeasonSelector } from "./SeasonSelector";

const NORMALIZED_STATUSES = [
  "current",
  "completed",
  "planned",
  "paused",
  "dropped",
  "repeating",
];

function ProgressStepper({
  label,
  heading,
  value,
  max,
  onChange,
}: {
  label: string;
  heading?: ReactNode;
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
    <div className="grid min-w-0 gap-2">
      <div className="flex flex-wrap items-center gap-2">
        {heading ?? <h3 className="text-sm font-semibold text-content-muted">{label}</h3>}
        <p className="text-lg font-black tabular-nums text-content">
          <span className="text-personal-accent-strong">{currentValue}</span>
          <span className="text-content-subtle"> / {max ?? "?"}</span>
        </p>
      </div>
      <div className="flex min-w-0 items-center gap-2 sm:gap-3">
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
  | "isSavingScore"
  | "isAddingToLibrary"
  | "onSetSelectedStatus"
  | "onSetProgressEpisodes"
  | "onSetProgressChapters"
  | "onSetProgressVolumes"
  | "onRefreshProgress"
  | "onAddToLibrary"
  | "onScoreChange"
> & {
  seasonOptions: readonly SeasonOption[];
  onSelectSeason: (selection: SeasonSelection) => void;
  selectedSeason: SeasonSelection;
  selectedSeasonProgress: SelectedSeasonProgress | null;
  onSetSelectedSeasonProgress: (value: number) => void;
};

function StatusSelect({
  value,
  mediaKind,
  onChange,
  visuallyHiddenLabel = false,
}: {
  value: string;
  mediaKind: string;
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
        <option key={status} value={status}>
          {mediaLibraryStatusLabel(status, mediaKind)}
        </option>
      ))}
    </SelectField>
  );
}

export function AddToLibraryActions({
  entry,
  selectedStatus,
  isAddingToLibrary,
  onSetSelectedStatus,
  onAddToLibrary,
}: Pick<
  ProgressCockpitProps,
  | "entry"
  | "selectedStatus"
  | "isAddingToLibrary"
  | "onSetSelectedStatus"
  | "onAddToLibrary"
>) {
  return (
    <>
      <StatusSelect
        mediaKind={entry.title.mediaKind}
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
    <div className="grid min-w-0 flex-1 gap-4">
      {capabilities.supportsEpisodes ? (
        <EpisodeProgressControl props={props} />
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

function EpisodeProgressControl({ props }: { props: ProgressCockpitProps }) {
  const heading = hasSeasonChoices(props.seasonOptions) ? (
    <SeasonSelector options={props.seasonOptions} value={props.selectedSeason}
      onChange={props.onSelectSeason} label="Progress season" />
  ) : <h3 className="text-sm font-semibold text-content-muted">Episodes</h3>;
  if (props.selectedSeason === "specials") {
    return (
      <div className="grid gap-1">
        {heading}
        <p className="text-sm text-content">
          Specials do not change watched-through progress.
        </p>
      </div>
    );
  }

  if (props.selectedSeasonProgress) {
    return (
      <SelectedSeasonProgressControl
        heading={heading}
        progress={props.selectedSeasonProgress}
        onChange={props.onSetSelectedSeasonProgress}
      />
    );
  }

  return (
    <ProgressStepper
      label="Episodes"
      heading={heading}
      value={props.progressEpisodes}
      max={props.entry.title.episodeCount}
      onChange={props.onSetProgressEpisodes}
    />
  );
}

function SelectedSeasonProgressControl({
  heading,
  progress,
  onChange,
}: {
  heading: ReactNode;
  progress: SelectedSeasonProgress;
  onChange: (value: number) => void;
}) {
  if (!progress.canEdit) {
    return (
      <div className="grid gap-1">
        {heading}
        <p className="text-sm text-content">
          This season cannot edit watched-through progress because its episode mapping is incomplete.
        </p>
        {progress.overallValue > 0 ? (
          <p className="text-sm tabular-nums text-content-subtle">
            {progress.overallValue} episodes watched overall
          </p>
        ) : null}
      </div>
    );
  }

  return (
    <ProgressStepper
      label={`Season ${progress.seasonNumber} episodes`}
      heading={heading}
      value={progress.value}
      max={progress.total}
      onChange={onChange}
    />
  );
}

function formatScore(score: number | null): string {
  if (score === null) return "Unrated";
  const value = (score / 10).toFixed(1).replace(/\.0$/, "");
  return `${value}/10`;
}

function ScoreControl({
  score,
  onChange,
  disabled = false,
  isSaving = false,
  readOnlyHint,
}: {
  score: number | null;
  onChange?: (score: number | null) => void;
  disabled?: boolean;
  isSaving?: boolean;
  readOnlyHint?: string;
}) {
  return (
    <div className="grid gap-1.5">
      <div className="flex flex-wrap items-center gap-2" aria-busy={isSaving}>
        <div
          role="group"
          aria-label="Your score, choose half-star increments"
          className={`flex gap-0.5 ${disabled ? "text-content-subtle" : "text-personal-accent-strong"}`}
        >
          {[0, 1, 2, 3, 4].map((starIndex) => {
            const fillPercent = Math.min(
              Math.max(((score ?? 0) - starIndex * 20) / 20, 0),
              1,
            ) * 100;
            return (
              <span key={starIndex} className="relative size-8 shrink-0">
                <Star
                  className="absolute inset-0 size-8 text-content-subtle/55"
                  strokeWidth={1.7}
                  aria-hidden
                />
                <span
                  className="absolute inset-0 overflow-hidden"
                  style={{ width: `${fillPercent}%` }}
                  aria-hidden
                >
                  <Star
                    className="size-8 min-w-8 fill-current"
                    strokeWidth={1.7}
                  />
                </span>
                {[0, 1].map((half) => {
                  const value = (starIndex * 2 + half + 1) * 10;
                  return (
                    <button
                      key={value}
                      type="button"
                      disabled={disabled || !onChange}
                      aria-label={`Set score to ${formatScore(value)}`}
                      aria-pressed={score === value}
                      onClick={() => onChange?.(value)}
                      className={`absolute inset-y-0 ${half === 0 ? "left-0 rounded-l" : "right-0 rounded-r"} w-1/2 focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-focus ${disabled ? "cursor-not-allowed" : "hover:bg-personal-accent/15"}`}
                    />
                  );
                })}
              </span>
            );
          })}
        </div>
        <span className="min-w-12 text-sm font-semibold tabular-nums text-content" aria-live="polite">
          {isSaving ? (
            <span className="inline-flex items-center gap-1.5 text-content-muted">
              <LoaderCircle size={15} className="animate-spin motion-reduce:animate-none" aria-hidden />
              Saving
            </span>
          ) : formatScore(score)}
        </span>
        {score !== null ? (
          <button
            type="button"
            disabled={disabled || !onChange}
            onClick={() => onChange?.(null)}
            className="min-h-8 px-1 text-xs font-semibold text-content-muted underline decoration-border-strong underline-offset-2 transition-colors hover:text-content focus-visible:outline-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45"
          >
            Clear score
          </button>
        ) : null}
      </div>
      {readOnlyHint ? (
        <p className="text-xs text-content-muted">{readOnlyHint}</p>
      ) : null}
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

function NotInLibraryProgress({
  label,
  total,
}: {
  label: string;
  total?: number;
}) {
  return (
    <section className="grid gap-4 border-b border-border-subtle px-4 py-5 sm:grid-cols-[auto_minmax(12rem,1fr)_auto] sm:items-center sm:px-5 xl:px-7">
      <div className="min-w-28">
        <h2 className="text-sm font-semibold text-content-muted">
          {label}
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
        <ScoreControl
          score={null}
          disabled
          readOnlyHint="Add this title to your library to rate it."
        />
      </div>
    </section>
  );
}

function TrackedProgressCockpit(props: ProgressCockpitProps) {
  const capabilities = getProgressCapabilities(props.entry.title);
  const hasStatusChanged = getEntryStatusChanged(props);

  return (
    <section
      aria-label="Progress"
      className="grid gap-6 border-b border-border-subtle px-4 py-5 sm:px-5 xl:grid-cols-[minmax(0,1fr)_auto] xl:items-center xl:px-7"
    >
      <div className="min-w-0">
        <ProgressControls props={props} capabilities={capabilities} />
        {hasStatusChanged ? (
          <p className="mt-2 text-xs font-semibold text-warning-content">
            Unsaved changes
          </p>
        ) : null}
      </div>

      <div className="flex flex-wrap items-end gap-4 xl:ml-auto xl:justify-end">
        <div>
          <p className="mb-1.5 text-sm text-content-muted">Your score</p>
          <ScoreControl
            score={props.entry.score}
            onChange={props.onScoreChange}
            disabled={props.isSavingScore}
            isSaving={props.isSavingScore}
          />
        </div>
        <StatusSelect
          mediaKind={props.entry.title.mediaKind}
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
    return (
      <NotInLibraryProgress
        label={
          progressSummary.noun.charAt(0).toUpperCase() +
          progressSummary.noun.slice(1)
        }
        total={progressSummary.total}
      />
    );
  }
  return <TrackedProgressCockpit {...props} />;
}

function SaveProgressAction({
  isSavingStatus,
  onSaveStatus,
}: {
  isSavingStatus: boolean;
  onSaveStatus: () => void;
}) {
  return (
    <ActionButton
      tone="personal"
      onClick={onSaveStatus}
      disabled={isSavingStatus}
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
      target={service.linkTarget}
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
  disabledWatchProviders,
  mediaKind,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  state: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  disabledWatchProviders?: readonly string[];
  mediaKind: string;
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
    mediaKind,
    disabledWatchProviders,
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
  onSaveStatus,
  continueWatching,
  seriesDestinations,
  episodeDestinations,
  preferredServiceId,
  onSelectStreamingService,
  canonicalTitle,
  disabledWatchProviders,
  mediaKind,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  onSaveStatus: () => void;
  continueWatching: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  disabledWatchProviders?: readonly string[];
  mediaKind: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      {hasStatusChanged ? (
        <SaveProgressAction
          isSavingStatus={isSavingStatus}
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
          disabledWatchProviders={disabledWatchProviders}
          mediaKind={mediaKind}
          nextReleaseAt={nextReleaseAt}
          nextReleaseLabel={nextReleaseLabel}
        />
      )}
    </div>
  );
}
