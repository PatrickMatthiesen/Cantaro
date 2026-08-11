import type { MediaReleaseAvailabilityDto } from '../../services/mediaApi';
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

export function formatReleaseAvailability(availability?: MediaReleaseAvailabilityDto | null) {
  if (!availability) return null;

  const summary = [
    availability.maxReleasedEpisodes == null
      ? null
      : `${availability.maxReleasedEpisodes} episode${availability.maxReleasedEpisodes === 1 ? '' : 's'} released`,
    ...availability.languages.map(language => {
      const tracks = [
        language.subReleasedEpisodes == null ? null : `Sub ${language.subReleasedEpisodes}`,
        language.dubReleasedEpisodes == null ? null : `Dub ${language.dubReleasedEpisodes}`,
      ].filter((track): track is string => track !== null);

      return tracks.length > 0 ? `${formatLanguageCode(language.languageCode)}: ${tracks.join(' · ')}` : null;
    }),
  ].filter((part): part is string => part !== null);

  return summary.length > 0 ? summary.join(' · ') : null;
}
