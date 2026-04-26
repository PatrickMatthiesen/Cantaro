import { createBrowserStorageQueue } from '../lib/observationQueue';
import { DEFAULT_API_BASE_URL, readExtensionConfig } from '../lib/extensionRuntimeConfig';
import { ensureExtensionAccessToken } from '../lib/cantaroAuthSession';
import type { MediaObservation } from '../lib/mediaObservation';

/** Media observation route expected by the Cantaro backend. */
const MEDIA_OBSERVATIONS_PATH = '/api/media/observations';

/** Number of queued items to replay in a single drain pass. */
const DRAIN_BATCH_SIZE = 10;

const observationQueue = createBrowserStorageQueue();

export default defineBackground(() => {
  console.log('Cantaro extension background script loaded');

  // Listen for messages from content scripts
  browser.runtime.onMessage.addListener(async (message, sender) => {
    console.log('Received message:', message.type, 'from:', sender.tab?.url);

    if (message.type === 'PLAYLIST_EVENT') {
      await handlePlaylistEvent(message.payload);
    } else if (message.type === 'MEDIA_OBSERVATION') {
      await handleMediaObservation(message.payload as MediaObservation);
    }

    return { success: true };
  });

  initialize();
});

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

async function handleMediaObservation(observation: MediaObservation): Promise<void> {
  const config = await readExtensionConfig();
  const accessToken = await ensureExtensionAccessToken(config.apiBaseUrl);

  if (!accessToken) {
    // Not authenticated yet — queue for later replay
    await observationQueue.enqueue(observation);
    console.log('Cantaro: queued media observation (no auth token)');
    return;
  }

  const sent = await sendObservation(
    config.apiBaseUrl,
    accessToken,
    observation,
  );

  if (!sent) {
    await observationQueue.enqueue(observation);
    console.log('Cantaro: queued media observation (send failed)');
  }
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
      const ok = await sendObservation(
        config.apiBaseUrl,
        accessToken,
        item.observation,
      );
      (ok ? succeeded : failed).push(item.id);
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
): Promise<boolean> {
  try {
    const response = await fetch(`${apiBaseUrl}${MEDIA_OBSERVATIONS_PATH}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify(observation),
    });

    if (!response.ok) {
      console.warn(
        `Cantaro: media observation rejected by backend: ${response.status} ${response.statusText}`,
      );
      return false;
    }

    return true;
  } catch (error) {
    console.error('Cantaro: error sending media observation', error);
    return false;
  }
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
