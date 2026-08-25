import React from 'react';
import ReactDOM from 'react-dom/client';
import { currentExtensionNamespace } from '../../../../../../platform/diagnostics/extensionIdentity';
import type {
  ResolveWatchObservationRequest,
  WatchResolution,
} from '../../../../contracts/watchObservation';

export interface WatchResolutionOverlayHandle {
  close(): void;
}

type ResolutionChoice =
  | { kind: 'candidate'; id: string; title: string; subtitle: string; candidateId: string }
  | { kind: 'provider'; id: string; title: string; subtitle: string; providerId: string; providerMediaId: string; requiresAdd: boolean };

export function showWatchResolutionOverlay(
  resolution: WatchResolution,
  resolve: (request: ResolveWatchObservationRequest) => Promise<void>,
): WatchResolutionOverlayHandle {
  const container = ensureOverlayContainer();
  const root = ReactDOM.createRoot(container);
  const close = () => {
    root.unmount();
    container.remove();
  };
  root.render(
    <React.StrictMode>
      <ResolutionOverlay resolution={resolution} resolve={resolve} onClose={close} />
    </React.StrictMode>,
  );
  return { close };
}

function ResolutionOverlay({
  resolution,
  resolve,
  onClose,
}: {
  resolution: WatchResolution;
  resolve: (request: ResolveWatchObservationRequest) => Promise<void>;
  onClose: () => void;
}) {
  const form = useResolutionForm(resolution, resolve, onClose);

  return (
    <div style={styles.backdrop}>
      <section style={styles.panel} aria-label="Resolve episode">
        <header style={styles.header}>
          <div>
            <h2 style={styles.title}>Match this episode</h2>
            <p style={styles.muted}>{resolution.observedTitle}</p>
          </div>
          <button type="button" style={styles.quietButton} onClick={onClose}>Close</button>
        </header>
        <ResolutionChoiceList form={form} unavailableReason={resolution.providerChoicesUnavailableReason} />
        <ResolutionFields form={form} />
        <ResolutionAction form={form} />
      </section>
    </div>
  );
}

function useResolutionForm(
  resolution: WatchResolution,
  resolve: (request: ResolveWatchObservationRequest) => Promise<void>,
  onClose: () => void,
) {
  const choices = React.useMemo(() => resolutionChoices(resolution), [resolution]);
  const [selectedId, setSelectedId] = React.useState(choices[0]?.id ?? '');
  const [offset, setOffset] = React.useState(String(resolution.suggestedEpisodeOffset));
  const [confirmAdd, setConfirmAdd] = React.useState(false);
  const [resolving, setResolving] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const selected = choices.find(choice => choice.id === selectedId);
  const parsedOffset = parseOffset(offset);
  const addRequired = selected?.kind === 'provider' && selected.requiresAdd;
  const canSubmit = canSubmitResolution(selected, parsedOffset, addRequired, confirmAdd);
  const submit = () => submitResolution({
    resolution,
    resolve,
    onClose,
    selected,
    parsedOffset,
    confirmAdd,
    setError,
    setResolving,
  });
  return {
    choices, selectedId, setSelectedId, offset, setOffset, confirmAdd, setConfirmAdd,
    resolving, error, addRequired, canSubmit, submit,
  };
}

type ResolutionForm = ReturnType<typeof useResolutionForm>;

function ResolutionChoiceList({
  form,
  unavailableReason,
}: {
  form: ResolutionForm;
  unavailableReason?: string;
}) {
  return (
    <div style={styles.choices}>
      {form.choices.map(choice => (
        <label key={choice.id} style={choice.id === form.selectedId ? styles.selectedChoice : styles.choice}>
          <input
            type="radio"
            name="cantaro-resolution"
            value={choice.id}
            checked={choice.id === form.selectedId}
            onChange={() => {
              form.setSelectedId(choice.id);
              form.setConfirmAdd(false);
            }}
          />
          <span><strong>{choice.title}</strong><small style={styles.subtitle}>{choice.subtitle}</small></span>
        </label>
      ))}
      {form.choices.length === 0 && (
        <p style={styles.muted}>{unavailableReason ?? 'No matching titles are available.'}</p>
      )}
    </div>
  );
}

function ResolutionFields({ form }: { form: ResolutionForm }) {
  return (
    <>
      <label style={styles.field}>
        Episode offset
        <input type="number" step="1" value={form.offset} style={styles.input} onChange={event => form.setOffset(event.target.value)} />
      </label>
      {form.addRequired && (
        <label style={styles.confirm}>
          <input type="checkbox" checked={form.confirmAdd} onChange={event => form.setConfirmAdd(event.target.checked)} />
          Add this title to my Cantaro library
        </label>
      )}
    </>
  );
}

function ResolutionAction({ form }: { form: ResolutionForm }) {
  return (
    <>
      {form.error && <p role="alert" style={styles.error}>{form.error}</p>}
      <button type="button" style={styles.primaryButton} disabled={!form.canSubmit || form.resolving} onClick={() => void form.submit()}>
        {form.resolving ? 'Saving…' : 'Use this match'}
      </button>
    </>
  );
}

