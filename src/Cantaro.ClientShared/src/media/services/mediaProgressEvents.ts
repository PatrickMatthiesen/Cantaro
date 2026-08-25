import { z } from 'zod';

const MEDIA_PROGRESS_UPDATED_EVENT = 'cantaro:media-progress-updated';

export interface MediaProgressUpdateNotification {
  mediaTitleId: string;
  progressEpisodes?: number;
}

const notificationSchema = z.object({
  mediaTitleId: z.string().min(1),
  progressEpisodes: z.number().int().optional(),
});

function parseNotification(detail: unknown): MediaProgressUpdateNotification | null {
  if (typeof detail !== 'string') {
    return null;
  }

  try {
    const parsed = notificationSchema.safeParse(JSON.parse(detail));
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

export function subscribeToMediaProgressUpdates(
  listener: (notification: MediaProgressUpdateNotification) => void,
): () => void {
  const onProgressUpdated = (event: Event) => {
    const notification = parseNotification((event as CustomEvent<unknown>).detail);
    if (notification) {
      listener(notification);
    }
  };
  window.addEventListener(MEDIA_PROGRESS_UPDATED_EVENT, onProgressUpdated);
  return () => window.removeEventListener(MEDIA_PROGRESS_UPDATED_EVENT, onProgressUpdated);
}
