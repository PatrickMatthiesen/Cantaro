import type { ProfilePreferences } from '@cantaro/client-shared/auth';
import {
  STREAMING_SERVICE_IDS,
  STREAMING_SERVICES,
  type StreamingServiceId,
} from '@cantaro/client-shared/media';

export interface WatchProviderOption {
  id: StreamingServiceId;
  name: string;
}

export const watchProviderOptions: readonly WatchProviderOption[] = STREAMING_SERVICE_IDS.map(id => ({
  id,
  name: STREAMING_SERVICES[id].displayName,
}));

export function isWatchProviderVisible(preferences: ProfilePreferences, providerId: StreamingServiceId): boolean {
  return !preferences.disabledWatchProviders?.includes(providerId);
}

export function setWatchProviderVisible(
  preferences: ProfilePreferences,
  providerId: StreamingServiceId,
  visible: boolean,
): ProfilePreferences {
  const disabledProviderIds = new Set(preferences.disabledWatchProviders ?? []);
  if (visible) {
    disabledProviderIds.delete(providerId);
  } else {
    disabledProviderIds.add(providerId);
  }

  return { ...preferences, disabledWatchProviders: [...disabledProviderIds] };
}
