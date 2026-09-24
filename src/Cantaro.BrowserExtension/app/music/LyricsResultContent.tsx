import { useState } from 'react';
import { displayLyricsText, type LyricsResult } from './musicLyrics';

export function LyricsResultContent({ result }: { result: LyricsResult }) {
  return <LyricsVersions key={result.candidates?.map(candidate => candidate.providerRecordId).join(',') ?? result.providerRecordId ?? result.state} result={result} />;
}

function LyricsVersions({ result }: { result: LyricsResult }) {
  const [index, setIndex] = useState(0);
  const candidates = result.candidates ?? [];
  const selected = candidates[index] ?? result;
  return <>
    {candidates.length > 1 ? <label className="mb-3 block text-xs text-content-muted">
      Lyrics version
      <select aria-label="Lyrics version" className="mt-1 block w-full border border-border-subtle bg-surface px-2 py-2 text-content"
        value={index} onChange={event => setIndex(Number(event.target.value))}>
        {candidates.map((candidate, candidateIndex) => <option key={candidate.providerRecordId ?? candidateIndex} value={candidateIndex}>
          {candidate.label}{candidateIndex === 0 ? ' (Best match)' : ''}
        </option>)}
      </select>
    </label> : null}
    <SelectedLyrics result={selected} />
  </>;
}

const lyricsNotices = {
  available: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
  instrumental: { title: 'Instrumental track', fallback: 'This track has no lyrics.' },
  ambiguous: { title: 'Lyrics need review', fallback: 'Cantaro found more than one possible lyrics match.' },
  provider_error: { title: 'Lyrics provider unavailable', fallback: 'Try again in a moment.' },
  disabled: { title: 'Lyrics are unavailable', fallback: 'Lyrics lookup is disabled for this Cantaro server.' },
  unavailable: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
} satisfies Record<LyricsResult['state'], { title: string; fallback: string }>;

function SelectedLyrics({ result }: { result: LyricsResult }) {
  const selected = result.state === 'available' ? displayLyricsText(result) : null;
  if (!selected) {
    const notice = lyricsNotices[result.state];
    return <LyricsNotice title={notice.title} detail={result.explanation || notice.fallback} attribution={result.attribution} />;
  }

  return (
    <>
      <div className="flex items-baseline justify-between gap-3">
        <p className="text-xs font-semibold text-content">{selected.synchronized ? 'Timed lyrics' : 'Lyrics'}</p>
        <span className="text-[11px] text-content-muted">{result.matchStatus === 'exact' ? 'Matched' : 'Best match'}</span>
      </div>
      <pre className="mt-2 whitespace-pre-wrap break-words font-sans text-sm leading-6 text-content">{selected.text}</pre>
      <p className="mt-3 text-[11px] text-content-muted">{result.attribution}</p>
    </>
  );
}

function LyricsNotice({ title, detail, attribution }: { title: string; detail: string; attribution?: string }) {
  return (
    <>
      <p className="text-sm font-bold text-content">{title}</p>
      <p className="mt-1 text-xs leading-5 text-content-muted">{detail}</p>
      {attribution ? <p className="mt-3 text-[11px] text-content-muted">{attribution}</p> : null}
    </>
  );
}
