import { describe, expect, test } from 'bun:test';
import type { InlineConfig, SubmitResults } from 'publish-browser-extension';
import { buildSubmissionConfig, submitBrowserExtension } from './submit-browser-extension';

const multilinePrivateKey = [
  '-----BEGIN PRIVATE KEY-----',
  'dummy-key-material',
  '-----END PRIVATE KEY-----',
  '',
].join('\n');

const chromeEnvironment = {
  CHROME_EXTENSION_ID: 'extension-id',
  CHROME_PUBLISHER_ID: 'publisher-id',
  CHROME_SERVICE_ACCOUNT_JSON: JSON.stringify({
    client_email: 'publisher@example.test',
    private_key: multilinePrivateKey,
  }),
  CHROME_ZIP: '/tmp/extension.zip',
};

describe('extension store submission', () => {
  test('passes the multiline Chrome private key through the API config without CLI parsing', () => {
    const config = buildSubmissionConfig('chrome', chromeEnvironment);

    expect(config.chrome?.serviceAccountPrivateKey).toBe(multilinePrivateKey);
    expect(config.chrome?.publishType).toBe('STAGED_PUBLISH');
    expect(config.chrome?.skipSubmitReview).toBe(false);
  });

  test('only succeeds after the publisher confirms the selected store', async () => {
    let receivedConfig: InlineConfig | undefined;
    const confirmedSubmit = async (config: InlineConfig): Promise<SubmitResults> => {
      receivedConfig = config;
      return { chrome: { success: true } };
    };

    await submitBrowserExtension('chrome', chromeEnvironment, confirmedSubmit);
    expect(receivedConfig?.chrome?.extensionId).toBe('extension-id');
  });

  test('rejects a no-op publisher result', async () => {
    const noOpSubmit = async (): Promise<SubmitResults> => ({});

    await expect(submitBrowserExtension('chrome', chromeEnvironment, noOpSubmit))
      .rejects.toThrow('did not confirm a successful upload and submission');
  });
});
