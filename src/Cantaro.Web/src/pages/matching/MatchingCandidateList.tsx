import { useState, type ReactNode } from 'react';
import { matchingApi } from '@cantaro/client-shared/music';
import type { MatchingCandidateComparisonResponse, MatchingQueueCandidateResponse } from '@cantaro/client-shared/music';
import { formatClusterReason, formatDuration, formatPercent } from './matchingReviewPresentation';
import { MarkerList } from './MarkerList';

type MatchingActionHandler = (observationId: string, action: () => Promise<void>) => Promise<void>;
interface TrackVersionOption { label: string; value: number }
interface SuggestedTrackVersion { label: string; value: number }

const trackVersionOptions: readonly TrackVersionOption[] = [
  ['Acoustic', 1], ['Live', 2], ['Instrumental', 4], ['Orchestral', 8], ['Remix', 16], ['Radio edit', 32],
  ['Extended', 64], ['Demo', 128], ['A cappella', 256], ['Karaoke', 512], ['Cover', 1024], ['Remastered', 2048],
  ['Re-recorded', 4096], ['Clean', 8192], ['Explicit', 16384], ['Slowed', 32768], ['Sped up', 65536], ['Alternate take', 131072],
].map(([label, value]) => ({ label: String(label), value: Number(value) }));

function getSuggestedTrackVersion(versionFlags: number): SuggestedTrackVersion | undefined {
  if (versionFlags === 0) return undefined;
  const labels = trackVersionOptions.filter((option) => (versionFlags & option.value) === option.value).map((option) => option.label);
  return { label: labels.length > 0 ? labels.join(' + ') : 'Inferred', value: versionFlags };
}

export function CandidateList({ candidates, disabled, observationId, onAction, suggestedVersionFlags }: { candidates: MatchingQueueCandidateResponse[]; disabled: boolean; observationId: string; onAction: MatchingActionHandler; suggestedVersionFlags: number }) {
  if (candidates.length === 0) return <p className="mt-3 text-sm text-content-muted">No candidates were stored for this observation yet.</p>;
  const rows = Array.from({ length: Math.ceil(candidates.length / 2) }, (_, index) => candidates.slice(index * 2, index * 2 + 2));
  const detectedVersion = getSuggestedTrackVersion(suggestedVersionFlags);
  return <div className="matching-candidate-grid mt-4">{rows.map((row) => <CandidateRow key={row.map((candidate) => candidate.candidateId).join('-')} row={row} detectedVersion={detectedVersion} disabled={disabled} observationId={observationId} onAction={onAction} />)}</div>;
}

function CandidateRow({ row, detectedVersion, disabled, observationId, onAction }: { row: MatchingQueueCandidateResponse[]; detectedVersion?: SuggestedTrackVersion; disabled: boolean; observationId: string; onAction: MatchingActionHandler }) {
  return <div className="matching-candidate-row">
    <CandidateChoice candidate={row[0]} detectedVersion={detectedVersion} disabled={disabled} observationId={observationId} onAction={onAction} />
    {row[1] ? <><div className="matching-candidate-column-divider" aria-hidden="true" /><CandidateChoice candidate={row[1]} detectedVersion={detectedVersion} disabled={disabled} observationId={observationId} onAction={onAction} /></> : null}
  </div>;
}

function CandidateChoice({ candidate, detectedVersion, disabled, observationId, onAction }: { candidate: MatchingQueueCandidateResponse; detectedVersion?: SuggestedTrackVersion; disabled: boolean; observationId: string; onAction: MatchingActionHandler }) {
  return <CandidateCard candidate={candidate} detectedVersion={detectedVersion} disabled={disabled}
    onUseExact={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, candidate.candidateId))}
    onUseAsVersion={(versionFlags) => void onAction(observationId, () => matchingApi.selectCandidateAsVersion(observationId, candidate.candidateId, versionFlags))} />;
}

