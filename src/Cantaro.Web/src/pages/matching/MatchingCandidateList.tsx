import { useState } from 'react';
import { matchingApi, type MatchingCandidateComparisonResponse, type MatchingQueueCandidateResponse } from '@cantaro/client-shared/music';
import { formatClusterReason, formatDuration, formatPercent } from './matchingReviewPresentation';
import { MarkerList } from './MarkerList';

type MatchingActionHandler = (observationId: string, action: () => Promise<void>) => Promise<void>;
interface TrackVersionOption { label: string; value: number }
interface SuggestedTrackVersion { label: string; value: number }
type ScoreTone = 'match' | 'close' | 'miss' | 'neutral';

const trackVersionOptions: readonly TrackVersionOption[] = [
  ['Acoustic', 1], ['Live', 2], ['Instrumental', 4], ['Orchestral', 8], ['Remix', 16], ['Radio edit', 32],
  ['Extended', 64], ['Demo', 128], ['A cappella', 256], ['Karaoke', 512], ['Cover', 1024], ['Remastered', 2048],
  ['Re-recorded', 4096], ['Clean', 8192], ['Explicit', 16384], ['Slowed', 32768], ['Sped up', 65536], ['Alternate take', 131072],
].map(([label, value]) => ({ label: String(label), value: Number(value) }));

function suggestedTrackVersion(flags: number): SuggestedTrackVersion | undefined {
  if (flags === 0) return undefined;
  const labels = trackVersionOptions.filter((option) => (flags & option.value) === option.value).map((option) => option.label);
  return { label: labels.length > 0 ? labels.join(' + ') : 'Inferred', value: flags };
}

function scoreTone(value?: number): ScoreTone {
  if (value === undefined || value === null) return 'neutral';
  if (value >= 0.9) return 'match';
  return value >= 0.65 ? 'close' : 'miss';
}

function scoreClasses(tone: ScoreTone): string {
  const classes: Record<ScoreTone, string> = {
    match: 'border-success-border bg-success-surface text-success-content',
    close: 'border-warning-border bg-warning-surface text-warning-content',
    miss: 'border-danger-border bg-danger-surface text-danger-content',
    neutral: 'border-border-subtle bg-surface-subtle text-content-muted',
  };
  return classes[tone];
}

export function CandidateList({ candidates, disabled, observationId, onAction, suggestedVersionFlags }: { candidates: MatchingQueueCandidateResponse[]; disabled: boolean; observationId: string; onAction: MatchingActionHandler; suggestedVersionFlags: number }) {
  if (candidates.length === 0) return <p className="mt-3 text-sm text-content-muted">No candidates were stored for this observation yet.</p>;
  const detectedVersion = suggestedTrackVersion(suggestedVersionFlags);
  return <div className="mt-4 grid border-y border-border-subtle xl:grid-cols-2 xl:divide-x xl:divide-border-subtle">{candidates.map((candidate) => <CandidateCard key={candidate.candidateId} candidate={candidate} detectedVersion={detectedVersion} disabled={disabled} onUseExact={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, candidate.candidateId))} onUseAsVersion={(flags) => void onAction(observationId, () => matchingApi.selectCandidateAsVersion(observationId, candidate.candidateId, flags))} />)}</div>;
}

function candidateSource(candidate: MatchingQueueCandidateResponse) {
  const sourceLabel = candidate.candidateSource === 'musicbrainz' ? 'MusicBrainz' : candidate.candidateSource?.replace(/_/g, ' ') ?? 'Unknown';
  return candidate.mbidRecording && candidate.candidateSource === 'musicbrainz'
    ? <a href={`https://musicbrainz.org/recording/${candidate.mbidRecording}`} target="_blank" rel="noreferrer" className="underline decoration-personal-accent underline-offset-4 hover:text-personal-accent-strong">{sourceLabel}</a>
    : sourceLabel;
}

