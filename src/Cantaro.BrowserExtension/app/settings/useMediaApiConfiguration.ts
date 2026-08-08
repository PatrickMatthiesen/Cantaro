import { configureMediaApi } from '@cantaro/client-shared/media';
import { useEffect } from 'react';
import { runtimeAccessTokenProvider } from '../../platform/auth/runtimeAuthClient';

export function useMediaApiConfiguration(apiBaseUrl: string) {
  useEffect(() => {
    configureMediaApi(async () => ({
      apiBaseUrl,
      accessToken: await runtimeAccessTokenProvider.getAccessToken(apiBaseUrl) || undefined,
      includeCredentials: false,
    }));
  }, [apiBaseUrl]);
}
