export type ExtensionBuildChannel = 'dev' | 'prod';

/**
 * WXT uses development for `wxt`/serve and production for build/zip by default.
 * Treat any non-development mode as production so custom release modes keep the
 * installed extension identity and DOM namespace stable.
 */
export function extensionBuildChannelForMode(mode: string): ExtensionBuildChannel {
  return mode === 'development' ? 'dev' : 'prod';
}

const extensionBuildChannel = extensionBuildChannelForMode(import.meta.env.MODE);

/**
 * FNV-1a over UTF-16 code units, represented in base36. A 64-bit value keeps
 * the DOM namespace short while making collisions between extension IDs
 * impractical for the small number of Cantaro installations on one page.
 */
export function hashExtensionRuntimeId(runtimeId: string): string {
  const offsetBasis = 14695981039346656037n;
  const prime = 1099511628211n;
  const mask = 0xffffffffffffffffn;
  let hash = offsetBasis;

  for (let index = 0; index < runtimeId.length; index += 1) {
    hash ^= BigInt(runtimeId.charCodeAt(index));
    hash = (hash * prime) & mask;
  }

  return hash.toString(36).padStart(13, '0');
}

export function extensionNamespace(
  runtimeId: string,
  channel: ExtensionBuildChannel = extensionBuildChannel,
): string {
  return `cantaro-${channel}-${hashExtensionRuntimeId(runtimeId)}`;
}

export function currentExtensionNamespace(): string {
  return extensionNamespace(browser.runtime.id);
}

function extensionLogPrefix(channel: ExtensionBuildChannel = extensionBuildChannel): string {
  return channel === 'dev' ? 'Cantaro Dev' : 'Cantaro';
}

/** Replace an existing Cantaro prefix, or add the channel-aware prefix. */
export function extensionLogMessage(
  message: string,
  channel: ExtensionBuildChannel = extensionBuildChannel,
): string {
  const unprefixedMessage = message.replace(/^Cantaro(?: Dev)?:\s*/, '');
  return `${extensionLogPrefix(channel)}: ${unprefixedMessage}`;
}
