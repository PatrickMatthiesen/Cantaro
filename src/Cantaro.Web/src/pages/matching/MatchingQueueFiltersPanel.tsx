import { GradientButton } from '@cantaro/client-shared/ui';
import type { MatchingQueueFilters } from '@cantaro/client-shared/music';

interface MatchingQueueFiltersPanelProps {
  filters: MatchingQueueFilters;
  filteredCount: number;
  isRetrying: boolean;
  retryMessage: string | null;
  onChange: (filters: MatchingQueueFilters) => void;
  onRetry: () => void;
}

function FilterSelect({ label, value, options, onChange }: { label: string; value: string; options: Array<[string, string]>; onChange: (value: string) => void }) {
  return <label className="text-sm font-semibold text-content">{label}<select value={value} onChange={(event) => onChange(event.target.value)} className="mt-1 min-h-11 w-full border border-border-strong bg-surface px-3 text-content">{options.map(([optionValue, optionLabel]) => <option key={optionValue} value={optionValue}>{optionLabel}</option>)}</select></label>;
}

function textValue(value?: string) { return value ?? ''; }
function RetryButton({ count, isRetrying, isReturning, onRetry }: { count: number; isRetrying: boolean; isReturning: boolean; onRetry: () => void }) {
  if (isRetrying) return <GradientButton disabled onClick={onRetry}>Queueing…</GradientButton>;
  return <GradientButton disabled={count === 0} onClick={onRetry}>{isReturning ? 'Return to matching' : 'Retry filtered'} ({count})</GradientButton>;
}

export function MatchingQueueFiltersPanel({ filters, filteredCount, isRetrying, retryMessage, onChange, onRetry }: MatchingQueueFiltersPanelProps) {
  const update = (changes: Partial<MatchingQueueFilters>) => onChange({ ...filters, ...changes });
  return (
    <section className="border-y border-border-subtle py-4">
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <FilterSelect label="Status" value={textValue(filters.status)} options={[["", "Needs review"], ["ambiguous", "Review candidates"], ["no_match", "No suitable candidates"], ["not_music", "Not music"]]} onChange={(value) => update({ status: value || undefined })} />
        <FilterSelect label="Source" value={textValue(filters.sourceType)} options={[["", "All sources"], ["youtube", "YouTube"], ["spotify", "Spotify"], ["browser-extension", "Browser extension"]]} onChange={(value) => update({ sourceType: value || undefined })} />
        <label className="text-sm font-semibold text-content">Search<input value={textValue(filters.query)} onChange={(event) => update({ query: event.target.value || undefined })} placeholder="Title, artist, ID, or error" className="mt-1 min-h-11 w-full border border-border-strong bg-surface px-3 text-content" /></label>
        <label className="flex min-h-11 items-center gap-2 self-end text-sm font-semibold text-content"><input type="checkbox" checked={Boolean(filters.errorsOnly)} onChange={(event) => update({ errorsOnly: event.target.checked })} className="h-4 w-4" />Remote/error failures only</label>
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-content-muted">{filteredCount} item(s) match these filters.</p>
        <div className="flex items-center gap-3">
          {retryMessage ? <p className="text-sm font-semibold text-success-content">{retryMessage}</p> : null}
          <RetryButton count={filteredCount} isRetrying={isRetrying} isReturning={filters.status === 'not_music'} onRetry={onRetry} />
        </div>
      </div>
    </section>
  );
}
