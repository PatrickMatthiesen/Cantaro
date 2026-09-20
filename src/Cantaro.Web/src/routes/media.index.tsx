import { createFileRoute, isRedirect, redirect } from '@tanstack/react-router';
import { connectedMediaProviderIds, mediaApi } from '@cantaro/client-shared/media';

export const Route = createFileRoute('/media/')({
  beforeLoad: async () => {
    try {
      const { statuses } = await mediaApi.getProviderStatuses();
      throw redirect({
        to: connectedMediaProviderIds(statuses).length > 0 ? '/media/library' : '/media/providers',
        replace: true,
      });
    } catch (error) {
      if (isRedirect(error)) {
        throw error;
      }

      throw redirect({ to: '/media/providers', replace: true });
    }
  },
});
