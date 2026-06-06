import { createBrowserStorageQueue } from '../lib/observationQueue';
import { DEFAULT_API_BASE_URL, readExtensionConfig } from '../lib/extensionRuntimeConfig';
import { ensureExtensionAccessToken } from '../lib/cantaroAuthSession';
import {
  toSubmitMediaObservationRequest,
  type MediaObservation,
  type MediaObservationDto,
  type MediaObservationMessage,
  type ResolveMediaObservationRequest,
  type SubmitMediaObservationResponse,
} from '../lib/mediaObservation';
import {
  clearLatestMediaResolution,
  readLatestMediaResolution,
  saveLatestMediaResolution,
} from '../lib/mediaResolutionStorage';
import type { Runtime } from 'webextension-polyfill';

/** Media observation route expected by the Cantaro backend. */
const MEDIA_OBSERVATIONS_PATH = '/api/media/observations';

/** Number of queued items to replay in a single drain pass. */
const DRAIN_BATCH_SIZE = 10;

const observationQueue = createBrowserStorageQueue();

export default defineBackground(() => {
  console.log('Cantaro extension background script loaded');

  // Listen for messages from content scripts
  browser.runtime.onMessage.addListener(handleRuntimeMessage);

  initialize();
});

type RuntimeMessage = MediaObservationMessage | { type: 'PLAYLIST_EVENT'; payload: unknown };

async function handleRuntimeMessage(message: RuntimeMessage, sender: Runtime.MessageSender) {
  console.log('Received message:', message.type, 'from:', sender.tab?.url);

  const handler = runtimeMessageHandlers[message.type] as RuntimeMessageHandler | undefined;
  if (!handler) return { success: true };
  return await handler(message, sender);
}

type RuntimeMessageHandler = (message: RuntimeMessage, sender: Runtime.MessageSender) => Promise<unknown>;

const runtimeMessageHandlers: Record<string, RuntimeMessageHandler> = {
  PLAYLIST_EVENT: async (message) => {
    await handlePlaylistEvent((message as { type: 'PLAYLIST_EVENT'; payload: unknown }).payload);
    return { success: true };
  },
  MEDIA_OBSERVATION: async (message, sender) => {
    await handleMediaObservation((message as Extract<MediaObservationMessage, { type: 'MEDIA_OBSERVATION' }>).payload, sender.tab?.id);
    return { success: true };
  },
  DRAIN_MEDIA_OBSERVATION_QUEUE: async () => {
    await drainObservationQueue();
    return { success: true };
  },
  GET_LATEST_MEDIA_RESOLUTION: async () => ({
    success: true,
    payload: await readLatestMediaResolution(),
  }),
  RESOLVE_MEDIA_OBSERVATION: async (message) => {
    const payload = (message as Extract<MediaObservationMessage, { type: 'RESOLVE_MEDIA_OBSERVATION' }>).payload;
    return {
      success: true,
      payload: await resolveMediaObservation(payload.observationId, payload.request),
    };
  },
};

async function initialize() {
  console.log('Initializing Cantaro extension');

  const config = await browser.storage.local.get('apiBaseUrl');
  if (!config.apiBaseUrl) {
    console.log('API base URL not configured. Setting build default.');
    await browser.storage.local.set({ apiBaseUrl: DEFAULT_API_BASE_URL });
  }

  // Replay any observations that were queued while the extension was offline
  // or the user was unauthenticated.
  await drainObservationQueue();
}

// ---------------------------------------------------------------------------
// Media observation handling
// ---------------------------------------------------------------------------

async function handleMediaObservation(observation: MediaObservation, tabId?: number): Promise<void> {
  const config = await readExtensionConfig();
  console.log('Cantaro: preparing media observation for API', summarizeObservation(observation));
  const accessToken = await ensureExtensionAccessToken(config.apiBaseUrl);

  if (!accessToken) {
    // Not authenticated yet — queue for later replay
    await observationQueue.enqueue(observation);
    console.log('Cantaro: queued media observation (no auth token)');
    return;
  }

  const response = await sendObservation(
    config.apiBaseUrl,
    accessToken,
    observation,
  );

  if (!response) {
    await observationQueue.enqueue(observation);
    console.log('Cantaro: queued media observation (send failed)');
    return;
  }

  await handleObservationResponse(response, tabId);
}

