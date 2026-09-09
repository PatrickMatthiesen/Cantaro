import { z } from 'zod';

const notificationSchema = z.object({ mediaTitleId: z.string().min(1).optional() });
export type MediaProgressUpdateNotification = z.infer<typeof notificationSchema>;

function parseFrame(event: string, data: string[]): MediaProgressUpdateNotification | null {
  if (!['', 'message', 'library-changed'].includes(event) || data.length === 0) return null;
  try {
    const parsed = notificationSchema.safeParse(JSON.parse(data.join('\n')));
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

/** Reads small invalidation frames; library state always comes from the API. */
export async function readLibraryEvents(
  body: ReadableStream<Uint8Array>,
  listener: (notification: MediaProgressUpdateNotification) => void,
): Promise<void> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let data: string[] = [];
  let event = '';
  const line = (value: string) => {
    if (value === '') {
      const notification = parseFrame(event, data);
      if (notification) listener(notification);
      data = [];
      event = '';
    } else if (value.startsWith('data:')) {
      data.push(value.slice(5).replace(/^ /, ''));
    } else if (value.startsWith('event:')) {
      event = value.slice(6).trim();
    }
  };
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) return;
      buffer += decoder.decode(value, { stream: true });
      if (buffer.length + data.join('').length > 16_384) throw new Error('Oversized library event');
      let end: number;
      while ((end = buffer.indexOf('\n')) !== -1) {
        line(buffer.slice(0, end).replace(/\r$/, ''));
        buffer = buffer.slice(end + 1);
      }
    }
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}
