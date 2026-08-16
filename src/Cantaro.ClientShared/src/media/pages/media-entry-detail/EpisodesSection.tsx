import { ExternalLink, Play, RefreshCcw } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { ActionButton } from "../../../ui";
import type { MediaEntryDetailModel } from "../../services/mediaApi";
import {
  type MediaStreamingDestinations,
  type StreamingDestination,
} from "../../services/streamingDestinations";
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from "../../services/streamingServices";
import { StreamingServiceIcon } from "../../components/StreamingServiceIcon";
import {
  formatEpisodeAvailability,
  formatReleaseAvailability,
} from "./episodeAvailability";
import { getEpisodeRows } from "./episodeRows";
import { DetailSectionHeading } from "./MediaDetailSections";
import type { EpisodeCatalogState } from "./mediaEntryDetailTypes";

const EPISODE_WINDOW_SIZE = 100;

function getInitialEpisodeWindowStart(
  rows: ReturnType<typeof getEpisodeRows>,
  watchedThrough: number,
) {
  const nextEpisodeIndex = rows.findIndex(
    (row) => row.episodeNumber >= watchedThrough + 1,
  );
  if (nextEpisodeIndex < 0) {
    return Math.max(0, rows.length - EPISODE_WINDOW_SIZE);
  }
  return Math.max(0, nextEpisodeIndex - 20);
}

function getEpisodeProgressLabel(
  episodeNumber: number,
  watchedThrough: number,
) {
  if (episodeNumber <= watchedThrough) return "Watched";
  if (episodeNumber === watchedThrough + 1) return "Up next";
  return "Not watched";
}

function EpisodeAvailabilityLabel({
  destination,
}: {
  destination?: MediaStreamingDestinations["episodes"][number];
}) {
  const availability = formatEpisodeAvailability(destination);
  return availability ? (
    <span className="text-xs text-content-subtle">{availability}</span>
  ) : null;
}

