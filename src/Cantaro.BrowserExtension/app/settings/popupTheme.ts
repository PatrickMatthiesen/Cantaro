type ThemePreference = 'system' | 'light' | 'dark';

export function applyPopupTheme(preference: ThemePreference): void {
  const theme = preference === 'system'
    ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : preference;
  document.documentElement.dataset.theme = theme;
}

export async function restorePopupTheme(): Promise<void> {
  const stored: Record<string, unknown> = await browser.storage.local.get('cantaroTheme').catch(() => ({}));
  const theme = stored.cantaroTheme;
  applyPopupTheme(theme === 'dark' || theme === 'light' ? theme : 'system');
}
