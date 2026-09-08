import { afterEach, describe, expect, it, vi } from 'vitest';
import { createExtensionLogger, sanitizeDiagnosticDetails } from './logger';

describe('extension diagnostics', () => {
  afterEach(() => vi.restoreAllMocks());

  it('does not expose exception messages in structured diagnostics', () => {
    const error = new Error('request failed at https://example.test/watch/episode?token=secret');

    expect(sanitizeDiagnosticDetails(error)).toEqual({ name: 'Error' });
  });

  it('strips query and fragment data from diagnostic URLs', () => {
    expect(sanitizeDiagnosticDetails({
      pageUrl: 'https://user:password@example.test/watch/episode?token=secret#player',
      message: 'failed at https://user:password@example.test/watch/episode?token=secret#player',
    })).toEqual({
      pageUrl: 'https://example.test/watch/episode',
      message: 'failed at https://example.test/watch/episode',
    });
  });

  it('sanitizes logger details before writing to the console', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    createExtensionLogger({ scope: 'background' }).error(
      'request failed',
      new Error('https://example.test/watch/episode?token=secret'),
    );

    expect(consoleError).toHaveBeenCalledWith(
      'Cantaro: request failed',
      { scope: 'background', details: { name: 'Error' } },
    );
  });

  it('handles cyclic diagnostic details without throwing', () => {
    const details: { self?: unknown } = {};
    details.self = details;

    expect(sanitizeDiagnosticDetails(details)).toEqual({ self: '[circular]' });
  });
});
