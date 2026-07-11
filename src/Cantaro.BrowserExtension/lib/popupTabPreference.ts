export type PopupTab = 'music' | 'media';

const LAST_POPUP_TAB_STORAGE_KEY = 'cantaro:last-popup-tab';

export function normalizePopupTab(value: unknown): PopupTab {
  return value === 'media' ? 'media' : 'music';
}

export async function readLastPopupTab(): Promise<PopupTab> {
  try {
    const stored = await browser.storage.local.get(LAST_POPUP_TAB_STORAGE_KEY);
    return normalizePopupTab(stored[LAST_POPUP_TAB_STORAGE_KEY]);
  } catch {
    return 'music';
  }
}

export async function rememberPopupTab(tab: PopupTab): Promise<void> {
  try {
    await browser.storage.local.set({ [LAST_POPUP_TAB_STORAGE_KEY]: tab });
  } catch {
    // The popup remains usable when extension storage is unavailable.
  }
}
