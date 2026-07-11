import { describe, expect, it } from 'vitest';
import { normalizePopupTab } from '../popupTabPreference';

describe('popup tab preference', () => {
  it.each([
    ['music', 'music'],
    ['media', 'media'],
    ['unknown', 'music'],
    [undefined, 'music'],
    [null, 'music'],
  ])('normalizes %j to %s', (value, expected) => {
    expect(normalizePopupTab(value)).toBe(expected);
  });
});