function CandidateMeta({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const duration = formatDuration(candidate.durationSeconds);
  return <p className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs font-semibold text-content-muted"><span>{candidate.artist ?? 'Unknown artist'}</span>{duration ? <span>{duration}</span> : null}<span>{candidateSource(candidate)}</span></p>;
}

function formattedScore(comparison: MatchingCandidateComparisonResponse): string {
  return comparison.scoreLabel ?? (comparison.score === undefined || comparison.score === null ? '-' : formatPercent(comparison.score) ?? '-');
}

const diffGrid = 'grid gap-1 border-t border-border-subtle py-2 text-xs sm:grid-cols-[minmax(4.8rem,.72fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(4.4rem,.62fr)] sm:gap-0 sm:py-0 sm:[&>span]:border-r sm:[&>span]:border-border-subtle sm:[&>span]:p-2 sm:[&>span:last-child]:border-r-0';

function DiffRow({ comparison }: { comparison: MatchingCandidateComparisonResponse }) {
  return <div className={diffGrid} role="row"><span className="text-[0.65rem] font-black tracking-wider text-content-muted uppercase" role="cell">{comparison.label}</span><span role="cell">{comparison.observationValue ?? 'None'}</span><span role="cell">{comparison.candidateValue ?? 'None'}</span><span role="cell"><span className={`inline-flex min-h-6 min-w-13 items-center justify-center border px-2 text-[0.68rem] font-black ${scoreClasses(comparison.tone)}`}>{formattedScore(comparison)}</span></span></div>;
}

function DiffTable({ comparisons }: { comparisons: MatchingCandidateComparisonResponse[] }) {
  if (comparisons.length === 0) return null;
  const matches = comparisons.filter((comparison) => comparison.scoreLabel === 'Match');
  return <div className="mt-3 border-y border-border-subtle" role="table" aria-label="Candidate differences">
    <div className="hidden bg-surface-subtle text-[0.62rem] font-black tracking-wider text-content-muted uppercase sm:grid sm:grid-cols-[minmax(4.8rem,.72fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(4.4rem,.62fr)] sm:[&>span]:border-r sm:[&>span]:border-border-subtle sm:[&>span]:p-2 sm:[&>span:last-child]:border-r-0" role="row"><span role="columnheader">Field</span><span role="columnheader">Observation</span><span role="columnheader">Candidate</span><span role="columnheader">Match</span></div>
    {comparisons.filter((comparison) => comparison.scoreLabel !== 'Match').map((comparison) => <DiffRow key={comparison.label} comparison={comparison} />)}
    {matches.length > 0 ? <details className="border-t border-border-subtle"><summary className="cursor-pointer list-none px-2 py-2 text-xs font-bold text-content-muted hover:text-content [&::-webkit-details-marker]:hidden">{matches.length} matching {matches.length === 1 ? 'field' : 'fields'} hidden</summary><div>{matches.map((comparison) => <DiffRow key={comparison.label} comparison={comparison} />)}</div></details> : null}
  </div>;
}

interface ActionProps { candidateId: string; detectedVersion?: SuggestedTrackVersion; disabled: boolean; onUseAsVersion: (flags: number) => void; onUseExact: () => void }

function PrimaryAction({ detectedVersion, disabled, onUseAsVersion, onUseExact }: Omit<ActionProps, 'candidateId'>) {
  return <button type="button" className="min-h-10 w-full bg-personal-accent px-3 text-sm font-black text-personal-accent-contrast hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50" onClick={detectedVersion ? () => onUseAsVersion(detectedVersion.value) : onUseExact} disabled={disabled}>{detectedVersion ? `Add as ${detectedVersion.label.toLowerCase()} version` : 'Use match'}</button>;
}

function AdditionalOptions({ candidateId, detectedVersion, disabled, onUseAsVersion, onUseExact }: ActionProps) {
  const initialFlag = trackVersionOptions.some((option) => option.value === detectedVersion?.value) ? detectedVersion?.value ?? 0 : 0;
  const [selectedFlag, setSelectedFlag] = useState(initialFlag);
  const selected = trackVersionOptions.find((option) => option.value === selectedFlag);
  const selectId = `candidate-version-${candidateId}`;
  return <details className="border border-border-subtle p-2"><summary className="cursor-pointer list-none px-2 py-1.5 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus [&::-webkit-details-marker]:hidden">Additional options</summary><div className="mt-2 space-y-2 px-2 pb-1"><label htmlFor={selectId} className="block text-xs font-semibold text-content-muted">Version type</label><select id={selectId} value={selectedFlag} onChange={(event) => setSelectedFlag(Number(event.target.value))} disabled={disabled} className="min-h-10 w-full border border-border-subtle bg-surface px-3 text-sm font-semibold text-content focus:border-focus focus:outline-none disabled:opacity-50"><option value={0}>Choose a version type</option>{trackVersionOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select><button type="button" className="min-h-10 w-full border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover disabled:opacity-50" onClick={() => onUseAsVersion(selectedFlag)} disabled={disabled || !selected}>{selected ? `Add as ${selected.label.toLowerCase()} version` : 'Choose a version type'}</button><button type="button" className="min-h-10 w-full px-3 text-sm font-semibold text-content hover:bg-surface-hover disabled:opacity-50" onClick={onUseExact} disabled={disabled}>Use as exact recording</button></div></details>;
}

function CandidateCard({ candidate, detectedVersion, disabled, onUseAsVersion, onUseExact }: { candidate: MatchingQueueCandidateResponse; detectedVersion?: SuggestedTrackVersion; disabled: boolean; onUseAsVersion: (flags: number) => void; onUseExact: () => void }) {
  const clusterReason = formatClusterReason(candidate.clusterReason);
  const showCluster = Boolean(clusterReason && (candidate.clusterSize > 1 || candidate.clusterReason !== 'representative'));
  return <article className="border-t border-border-subtle p-4 first:border-0 xl:nth-[2]:border-t-0"><div className="flex min-h-full flex-col gap-3"><div className="flex flex-col items-start justify-between gap-3 sm:flex-row"><div className="min-w-0"><div className="flex min-w-0 flex-wrap items-center gap-2"><h3 className="m-0 text-base font-extrabold text-content">{candidate.title}</h3><span className={`inline-flex min-h-6 items-center border px-2 text-[0.68rem] font-black ${scoreClasses(scoreTone(candidate.score))}`}>{Math.round(candidate.score * 100)}%</span></div><CandidateMeta candidate={candidate} /></div><div className="flex w-full shrink-0 flex-col gap-2 sm:w-56"><PrimaryAction detectedVersion={detectedVersion} disabled={disabled} onUseAsVersion={onUseAsVersion} onUseExact={onUseExact} /><AdditionalOptions candidateId={candidate.candidateId} detectedVersion={detectedVersion} disabled={disabled} onUseAsVersion={onUseAsVersion} onUseExact={onUseExact} /></div></div><MarkerList markers={candidate.versionMarkers} tone="version" /><MarkerList markers={candidate.playbackModifiers} tone="playback" /><DiffTable comparisons={candidate.comparisons ?? []} />{showCluster ? <dl className="mt-1 border-y border-border-subtle"><div className="grid gap-1 py-2 sm:grid-cols-[7rem_1fr]"><dt className="text-[0.65rem] font-black tracking-wider text-content-muted uppercase">Cluster</dt><dd className="m-0 text-xs font-semibold text-content">{clusterReason} ({candidate.clusterSize})</dd></div></dl> : null}</div></article>;
}
