import { ExternalLink, Play, RefreshCcw } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { ActionButton } from "../../../ui";
import type {
  MediaEntryDetailModel,
  MediaSpecialEpisodeDestinationDto,
} from "../../services/mediaApi";
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
} from "./episodeAvailability";
import { getEpisodeRows } from "./episodeRows";
import type { EpisodeCatalogState } from "./mediaEntryDetailTypes";
import { resolvePreferredEpisodeAudioLanguage } from "./preferredEpisodeAudio";
import {
  formatEpisodeNumber,
  getEpisodesForSeason,
  type SeasonOption,
  type SeasonSelection,
} from "./seasonEpisodes";

import { SeasonSelector } from "./SeasonSelector";

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

function episodeProgressClassName(progressLabel: string) {
  if (progressLabel === "Watched") return "text-info-content";
  if (progressLabel === "Up next") return "text-success-content";
  return "text-warning-content";
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

function getLanguageCode(locale: string) {
  try {
    return new Intl.Locale(locale).language;
  } catch {
    return locale.split("-")[0]?.toLowerCase() ?? locale;
  }
}

function formatAudioLanguage(language: string) {
  try {
    return new Intl.DisplayNames(undefined, { type: "language" }).of(language)
      ?? language;
  } catch {
    return language;
  }
}

function getAudioLanguages(destinations: MediaStreamingDestinations) {
  return [...new Set(destinations.episodes
    .flatMap((episode) => episode.destinations)
    .map((destination) => destination.audioLocale
      ?? (destination.releaseTrack?.startsWith("dub:")
        ? destination.releaseTrack.slice(4)
        : destination.releaseTrack?.startsWith("sub:") ? "ja" : undefined))
    .filter((locale): locale is string => Boolean(locale))
    .map(getLanguageCode))]
    .sort((left, right) => formatAudioLanguage(left).localeCompare(formatAudioLanguage(right)));
}

function filterDestinationsByAudioLanguage(
  destinations: readonly StreamingDestination[],
  audioLanguage: string | null,
  preferredReleaseTrack?: string,
) {
  return audioLanguage
    ? destinations.filter((destination) =>
        (destination.audioLocale
          && getLanguageCode(destination.audioLocale) === audioLanguage)
        || (preferredReleaseTrack !== undefined
          && destination.releaseTrack === preferredReleaseTrack
          && (preferredReleaseTrack.startsWith("dub:")
            ? getLanguageCode(preferredReleaseTrack.slice(4)) === audioLanguage
            : audioLanguage === "ja")))
    : destinations;
}

function EpisodeDestinationActions({
  episodeNumber,
  episodeLabel,
  episodeDestinations,
  seriesDestinations,
  onSelectStreamingService,
}: {
  episodeNumber: number;
  episodeLabel?: string;
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
    <div className="flex min-w-0 flex-wrap gap-2 @4xl/episodes:justify-end">
      {destinations.map((destination) => {
        const service = STREAMING_SERVICES[destination.serviceId];
        const opensEpisode = destination.kind === "episode";
        return (
          <a
            key={destination.serviceId}
            href={destination.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`${opensEpisode ? `Open ${episodeLabel ?? `episode ${episodeNumber}`}` : "Open series"} on ${service.displayName}`}
            title={
              opensEpisode
                ? `Watch ${episodeLabel ?? `episode ${episodeNumber}`}`
                : "Open series page"
            }
            onClick={() => onSelectStreamingService(destination.serviceId)}
            className="inline-flex min-h-10 items-center gap-2 bg-surface-subtle px-3 text-sm font-semibold text-content transition-colors hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
          >
            <StreamingServiceIcon
              serviceId={destination.serviceId}
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
  seasonSelection,
  watchedThrough,
  seriesDestinations,
  audioLanguage,
  preferredReleaseTrack,
  onSelectStreamingService,
}: {
  episodeNumber: number;
  destination?: MediaStreamingDestinations["episodes"][number];
  seasonSelection: SeasonSelection;
  watchedThrough: number;
  seriesDestinations: readonly StreamingDestination[];
  audioLanguage: string | null;
  preferredReleaseTrack?: string;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
}) {
  const hasLinks = seriesDestinations.length > 0 || (destination?.destinations.length ?? 0) > 0;
  const isNext = episodeNumber === watchedThrough + 1;
  const progressLabel = getEpisodeProgressLabel(episodeNumber, watchedThrough);
  const number = formatEpisodeNumber(
    destination ?? { episodeNumber },
    seasonSelection,
  );

  return (
    <li
      className={`grid items-center gap-x-3 gap-y-2 border-t border-border-subtle px-2 py-3 first:border-t-0 ${hasLinks ? "grid-cols-[max-content_minmax(0,1fr)] @4xl/episodes:grid-cols-[max-content_minmax(0,1fr)_minmax(0,auto)]" : "grid-cols-[max-content_minmax(0,1fr)_auto]"} ${isNext ? "bg-personal-accent/10" : ""}`}
    >
      <EpisodeNumberLabel
        label={number.primary}
        value={destination?.seasonEpisodeNumber ?? episodeNumber}
        season={seasonSelection === "all" ? destination?.seasonNumber : undefined}
        seriesNumber={number.secondary ? episodeNumber : undefined}
      />
      <EpisodeDescription
        title={destination?.title}
        progressLabel={progressLabel}
        destination={destination}
      />
      <div className={hasLinks ? "col-start-2 min-w-0 @4xl/episodes:col-start-3 @4xl/episodes:row-start-1" : "col-start-3 min-w-0"}>
        <EpisodeDestinationActions
          episodeNumber={episodeNumber}
          episodeLabel={number.secondary
            ? `${number.primary}, ${number.secondary.toLowerCase()}`
            : number.primary}
          episodeDestinations={filterDestinationsByAudioLanguage(
            destination?.destinations ?? [],
            audioLanguage,
            preferredReleaseTrack,
          )}
          seriesDestinations={seriesDestinations}
          onSelectStreamingService={onSelectStreamingService}
        />
      </div>
    </li>
  );
}

function EpisodeNumberLabel({
  label,
  value,
  season,
  seriesNumber,
}: {
  label: string;
  value: number;
  season?: number;
  seriesNumber?: number;
}) {
  const [showDetails, setShowDetails] = useState(false);
  const visibleLabel = season ? `S${season} · #${value}` : `#${value}`;
  if (seriesNumber === undefined) {
    return <span className="min-w-6 text-center text-sm font-bold tabular-nums text-content" aria-label={label}>{visibleLabel}</span>;
  }
  return (
    <span className="relative self-center" onMouseEnter={() => setShowDetails(true)} onMouseLeave={() => setShowDetails(false)}>
      <button
        type="button"
        aria-label={`${label}, series number ${seriesNumber}`}
        onFocus={() => setShowDetails(true)}
        onBlur={() => setShowDetails(false)}
        onClick={() => setShowDetails(true)}
        onKeyDown={(event) => { if (event.key === "Escape") setShowDetails(false); }}
        className="min-h-8 min-w-6 cursor-help text-center text-sm font-bold tabular-nums text-content focus-visible:outline-2 focus-visible:outline-focus"
      >
        {visibleLabel}
      </button>
      {showDetails ? (
        <span role="tooltip" className="absolute left-0 bottom-full z-10 whitespace-nowrap border border-border-strong bg-surface-raised px-2 py-1 text-xs font-medium text-content">
          Series #{seriesNumber}
        </span>
      ) : null}
    </span>
  );
}

function EpisodeDescription({
  title,
  progressLabel,
  destination,
}: {
  title?: string;
  progressLabel: string;
  destination?: MediaStreamingDestinations["episodes"][number];
}) {
  return (
    <div className="min-w-0">
      {title ? (
        <strong className="block truncate text-content">{title}</strong>
      ) : null}
      <div className={`${title ? "mt-1" : ""} flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm`}>
        <span className={episodeProgressClassName(progressLabel)}>
          {progressLabel}
        </span>
        <EpisodeAvailabilityLabel destination={destination} />
      </div>
    </div>
  );
}

function EpisodeSectionContent({
  entry,
  state,
  rows,
  specials,
  seasonSelection,
  totalRowCount,
  windowStart,
  seriesDestinations,
  audioLanguage,
  preferredReleaseTrack,
  onSelectStreamingService,
  onRefresh,
  onShowEarlier,
  onShowLater,
}: {
  entry: MediaEntryDetailModel;
  state: EpisodeCatalogState;
  rows: ReturnType<typeof getEpisodeRows>;
  specials: readonly MediaSpecialEpisodeDestinationDto[];
  seasonSelection: SeasonSelection;
  totalRowCount: number;
  windowStart: number;
  seriesDestinations: readonly StreamingDestination[];
  audioLanguage: string | null;
  preferredReleaseTrack?: string;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
  onShowEarlier: () => void;
  onShowLater: () => void;
}) {
  if (state.status === "error") {
    return <EpisodeError onRefresh={onRefresh} />;
  }

  if (state.status === "loaded" && rows.length === 0) {
    if (seasonSelection === "specials" && specials.length > 0) {
      return <SpecialEpisodeWindow specials={specials} />;
    }
    return <EpisodeEmpty />;
  }

  if (rows.length === 0) return null;
  return (
    <EpisodeWindow
      entry={entry}
      rows={rows}
      seasonSelection={seasonSelection}
      totalRowCount={totalRowCount}
      windowStart={windowStart}
      seriesDestinations={seriesDestinations}
      audioLanguage={audioLanguage}
      preferredReleaseTrack={preferredReleaseTrack}
      onSelectStreamingService={onSelectStreamingService}
      onShowEarlier={onShowEarlier}
      onShowLater={onShowLater}
    />
  );
}

function SpecialEpisodeWindow({
  specials,
}: {
  specials: readonly MediaSpecialEpisodeDestinationDto[];
}) {
  return (
    <div className="mt-5">
      <p className="mb-3 text-sm text-content-muted">
        Specials do not change watched-through progress.
      </p>
      <ol>
        {specials.map((special) => (
          <li
            key={special.specialEpisodeNumber}
            className="grid gap-3 border-t border-border-subtle px-3 py-4 first:border-t-0 sm:grid-cols-[9rem_minmax(0,1fr)] sm:items-center"
          >
            <span className="text-sm font-bold tabular-nums text-content">
              Special {special.specialEpisodeNumber}
            </span>
            <div className="min-w-0">
              <strong className="block truncate text-content">
                {special.title || `Special ${special.specialEpisodeNumber}`}
              </strong>
            </div>
          </li>
        ))}
      </ol>
    </div>
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
  seasonSelection,
  totalRowCount,
  windowStart,
  seriesDestinations,
  audioLanguage,
  preferredReleaseTrack,
  onSelectStreamingService,
  onShowEarlier,
  onShowLater,
}: {
  entry: MediaEntryDetailModel;
  rows: ReturnType<typeof getEpisodeRows>;
  seasonSelection: SeasonSelection;
  totalRowCount: number;
  windowStart: number;
  seriesDestinations: readonly StreamingDestination[];
  audioLanguage: string | null;
  preferredReleaseTrack?: string;
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
            seasonSelection={seasonSelection}
            watchedThrough={entry.progressEpisodes ?? 0}
            seriesDestinations={seriesDestinations}
            audioLanguage={audioLanguage}
            preferredReleaseTrack={preferredReleaseTrack}
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
  seasonSelection: SeasonSelection,
) {
  if (state.status !== "loaded") return { rows: [] };
  const allRows = getEpisodeRows(entry, streamingDestinations.episodes);
  const selectedEpisodes = getEpisodesForSeason(
    streamingDestinations.episodes,
    seasonSelection,
  );
  const selectedEpisodeNumbers = new Set(
    selectedEpisodes.map((episode) => episode.episodeNumber),
  );
  return {
    rows: seasonSelection === "all"
      ? allRows
      : allRows.filter((row) => selectedEpisodeNumbers.has(row.episodeNumber)),
  };
}

function orderSeriesDestinations(
  destinations: readonly StreamingDestination[],
  preferredServiceId: StreamingServiceId | null,
) {
  if (!preferredServiceId) return destinations;
  return [...destinations].sort((left, right) => {
    if (left.serviceId === preferredServiceId) return -1;
    if (right.serviceId === preferredServiceId) return 1;
    return 0;
  });
}

export function EpisodesSection({
  entry,
  state,
  streamingDestinations,
  preferredServiceId,
  preferredMediaReleaseTrack,
  seasonOptions,
  selectedSeason,
  onSelectSeason,
  onSelectStreamingService,
  onRefresh,
}: {
  entry: MediaEntryDetailModel;
  state: EpisodeCatalogState;
  streamingDestinations: MediaStreamingDestinations;
  preferredServiceId: StreamingServiceId | null;
  preferredMediaReleaseTrack?: string;
  seasonOptions: readonly SeasonOption[];
  selectedSeason: SeasonSelection;
  onSelectSeason: (selection: SeasonSelection) => void;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onRefresh: () => void;
}) {
  const { rows } = getEpisodeSectionData(
    entry,
    state,
    streamingDestinations,
    selectedSeason,
  );
  const initialWindowStart = useMemo(
    () => getInitialEpisodeWindowStart(rows, entry.progressEpisodes ?? 0),
    [entry.progressEpisodes, rows],
  );
  const [windowStart, setWindowStart] = useState(initialWindowStart);
  const audioLanguages = useMemo(
    () => getAudioLanguages(streamingDestinations),
    [streamingDestinations],
  );
  const [audioLanguage, setAudioLanguage] = useState<string | null>(null);
  const preferredAudioLanguage = resolvePreferredEpisodeAudioLanguage(
    preferredMediaReleaseTrack,
    audioLanguages,
  );

  useEffect(() => {
    setWindowStart(initialWindowStart);
  }, [entry.title.id, initialWindowStart]);

  useEffect(() => {
    setAudioLanguage((current) =>
      current && audioLanguages.includes(current)
        ? current
        : preferredAudioLanguage,
    );
  }, [audioLanguages, preferredAudioLanguage]);

  const visibleRows = rows.slice(
    windowStart,
    windowStart + EPISODE_WINDOW_SIZE,
  );
  const orderedSeriesDestinations = orderSeriesDestinations(
    streamingDestinations.seriesDestinations,
    preferredServiceId,
  );
  const isLoading = state.status === "loading";
  const specials = state.status === "loaded" ? state.value.specials : [];

  return (
    <section
      className="@container/episodes py-6"
      role="tabpanel"
      id="media-detail-panel-episodes"
      aria-labelledby="media-detail-tab-episodes"
    >
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <SeasonSelector options={seasonOptions} value={selectedSeason} onChange={onSelectSeason} />
          {audioLanguages.length > 0 ? (
            <label className="grid gap-1 text-xs font-semibold text-content-muted">
              Audio
              <select
                value={audioLanguage ?? ""}
                onChange={(event) => setAudioLanguage(event.target.value || null)}
                className="min-h-10 border border-border-strong bg-surface px-3 text-sm font-medium text-content focus-visible:outline-2 focus-visible:outline-focus"
              >
                <option value="">Default</option>
                {audioLanguages.map((language) => (
                  <option key={language} value={language}>
                    {formatAudioLanguage(language)}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
        </div>
          <ActionButton tone="ghost" onClick={onRefresh} disabled={isLoading}>
            <RefreshCcw
              size={17}
              className={isLoading ? "animate-spin" : ""}
              aria-hidden
            />
            Check for updates
          </ActionButton>
      </div>
      <EpisodeSectionContent
        entry={entry}
        state={state}
        rows={visibleRows}
        specials={specials}
        seasonSelection={selectedSeason}
        totalRowCount={rows.length}
        windowStart={windowStart}
        seriesDestinations={orderedSeriesDestinations}
        audioLanguage={audioLanguage}
        preferredReleaseTrack={preferredMediaReleaseTrack}
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
