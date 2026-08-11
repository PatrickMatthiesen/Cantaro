import packageMetadata from '../../package.json';

export function getExtensionVersion(): string {
  if (typeof browser !== 'undefined' && browser.runtime?.getManifest) {
    return browser.runtime.getManifest().version;
  }

  return packageMetadata.version;
}
