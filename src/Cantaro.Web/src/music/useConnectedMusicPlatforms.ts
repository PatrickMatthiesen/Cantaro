import { useCallback, useEffect, useState } from 'react';
import { platformCatalog, platformManager, type PlatformId } from '@cantaro/client-shared/music';

export function useConnectedMusicPlatforms() {
  const [connectedPlatformIds, setConnectedPlatformIds] = useState<PlatformId[]>([]);
  const [isCheckingConnectedAccounts, setIsCheckingConnectedAccounts] = useState(true);

  const reloadConnectedPlatforms = useCallback(async () => {
    setIsCheckingConnectedAccounts(true);
    try {
      const statuses = await Promise.all(
        platformCatalog
          .filter((platform) => platform.implemented)
          .map(async (platform) => {
            try {
              const status = await platformManager.status(platform.id);
              return status.isConnected ? platform.id : null;
            } catch {
              return null;
            }
          }),
      );

      setConnectedPlatformIds(statuses.filter((platformId): platformId is PlatformId => platformId !== null));
    } catch {
      setConnectedPlatformIds([]);
    } finally {
      setIsCheckingConnectedAccounts(false);
    }
  }, []);

  useEffect(() => {
    void reloadConnectedPlatforms();
  }, [reloadConnectedPlatforms]);

  return {
    connectedPlatformIds,
    isCheckingConnectedAccounts,
    reloadConnectedPlatforms,
  };
}
