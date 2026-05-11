const REMOTE_REFRESH_TTL_MS = 60 * 60 * 1000;

export function remoteCheckTimestampKey(providerId: string): string {
    return `cantaro.media.provider.${providerId}.lastRemoteCheckAt`;
}

export function readStoredValue(key: string): string | null {
    try {
        return window.localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function writeStoredValue(key: string, value: string): void {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // Local storage is best-effort only.
    }
}

export function clearStoredValue(key: string): void {
    try {
        window.localStorage.removeItem(key);
    } catch {
        // Local storage is best-effort only.
    }
}

export function isRemoteCheckStale(lastCheckedAt: string | null): boolean {
    if (!lastCheckedAt) {
        return true;
    }

    const parsed = Date.parse(lastCheckedAt);
    if (Number.isNaN(parsed)) {
        return true;
    }

    return Date.now() - parsed >= REMOTE_REFRESH_TTL_MS;
}

export function formatTimestamp(timestamp: string | null): string | null {
    if (!timestamp) {
        return null;
    }

    const parsed = new Date(timestamp);
    if (Number.isNaN(parsed.getTime())) {
        return null;
    }

    return parsed.toLocaleString();
}
