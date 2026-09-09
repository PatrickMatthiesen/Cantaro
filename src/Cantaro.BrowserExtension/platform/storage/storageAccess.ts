interface LocalStorageAccessController {
  setAccessLevel?(options: { accessLevel: 'TRUSTED_CONTEXTS' }): Promise<void>;
}

export async function restrictLocalStorageToTrustedContexts(
  storage: LocalStorageAccessController = browser.storage.local as LocalStorageAccessController,
): Promise<void> {
  await storage.setAccessLevel?.({ accessLevel: 'TRUSTED_CONTEXTS' });
}
