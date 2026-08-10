import { configureMediaApi } from '@cantaro/client-shared/media';
import { useEffect } from 'react';
import { runtimeAccessTokenProvider } from '../../platform/auth/runtimeAuthClient';

export function useMediaApiConfiguration(baseUrl: string) {
  useEffect(() => {
    configureMediaApi(async () => ({
      baseUrl,
      accessToken: await runtimeAccessTokenProvider.getAccessToken(baseUrl) || undefined,
      includeCredentials: false,
    }));
  }, [baseUrl]);
}