async function handleObservationResponse(response: SubmitMediaObservationResponse, tabId?: number): Promise<void> {
  if (!response.requiresResolution) {
    await clearLatestMediaResolution(response.observationId);
    return;
  }

  await saveLatestMediaResolution(response);

  if (tabId === undefined) return;
  await browser.tabs.sendMessage(tabId, {
    type: 'SHOW_MEDIA_RESOLUTION',
    payload: response,
  } satisfies MediaObservationMessage).catch((error: unknown) => {
    console.warn('Cantaro: failed to show media resolution overlay', error);
  });
}

async function resolveMediaObservation(
  observationId: string,
  request: ResolveMediaObservationRequest,
): Promise<MediaObservationDto> {
  const config = await readExtensionConfig();
  const accessToken = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!accessToken) {
    throw new Error('Cantaro sign-in is required before resolving this episode.');
  }

  const response = await fetch(`${config.apiBaseUrl}${MEDIA_OBSERVATIONS_PATH}/${encodeURIComponent(observationId)}/resolve`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({ error: 'Failed to resolve media observation.' }));
    throw new Error(body.error ?? 'Failed to resolve media observation.');
  }

  const result = await response.json() as MediaObservationDto;
  await clearLatestMediaResolution(observationId);
  return result;
}

/**
 * Attempt to deliver all queued observations to the backend.
 * Delivers up to DRAIN_BATCH_SIZE items per call to keep the work bounded.
 * Items that fail are left in the queue with an incremented attempt count.
 */
async function drainObservationQueue(): Promise<void> {
  const config = await readExtensionConfig();
  const accessToken = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!accessToken) return;

  const queued = await observationQueue.drain();
  const batch = queued.slice(0, DRAIN_BATCH_SIZE);
  if (batch.length === 0) return;

  console.log(`Cantaro: replaying ${batch.length} queued observation(s)`);

  const succeeded: string[] = [];
  const failed: string[] = [];

  await Promise.all(
    batch.map(async (item) => {
      const response = await sendObservation(
        config.apiBaseUrl,
        accessToken,
        item.observation,
      );
      if (response) {
        await handleObservationResponse(response);
        succeeded.push(item.id);
        return;
      }

      failed.push(item.id);
    }),
  );

  await observationQueue.remove(succeeded);
  if (failed.length > 0) {
    await observationQueue.incrementAttempts(failed);
  }
}

/**
 * POST a single observation to the backend.
 * Returns true on success, false on any network or HTTP error.
 */
async function sendObservation(
  apiBaseUrl: string,
  accessToken: string,
  observation: MediaObservation,
): Promise<SubmitMediaObservationResponse | null> {
  try {
    const request = toSubmitMediaObservationRequest(observation);
    console.log('Cantaro: posting media observation to API', {
      apiBaseUrl,
      path: MEDIA_OBSERVATIONS_PATH,
      ...summarizeObservation(observation),
    });

    const response = await fetch(`${apiBaseUrl}${MEDIA_OBSERVATIONS_PATH}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      console.warn(
        `Cantaro: media observation rejected by backend: ${response.status} ${response.statusText}`,
      );
      return null;
    }

    console.log('Cantaro: media observation accepted by API', summarizeObservation(observation));
    return await response.json() as SubmitMediaObservationResponse;
  } catch (error) {
    console.error('Cantaro: error sending media observation', error);
    return null;
  }
}

function summarizeObservation(observation: MediaObservation): Record<string, unknown> {
  return {
    siteId: observation.siteId,
    siteMediaId: observation.siteMediaId,
    titleText: observation.titleText,
    episodeNumber: observation.episodeNumber,
    watchProgressPercent: observation.watchProgressPercent,
    positionSeconds: observation.positionSeconds,
    durationSeconds: observation.durationSeconds,
  };
}

// ---------------------------------------------------------------------------
// Legacy playlist event handling (music MVP placeholder)
// ---------------------------------------------------------------------------

async function handlePlaylistEvent(payload: unknown) {
  console.log('Handling playlist event:', payload);

  try {
    const config = await readExtensionConfig();
    const accessToken = await ensureExtensionAccessToken(config.apiBaseUrl);

    if (!accessToken) {
      console.warn('No auth token found. User needs to authenticate.');
      return;
    }

    const response = await fetch(`${config.apiBaseUrl}/api/extension/events`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      console.error('Failed to send event to backend:', response.status, response.statusText);
    } else {
      console.log('Event successfully sent to backend');
    }
  } catch (error) {
    console.error('Error sending event to backend:', error);
  }
}
