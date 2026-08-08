import { describe, expect, it } from 'vitest';
import { computeDisabledUntil } from './episodeTrackingPreference';

describe('computeDisabledUntil', () => {
  const now = new Date('2026-08-08T10:00:00.000Z');

  it('adds the selected relative duration', () => {
    expect(computeDisabledUntil('30m', now).toISOString())
      .toBe('2026-08-08T10:30:00.000Z');
    expect(computeDisabledUntil('2h', now).toISOString())
      .toBe('2026-08-08T12:00:00.000Z');
  });
});
