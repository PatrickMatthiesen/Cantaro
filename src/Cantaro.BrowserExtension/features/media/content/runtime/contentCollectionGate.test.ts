import { describe, expect, it } from 'vitest';
import { createContentCollectionGate } from './contentCollectionGate';

describe('content collection gate', () => {
  it('ignores an initial consent read that completes after a status change', async () => {
    let resolveInitial!: (allowed: boolean) => void;
    let listener!: (allowed: boolean) => void;
    const states: Array<{ allowed: boolean; generation: number }> = [];
    const gate = createContentCollectionGate({
      readCollectionConsent: () => new Promise<boolean>(resolve => { resolveInitial = resolve; }),
      watchCollectionConsent: next => {
        listener = next;
        return () => undefined;
      },
    }, state => states.push({ allowed: state.allowed, generation: state.generation }));

    listener(true);
    listener(false);
    resolveInitial(true);
    await Promise.resolve();

    expect(gate.getState()).toMatchObject({ allowed: false, ready: true, generation: 1 });
    expect(states.at(-1)).toEqual({ allowed: false, generation: 1 });
    gate.dispose();
  });
});
