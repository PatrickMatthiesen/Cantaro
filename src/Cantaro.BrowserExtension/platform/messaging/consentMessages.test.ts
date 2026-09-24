import { describe, expect, it } from 'vitest';
import { isConsentBackgroundRequest, isConsentChangedMessage } from './consentMessages';

describe('consent message guards', () => {
  it('requires the YouTube lyrics choice on save requests', () => {
    const request = {
      type: 'consent.save', correlationId: 'id', payload: {
        baseUrl: 'https://api.example.test', watchTracking: false, catalogCollection: false,
      },
    };
    expect(isConsentBackgroundRequest(request)).toBe(false);
    expect(isConsentBackgroundRequest({
      ...request, payload: { ...request.payload, musicLyrics: true },
    })).toBe(true);
  });

  it('requires the lyrics allowance in consent change messages', () => {
    const message = {
      type: 'consent.changed', payload: {
        consentVersion: 2, needsReview: false, authenticated: true,
        watchTrackingAllowed: false, catalogCollectionAllowed: false,
      },
    };
    expect(isConsentChangedMessage(message)).toBe(false);
    expect(isConsentChangedMessage({
      ...message, payload: { ...message.payload, musicLyricsAllowed: true },
    })).toBe(true);
  });
});
