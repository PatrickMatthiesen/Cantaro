const LANGUAGE_NAME_TO_CODE: Record<string, string> = {
  japanese: 'ja',
  japanisch: 'ja',
  english: 'en',
  englisch: 'en',
  german: 'de',
  deutsch: 'de',
  spanish: 'es',
  espanol: 'es',
  'español': 'es',
  french: 'fr',
  francais: 'fr',
  'français': 'fr',
  portuguese: 'pt',
  portugues: 'pt',
  'português': 'pt',
  italian: 'it',
  hindi: 'hi',
  arabic: 'ar',
  russian: 'ru',
};

function languageCodeFromLabel(value?: string) {
  return value ? LANGUAGE_NAME_TO_CODE[value.trim().toLowerCase()] : undefined;
}

export function releaseTrackFromSeasonLabel(value?: string) {
  const match = value?.match(/\(([^)]+?)\s+(dub|sub)\)/i);
  const languageName = match?.[1]?.trim().toLowerCase();
  const presentation = match?.[2]?.toLowerCase();
  const languageCode = languageCodeFromLabel(languageName);
  return presentation && languageCode ? `${presentation}:${languageCode}` : undefined;
}

export function releaseTrackFromProviderEpisodeId(providerEpisodeId: string) {
  const suffix = /[A-Z0-9]{10}(?<locale>[A-Z]{4})$/i.exec(providerEpisodeId)?.groups?.locale?.toUpperCase();
  if (suffix === 'ENUS') return 'dub:en';
  if (suffix === 'JAJP') return 'sub:en';
  return undefined;
}

export function releaseTrackFromPresentationLabel(value?: string) {
  if (/\bsubtitled\b/i.test(value ?? '')) return 'sub:en';
  if (/\bdubbed\b/i.test(value ?? '')) return 'dub:en';
  return undefined;
}

export function releaseTrackFromSelectedPlayerTracks(
  audioLabel?: string,
  subtitleLabel?: string,
) {
  const audioLanguage = languageCodeFromLabel(audioLabel);
  if (!audioLanguage) return undefined;
  if (audioLanguage !== 'ja') return `dub:${audioLanguage}`;
  const subtitleLanguage = languageCodeFromLabel(subtitleLabel);
  return subtitleLanguage ? `sub:${subtitleLanguage}` : undefined;
}
