/** Extension version — kept in sync with package.json. */
export const EXTENSION_VERSION = '0.1.0';

/** Site identifiers for each supported observation adapter. */
export const SiteIds = {
  Crunchyroll: 'crunchyroll',
} as const;

export type SiteId = (typeof SiteIds)[keyof typeof SiteIds];

/**
 * Structured observation emitted by a content-script adapter.
 * The backend owns all write decisions; the extension only observes and
 * forwards this payload — it never directly mutates provider progress.
 */
export interface MediaObservation {
  /** Stable site identifier, e.g. "crunchyroll". */
  siteId: SiteId;
  /** Full URL of the observed page at the time of observation. */
  observedUrl: string;
  /** Stable site-specific media identifier extracted from the URL or DOM, if available. */
  siteMediaId?: string;
  /** Human-readable title text of the observed content (series + episode if available). */
  titleText: string;
  /** Progress hint extracted from the page, e.g. episode number. null when not determinable. */
  progressHint?: number | null;
  /** ISO 8601 timestamp when the observation was captured. */
  observedAt: string;
  /** Extension version string that produced this observation. */
  extensionVersion: string;
}

/** Internal message envelope sent from content scripts to the background worker. */
export interface MediaObservationMessage {
  type: 'MEDIA_OBSERVATION';
  payload: MediaObservation;
}
