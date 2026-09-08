import { parseHTML } from 'linkedom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import type { SeriesCatalogObservation } from '../../../../contracts/catalogObservation';

vi.mock('../../../../../../platform/messaging/tabContext', () => ({
  registerTabContext: () => () => undefined,
}));

import { createCrunchyrollSeriesController } from './seriesController';

function seriesMarkup(providerEpisodeId: string) {
  return `
    <h1>My Show</h1>
    <button>Season 1</button>
    <article data-t="episode-card ">
      <h3>E1 - Episode One</h3>
      <a href="/watch/${providerEpisodeId}/episode-one"></a>
    </article>
  `;
}

function createContext(): ContentScriptContext {
  const invalidationCallbacks: Array<() => void> = [];
  return {
    isInvalid: false,
    setTimeout: (callback: () => void) => window.setTimeout(callback, 0),
    addEventListener: () => undefined,
    onInvalidated: (callback: () => void) => invalidationCallbacks.push(callback),
    abort: () => invalidationCallbacks.forEach(callback => callback()),
  } as unknown as ContentScriptContext;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Crunchyroll series controller', () => {
  it('submits again when a language switch replaces JAJP with ENUS', async () => {
    const page = parseHTML(`<html><body>${seriesMarkup('GE00374382JAJP')}</body></html>`);
    vi.stubGlobal('window', page.window);
    vi.stubGlobal('document', page.document);
    vi.stubGlobal('location', new URL('https://www.crunchyroll.com/series/GEXAMPLE/my-show'));
    vi.stubGlobal('MutationObserver', page.window.MutationObserver);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);

    const submissions: SeriesCatalogObservation[] = [];
    const controller = createCrunchyrollSeriesController(createContext(), {
      readVerboseLogging: async () => false,
      watchVerboseLogging: () => () => undefined,
      readCollectionConsent: async () => true,
      watchCollectionConsent: () => () => undefined,
      submitCatalog: async (observation, correlationId) => {
        submissions.push(observation);
        return {
          ok: true,
          correlationId,
          value: { status: 'accepted', acceptedEpisodeCount: observation.episodes.length },
        };
      },
    });

    await vi.waitFor(() => expect(submissions).toHaveLength(1));
    expect(submissions[0]?.episodes[0]?.providerEpisodeId).toBe('GE00374382JAJP');

    page.document.body.innerHTML = seriesMarkup('GE00374382ENUS');
    controller.requestScan();

    await vi.waitFor(() => expect(submissions).toHaveLength(2));
    expect(submissions[1]?.episodes[0]?.providerEpisodeId).toBe('GE00374382ENUS');
    controller.dispose();
  });

  it('does not inspect or submit a catalog before consent, then stops and clears on revocation', async () => {
    const page = parseHTML(`<html><body>${seriesMarkup('GE00374382JAJP')}</body></html>`);
    vi.stubGlobal('window', page.window);
    vi.stubGlobal('document', page.document);
    vi.stubGlobal('location', new URL('https://www.crunchyroll.com/series/GEXAMPLE/my-show'));
    vi.stubGlobal('MutationObserver', page.window.MutationObserver);

    let resolveConsent!: (allowed: boolean) => void;
    const consent = new Promise<boolean>(resolve => { resolveConsent = resolve; });
    let consentListener: ((allowed: boolean) => void) | undefined;
    const submissions: SeriesCatalogObservation[] = [];
    const controller = createCrunchyrollSeriesController(createContext(), {
      readVerboseLogging: async () => false,
      watchVerboseLogging: () => () => undefined,
      readCollectionConsent: () => consent,
      watchCollectionConsent: listener => {
        consentListener = listener;
        return () => undefined;
      },
      submitCatalog: async (observation, correlationId) => {
        submissions.push(observation);
        return {
          ok: true,
          correlationId,
          value: { status: 'accepted', acceptedEpisodeCount: observation.episodes.length },
        };
      },
    });

    await new Promise(resolve => setTimeout(resolve, 10));
    expect(submissions).toHaveLength(0);
    expect(controller.getSnapshot().pageUrl).toBe('');

    resolveConsent(true);
    await vi.waitFor(() => expect(submissions).toHaveLength(1));
    expect(controller.getSnapshot().pageUrl).toContain('/series/GEXAMPLE/my-show');

    consentListener?.(false);
    expect(controller.getSnapshot()).toMatchObject({
      pageUrl: '',
      status: 'starting',
      observedEpisodeCount: 0,
    });
    controller.requestScan();
    await new Promise(resolve => setTimeout(resolve, 10));
    expect(submissions).toHaveLength(1);
    controller.dispose();
  });
});
