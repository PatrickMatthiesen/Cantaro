import { afterEach, describe, expect, test } from 'bun:test';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';

type Step = { name: string; run?: string; env?: Record<string, string> };
const workflow = Bun.YAML.parse(readFileSync(new URL('../.github/workflows/publish-extension.yml', import.meta.url), 'utf8')) as {
  jobs: Record<string, { steps: Step[] }>;
};
const chrome = workflow.jobs['publish-stores'].steps.find(step => step.name === 'Submit Chrome extension for staged publishing')!;
const tag = workflow.jobs['tag-release'].steps.find(step => step.name === 'Create immutable extension version tag')!;
const temporaryRoot = resolve(tmpdir());
const fixtures: string[] = [];

function fixture() {
  const directory = mkdtempSync(join(temporaryRoot, 'cantaro-release-'));
  fixtures.push(directory);
  return directory;
}

afterEach(() => {
  for (const directory of fixtures.splice(0)) {
    if (dirname(resolve(directory)) !== temporaryRoot) throw new Error('Unexpected fixture location');
    rmSync(directory, { recursive: true, force: true });
  }
});

function run(command: string[], cwd: string, env: Record<string, string> = {}) {
  const result = Bun.spawnSync(command, {
    cwd,
    env: { ...process.env, GIT_CONFIG_GLOBAL: join(cwd, 'no-global-config'), GIT_CONFIG_NOSYSTEM: '1', ...env },
    stdout: 'pipe', stderr: 'pipe',
  });
  return { code: result.exitCode, stdout: result.stdout.toString(), stderr: result.stderr.toString() };
}

function submit(serviceAccount: string, exitCode = 0) {
  const directory = fixture();
  const capture = join(directory, 'submission.json');
  // Replace only the external command. Execute the workflow's real Bash step,
  // including JSON extraction, validation and environment exports.
  const result = run(['bash', '-c', `
    bun() {
      node -e 'require("node:fs").writeFileSync(process.env.CAPTURE, JSON.stringify({
        args: process.argv.slice(1), email: process.env.CHROME_SERVICE_ACCOUNT_CLIENT_EMAIL,
        key: process.env.CHROME_SERVICE_ACCOUNT_PRIVATE_KEY,
        api: process.env.CHROME_API_VERSION, publishType: process.env.CHROME_PUBLISH_TYPE,
        zip: process.env.CHROME_ZIP, id: process.env.CHROME_EXTENSION_ID,
        publisher: process.env.CHROME_PUBLISHER_ID
      }))' "$@"
      return "$SUBMIT_EXIT_CODE"
    }
    ${chrome.run}
  `], directory, {
    ...chrome.env,
    CHROME_SERVICE_ACCOUNT_JSON: serviceAccount,
    CHROME_SERVICE_ACCOUNT_CLIENT_EMAIL: '', CHROME_SERVICE_ACCOUNT_PRIVATE_KEY: '',
    CHROME_EXTENSION_ID: 'test-extension', CHROME_PUBLISHER_ID: 'test-publisher',
    CHROME_ZIP: 'extension.zip', CAPTURE: capture, SUBMIT_EXIT_CODE: String(exitCode),
  });
  return { ...result, captured: existsSync(capture) ? JSON.parse(readFileSync(capture, 'utf8')) : null };
}

describe('Chrome submission workflow', () => {
  test.each(['-----BEGIN PRIVATE KEY-----\ndummy-key-material\n-----END PRIVATE KEY-----', '-hv'])(
    'passes option-looking credentials through the environment: %s', key => {
      const result = submit(JSON.stringify({ client_email: 'publisher@example.test', private_key: key }));
      expect(result.code).toBe(0);
      expect(result.captured).toEqual({
        args: ['wxt', 'submit'], email: 'publisher@example.test', key,
        api: 'v2', publishType: 'STAGED_PUBLISH', zip: 'extension.zip',
        id: 'test-extension', publisher: 'test-publisher',
      });
    },
  );

  test('fails the step when WXT fails', () => {
    const result = submit(JSON.stringify({ client_email: 'publisher@example.test', private_key: 'dummy' }), 37);
    expect(result.code).toBe(37);
  });

  test.each(['{}', '{"client_email":"publisher@example.test"}', '{"private_key":"dummy"}', 'null', 'invalid-json'])(
    'does not call WXT with incomplete or invalid credentials: %s', json => {
      const result = submit(json);
      expect(result.code).not.toBe(0);
      expect(result.captured).toBeNull();
    },
  );
});

test('release tags are created once and never moved to a different commit', () => {
  const directory = fixture();
  function git(...args: string[]) {
    const result = run(['git', ...args], directory);
    expect(result.code).toBe(0);
    return result.stdout.trim();
  }
  git('init', '--bare', '--quiet', 'remote.git');
  git('init', '--quiet');
  git('config', 'user.name', 'Release Test');
  git('config', 'user.email', 'release-test@example.test');
  git('remote', 'add', 'origin', './remote.git');
  writeFileSync(join(directory, 'release.txt'), 'first');
  git('add', 'release.txt');
  git('commit', '--quiet', '-m', 'first');
  const first = git('rev-parse', 'HEAD');
  const env = { EXTENSION_VERSION: '9.8.7', TARGET_SHA: first, CHROME_SUBMITTED: 'true', FIREFOX_SUBMITTED: 'false' };

  expect(run(['bash', '-c', tag.run!], directory, env).code).toBe(0);
  const remoteTag = () => git('--git-dir=remote.git', 'rev-parse', 'extension/v9.8.7^{commit}');
  expect(remoteTag()).toBe(first);
  const repeated = run(['bash', '-c', tag.run!], directory, env);
  expect(repeated.code).toBe(0);
  expect(repeated.stdout).toContain(`already points to ${first}`);

  writeFileSync(join(directory, 'release.txt'), 'second');
  git('commit', '--quiet', '-am', 'second');
  const conflicting = run(['bash', '-c', tag.run!], directory, { ...env, TARGET_SHA: git('rev-parse', 'HEAD') });
  expect(conflicting.code).not.toBe(0);
  expect(conflicting.stdout).toContain('refusing to move');
  expect(remoteTag()).toBe(first);
});
