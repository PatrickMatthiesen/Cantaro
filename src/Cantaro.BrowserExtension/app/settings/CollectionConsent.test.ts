import { createElement, act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CollectionConsent, type CollectionConsentProps } from './CollectionConsent';

let root: Root;
let container: HTMLElement;

beforeEach(() => {
  const dom = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', dom.window);
  vi.stubGlobal('document', dom.document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = dom.document.getElementById('root') as unknown as HTMLElement;
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  vi.unstubAllGlobals();
});

async function render(overrides: Partial<CollectionConsentProps> = {}) {
  const props: CollectionConsentProps = {
    choices: { watchTracking: false, catalogCollection: false },
    needsReview: true, authenticated: false, baseUrl: 'https://cantaro.example',
    busy: false, error: null,
    onSave: vi.fn(async () => {}), onRevoke: vi.fn(async () => {}),
    onReset: vi.fn(async () => {}),
    onSignIn: vi.fn(async () => true), ...overrides,
  };
  await act(async () => root.render(createElement(CollectionConsent, props)));
  return props;
}

async function clickButton(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === text);
  expect(button).toBeDefined();
  await act(async () => button!.click());
}

describe('collection consent disclosure', () => {
  it('starts unchecked and allows declining without signing in', async () => {
    const props = await render();
    expect(Array.from(container.querySelectorAll('input')).every(input => !input.checked)).toBe(true);
    expect(container.textContent).toContain('Keep track of what you watch');
    expect(container.textContent).toContain('Keep your library up to date as you watch on Crunchyroll');
    expect(container.textContent).toContain('Automatically track what I watch');
    expect(container.textContent).toContain('Help Cantaro find episodes and watch links');
    expect(container.textContent).toContain('Where your information goes');
    expect(container.textContent).toContain('Temporary matching details are deleted once processed');
    expect(container.textContent).not.toContain('Cantaro can read supported Crunchyroll pages to update your library');
    expect(container.textContent).toContain('https://cantaro.example');
    expect(container.querySelector('a')?.getAttribute('href')).toContain('/PRIVACY.md');
    await clickButton('Continue without collection');
    expect(props.onRevoke).toHaveBeenCalledOnce();
    expect(props.onSave).not.toHaveBeenCalled();
    expect(props.onSignIn).not.toHaveBeenCalled();
  });

  it.each([
    { watchTracking: true, catalogCollection: false },
    { watchTracking: false, catalogCollection: true },
    { watchTracking: true, catalogCollection: true },
    { watchTracking: false, catalogCollection: false },
  ])('saves the selected choices only after successful sign-in: %j', async choices => {
    let finishSignIn: (success: boolean) => void = () => {};
    const props = await render({
      choices,
      onSignIn: vi.fn(() => new Promise<boolean>(resolve => { finishSignIn = resolve; })),
    });
    await clickButton('Sign in to enable collection');
    expect(props.onSignIn).toHaveBeenCalledOnce();
    expect(props.onSave).not.toHaveBeenCalled();

    // Authentication can rerender the form before its sign-in promise resolves.
    await render({ ...props, authenticated: true });
    await act(async () => finishSignIn(true));
    expect(props.onSave).toHaveBeenCalledExactlyOnceWith(choices);
  });

  it('keeps choices available and does not save when sign-in is cancelled or fails', async () => {
    const choices = { watchTracking: true, catalogCollection: false };
    const props = await render({ choices, onSignIn: vi.fn(async () => false) });
    await clickButton('Sign in to enable collection');
    expect(props.onSave).not.toHaveBeenCalled();
    const inputs = Array.from(container.querySelectorAll('input'));
    expect(inputs.map(input => input.checked)).toEqual([true, false]);
  });

  it('keeps the selected choices available for retry when saving after sign-in fails', async () => {
    const choices = { watchTracking: true, catalogCollection: false };
    const props = await render({ choices });
    await clickButton('Sign in to enable collection');
    await render({ ...props, authenticated: true, error: 'Could not save choices.' });
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Could not save choices.');
    await clickButton('Allow selected collection');
    expect(props.onSave).toHaveBeenNthCalledWith(2, choices);
  });

  it('requires a separate explicit save and preserves granular choices', async () => {
    const props = await render({ authenticated: true, choices: { watchTracking: true, catalogCollection: false } });
    expect(props.onSave).not.toHaveBeenCalled();
    await clickButton('Allow selected collection');
    expect(props.onSave).toHaveBeenCalledWith({ watchTracking: true, catalogCollection: false });
  });

  it('exposes revocation after onboarding and announces storage errors', async () => {
    const props = await render({ authenticated: true, needsReview: false,
      choices: { watchTracking: true, catalogCollection: true }, error: 'Could not save choices.' });
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Could not save choices.');
    await clickButton('Turn off all collection');
    expect(props.onRevoke).toHaveBeenCalledOnce();
  });

  it('allows resetting the saved choices for a fresh review', async () => {
    const props = await render({ authenticated: true, needsReview: false });
    await clickButton('Reset choices');
    expect(props.onReset).toHaveBeenCalledOnce();
    expect(props.onSave).not.toHaveBeenCalled();
  });
});
