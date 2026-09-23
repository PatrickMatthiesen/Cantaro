import { submit, type InlineConfig, type SubmitResults } from 'publish-browser-extension';

export type ExtensionStore = 'chrome' | 'firefox';

type Environment = Record<string, string | undefined>;
type SubmitExtension = (config: InlineConfig) => Promise<SubmitResults>;

function required(environment: Environment, name: string): string {
  const value = environment[name];
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function chromeConfig(environment: Environment): InlineConfig {
  let serviceAccount: { client_email?: unknown; private_key?: unknown };
  try {
    serviceAccount = JSON.parse(required(environment, 'CHROME_SERVICE_ACCOUNT_JSON'));
  } catch {
    throw new Error('CHROME_SERVICE_ACCOUNT_JSON must be valid JSON.');
  }

  if (typeof serviceAccount.client_email !== 'string' || !serviceAccount.client_email) {
    throw new Error('CHROME_SERVICE_ACCOUNT_JSON must contain client_email.');
  }
  if (typeof serviceAccount.private_key !== 'string' || !serviceAccount.private_key) {
    throw new Error('CHROME_SERVICE_ACCOUNT_JSON must contain private_key.');
  }

  return {
    chrome: {
      apiVersion: 'v2',
      extensionId: required(environment, 'CHROME_EXTENSION_ID'),
      publisherId: required(environment, 'CHROME_PUBLISHER_ID'),
      serviceAccountClientEmail: serviceAccount.client_email,
      serviceAccountPrivateKey: serviceAccount.private_key,
      publishType: 'STAGED_PUBLISH',
      skipSubmitReview: false,
      zip: required(environment, 'CHROME_ZIP'),
    },
  };
}

function firefoxConfig(environment: Environment): InlineConfig {
  return {
    firefox: {
      channel: 'unlisted',
      extensionId: required(environment, 'FIREFOX_EXTENSION_ID'),
      jwtIssuer: required(environment, 'FIREFOX_JWT_ISSUER'),
      jwtSecret: required(environment, 'FIREFOX_JWT_SECRET'),
      skipSubmitReview: false,
      sourcesZip: required(environment, 'FIREFOX_SOURCES_ZIP'),
      zip: required(environment, 'FIREFOX_ZIP'),
    },
  };
}

export function buildSubmissionConfig(store: ExtensionStore, environment: Environment): InlineConfig {
  return store === 'chrome' ? chromeConfig(environment) : firefoxConfig(environment);
}

export async function submitBrowserExtension(
  store: ExtensionStore,
  environment: Environment,
  submitExtension: SubmitExtension = submit,
): Promise<void> {
  const results = await submitExtension(buildSubmissionConfig(store, environment));
  if (results[store]?.success !== true) {
    throw new Error(`${store} publisher did not confirm a successful upload and submission.`);
  }

  console.log(`Confirmed ${store} extension upload and submission.`);
}

if (import.meta.main) {
  const store = Bun.argv[2];
  if (store !== 'chrome' && store !== 'firefox') {
    console.error('Usage: bun scripts/submit-browser-extension.ts <chrome|firefox>');
    process.exit(1);
  }

  try {
    await submitBrowserExtension(store, process.env);
  } catch (error) {
    console.error(error instanceof Error ? error.message : 'Extension submission failed.');
    process.exit(1);
  }
}