function resolutionChoices(resolution: WatchResolution): ResolutionChoice[] {
  return [
    ...resolution.candidates.map(candidate => ({
      kind: 'candidate' as const,
      id: `candidate:${candidate.candidateId}`,
      title: candidate.title,
      subtitle: candidate.explanation ?? candidate.candidateSource,
      candidateId: candidate.candidateId,
    })),
    ...resolution.providerChoices.map(choice => ({
      kind: 'provider' as const,
      id: `provider:${choice.providerId}:${choice.providerMediaId}`,
      title: choice.title,
      subtitle: choice.isInLibrary ? choice.providerId : `${choice.providerId} · not in library`,
      providerId: choice.providerId,
      providerMediaId: choice.providerMediaId,
      requiresAdd: !choice.isInLibrary,
    })),
  ];
}

function canSubmitResolution(
  selected: ResolutionChoice | undefined,
  offset: number | null,
  addRequired: boolean,
  confirmAdd: boolean,
): boolean {
  if (!selected || offset === null) return false;
  return !addRequired || confirmAdd;
}

async function submitResolution(input: {
  resolution: WatchResolution;
  resolve: (request: ResolveWatchObservationRequest) => Promise<void>;
  onClose: () => void;
  selected: ResolutionChoice | undefined;
  parsedOffset: number | null;
  confirmAdd: boolean;
  setError: (value: string | null) => void;
  setResolving: (value: boolean) => void;
}): Promise<void> {
  if (!input.selected || input.parsedOffset === null) return;
  input.setResolving(true);
  input.setError(null);
  try {
    await input.resolve(toResolveRequest(
      input.resolution.observationId,
      input.selected,
      input.parsedOffset,
      input.confirmAdd,
    ));
    input.onClose();
  } catch (caught) {
    input.setError(caught instanceof Error ? caught.message : 'Failed to resolve episode.');
  } finally {
    input.setResolving(false);
  }
}

function toResolveRequest(
  observationId: string,
  choice: ResolutionChoice,
  episodeOffset: number,
  confirmAdd: boolean,
): ResolveWatchObservationRequest {
  return choice.kind === 'candidate'
    ? { observationId, candidateId: choice.candidateId, episodeOffset }
    : {
      observationId,
      providerId: choice.providerId,
      providerMediaId: choice.providerMediaId,
      episodeOffset,
      addToLibraryConfirmed: confirmAdd,
    };
}

function parseOffset(value: string): number | null {
  if (!/^-?\d+$/.test(value.trim())) return null;
  const parsed = Number.parseInt(value, 10);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function ensureOverlayContainer(): HTMLDivElement {
  const overlayId = `${currentExtensionNamespace()}-resolution-overlay`;
  document.getElementById(overlayId)?.remove();
  const container = document.createElement('div');
  container.id = overlayId;
  container.dataset.cantaroOwner = currentExtensionNamespace();
  document.documentElement.appendChild(container);
  return container;
}

const styles = {
  backdrop: { position: 'fixed', inset: 0, zIndex: 2147483647, display: 'grid', placeItems: 'center', padding: 16, background: 'rgba(15, 23, 42, .5)', backdropFilter: 'blur(3px)' },
  panel: { width: 'min(480px, calc(100vw - 32px))', maxHeight: 'min(720px, calc(100vh - 32px))', overflow: 'auto', border: '1px solid #39435a', borderRadius: 16, padding: 18, color: '#edf1fa', background: '#111827', boxShadow: '0 24px 70px rgba(0,0,0,.45)', fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif' },
  header: { display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start' },
  title: { margin: 0, fontSize: 20 },
  muted: { margin: '5px 0 0', color: '#aebbd2', fontSize: 13 },
  choices: { display: 'grid', gap: 8, margin: '16px 0' },
  choice: { display: 'flex', gap: 10, padding: 12, border: '1px solid #303a50', borderRadius: 10, cursor: 'pointer' },
  selectedChoice: { display: 'flex', gap: 10, padding: 12, border: '1px solid #8b5cf6', borderRadius: 10, background: '#2e1f5d', cursor: 'pointer' },
  subtitle: { display: 'block', marginTop: 3, color: '#aebbd2', fontSize: 12 },
  field: { display: 'grid', gap: 6, color: '#c2ccdd', fontSize: 13 },
  input: { width: '100%', border: '1px solid #39435a', borderRadius: 8, padding: '9px 10px', color: '#edf1fa', background: '#0b1220' },
  confirm: { display: 'flex', gap: 8, alignItems: 'center', marginTop: 12, color: '#c2ccdd', fontSize: 13 },
  error: { color: '#fca5a5', fontSize: 13 },
  primaryButton: { width: '100%', marginTop: 16, border: 0, borderRadius: 10, padding: '11px 14px', color: 'white', background: '#7c3aed', fontWeight: 700, cursor: 'pointer' },
  quietButton: { border: 0, borderRadius: 8, padding: '7px 9px', color: '#c2ccdd', background: '#263148', cursor: 'pointer' },
} satisfies Record<string, React.CSSProperties>;
