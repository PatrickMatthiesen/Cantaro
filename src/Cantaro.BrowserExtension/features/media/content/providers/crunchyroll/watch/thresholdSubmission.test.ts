import { describe, expect, it, vi } from 'vitest';
import { decideThresholdSubmission } from './thresholdSubmission';

describe('watch threshold submission', () => {
  it('re-checks the pause state immediately before each submission', async () => {
    const isPaused = vi.fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);
    expect(await decideThresholdSubmission(isPaused, () => true)).toBe('submit');
    expect(await decideThresholdSubmission(isPaused, () => true)).toBe('paused');
    expect(isPaused).toHaveBeenCalledTimes(2);
  });

  it('drops an observation when navigation made its watch page stale', async () => {
    expect(await decideThresholdSubmission(async () => false, () => false)).toBe('stale');
  });
});
