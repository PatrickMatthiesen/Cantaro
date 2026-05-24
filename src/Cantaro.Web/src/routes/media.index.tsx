import { createFileRoute, isRedirect, redirect } from '@tanstack/react-router';
import { mainMediaProviderId, mediaApi } from '@cantaro/client-shared/media';

export const Route = createFileRoute('/media/')({
  beforeLoad: async () => {
    try {
      const status = await mediaApi.getProviderStatus(mainMediaProviderId);
      throw redirect({ to: status.isConnected ? '/media/library' : '/media/providers', replace: true });
    } catch (error) {
      if (isRedirect(error)) {
        throw error;
      }

      throw redirect({ to: '/media/providers', replace: true });
    }
  },
});
