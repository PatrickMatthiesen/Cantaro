import { providerAvailabilityKey, type ProviderAvailabilityState } from "../../components/media-entry-detail/providerAvailability";
import type { MediaEntryDetailModel, MediaTitleDetailDto } from "../../services/mediaApi";
import { mediaFormatLabel } from "../../services/mediaFormatting";
import { isImdbId } from "../../services/playbackLinks";
import type {
  MediaEntryDetailContentProps,
  ProgressSummary,
} from "./mediaEntryDetailTypes";

export function getStremioTargets(state: ProviderAvailabilityState | undefined) {
  return state?.stremioTargets?.length ? state.stremioTargets : [state?.stremioTarget];
}

function getAioStreamsType({ mediaKind, format }: MediaTitleDetailDto) {
  return mediaKind === "anime" && format === "movie" ? "movie" : mediaKind;
}

function getAioStreamsAnimeTargets(
  title: MediaTitleDetailDto,
  availability: (ProviderAvailabilityState | undefined)[],
) {
  const imdb = title.providerLinks.find(link => link.provider === "imdb" && link.lastVerifiedAt);
  const targets = availability.flatMap(getStremioTargets);
  if (imdb && targets.some(target => target && isImdbId(target.id) && target.id !== imdb.externalId)) return [];
  return targets.filter(target => target?.type === "series");
}

export function getAioStreamsTargets({
  entry,
  availabilityByProviderLink,
}: Pick<MediaEntryDetailContentProps, "entry" | "availabilityByProviderLink">) {
  const verifiedAvailability = entry.providerLinks.filter(link => link.lastVerifiedAt)
    .map(link => availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)]);
  if (verifiedAvailability.some(state => state?.aioStreamsIdentityConflict)) return [];

  const type = getAioStreamsType(entry.title);
  switch (type) {
    case "movie":
    case "series":
      return entry.title.providerLinks.filter(link => link.provider === "imdb" && link.lastVerifiedAt)
        .map(link => ({ id: link.externalId, type }));
    case "anime":
      return getAioStreamsAnimeTargets(entry.title, verifiedAvailability);
    default:
      return [];
  }
}

export function mergeTitleMetadata(
  current: MediaEntryDetailModel | null,
  requestedMediaTitleId: string,
  currentRequestedMediaTitleId: string,
  title: MediaTitleDetailDto,
): MediaEntryDetailModel | null {
  if (
    !current
    || current.mediaTitleId !== requestedMediaTitleId
    || currentRequestedMediaTitleId !== requestedMediaTitleId
  ) return current;

  const titleProvider = title.providerLinks[0];
  return {
    ...current,
    title,
    nextReleaseAt: title.nextReleaseAt,
    nextReleaseLabel: title.nextReleaseLabel,
    providerLinks: title.providerLinks,
    ...(!current.isInLibrary && titleProvider
      ? { provider: titleProvider.provider, providerMediaId: titleProvider.externalId }
      : {}),
  };
}

export function releaseStatusLabel(dimension: string): string {
  if (["episode", "chapter", "volume"].includes(dimension.toLowerCase())) {
    return "Unknown";
  }

  const map: Record<string, string> = {
    airing: "Currently Airing",
    finished: "Finished",
    notYetAired: "Not Yet Aired",
    not_yet_aired: "Not Yet Aired",
    cancelled: "Cancelled",
    hiatus: "On Hiatus",
    unknown: "Unknown",
  };
  return map[dimension] ?? dimension;
}

export function progressKindLabel(title: MediaEntryDetailModel["title"]) {
  if (title.format) {
    return mediaFormatLabel(title.format);
  }

  if (title.primaryProgressDimension === "episode") {
    return title.episodeCount ? "TV Series" : "Episode tracking";
  }

  if (title.primaryProgressDimension === "chapter") {
    return "Manga";
  }

  if (title.primaryProgressDimension === "volume") {
    return "Volumes";
  }

  return releaseStatusLabel(title.releaseStatusDimension);
}

export function clampProgressValue(value: number, max?: number) {
  const lowerBoundedValue = Math.max(0, Math.round(value));
  return max ? Math.min(lowerBoundedValue, max) : lowerBoundedValue;
}

function displayProgressValue(value: number | undefined): number {
  return value ?? 0;
}

function displayProgressTotal(total: number | undefined): number | "?" {
  return total ?? "?";
}

function createProgressSummary(
  label: string,
  noun: string,
  action: string,
  unit: string,
  value: number | undefined,
  total: number | undefined,
): ProgressSummary {
  return {
    label,
    noun,
    value,
    total,
    progressLabel: `${action}: ${unit} ${displayProgressValue(value)} / ${displayProgressTotal(total)}`,
  };
}

export function getPrimaryProgressSummary(
  title: MediaEntryDetailModel["title"],
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
): ProgressSummary {
  if (title.primaryProgressDimension === "chapter") {
    return createProgressSummary(
      "Chapters read",
      "chapters",
      "Read",
      "Chapter",
      progressChapters,
      title.chapterCount,
    );
  }

  if (title.primaryProgressDimension === "volume") {
    return createProgressSummary(
      "Volumes read",
      "volumes",
      "Read",
      "Volume",
      progressVolumes,
      title.volumeCount,
    );
  }

  return createProgressSummary(
    "Watched",
    "episodes",
    "Watched",
    "Episode",
    progressEpisodes,
    title.episodeCount,
  );
}

export function getRemainingLabel(summary: ProgressSummary) {
  if (!summary.total) {
    return "Total unknown";
  }

  const remaining = Math.max(summary.total - (summary.value ?? 0), 0);
  return remaining === 1
    ? `1 ${summary.noun.slice(0, -1)} left`
    : `${remaining} ${summary.noun} left`;
}

export function getProgressCapabilities(title: MediaEntryDetailModel["title"]) {
  const dim = title.primaryProgressDimension;
  return {
    supportsEpisodes: dim === "episode",
    supportsChapters: dim === "chapter",
    supportsVolumes: dim === "volume" || dim === "chapter",
  };
}

export function getEntryStatusChanged(
  props: Pick<
    MediaEntryDetailContentProps,
    | "entry"
    | "selectedStatus"
    | "progressEpisodes"
    | "progressChapters"
    | "progressVolumes"
  >,
) {
  return (
    props.selectedStatus !== props.entry.status ||
    props.progressEpisodes !== props.entry.progressEpisodes ||
    props.progressChapters !== props.entry.progressChapters ||
    props.progressVolumes !== props.entry.progressVolumes
  );
}

export function getStatusSaveLabel(isSavingStatus: boolean) {
  return isSavingStatus ? "Saving..." : "Save progress";
}

export function isStatusRefreshDisabled(
  canRefreshProgress: boolean,
  isSavingStatus: boolean,
  isRefreshingProgress: boolean,
) {
  return [!canRefreshProgress, isSavingStatus, isRefreshingProgress].some(
    Boolean,
  );
}
