import { useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type {
  MediaObservationCandidateDto,
  MediaObservationProviderChoiceDto,
  ResolveMediaObservationRequest,
  SubmitMediaObservationResponse,
} from './mediaObservation';

type ResolutionItem =
  | { kind: 'candidate'; id: string; title: string; subtitle: string; candidate: MediaObservationCandidateDto; inLibrary: true }
  | { kind: 'provider'; id: string; title: string; subtitle: string; choice: MediaObservationProviderChoiceDto; inLibrary: boolean };

export interface MediaResolutionPickerProps {
  response: SubmitMediaObservationResponse;
  surface: 'overlay' | 'popup';
  resolving?: boolean;
  error?: string | null;
  onResolve: (observationId: string, request: ResolveMediaObservationRequest) => Promise<void> | void;
  onClose?: () => void;
}

export function MediaResolutionPicker(props: MediaResolutionPickerProps) {
  return <MediaResolutionPickerBody {...props} />;
}

function MediaResolutionPickerBody({
  response,
  surface,
  resolving = false,
  error,
  onResolve,
  onClose,
}: MediaResolutionPickerProps) {
  const state = useResolutionPickerState(response);
  const { observation, items, selectedId, selected, offsetInput, setOffsetInput, setSelectedId, confirmAdd, setConfirmAdd } = state;
  const parsedOffset = parseOffsetInput(offsetInput);
  const derived = getPickerDerivedState(response, surface, observation?.observedTitle, parsedOffset.value, selected, confirmAdd);
  const offsetError = getOffsetValidationError(derived.observedProgress, derived.resolvedProgress, parsedOffset);
  const submit = () => submitResolution({
    selected,
    confirmAdd,
    offset: parsedOffset.value,
    offsetError,
    observationId: response.observationId,
    setConfirmAdd,
    onResolve,
  });

  return (
    <section style={derived.panelStyle} aria-label="Resolve episode">
      <PickerHeader title={derived.title} onClose={onClose} />
      <PickerMeta observedProgress={derived.observedProgress} matchStatus={response.matchStatus} />
      <ResolutionChoices
        items={items}
        unavailableReason={response.providerChoicesUnavailableReason}
        selectedId={selectedId}
        onSelect={(id) => {
          setSelectedId(id);
          setConfirmAdd(false);
        }}
      />
      <OffsetControl
        offsetInput={offsetInput}
        resolvedProgress={derived.resolvedProgress}
        onOffsetChange={setOffsetInput}
      />
      <PickerNotice confirmAdd={confirmAdd} error={error} offsetError={offsetError} />
      <PickerActions resolving={resolving} confirmAdd={confirmAdd} disabled={!selected || Boolean(offsetError)} onSubmit={submit} />
    </section>
  );
}

function getPickerDerivedState(
  response: SubmitMediaObservationResponse,
  surface: 'overlay' | 'popup',
  observedTitle: string | undefined,
  offset: number | undefined,
  selected: ResolutionItem | undefined,
  confirmAdd: boolean,
) {
  const observedProgress = getObservedProgress(response);
  return {
    title: observedTitle ?? response.matchedTitle ?? 'Resolve episode',
    observedProgress,
    resolvedProgress: getResolvedProgress(observedProgress, offset),
    needsLibraryConfirm: needsLibraryConfirmation(selected, confirmAdd),
    panelStyle: surface === 'overlay' ? styles.overlayPanel : styles.popupPanel,
  };
}

function getObservedProgress(response: SubmitMediaObservationResponse): number | undefined {
  return response.observedProgress ?? response.observation?.observedProgress;
}

function getResolvedProgress(observedProgress: number | undefined, offset: number | undefined): number | undefined {
  return typeof observedProgress === 'number' && typeof offset === 'number' ? observedProgress + offset : undefined;
}

function needsLibraryConfirmation(selected: ResolutionItem | undefined, confirmAdd: boolean): boolean {
  return selected?.kind === 'provider' && !selected.inLibrary && !confirmAdd;
}

async function submitResolution({
  selected,
  confirmAdd,
  offset,
  offsetError,
  observationId,
  setConfirmAdd,
  onResolve,
}: {
  selected: ResolutionItem | undefined;
  confirmAdd: boolean;
  offset: number | undefined;
  offsetError: string | undefined;
  observationId: string;
  setConfirmAdd: (value: boolean) => void;
  onResolve: (observationId: string, request: ResolveMediaObservationRequest) => Promise<void> | void;
}) {
  if (!selected || offset === undefined || offsetError) return;
  if (needsLibraryConfirmation(selected, confirmAdd)) {
    setConfirmAdd(true);
    return;
  }

  await onResolve(observationId, buildResolveRequest(selected, offset, confirmAdd));
}

function useResolutionPickerState(response: SubmitMediaObservationResponse) {
  const observation = response.observation;
  const items = useMemo(() => buildResolutionItems(response), [response]);
  const [selectedId, setSelectedId] = useState(items[0]?.id ?? '');
  const [offsetInput, setOffsetInput] = useState(String(response.suggestedEpisodeOffset ?? observation?.episodeOffset ?? 0));
  const [confirmAdd, setConfirmAdd] = useState(false);
  const selected = items.find((item) => item.id === selectedId);

  return {
    observation,
    items,
    selectedId,
    selected,
    offsetInput,
    setOffsetInput,
    setSelectedId,
    confirmAdd,
    setConfirmAdd,
  };
}

function PickerHeader({ title, onClose }: { title: string; onClose?: () => void }) {
  return (
    <div style={styles.header}>
      <div>
        <div style={styles.kicker}>Episode match</div>
        <h2 style={styles.title}>{title}</h2>
      </div>
      {onClose ? (
        <button type="button" onClick={onClose} style={styles.iconButton} aria-label="Close">x</button>
      ) : null}
    </div>
  );
}

function PickerMeta({ observedProgress, matchStatus }: { observedProgress?: number; matchStatus: string }) {
  return (
    <div style={styles.metaRow}>
      <span>Observed {observedProgress ?? 'unknown'}</span>
      <span>Status {matchStatus}</span>
    </div>
  );
}

function ResolutionChoices({
  items,
  unavailableReason,
  selectedId,
  onSelect,
}: {
  items: ResolutionItem[];
  unavailableReason?: string;
  selectedId: string;
  onSelect: (id: string) => void;
}) {
  if (items.length === 0) {
    return (
      <div style={styles.choiceList}>
        <div style={styles.empty}>{getEmptyChoicesMessage(unavailableReason)}</div>
      </div>
    );
  }

  return (
    <div style={styles.choiceList}>
      {items.map((item) => (
        <ResolutionChoice
          key={item.id}
          item={item}
          selected={item.id === selectedId}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}

function getEmptyChoicesMessage(unavailableReason: string | undefined): string {
  if (unavailableReason === 'anilist_not_connected') {
    return 'Connect AniList in Cantaro to search and resolve this episode.';
  }

  if (unavailableReason === 'provider_search_failed') {
    return 'Provider search is temporarily unavailable. The observation was saved for later resolution.';
  }

  return 'No choices returned yet. Open the media page again or retry matching from Cantaro.';
}

function ResolutionChoice({
  item,
  selected,
  onSelect,
}: {
  item: ResolutionItem;
  selected: boolean;
  onSelect: (id: string) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onSelect(item.id)}
      style={{
        ...styles.choice,
        ...(selected ? styles.choiceSelected : {}),
      }}
    >
      <span style={styles.choiceTitle}>{item.title}</span>
      <span style={styles.choiceSubtitle}>{item.subtitle}</span>
      {!item.inLibrary ? <span style={styles.choiceBadge}>Add required</span> : null}
    </button>
  );
}

function OffsetControl({
  offsetInput,
  resolvedProgress,
  onOffsetChange,
}: {
  offsetInput: string;
  resolvedProgress?: number;
  onOffsetChange: (offset: string) => void;
}) {
  return (
    <>
      <label style={styles.offsetLabel}>
        Episode offset
        <input
          type="text"
          inputMode="numeric"
          value={offsetInput}
          onChange={(event) => onOffsetChange(event.target.value)}
          style={styles.offsetInput}
        />
      </label>
      <div style={styles.progressPreview}>
        Resolved progress: {resolvedProgress && resolvedProgress > 0 ? resolvedProgress : 'not available'}
      </div>
    </>
  );
}

function PickerNotice({
  confirmAdd,
  error,
  offsetError,
}: {
  confirmAdd: boolean;
  error?: string | null;
  offsetError?: string;
}) {
  return (
    <>
      {confirmAdd ? (
        <div style={styles.confirmBox}>
          This title is not in your library. Confirm to add it as current and enable episode tracking for it.
        </div>
      ) : null}
      {offsetError ? <div style={styles.error}>{offsetError}</div> : null}
      {error ? <div style={styles.error}>{error}</div> : null}
    </>
  );
}

function PickerActions({
  resolving,
  confirmAdd,
  disabled,
  onSubmit,
}: {
  resolving: boolean;
  confirmAdd: boolean;
  disabled: boolean;
  onSubmit: () => void;
}) {
  return (
    <div style={styles.actions}>
      <button type="button" onClick={onSubmit} disabled={disabled || resolving} style={styles.primaryButton}>
        {getSubmitLabel(resolving, confirmAdd)}
      </button>
    </div>
  );
}

function getSubmitLabel(resolving: boolean, confirmAdd: boolean): string {
  if (resolving) return 'Saving...';
  return confirmAdd ? 'Add and resolve' : 'Use this match';
}

interface ParsedOffset {
  value?: number;
  error?: string;
}

function parseOffsetInput(input: string): ParsedOffset {
  const trimmed = input.trim();
  if (!/^-?\d+$/.test(trimmed)) {
    return { error: 'Episode offset must be a whole number.' };
  }

  const value = Number(trimmed);
  if (!Number.isSafeInteger(value)) {
    return { error: 'Episode offset is too large.' };
  }

  return { value };
}

function getOffsetValidationError(
  observedProgress: number | undefined,
  resolvedProgress: number | undefined,
  parsedOffset: ParsedOffset,
): string | undefined {
  if (parsedOffset.error) return parsedOffset.error;
  if (typeof observedProgress === 'number' && typeof resolvedProgress === 'number' && resolvedProgress <= 0) {
    return 'Resolved progress must be at least 1.';
  }

  return undefined;
}

function buildResolutionItems(response: SubmitMediaObservationResponse): ResolutionItem[] {
  const observation = response.observation;
  const candidates = observation?.candidates ?? [];
  const providerChoices = response.providerChoices.length > 0 ? response.providerChoices : observation?.providerChoices ?? [];

  return [
    ...candidates.map((candidate) => ({
      kind: 'candidate' as const,
      id: `candidate:${candidate.candidateId}`,
      title: candidate.title,
      subtitle: `${candidate.candidateSource} - ${Math.round(candidate.score * 100)}%`,
      candidate,
      inLibrary: true as const,
    })),
    ...providerChoices.map((choice) => ({
      kind: 'provider' as const,
      id: `provider:${choice.providerId}:${choice.providerMediaId}`,
      title: choice.title,
      subtitle: [
        choice.providerId,
        choice.startYear,
        choice.episodeCount ? `${choice.episodeCount} ep` : null,
        choice.isInLibrary ? 'in library' : null,
      ].filter(Boolean).join(' - '),
      choice,
      inLibrary: choice.isInLibrary,
    })),
  ];
}

function buildResolveRequest(
  selected: ResolutionItem,
  episodeOffset: number,
  addToLibraryConfirmed: boolean,
): ResolveMediaObservationRequest {
  if (selected.kind === 'candidate') {
    return {
      candidateId: selected.candidate.candidateId,
      episodeOffset,
    };
  }

  return {
    providerId: selected.choice.providerId,
    providerMediaId: selected.choice.providerMediaId,
    episodeOffset,
    addToLibraryConfirmed,
  };
}

const styles = {
  overlayPanel: {
    width: 'min(520px, calc(100vw - 32px))',
    maxHeight: 'min(720px, calc(100vh - 48px))',
    overflow: 'auto',
    border: '1px solid rgba(15, 23, 42, 0.16)',
    borderRadius: 18,
    background: 'rgba(248, 250, 252, 0.98)',
    color: '#0f172a',
    boxShadow: '0 24px 80px rgba(15, 23, 42, 0.36)',
    padding: 18,
    fontFamily: '"Segoe UI Variable", "Segoe UI", sans-serif',
  },
  popupPanel: {
    border: '1px solid rgba(15, 23, 42, 0.12)',
    borderRadius: 18,
    background: 'rgba(255, 255, 255, 0.92)',
    color: '#0f172a',
    boxShadow: '0 18px 48px rgba(15, 23, 42, 0.18)',
    padding: 16,
    fontFamily: '"Segoe UI Variable", "Segoe UI", sans-serif',
  },
  header: { display: 'flex', alignItems: 'flex-start', gap: 12, justifyContent: 'space-between' },
  kicker: { fontSize: 11, textTransform: 'uppercase' as const, letterSpacing: 0, color: '#64748b', fontWeight: 700 },
  title: { margin: '2px 0 0', fontSize: 18, lineHeight: 1.2, fontWeight: 800 },
  iconButton: {
    width: 32,
    height: 32,
    borderRadius: 16,
    border: '1px solid #cbd5e1',
    background: '#fff',
    color: '#0f172a',
    cursor: 'pointer',
  },
  metaRow: { display: 'flex', gap: 8, flexWrap: 'wrap' as const, marginTop: 12, fontSize: 12, color: '#475569' },
  choiceList: { display: 'grid', gap: 8, marginTop: 14 },
  choice: {
    display: 'grid',
    gap: 2,
    textAlign: 'left' as const,
    border: '1px solid #cbd5e1',
    borderRadius: 12,
    background: '#ffffff',
    padding: '10px 12px',
    color: '#0f172a',
    cursor: 'pointer',
  },
  choiceSelected: { borderColor: '#0891b2', boxShadow: '0 0 0 2px rgba(8, 145, 178, 0.18)' },
  choiceTitle: { fontSize: 14, fontWeight: 800 },
  choiceSubtitle: { fontSize: 12, color: '#64748b' },
  choiceBadge: { marginTop: 4, width: 'fit-content', borderRadius: 999, background: '#fef3c7', color: '#92400e', padding: '2px 8px', fontSize: 11, fontWeight: 700 },
  empty: { color: '#64748b', fontSize: 13, padding: 12, border: '1px dashed #cbd5e1', borderRadius: 12 },
  offsetLabel: { display: 'grid', gap: 6, marginTop: 14, fontSize: 12, fontWeight: 800, color: '#334155' },
  offsetInput: { width: 96, border: '1px solid #cbd5e1', borderRadius: 10, padding: '8px 10px', fontSize: 14 },
  progressPreview: { marginTop: 8, fontSize: 13, color: '#334155' },
  confirmBox: { marginTop: 12, borderRadius: 12, background: '#fff7ed', color: '#9a3412', padding: 10, fontSize: 13 },
  error: { marginTop: 12, borderRadius: 12, background: '#fff1f2', color: '#be123c', padding: 10, fontSize: 13 },
  actions: { display: 'flex', justifyContent: 'flex-end', marginTop: 14 },
  primaryButton: {
    border: 0,
    borderRadius: 12,
    background: '#0f172a',
    color: '#ffffff',
    padding: '10px 14px',
    fontWeight: 800,
    cursor: 'pointer',
  },
} satisfies Record<string, CSSProperties>;
