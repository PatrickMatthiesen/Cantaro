import { browserSettingsRepository } from '../../platform/settings/settingsRepository';

export async function openCantaroPage(path: string) {
  const settings = await browserSettingsRepository.read();
  await browser.tabs.create({ url: `${settings.baseUrl}${path}` });
}
