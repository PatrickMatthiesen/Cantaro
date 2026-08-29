function primaryLanguage(value: string) {
  return value.split("-")[0]?.toLowerCase() ?? value;
}

const audioLanguageResolvers: Record<string, (language: string) => string> = {
  dub: primaryLanguage,
  sub: () => "ja",
};

export function resolvePreferredEpisodeAudioLanguage(
  preferredMediaReleaseTrack: string | null | undefined,
  availableAudioLanguages: readonly string[],
) {
  const [presentation, language] = preferredMediaReleaseTrack
    ?.trim()
    .toLowerCase()
    .split(":", 2) ?? [];
  const resolveAudioLanguage = presentation
    ? audioLanguageResolvers[presentation]
    : undefined;
  if (!language || !resolveAudioLanguage) return null;
  const preferredLanguage = resolveAudioLanguage(language);
  return availableAudioLanguages.includes(preferredLanguage)
    ? preferredLanguage
    : null;
}
