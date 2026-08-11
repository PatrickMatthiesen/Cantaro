import { DEFAULT_BASE_URL, normalizeBaseUrl } from './extensionSettings';

export function apiOriginMatchPattern(value: string | null | undefined): string | null {
  const normalized = normalizeBaseUrl(value);
  if (!normalized) return null;

  try {
    const url = new URL(normalized);
    return url.protocol === 'http:' || url.protocol === 'https:'
      ? `${url.origin}/*`
      : null;
  } catch {
    return null;
  }
}

export async function ensureApiPermission(baseUrl: string): Promise<void> {
  const origin = apiOriginMatchPattern(baseUrl);
  if (!origin) return;

  const permissions = browser.permissions;
  if (!permissions?.contains) return;
  if (!permissions.request) return;

  const request = { origins: [origin] };
  if (await permissions.contains(request)) return;

  if (new URL(baseUrl).protocol !== 'https:') {
    throw new Error('Self-hosted Cantaro instances must use HTTPS.');
  }

  if (!await permissions.request(request)) {
    throw new Error(`Access to ${new URL(baseUrl).origin} was not granted.`);
  }
}

export async function removeReplacedApiPermission(
  previousBaseUrl: string,
  currentBaseUrl: string,
): Promise<void> {
  const previousOrigin = apiOriginMatchPattern(previousBaseUrl);
  const currentOrigin = apiOriginMatchPattern(currentBaseUrl);
  if (!previousOrigin
    || previousOrigin === currentOrigin
    || previousOrigin === apiOriginMatchPattern(DEFAULT_BASE_URL)) return;

  const permissions = browser.permissions;
  if (!permissions?.remove) return;

  try {
    await permissions.remove({ origins: [previousOrigin] });
  } catch (error) {
    console.warn(`Could not remove access to ${previousOrigin}`, error);
  }
}
