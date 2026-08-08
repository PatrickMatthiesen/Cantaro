import type { PrimaryAppSection } from './extensionAppTypes';

const STORAGE_KEY = 'cantaro.popup.section.v1';

function isPrimarySection(value: unknown): value is PrimaryAppSection {
  return value === 'music' || value === 'media';
}

export async function readPreferredSection(): Promise<PrimaryAppSection> {
  try {
    const stored = await browser.storage.local.get(STORAGE_KEY);
    return isPrimarySection(stored[STORAGE_KEY]) ? stored[STORAGE_KEY] : 'music';
  } catch {
    return 'music';
  }
}

export async function rememberPreferredSection(section: PrimaryAppSection) {
  await browser.storage.local.set({ [STORAGE_KEY]: section }).catch(() => undefined);
}
