export interface ExtensionStorage {
  get(key: string | string[]): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
  remove(key: string): Promise<void>;
}

export function browserStorage(area: 'local' | 'session'): ExtensionStorage {
  return {
    get: key => browser.storage[area].get(key),
    set: items => browser.storage[area].set(items),
    remove: key => browser.storage[area].remove(key),
  };
}
