import type { SeriesCatalogObservation, CatalogSubmissionResult } from './catalogObservation';
import type {
  ResolveWatchObservationRequest,
  WatchProgressObservation,
  WatchSubmissionResult,
} from './watchObservation';

export type MediaBackgroundRequest =
  | {
    type: 'media.catalog.submit';
    correlationId: string;
    payload: SeriesCatalogObservation;
  }
  | {
    type: 'media.watch.submit';
    correlationId: string;
    payload: WatchProgressObservation;
  }
  | {
    type: 'media.watch.resolve';
    correlationId: string;
    payload: ResolveWatchObservationRequest;
  };

export type MediaBackgroundResponse = CatalogSubmissionResult | WatchSubmissionResult | { resolved: true };

export function isMediaBackgroundRequest(value: unknown): value is MediaBackgroundRequest {
  if (!value || typeof value !== 'object') return false;
  const type = (value as { type?: unknown }).type;
  return type === 'media.catalog.submit'
    || type === 'media.watch.submit'
    || type === 'media.watch.resolve';
}
