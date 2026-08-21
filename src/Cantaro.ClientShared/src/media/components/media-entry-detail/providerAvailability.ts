import { mediaApi, type MediaProviderAvailabilityLinkDto, type MediaProviderCharacterCreditDto, type MediaProviderLinkSummaryDto, type MediaProviderTitleDetailsDto } from '../../services/mediaApi';

export type ProviderAvailabilityStatus = 'loading' | 'loaded' | 'unavailable' | 'error';

export interface ProviderAvailabilityState {
  status: ProviderAvailabilityStatus;
  links: MediaProviderAvailabilityLinkDto[];
  characters: MediaProviderCharacterCreditDto[];
  /** The provider returned cached data rather than a fresh availability check. */
  isStale?: boolean;
  refreshedAt?: string;
  error?: string;
}

export type ProviderAvailabilityMap = Record<string, ProviderAvailabilityState>;

export function providerAvailabilityKey(provider: string, externalId: string): string {
  return `${provider}:${externalId}`;
}

type AvailabilityMetadata = {
  status?: string;
  isStale?: boolean;
  refreshedAt?: string;
  error?: string;
};

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object'
    ? value as Record<string, unknown>
    : undefined;
}

export function readAvailabilityMetadata(value: unknown): AvailabilityMetadata {
  const record = asRecord(value);
  if (!record) return {};

  return {
    status: readString(record.availabilityStatus)?.toLowerCase(),
    isStale: readBoolean(record.availabilityIsStale),
    refreshedAt: readString(record.availabilityLastVerifiedAt),
    error: readString(record.availabilityError),
  };
}

function readString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function readBoolean(value: unknown): boolean | undefined {
  return typeof value === 'boolean' ? value : undefined;
}

export function isAvailabilityUnavailable(value: unknown): boolean {
  const metadata = readAvailabilityMetadata(value);
  return metadata.status === 'unavailable' || metadata.status === 'no_links' || metadata.status === 'none';
}

export function isAvailabilityStale(value: unknown): boolean {
  const metadata = readAvailabilityMetadata(value);
  return metadata.isStale === true || metadata.status === 'stale' || metadata.status === 'cached';
}

const providerTitleDetailsCache = new Map<string, MediaProviderTitleDetailsDto>();
const providerTitleDetailsInFlight = new Map<string, Promise<MediaProviderTitleDetailsDto>>();

/**
 * Coalesce duplicate title-detail calls by provider identity. A forced call is
 * deliberately never served from the completed/in-flight cache and replaces
 * the cached value when it succeeds; this is what the Refresh links action
 * uses to request a new provider check.
 */
export function getProviderTitleDetails(
  link: Pick<MediaProviderLinkSummaryDto, 'provider' | 'externalId'>,
  forceRefresh = false,
): Promise<MediaProviderTitleDetailsDto> {
  const key = providerAvailabilityKey(link.provider, link.externalId);
  if (!forceRefresh) {
    const cached = providerTitleDetailsCache.get(key);
    if (cached) return Promise.resolve(cached);
    const inFlight = providerTitleDetailsInFlight.get(key);
    if (inFlight) return inFlight;
  }

  const request = mediaApi.getTitleDetails(link.provider, link.externalId);
  if (forceRefresh) {
    void request.then(
      (details) => providerTitleDetailsCache.set(key, details),
      () => undefined,
    );
    return request;
  }

  providerTitleDetailsInFlight.set(key, request);
  void request.then(
    (details) => {
      providerTitleDetailsCache.set(key, details);
      providerTitleDetailsInFlight.delete(key);
    },
    () => providerTitleDetailsInFlight.delete(key),
  );
  return request;
}
