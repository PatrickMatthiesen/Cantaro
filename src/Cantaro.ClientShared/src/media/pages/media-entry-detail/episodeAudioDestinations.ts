import type { StreamingDestination } from "../../services/streamingDestinations";

function getLanguageCode(locale: string) {
  try {
    return new Intl.Locale(locale).language;
  } catch {
    return locale.split("-")[0]?.toLowerCase() ?? locale;
  }
}

export function filterDestinationsByAudioLanguage(
  destinations: readonly StreamingDestination[],
  audioLanguage: string | null,
) {
  if (!audioLanguage) return destinations;

  return destinations.filter((destination) =>
    destination.serviceId === "stremio" || destination.serviceId === "aiostreams"
    || getDestinationAudioLanguages(destination).includes(audioLanguage));
}

export function getDestinationAudioLanguages(destination: StreamingDestination) {
  const locales = [...(destination.availableAudioLanguageCodes ?? [])];
  if (destination.audioLocale) locales.push(destination.audioLocale);
  const track = destination.releaseTrack;
  const dubLocale = getDubAudioLocale(track);
  if (dubLocale) locales.push(dubLocale);
  if (locales.length === 0 && track?.startsWith("sub:")) locales.push("ja");
  return [...new Set(locales.map(getLanguageCode))];
}

function getDubAudioLocale(track: string | undefined) {
  return track?.startsWith("dub:") && track.length > 4 ? track.slice(4) : undefined;
}
