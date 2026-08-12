import { useCallback, useEffect, useState } from 'react';
import { mediaApi } from '../services/mediaApi';
import { DetailErrorState, DetailLoadingState } from './media-entry-detail/MediaEntryDetailHero';

interface MediaCatalogDetailPageProps {
  providerId: string;
  providerMediaId: string;
  onNavigateBack: () => void;
  onNavigateTitle: (mediaTitleId: string) => void;
  embedded?: boolean;
}

/**
 * Provider catalog URLs are discovery aliases. Loading one ensures the global
 * Cantaro title exists, then replaces it with the canonical media-title route.
 */
export function MediaCatalogDetailPage({
  providerId,
  providerMediaId,
  onNavigateBack,
  onNavigateTitle,
  embedded = false,
}: MediaCatalogDetailPageProps) {
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);

  const retry = useCallback(async () => {
    setError(null);
    setRevision((current) => current + 1);
  }, []);

  useEffect(() => {
    let cancelled = false;
    void mediaApi.getTitleDetails(providerId, providerMediaId)
      .then((details) => {
        if (!cancelled) onNavigateTitle(details.mediaTitleId);
      })
      .catch((loadError) => {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : 'Failed to resolve media title');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [onNavigateTitle, providerId, providerMediaId, revision]);

  return error
    ? <DetailErrorState error={error} embedded={embedded} onNavigateBack={onNavigateBack} onRetry={retry} />
    : <DetailLoadingState embedded={embedded} />;
}