function getEpisodeServiceDestinations(
  episodeDestinations: readonly StreamingDestination[],
  seriesDestinations: readonly StreamingDestination[],
) {
  const serviceIds = new Set([
    ...episodeDestinations.map((destination) => destination.serviceId),
    ...seriesDestinations.map((destination) => destination.serviceId),
  ]);
  return [...serviceIds]
    .map(
      (serviceId) =>
        episodeDestinations.find(
          (destination) => destination.serviceId === serviceId,
        ) ??
        seriesDestinations.find(
          (destination) => destination.serviceId === serviceId,
        ),
    )
    .filter((destination): destination is StreamingDestination =>
      Boolean(destination),
    );
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
  const destinations = getEpisodeServiceDestinations(
    episodeDestinations,
    seriesDestinations,
  );
  if (destinations.length === 0) {
    return (
      <span className="text-sm text-content-subtle">No streaming link</span>
    );
  }

  return (
    <div className="flex flex-wrap justify-end gap-2">
      {destinations.map((destination) => {
        const service = STREAMING_SERVICES[destination.serviceId];
        const opensEpisode = destination.kind === "episode";
        return (
          <a
            key={destination.serviceId}
            href={destination.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`${opensEpisode ? `Open episode ${episodeNumber}` : "Open series"} on ${service.displayName}`}
            title={
              opensEpisode
                ? `Watch episode ${episodeNumber}`
                : "Open series page"
            }
            onClick={() => onSelectStreamingService(destination.serviceId)}
            className="inline-flex min-h-10 items-center gap-2 bg-surface-subtle px-3 text-sm font-semibold text-content transition-colors hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
          >
            <StreamingServiceIcon
              serviceId={destination.serviceId}
              style={{ color: "currentColor", fill: "currentColor" }}
              aria-hidden
            />
            <span>{service.displayName}</span>
            {opensEpisode ? (
              <Play size={15} aria-hidden />
            ) : (
              <ExternalLink size={15} aria-hidden />
            )}
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
  destination?: MediaStreamingDestinations["episodes"][number];
  watchedThrough: number;
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
}) {
  const isNext = episodeNumber === watchedThrough + 1;
  const progressLabel = getEpisodeProgressLabel(episodeNumber, watchedThrough);

  return (
    <li
      className={`grid gap-3 border-t border-border-subtle px-3 py-4 first:border-t-0 sm:grid-cols-[3rem_minmax(0,1fr)_auto] sm:items-center ${isNext ? "bg-personal-accent/10" : ""}`}
    >
      <span
        className="text-sm font-bold tabular-nums text-content-subtle"
        aria-hidden
      >
        {String(episodeNumber).padStart(2, "0")}
      </span>
      <div className="min-w-0">
        <strong className="block truncate text-content">
          {destination?.title || `Episode ${episodeNumber}`}
        </strong>
        <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-sm text-content-muted">
          <span>{progressLabel}</span>
          <EpisodeAvailabilityLabel destination={destination} />
        </div>
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

function EpisodeSectionContent({
  entry,
  state,
  rows,
  totalRowCount,
  windowStart,
  seriesDestinations,
  onSelectStreamingService,
  onRefresh,
  onShowEarlier,
  onShowLater,
}: {
  entry: MediaEntryDetailModel;
  state: EpisodeCatalogState;
  rows: ReturnType<typeof getEpisodeRows>;
  totalRowCount: number;
  windowStart: number;
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
  onShowEarlier: () => void;
  onShowLater: () => void;
}) {
  if (state.status === "error") {
    return <EpisodeError onRefresh={onRefresh} />;
  }

  if (state.status === "loaded" && rows.length === 0) {
    return <EpisodeEmpty />;
  }

  if (rows.length === 0) return null;
  return (
    <EpisodeWindow
      entry={entry}
      rows={rows}
      totalRowCount={totalRowCount}
      windowStart={windowStart}
      seriesDestinations={seriesDestinations}
      onSelectStreamingService={onSelectStreamingService}
      onShowEarlier={onShowEarlier}
      onShowLater={onShowLater}
    />
  );
}

function EpisodeError({ onRefresh }: { onRefresh: () => void }) {
  return (
    <div
      className="mt-5 flex flex-wrap items-center justify-between gap-4 py-6"
      role="alert"
    >
      <p className="text-content-muted">Episode links could not be loaded.</p>
      <ActionButton tone="secondary" onClick={onRefresh}>
        Try again
      </ActionButton>
    </div>
  );
}

function EpisodeEmpty() {
  return (
    <div className="mt-5 max-w-2xl py-6">
      <p className="font-semibold text-content">
        No episode URLs have been observed yet.
      </p>
      <p className="mt-2 text-sm leading-6 text-content-muted">
        Visit a supported streaming series page with the Cantaro extension
        enabled, then refresh this tab.
      </p>
    </div>
  );
}

function EpisodeWindow({
  entry,
  rows,
  totalRowCount,
  windowStart,
  seriesDestinations,
  onSelectStreamingService,
  onShowEarlier,
  onShowLater,
}: {
  entry: MediaEntryDetailModel;
  rows: ReturnType<typeof getEpisodeRows>;
  totalRowCount: number;
  windowStart: number;
  seriesDestinations: readonly StreamingDestination[];
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onShowEarlier: () => void;
  onShowLater: () => void;
}) {
  return (
    <div className="mt-5">
      {windowStart > 0 ? (
        <div className="flex items-center justify-between gap-4 border-b border-border-subtle py-3">
          <span className="text-sm text-content-muted">
            Showing {windowStart + 1}–{windowStart + rows.length} of{" "}
            {totalRowCount}
          </span>
          <ActionButton tone="ghost" onClick={onShowEarlier}>
            Show earlier episodes
          </ActionButton>
        </div>
      ) : null}
      <ol>
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
      {windowStart + rows.length < totalRowCount ? (
        <div className="flex justify-end border-t border-border-subtle py-4">
          <ActionButton tone="secondary" onClick={onShowLater}>
            Show later episodes
          </ActionButton>
        </div>
      ) : null}
    </div>
  );
}

function getEpisodeSectionData(
  entry: MediaEntryDetailModel,
  state: EpisodeCatalogState,
  streamingDestinations: MediaStreamingDestinations,
) {
  if (state.status !== "loaded") return { rows: [], availableCount: 0 };
  return {
    rows: getEpisodeRows(entry, streamingDestinations.episodes),
    availableCount: streamingDestinations.episodes.filter(
      (episode) => episode.destinations.length > 0,
    ).length,
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
  entry: MediaEntryDetailModel;
  state: EpisodeCatalogState;
  streamingDestinations: MediaStreamingDestinations;
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
}) {
  const { rows, availableCount } = getEpisodeSectionData(
    entry,
    state,
    streamingDestinations,
  );
  const initialWindowStart = useMemo(
    () => getInitialEpisodeWindowStart(rows, entry.progressEpisodes ?? 0),
    [entry.progressEpisodes, rows],
  );
  const [windowStart, setWindowStart] = useState(initialWindowStart);

  useEffect(() => {
    setWindowStart(initialWindowStart);
  }, [entry.title.id, initialWindowStart]);

  const visibleRows = rows.slice(
    windowStart,
    windowStart + EPISODE_WINDOW_SIZE,
  );
  const orderedSeriesDestinations = preferredServiceId
    ? [...streamingDestinations.seriesDestinations].sort((left, right) => {
        if (left.serviceId === preferredServiceId) return -1;
        if (right.serviceId === preferredServiceId) return 1;
        return 0;
      })
    : streamingDestinations.seriesDestinations;
  const isLoading = state.status === "loading";
  const summary =
    state.status === "loaded"
      ? (formatReleaseAvailability(state.value.releaseAvailability) ??
        `${availableCount} episode links collected`)
      : "Loading collected episode links…";

  return (
    <section
      className="py-9"
      role="tabpanel"
      id="media-detail-panel-episodes"
      aria-labelledby="media-detail-tab-episodes"
    >
      <div className="flex flex-wrap items-start justify-between gap-4">
        <DetailSectionHeading title="Episodes" detail={summary} />
        <ActionButton tone="ghost" onClick={onRefresh} disabled={isLoading}>
          <RefreshCcw
            size={17}
            className={isLoading ? "animate-spin" : ""}
            aria-hidden
          />
          Refresh links
        </ActionButton>
      </div>
      <EpisodeSectionContent
        entry={entry}
        state={state}
        rows={visibleRows}
        totalRowCount={rows.length}
        windowStart={windowStart}
        seriesDestinations={orderedSeriesDestinations}
        onSelectStreamingService={onSelectStreamingService}
        onRefresh={onRefresh}
        onShowEarlier={() =>
          setWindowStart((current) =>
            Math.max(0, current - EPISODE_WINDOW_SIZE),
          )
        }
        onShowLater={() =>
          setWindowStart((current) =>
            Math.min(
              Math.max(0, rows.length - EPISODE_WINDOW_SIZE),
              current + EPISODE_WINDOW_SIZE,
            ),
          )
        }
      />
    </section>
  );
}
