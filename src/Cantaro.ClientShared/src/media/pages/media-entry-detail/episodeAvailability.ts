import type { EpisodeStreamingDestinations } from '../../services/streamingDestinations';

function formatLanguageCode(languageCode: string) {
  return languageCode.toUpperCase();
}

function formatLanguages(languageCodes: readonly string[] | undefined) {
  return languageCodes && languageCodes.length > 0
    ? languageCodes.map(formatLanguageCode).join(', ')
    : null;
}

export function formatEpisodeAvailability(destination?: EpisodeStreamingDestinations) {
  const subtitleLanguages = formatLanguages(destination?.availableSubtitleLanguageCodes);
  const audioLanguages = formatLanguages(destination?.availableAudioLanguageCodes);
  const tracks = [
    subtitleLanguages ? `Sub: ${subtitleLanguages}` : null,
    audioLanguages ? `Dub: ${audioLanguages}` : null,
  ].filter((track): track is string => track !== null);

  return tracks.length > 0 ? tracks.join(' · ') : null;
}
