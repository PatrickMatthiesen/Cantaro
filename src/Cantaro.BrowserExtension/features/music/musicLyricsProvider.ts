import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { displayLyricsText, type LyricsCandidate, type LyricsResult } from '../../app/music/musicLyrics';

const attribution = 'Lyrics provided by LRCLIB (https://lrclib.net)';
const endpoint = 'https://lrclib.net/api/search';

interface LrclibRecord {
  id: number;
  trackName: string;
  artistName: string;
  albumName?: string | null;
  duration: number;
  instrumental?: boolean;
  plainLyrics?: string | null;
  syncedLyrics?: string | null;
}

interface RankedCandidate {
  record: LrclibRecord;
  score: number;
  result: LyricsCandidate;
}

function normalize(value: string | null | undefined): string {
  return (value ?? '').toLocaleLowerCase().normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

function similarity(left: string, right: string): number {
  const leftWords = new Set(normalize(left).split(' ').filter(Boolean));
  const rightWords = new Set(normalize(right).split(' ').filter(Boolean));
  const union = new Set([...leftWords, ...rightWords]);
  if (union.size === 0) return 0;
  return [...leftWords].filter(word => rightWords.has(word)).length / union.size;
}

function score(song: MusicLibrarySong, record: LrclibRecord): number {
  const title = similarity(song.title, record.trackName);
  const artist = song.artist?.trim() ? similarity(song.artist, record.artistName) : 1;
  const album = song.albums[0]
    ? similarity(song.albums[0], record.albumName ?? '')
    : 1;
  return Math.round((title * 0.6 + artist * 0.3 + album * 0.05 + durationBonus(song.durationSeconds, record.duration)) * 1000) / 1000;
}

function durationBonus(songDuration: number | undefined, candidateDuration: number): number {
  if (songDuration == null) return 0.05;
  const difference = Math.abs(songDuration - candidateDuration);
  if (difference <= 2) return 0.05;
  if (difference <= 5) return 0.035;
  if (difference <= 10) return 0.015;
  return 0;
}

function isOptionalString(value: unknown): boolean {
  return value === undefined || value === null || typeof value === 'string';
}

function hasCoreRecordFields(record: Partial<LrclibRecord>): boolean {
  return Number.isSafeInteger(record.id)
    && typeof record.trackName === 'string'
    && typeof record.artistName === 'string'
    && typeof record.duration === 'number'
    && Number.isFinite(record.duration)
    && record.duration > 0;
}

function hasOptionalRecordFields(record: Partial<LrclibRecord>): boolean {
  return (record.instrumental === undefined || typeof record.instrumental === 'boolean')
    && isOptionalString(record.albumName)
    && isOptionalString(record.plainLyrics)
    && isOptionalString(record.syncedLyrics);
}

function hasLyrics(record: LrclibRecord): boolean {
  return Boolean(record.instrumental || record.plainLyrics?.trim() || record.syncedLyrics?.trim());
}

function isRecord(value: unknown): value is LrclibRecord {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Partial<LrclibRecord>;
  return hasCoreRecordFields(record) && hasOptionalRecordFields(record);
}

function acceptable(song: MusicLibrarySong, candidate: RankedCandidate): boolean {
  return candidate.score >= 0.9
    && similarity(song.title, candidate.record.trackName) >= 0.9
    && (!song.artist?.trim() || similarity(song.artist, candidate.record.artistName) >= 0.8)
    && (song.durationSeconds == null || Math.abs(song.durationSeconds - candidate.record.duration) <= 10);
}

function lyricTextKey(candidate: LyricsCandidate): string {
  if (candidate.state === 'instrumental') return 'instrumental';
  const displayed = displayLyricsText(candidate);
  return (displayed?.text ?? '').toLocaleLowerCase().normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

function toCandidate(record: LrclibRecord, confidence: number): LyricsCandidate {
  const duration = `${Math.floor(record.duration / 60)}:${String(Math.floor(record.duration % 60)).padStart(2, '0')}`;
  const version = record.instrumental ? 'Instrumental' : record.syncedLyrics?.trim() ? 'Synced' : 'Plain';
  const label = [record.artistName, record.albumName?.trim() || 'Unknown album', duration, version, `#${record.id}`].join(' · ');
  return {
    label,
    state: record.instrumental ? 'instrumental' : 'available',
    matchStatus: 'fallback',
    provider: 'lrclib',
    providerRecordId: String(record.id),
    plainLyrics: record.instrumental ? undefined : record.plainLyrics ?? undefined,
    syncedLyrics: record.instrumental ? undefined : record.syncedLyrics ?? undefined,
    confidence,
    attribution,
  };
}

export function rankLrclibCandidates(song: MusicLibrarySong, records: readonly unknown[]): LyricsCandidate[] {
  if (!song.title.trim()) return [];
  const ranked = records.filter(isRecord).filter(hasLyrics).map(record => ({
    record,
    score: score(song, record),
    result: toCandidate(record, score(song, record)),
  })).filter(candidate => acceptable(song, candidate))
    .sort((left, right) => right.score - left.score
      || (song.durationSeconds == null ? 0 : Math.abs(song.durationSeconds - left.record.duration) - Math.abs(song.durationSeconds - right.record.duration))
      || Number(Boolean(right.record.syncedLyrics?.trim())) - Number(Boolean(left.record.syncedLyrics?.trim()))
      || left.record.id - right.record.id);
  const seenLyrics = new Set<string>();
  return ranked.flatMap(candidate => {
    const result = candidate.result;
    const key = normalize(candidate.record.artistName) + '|' + lyricTextKey(result);
    if (seenLyrics.has(key)) return [];
    seenLyrics.add(key);
    return [result];
  });
}

export async function getLocalLyrics(song: MusicLibrarySong, signal?: AbortSignal): Promise<LyricsResult> {
  if (!song.title.trim()) {
    return {
      state: 'unavailable', matchStatus: 'unavailable', provider: 'lrclib', attribution,
      explanation: 'A song title is required to search for lyrics.',
    };
  }

  const params = new URLSearchParams({ track_name: song.title });
  if (song.artist?.trim()) params.set('artist_name', song.artist.trim());
  const requestSignal = signal
    ? AbortSignal.any([signal, AbortSignal.timeout(10_000)])
    : AbortSignal.timeout(10_000);
  const response = await fetch(`${endpoint}?${params}`, { signal: requestSignal, credentials: 'omit' });
  if (!response.ok) throw new Error('Lyrics could not be loaded from LRCLIB right now.');
  const body: unknown = await response.json();
  const candidates = rankLrclibCandidates(song, Array.isArray(body) ? body : []);
  const selected = candidates[0];
  if (!selected) {
    return {
      state: 'unavailable', matchStatus: 'unavailable', provider: 'lrclib', attribution,
      explanation: 'LRCLIB has no confident match for this song.',
    };
  }
  return {
    ...selected,
    explanation: 'Selected the best matching LRCLIB result. You can choose another version.',
    candidates,
  };
}
