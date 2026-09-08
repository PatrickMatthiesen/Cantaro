export interface ContentCollectionConsentDependencies {
  readCollectionConsent(): Promise<boolean>;
  watchCollectionConsent(listener: (allowed: boolean) => void): () => void;
}

export interface ContentCollectionGateState {
  allowed: boolean;
  ready: boolean;
  generation: number;
}

export interface ContentCollectionGate {
  getState(): ContentCollectionGateState;
  dispose(): void;
}

function applyContentCollectionState(
  state: ContentCollectionGateState,
  currentAllowed: boolean,
  setAllowed: (allowed: boolean) => void,
  onRevoked: () => void,
  onEnabled: () => void,
  onReady: () => void,
): boolean {
  const changed = currentAllowed !== state.allowed;
  setAllowed(state.allowed);
  if (!state.allowed) onRevoked();
  else {
    onEnabled();
    if (state.ready && changed) onReady();
  }
  return changed;
}

export function createContentCollectionStateHandler(
  getAllowed: () => boolean,
  setAllowed: (allowed: boolean) => void,
  onRevoked: () => void,
  onEnabled: () => void,
  onReady: () => void,
): (state: ContentCollectionGateState) => void {
  return state => {
    applyContentCollectionState(
      state,
      getAllowed(),
      setAllowed,
      onRevoked,
      onEnabled,
      onReady,
    );
  };
}

export function updateContentSnapshot<T extends { pageUrl: string }>(
  snapshot: T,
  changes: Partial<T>,
  allowed: boolean,
): T {
  return {
    ...snapshot,
    ...changes,
    pageUrl: allowed ? stripUrlQueryAndFragment(location.href) : '',
  };
}

/** Keeps asynchronous consent reads from reopening a controller after revoke. */
export function createContentCollectionGate(
  dependencies: ContentCollectionConsentDependencies,
  onStateChanged: (state: ContentCollectionGateState) => void,
): ContentCollectionGate {
  let state: ContentCollectionGateState = { allowed: false, ready: false, generation: 0 };
  let revision = 0;
  let disposed = false;

  const apply = (allowed: boolean, ready: boolean) => {
    if (disposed) return;
    const changed = state.allowed !== allowed;
    state = {
      allowed,
      ready: state.ready || ready,
      generation: state.generation + (changed && !allowed ? 1 : 0),
    };
    onStateChanged(state);
  };

  const stopWatching = dependencies.watchCollectionConsent(allowed => {
    revision++;
    apply(allowed, true);
  });
  const initialRevision = revision;
  void dependencies.readCollectionConsent()
    .then(allowed => {
      if (initialRevision === revision) apply(allowed, true);
    })
    .catch(() => {
      if (initialRevision === revision) apply(false, true);
    });

  return {
    getState: () => state,
    dispose: () => {
      if (disposed) return;
      disposed = true;
      stopWatching();
    },
  };
}
import { stripUrlQueryAndFragment } from '../../contracts/observationUrl';
