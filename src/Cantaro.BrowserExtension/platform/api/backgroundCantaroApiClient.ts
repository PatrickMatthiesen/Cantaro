import { browserAuthService } from '../auth/authService';
import { browserConsentService } from '../consent/consentService';
import { browserSettingsRepository } from '../settings/settingsRepository';
import { ApiError } from './apiError';
import { createCantaroApiClient } from './cantaroApiClient';

export const backgroundCantaroApiClient = createCantaroApiClient(
  browserSettingsRepository,
  browserAuthService,
  async (path, baseUrl, init) => {
    const status = await browserConsentService.getStatus(baseUrl);
    const isCatalog = path.startsWith('/api/media/catalog-observations');
    let containsCatalogEvidence = false;
    if (!isCatalog && path.startsWith('/api/media/observations') && typeof init.body === 'string') {
      try {
        const body = JSON.parse(init.body) as Record<string, unknown>;
        containsCatalogEvidence = [
          'nextEpisodeProviderId', 'nextEpisodeUrl', 'nextEpisodeTitle',
          'nextEpisodeNumber', 'nextEpisodeReleaseTrack',
        ].some(key => body[key] !== undefined && body[key] !== null);
      } catch {
        containsCatalogEvidence = true;
      }
    }
    const allowed = isCatalog
      ? status.catalogCollectionAllowed
      : status.watchTrackingAllowed && (!containsCatalogEvidence || status.catalogCollectionAllowed);
    if (!status.authenticated || !allowed) {
      throw new ApiError('Collection consent was revoked before the observation was sent.', 403, false);
    }
  },
);
