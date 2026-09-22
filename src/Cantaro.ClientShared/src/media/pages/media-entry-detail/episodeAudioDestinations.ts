import type { StreamingDestination } from "../../services/streamingDestinations";

export function getLanguageCode(locale: string) {
  try {
    return new Intl.Locale(locale).language;
  } catch {
    return locale.split("-")[0]?.toLowerCase() ?? locale;
  }
}

export function filterDestinationsByAudioLanguage(
  destinations: readonly StreamingDestination[],
  audioLanguage: string | null,
  preferredReleaseTrack?: string,
) {
  if (!audioLanguage) return destinations;

  return destinations.filter((destination) =>
    destination.serviceId === "stremio"
    || matchesAudioLocale(destination, audioLanguage)
    || matchesReleaseTrack(destination, audioLanguage, preferredReleaseTrack));
}

function matchesAudioLocale(
  destination: StreamingDestination,
  audioLanguage: string,
) {
  return destination.audioLocale !== undefined
    && getLanguageCode(destination.audioLocale) === audioLanguage;
}

function matchesReleaseTrack(
  destination: StreamingDestination,
  audioLanguage: string,
  preferredReleaseTrack: string | undefined,
) {
  if (preferredReleaseTrack === undefined
    || destination.releaseTrack !== preferredReleaseTrack) return false;

  return preferredReleaseTrack.startsWith("dub:")
    ? getLanguageCode(preferredReleaseTrack.slice(4)) === audioLanguage
    : audioLanguage === "ja";
}
