import { browserAuthService } from '../auth/authService';
import { browserSettingsRepository } from '../settings/settingsRepository';
import { createCantaroApiClient } from './cantaroApiClient';

export const backgroundCantaroApiClient = createCantaroApiClient(
  browserSettingsRepository,
  browserAuthService,
);