type ScoreTone = 'match' | 'close' | 'miss' | 'neutral';
function scoreTone(value?: number): ScoreTone {
  if (value === undefined || value === null) return 'neutral';
  if (value >= 0.9) return 'match';
  return value >= 0.65 ? 'close' : 'miss';
}
function scoreToneClasses(tone: ScoreTone): string {
  const classes: Record<ScoreTone, string> = { match: 'score-tone--match', close: 'score-tone--close', miss: 'score-tone--miss', neutral: 'score-tone--neutral' };
  return classes[tone];
}
function CandidateConfidence({ score }: { score: number }) {
  return <span className={`matching-candidate-confidence ${scoreToneClasses(scoreTone(score))}`}>{Math.round(score * 100)}%</span>;
}
function formatCandidateSource(source?: string): string {
  if (!source) return 'Unknown';
  return source === 'musicbrainz' ? 'MusicBrainz' : source.replace(/_/g, ' ');
}
function CandidateMeta({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const duration = formatDuration(candidate.durationSeconds);
  const sourceLabel = formatCandidateSource(candidate.candidateSource);
  const source = candidate.mbidRecording && candidate.candidateSource === 'musicbrainz'
    ? <a href={`https://musicbrainz.org/recording/${candidate.mbidRecording}`} target="_blank" rel="noreferrer" className="matching-candidate-source-link">{sourceLabel}</a>
    : sourceLabel;
  return <p className="matching-candidate-meta"><span>{candidate.artist ?? 'Unknown artist'}</span>{duration ? <span>{duration}</span> : null}<span>{source}</span></p>;
}
function CandidateMarkers({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  return <><MarkerList markers={candidate.versionMarkers} tone="version" /><MarkerList markers={candidate.playbackModifiers} tone="playback" /></>;
}

interface CandidateEvidenceItem { label: string; value: ReactNode }
function getCandidateEvidenceItems(candidate: MatchingQueueCandidateResponse): CandidateEvidenceItem[] {
  const clusterReason = formatClusterReason(candidate.clusterReason);
  const show = Boolean(clusterReason && (candidate.clusterSize > 1 || candidate.clusterReason !== 'representative'));
  return show ? [{ label: 'Cluster', value: `${clusterReason} (${candidate.clusterSize})` }] : [];
}
function CandidateEvidenceTable({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const items = getCandidateEvidenceItems(candidate);
  if (items.length === 0) return null;
  return <dl className="matching-evidence-table">{items.map((item) => <div key={item.label} className="matching-evidence-row"><dt>{item.label}</dt><dd>{item.value}</dd></div>)}</dl>;
}

function formatComparisonScore(comparison: MatchingCandidateComparisonResponse): string {
  if (comparison.scoreLabel) return comparison.scoreLabel;
  return comparison.score === undefined || comparison.score === null ? '-' : formatPercent(comparison.score) ?? '-';
}
function CandidateDiffRow({ comparison }: { comparison: MatchingCandidateComparisonResponse }) {
  return <div className="matching-diff-row" role="row"><span className="matching-diff-label" role="cell">{comparison.label}</span><span role="cell">{comparison.observationValue ?? 'None'}</span><span role="cell">{comparison.candidateValue ?? 'None'}</span><span role="cell"><span className={`matching-diff-score ${scoreToneClasses(comparison.tone)}`}>{formatComparisonScore(comparison)}</span></span></div>;
}
function CandidateDiffTable({ comparisons }: { comparisons: MatchingCandidateComparisonResponse[] }) {
  if (comparisons.length === 0) return null;
  const hiddenMatches = comparisons.filter((comparison) => comparison.scoreLabel === 'Match');
  const visibleComparisons = comparisons.filter((comparison) => comparison.scoreLabel !== 'Match');
  return <div className="matching-diff-table" role="table" aria-label="Candidate differences">
    <div className="matching-diff-header" role="row"><span role="columnheader">Field</span><span role="columnheader">Observation</span><span role="columnheader">Candidate</span><span role="columnheader">Match</span></div>
    {visibleComparisons.map((comparison) => <CandidateDiffRow key={comparison.label} comparison={comparison} />)}
    {hiddenMatches.length > 0 ? <details className="matching-diff-matches"><summary>{hiddenMatches.length} matching {hiddenMatches.length === 1 ? 'field' : 'fields'} hidden</summary><div className="matching-diff-matches-body">{hiddenMatches.map((comparison) => <CandidateDiffRow key={comparison.label} comparison={comparison} />)}</div></details> : null}
  </div>;
}

interface CandidateSelectionActionProps { candidateId: string; detectedVersion?: SuggestedTrackVersion; disabled: boolean; onUseAsVersion: (versionFlags: number) => void; onUseExact: () => void }
function CandidatePrimaryAction({ detectedVersion, disabled, onUseAsVersion, onUseExact }: Omit<CandidateSelectionActionProps, 'candidateId'>) {
  if (!detectedVersion) return <button type="button" className="matching-candidate-action w-full focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none" onClick={onUseExact} disabled={disabled}>Use match</button>;
  return <button type="button" className="matching-candidate-action w-full focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none" onClick={() => onUseAsVersion(detectedVersion.value)} disabled={disabled}>Add as {detectedVersion.label.toLowerCase()} version</button>;
}
function CandidateAdditionalOptions({ candidateId, detectedVersion, disabled, onUseAsVersion, onUseExact }: CandidateSelectionActionProps) {
  const initialFlag = trackVersionOptions.some((option) => option.value === detectedVersion?.value) ? detectedVersion?.value ?? 0 : 0;
  const [selectedVersionFlag, setSelectedVersionFlag] = useState(initialFlag);
  const selectedVersion = trackVersionOptions.find((option) => option.value === selectedVersionFlag);
  const selectId = `candidate-version-${candidateId}`;
  return <details className="rounded-xl border border-border-subtle bg-surface-translucent p-2"><summary className="cursor-pointer list-none rounded-lg px-2 py-1.5 text-sm font-semibold text-content transition hover:bg-accent-soft focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none [&::-webkit-details-marker]:hidden">Additional options</summary><div className="mt-2 space-y-2 px-2 pb-1">
    <label htmlFor={selectId} className="block text-xs font-semibold text-content-muted">Version type</label>
    <select id={selectId} value={selectedVersionFlag} onChange={(event) => setSelectedVersionFlag(Number(event.target.value))} disabled={disabled} className="min-h-10 w-full rounded-lg border border-border-subtle bg-surface px-3 text-sm font-semibold text-content focus:border-accent focus:ring-2 focus:ring-focus focus:outline-none disabled:cursor-not-allowed disabled:opacity-50"><option value={0}>Choose a version type</option>{trackVersionOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select>
    <button type="button" className="w-full rounded-lg border border-border-subtle bg-accent-soft px-3 py-2 text-sm font-semibold text-accent-strong transition hover:bg-accent-soft focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50" onClick={() => onUseAsVersion(selectedVersionFlag)} disabled={disabled || !selectedVersion}>{selectedVersion ? `Add as ${selectedVersion.label.toLowerCase()} version` : 'Choose a version type'}</button>
    <button type="button" className="w-full rounded-lg px-3 py-2 text-sm font-semibold text-content transition hover:bg-surface-subtle focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50" onClick={onUseExact} disabled={disabled}>Use as exact recording</button>
  </div></details>;
}
function CandidateSelectionActions(props: CandidateSelectionActionProps) {
  return <div className="flex w-full shrink-0 flex-col gap-2 sm:w-56"><CandidatePrimaryAction {...props} /><CandidateAdditionalOptions {...props} /></div>;
}
function CandidateCard({ candidate, detectedVersion, disabled, onUseAsVersion, onUseExact }: { candidate: MatchingQueueCandidateResponse; detectedVersion?: SuggestedTrackVersion; disabled: boolean; onUseAsVersion: (versionFlags: number) => void; onUseExact: () => void }) {
  return <div className="matching-candidate-card"><div className="matching-candidate-shell"><div className="matching-candidate-header"><div className="min-w-0"><div className="matching-candidate-title-row"><h3>{candidate.title}</h3><CandidateConfidence score={candidate.score} /></div><CandidateMeta candidate={candidate} /></div><CandidateSelectionActions candidateId={candidate.candidateId} detectedVersion={detectedVersion} disabled={disabled} onUseAsVersion={onUseAsVersion} onUseExact={onUseExact} /></div><CandidateMarkers candidate={candidate} /><CandidateDiffTable comparisons={candidate.comparisons ?? []} /><CandidateEvidenceTable candidate={candidate} /></div></div>;
}
