import { createElement, type ReactNode } from 'react';

export function formatDuration(durationSeconds?: number): string | null {
  if (!durationSeconds || durationSeconds <= 0) return null;
  const minutes = Math.floor(durationSeconds / 60);
  const seconds = durationSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function formatStatus(status: string): string {
  const labels: Readonly<Record<string, string>> = { ambiguous: 'Ambiguous', no_match: 'No match', pending: 'Pending', matched: 'Matched' };
  return labels[status] ?? status;
}

export function formatPercent(value?: number): string | null {
  return value === undefined || value === null ? null : `${Math.round(value * 100)}%`;
}

export function formatClusterReason(clusterReason?: string): string | null {
  if (!clusterReason) return null;
  const labels: Readonly<Record<string, string>> = { 'shared-strong-identifier': 'Shared strong identifier', 'normalized-title-artist-duration': 'Merged by normalized title, artist, and duration', representative: 'Standalone cluster' };
  return labels[clusterReason] ?? clusterReason;
}

export function statusClasses(status: string): string {
  const classes: Readonly<Record<string, string>> = { ambiguous: 'bg-amber-100 text-amber-800', no_match: 'bg-rose-100 text-rose-800', pending: 'bg-indigo-100 text-indigo-800' };
  return classes[status] ?? 'bg-emerald-100 text-emerald-800';
}

function sourceLink(href: string, className: string, label: string): ReactNode {
  return createElement('a', { href, target: '_blank', rel: 'noopener noreferrer', className }, label);
}

export function formatSource(sourceType: string, externalId: string): string | ReactNode {
  if (sourceType === 'spotify') return sourceLink(`https://open.spotify.com/track/${externalId}`, 'text-indigo-600 hover:underline', `Spotify - ${externalId}`);
  if (sourceType === 'apple_music') return sourceLink(`https://music.apple.com/track/${externalId}`, 'text-red-500 hover:underline', `Apple Music - ${externalId}`);
  if (sourceType === 'youtube') return sourceLink(`https://youtu.be/${externalId}`, 'text-red-600 hover:underline', `YouTube - ${externalId}`);
  return `${sourceType} / ${externalId}`;
}
