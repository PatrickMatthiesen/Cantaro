import type { ReactNode } from 'react';
import { GlassCard } from '@cantaro/client-shared/ui';
import type {
  SongGroupingSuggestionResponse,
  SongGroupingTrackResponse,
} from '@cantaro/client-shared/music';
import type { SongGroupingReviewState } from './useSongGroupingReview';

function groupingReason(suggestion: SongGroupingSuggestionResponse): string {
  try {
    const evidence = JSON.parse(suggestion.evidenceJson) as { reason?: string };
    return evidence.reason ?? 'The recordings have matching composition evidence.';
  } catch {
    return 'The recordings have matching composition evidence.';
  }
}

function SongGroupingTrack({ label, track }: { label: string; track: SongGroupingTrackResponse }) {
  return <div className="min-w-0 flex-1 px-4 py-3">
    <p className="text-xs font-black text-accent-strong">{label}</p>
    <h3 className="mt-1 truncate text-base font-black text-content">{track.title ?? 'Untitled recording'}</h3>
    <p className="mt-1 truncate text-sm font-semibold text-content-muted">{track.artist ?? 'Unknown artist'}</p>
    <p className="mt-2 font-mono text-xs text-content-muted">{track.isrc ? `ISRC ${track.isrc}` : track.musicBrainzRecordingId ? `MBID ${track.musicBrainzRecordingId}` : 'No stable recording ID'}</p>
  </div>;
}

function SongGroupingSuggestionRow({ review, suggestion }: { review: SongGroupingReviewState; suggestion: SongGroupingSuggestionResponse }) {
  const busy = review.activeSuggestionId === suggestion.suggestionId;
  return <article className="px-5 py-4">
    <div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm font-semibold text-content">{groupingReason(suggestion)}</p><span className="rounded-full bg-accent-soft px-3 py-1 text-xs font-black text-accent-strong">{Math.round(suggestion.confidence * 100)}% confidence</span></div>
    <div className="mt-3 flex flex-col rounded-xl bg-accent-soft sm:flex-row sm:divide-x sm:divide-border-subtle"><SongGroupingTrack label="Move this recording" track={suggestion.candidate} /><SongGroupingTrack label="Into this song" track={suggestion.anchor} /></div>
    <div className="mt-3 flex flex-wrap justify-end gap-2"><button type="button" disabled={busy} onClick={() => void review.reviewSuggestion(suggestion.suggestionId, false)} className="rounded-xl px-4 py-2 text-sm font-black text-content transition hover:bg-surface-subtle focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none disabled:opacity-50">Keep separate</button><button type="button" disabled={busy} onClick={() => void review.reviewSuggestion(suggestion.suggestionId, true)} className="rounded-xl bg-action px-4 py-2 text-sm font-black text-action-content transition hover:bg-action-hover focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:opacity-50">{busy ? 'Applying…' : 'Group versions'}</button></div>
  </article>;
}

function SongGroupingReviewBody({ review }: { review: SongGroupingReviewState }) {
  const items = review.pageData?.items ?? [];
  if (review.isLoading) return <p className="px-5 py-6 text-sm font-semibold text-content-muted">Loading song grouping review…</p>;
  if (items.length === 0) return <div className="px-5 py-6"><p className="font-black text-content">No grouping decisions waiting</p><p className="mt-1 text-sm font-semibold text-content-muted">Run the candidate finder after importing new playlists or resolving Track matches.</p></div>;
  return <div className="divide-y divide-border-subtle">{items.map((suggestion) => <SongGroupingSuggestionRow key={suggestion.suggestionId} review={review} suggestion={suggestion} />)}</div>;
}

export function SongGroupingReviewPanel({ review, pagination }: { review: SongGroupingReviewState; pagination: ReactNode }) {
  return <section aria-labelledby="song-grouping-heading">
    <GlassCard className="overflow-hidden p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border-subtle px-5 py-4"><div><h2 id="song-grouping-heading" className="text-xl font-black text-content">Group recordings into songs</h2><p className="mt-1 max-w-2xl text-sm font-semibold text-content-muted">Review distinct recordings that may be versions of the same composition. Identical recording IDs stay in the separate Track-reconciliation workflow.</p></div><button type="button" onClick={() => void review.runGeneration()} disabled={review.isGenerating} className="rounded-xl bg-action px-4 py-2.5 text-sm font-black text-action-content transition hover:bg-action-hover focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50">{review.isGenerating ? 'Finding candidates…' : 'Find candidates'}</button></div>
      {review.error ? <p className="bg-rose-50 px-5 py-3 text-sm font-semibold text-rose-800" role="alert">{review.error}</p> : null}
      <SongGroupingReviewBody review={review} />
    </GlassCard>
    {pagination}
  </section>;
}
