import { afterEach, describe, expect, it } from 'bun:test';
import {
  configureMediaApi,
  isMediaApiError,
  mediaApi,
  toMediaApiError,
  type MediaApiError,
} from '../../Cantaro.ClientShared/src/media/services/mediaApi';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
  configureMediaApi(() => ({ baseUrl: '', includeCredentials: true }));
});

async function expectApiError(request: () => Promise<unknown>): Promise<MediaApiError> {
  try {
    await request();
  } catch (error) {
    expect(isMediaApiError(error)).toBe(true);
    return error as MediaApiError;
  }

  throw new Error('Expected the media API request to fail');
}

describe('media API user-facing errors', () => {
  it('classifies a network failure without exposing the browser error', async () => {
    globalThis.fetch = () => Promise.reject(new TypeError('Failed to fetch'));

    const error = await expectApiError(() => mediaApi.getLibrary());

    expect(error.kind).toBe('connection');
    expect(error.message).toBe("Can't connect to the Cantaro server. Check your connection and try again.");
    expect(error.message).not.toContain('Failed to fetch');
    expect(error.retryable).toBe(true);
  });

  it('maps authentication failures to safe copy', async () => {
    globalThis.fetch = () => Promise.resolve(Response.json({ error: { message: 'internal detail' } }, { status: 401 }));

    const error = await expectApiError(() => mediaApi.getLibrary());

    expect(error.kind).toBe('authentication');
    expect(error.message).toBe('Your Cantaro session has expired. Sign in again, then try again.');
    expect(error.message).not.toContain('internal detail');
  });

  it('maps service-unavailable failures to retryable safe copy', async () => {
    globalThis.fetch = () => Promise.resolve(Response.json({ error: { message: 'database stack trace' } }, { status: 503 }));

    const error = await expectApiError(() => mediaApi.getLibrary());

    expect(error.kind).toBe('service-unavailable');
    expect(error.message).toBe('The Cantaro server is temporarily unavailable. Try again in a moment.');
    expect(error.message).not.toContain('database stack trace');
    expect(error.retryable).toBe(true);
  });

  it('identifies rate limits without calling them a server failure', async () => {
    globalThis.fetch = () => Promise.resolve(new Response(null, { status: 429 }));

    const error = await expectApiError(() => mediaApi.getLibrary());

    expect(error.kind).toBe('request');
    expect(error.message).toBe('Too many requests. Try again in a moment.');
    expect(error.retryable).toBe(true);
  });

  it('keeps an application server failure distinct from gateway unavailability', async () => {
    globalThis.fetch = () => Promise.resolve(new Response(null, { status: 500 }));

    const error = await expectApiError(() => mediaApi.getLibrary());

    expect(error.kind).toBe('server');
    expect(error.message).toBe('The Cantaro server could not complete this request. Try again in a moment.');
  });

  it('keeps unknown runtime failures out of the connection state', () => {
    const error = toMediaApiError(new SyntaxError('Unexpected token from configuration'), 'Cantaro could not load your library.');

    expect(error.kind).toBe('request');
    expect(error.message).toBe('Cantaro could not load your library.');
    expect(error.message).not.toContain('Unexpected token');
  });
});
