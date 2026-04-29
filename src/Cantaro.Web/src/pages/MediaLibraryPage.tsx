import { useState, useEffect, useCallback, useRef } from 'react';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';
import { mediaApi } from '../services/mediaApi';
import { mainMediaProviderId, mediaProviderCatalog } from '../services/mediaProviders';
import {
  clearStoredValue,
  formatTimestamp,
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../services/mediaRefreshCache';
import type { MediaLibraryListItemDto, MediaLibraryQueryParams, MediaProviderAccountStatusDto } from '../services/mediaApi';

const NORMALIZED_STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planning', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

const MEDIA_KIND_OPTIONS = [
  { value: '', label: 'All types' },
  { value: 'anime', label: 'Anime' },
  { value: 'manga', label: 'Manga' },
  { value: 'lightNovel', label: 'Light Novel' },
  { value: 'oneShot', label: 'One Shot' },
];

const SORT_OPTIONS = [
  { value: 'updatedAt', label: 'Last updated' },
  { value: 'title', label: 'Title' },
  { value: 'status', label: 'Status' },
  { value: 'progress', label: 'Progress' },
];

const FIRST_PROVIDER_ID = mainMediaProviderId;
const DEFAULT_PRIMARY_LIST_NAME = 'Watching';

function storedListNameKey(providerId: string): string {
  return `cantaro.media.provider.${providerId}.lastListName`;
}

function statusLabel(status: string): string {
  return NORMALIZED_STATUS_OPTIONS.find((o) => o.value === status)?.label ?? status;
}

function mediaKindLabel(kind: string): string {
  return MEDIA_KIND_OPTIONS.find((o) => o.value === kind)?.label ?? kind;
}

function statusColor(status: string): string {
  switch (status) {
    case 'current': return 'bg-emerald-100 text-emerald-800';
    case 'completed': return 'bg-blue-100 text-blue-800';
    case 'planning': return 'bg-violet-100 text-violet-800';
    case 'paused': return 'bg-amber-100 text-amber-800';
    case 'dropped': return 'bg-rose-100 text-rose-800';
    case 'repeating': return 'bg-cyan-100 text-cyan-800';
    default: return 'bg-gray-100 text-gray-700';
  }
}

function progressText(entry: MediaLibraryListItemDto): string {
  switch (entry.primaryProgressDimension) {
    case 'episode': {
      const current = entry.progressEpisodes ?? 0;
      const total = entry.episodeCount;
      return total ? `Ep ${current} / ${total}` : `Ep ${current}`;
    }
    case 'chapter': {
      const current = entry.progressChapters ?? 0;
      const total = entry.chapterCount;
      return total ? `Ch ${current} / ${total}` : `Ch ${current}`;
    }
    case 'volume': {
      const current = entry.progressVolumes ?? 0;
      const total = entry.volumeCount;
      return total ? `Vol ${current} / ${total}` : `Vol ${current}`;
    }
    default:
      return '';
  }
}

function progressPercent(entry: MediaLibraryListItemDto): number | null {
  switch (entry.primaryProgressDimension) {
    case 'episode': {
      if (!entry.episodeCount || entry.episodeCount <= 0) {
        return null;
      }

      return Math.max(0, Math.min(100, ((entry.progressEpisodes ?? 0) / entry.episodeCount) * 100));
    }
    case 'chapter': {
      if (!entry.chapterCount || entry.chapterCount <= 0) {
        return null;
      }

      return Math.max(0, Math.min(100, ((entry.progressChapters ?? 0) / entry.chapterCount) * 100));
    }
    case 'volume': {
      if (!entry.volumeCount || entry.volumeCount <= 0) {
        return null;
      }

      return Math.max(0, Math.min(100, ((entry.progressVolumes ?? 0) / entry.volumeCount) * 100));
    }
    default:
      return null;
  }
}

function relativeReleaseTime(timestamp: string): string | null {
  const parsed = Date.parse(timestamp);
  if (Number.isNaN(parsed)) {
    return null;
  }

  const diffMs = parsed - Date.now();
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const day = 24 * hour;
  const week = 7 * day;
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

  if (Math.abs(diffMs) < hour) {
    return formatter.format(Math.round(diffMs / minute), 'minute');
  }

  if (Math.abs(diffMs) < day) {
    return formatter.format(Math.round(diffMs / hour), 'hour');
  }

  if (Math.abs(diffMs) < week) {
    return formatter.format(Math.round(diffMs / day), 'day');
  }

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(parsed);
}

interface LibraryEntryCardProps {
  entry: MediaLibraryListItemDto;
  onClick: () => void;
}

function LibraryArtwork({ posterUrl, title }: { posterUrl?: string; title: string }) {
  const [failed, setFailed] = useState(false);

  if (!posterUrl || failed) {
    return (
      <div className="flex h-full w-full items-center justify-center bg-linear-to-br from-indigo-100 to-purple-100">
        <span className="text-2xl" aria-hidden>🎌</span>
        <span className="sr-only">{title} — no artwork available</span>
      </div>
    );
  }

  return (
    <img
      src={posterUrl}
      alt={`${title} cover art`}
      className="h-full w-full object-cover"
      onError={() => setFailed(true)}
    />
  );
}

function LibraryEntryCard({ entry, onClick }: LibraryEntryCardProps) {
  const progress = progressText(entry);
  const completion = progressPercent(entry);
  const nextReleaseRelative = entry.nextReleaseAt ? relativeReleaseTime(entry.nextReleaseAt) : null;
  const releaseBadgeLabel = entry.nextReleaseLabel ?? 'Next release';
  const topLeftBadge = nextReleaseRelative
    ? {
      label: releaseBadgeLabel,
      detail: nextReleaseRelative,
    }
    : null;

  return (
    <button type="button" onClick={onClick} className="h-full w-full text-left">
      <GlassCard interactive className="group h-full p-0 transition duration-500">
        <div className="relative aspect-[0.72] min-h-80 overflow-hidden rounded-[1.75rem]">
          <LibraryArtwork posterUrl={entry.posterUrl} title={entry.canonicalTitle} />
          <div className="absolute inset-0 bg-linear-to-t from-slate-950 via-slate-900/30 to-slate-900/10" aria-hidden />

          <div className="absolute inset-x-0 top-0 flex items-start justify-between gap-3 p-4">
            {topLeftBadge ? (
              <span
                className="inline-flex max-w-40 flex-col rounded-2xl bg-cyan-50/92 px-3 py-2 text-left text-[11px] text-slate-900 shadow-lg backdrop-blur-md"
              >
                <span className="truncate font-semibold">{topLeftBadge.label}</span>
                {topLeftBadge.detail ? (
                  <span className="mt-0.5 truncate text-slate-700">
                    {topLeftBadge.detail}
                  </span>
                ) : null}
              </span>
            ) : <span />}

            <div className="flex flex-col items-end gap-2">
              {progress ? (
                <span className="rounded-full bg-white/90 px-3 py-1 text-xs font-semibold text-slate-900 shadow-lg backdrop-blur-md">
                  {progress}
                </span>
              ) : null}
              {!entry.isConnected ? (
                <span className="rounded-full bg-amber-400/95 px-3 py-1 text-[11px] font-semibold tracking-wide text-slate-950 uppercase shadow-lg">
                  Not synced
                </span>
              ) : null}
            </div>
          </div>

          <div className="absolute inset-x-0 bottom-0 p-4">
            <div className="rounded-3xl border border-white/16 bg-white/14 p-4 shadow-[0_18px_50px_rgba(15,23,42,0.38)] backdrop-blur-xl">
              <div className="flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.22em] text-white/78">
                <span>{mediaKindLabel(entry.mediaKind)}</span>
                <span className="text-white/38">•</span>
                <span>{entry.provider}</span>
                {entry.rawListName ? (
                  <>
                    <span className="text-white/38">•</span>
                    <span>{entry.rawListName}</span>
                  </>
                ) : null}
              </div>

              <p className="mt-3 line-clamp-2 text-xl leading-tight font-semibold text-white transition-colors group-hover:text-cyan-100">
                {entry.canonicalTitle}
              </p>
              {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                <p className="mt-1 line-clamp-1 text-sm text-white/64">{entry.originalTitle}</p>
              ) : null}

              <div className="mt-4 flex flex-wrap items-center gap-2">
                <span className={`rounded-full px-3 py-1 text-[11px] font-semibold shadow-sm ${statusColor(entry.normalizedStatus)}`}>
                  {statusLabel(entry.normalizedStatus)}
                </span>
                {completion !== null ? (
                  <span className="rounded-full bg-white/18 px-3 py-1 text-[11px] font-semibold text-white/88 shadow-sm backdrop-blur-md">
                    {Math.round(completion)}% complete
                  </span>
                ) : null}
              </div>

              {completion !== null ? (
                <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/18">
                  <div
                    className="h-full rounded-full bg-linear-to-r from-cyan-300 via-sky-400 to-rose-400 transition-all duration-500"
                    style={{ width: `${completion}%` }}
                  />
                </div>
              ) : (
                <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/10">
                  <div className="h-full w-1/3 rounded-full bg-linear-to-r from-white/55 to-white/15" />
                </div>
              )}

              {entry.lastSyncedAt ? (
                <p className="mt-3 text-xs text-white/62">
                  Synced {formatTimestamp(entry.lastSyncedAt) ?? new Date(entry.lastSyncedAt).toLocaleString()}
                </p>
              ) : null}
            </div>
          </div>
        </div>
      </GlassCard>
    </button>
  );
}

interface MediaLibraryPageProps {
  onNavigateHome?: () => void;
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  embedded?: boolean;
}

interface FilterOption {
  value: string;
  label: string;
}

interface FilterSelectProps {
  label: string;
  value: string;
  options: FilterOption[];
  onChange: (value: string | undefined) => void;
  disabled?: boolean;
}

function FilterSelect({ label, value, options, onChange, disabled = false }: FilterSelectProps) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{label}</label>
      <select
        className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400 disabled:cursor-not-allowed disabled:opacity-60"
        value={value}
        onChange={(e) => onChange(e.target.value || undefined)}
        disabled={disabled}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
    </div>
  );
}

interface SortControlsProps {
  sortBy: string;
  sortDir: 'asc' | 'desc';
  onSortByChange: (value: string) => void;
  onToggleSortDir: () => void;
}

function SortControls({ sortBy, sortDir, onSortByChange, onToggleSortDir }: SortControlsProps) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">Sort by</label>
      <div className="flex gap-1">
        <select
          className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
          value={sortBy}
          onChange={(e) => onSortByChange(e.target.value)}
        >
          {SORT_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>{option.label}</option>
          ))}
        </select>
        <button
          type="button"
          className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-600 transition hover:bg-white"
          onClick={onToggleSortDir}
          title={sortDir === 'asc' ? 'Ascending - click to switch' : 'Descending - click to switch'}
        >
          {sortDir === 'asc' ? '↑' : '↓'}
        </button>
      </div>
    </div>
  );
}

interface LibraryFiltersProps {
  filters: MediaLibraryQueryParams;
  availableListNames: string[];
  providerStatus: MediaProviderAccountStatusDto | null;
  isRefreshing: boolean;
  onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
  onUpdateProviderFilter: (provider: string | undefined) => void;
  onToggleSortDir: () => void;
  onRefreshFromRemote: () => Promise<void>;
}

function LibraryFilters({
  filters,
  availableListNames,
  providerStatus,
  isRefreshing,
  onUpdateFilter,
  onUpdateProviderFilter,
  onToggleSortDir,
  onRefreshFromRemote,
}: LibraryFiltersProps) {
  const providerOptions: FilterOption[] = [
    { value: '', label: 'All providers' },
    ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
  ];
  const listOptions: FilterOption[] = [
    { value: '', label: 'All lists' },
    ...availableListNames.map((listName) => ({ value: listName, label: listName })),
  ];

  return (
    <GlassCard className="p-4">
      <div className="flex flex-wrap items-end gap-3">
        <FilterSelect
          label="Status"
          value={filters.status ?? ''}
          options={NORMALIZED_STATUS_OPTIONS}
          onChange={(value) => onUpdateFilter('status', value)}
        />

        <FilterSelect
          label="Type"
          value={filters.mediaKind ?? ''}
          options={MEDIA_KIND_OPTIONS}
          onChange={(value) => onUpdateFilter('mediaKind', value)}
        />

        <FilterSelect
          label="Provider"
          value={filters.provider ?? ''}
          options={providerOptions}
          onChange={onUpdateProviderFilter}
        />

        <FilterSelect
          label="List"
          value={filters.listName ?? ''}
          options={listOptions}
          onChange={(value) => onUpdateFilter('listName', value)}
          disabled={availableListNames.length === 0 || filters.provider !== FIRST_PROVIDER_ID}
        />

        <SortControls
          sortBy={filters.sortBy ?? 'updatedAt'}
          sortDir={filters.sortDir ?? 'desc'}
          onSortByChange={(value) => onUpdateFilter('sortBy', value)}
          onToggleSortDir={onToggleSortDir}
        />

        {providerStatus?.isConnected ? (
          <div className="ml-auto flex flex-col gap-1">
            <span className="text-xs font-medium text-gray-500 uppercase tracking-wide">Reload</span>
            <button
              type="button"
              className="inline-flex h-10 items-center justify-center rounded-xl border border-gray-200 bg-white/80 px-4 text-sm font-medium text-gray-700 transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
              onClick={() => void onRefreshFromRemote()}
              disabled={isRefreshing}
              aria-busy={isRefreshing}
              title="Reload the primary provider library"
            >
              {isRefreshing ? 'Reloading…' : '↻ Reload'}
            </button>
          </div>
        ) : null}
      </div>
    </GlassCard>
  );
}

export function MediaLibraryPage({
  onNavigateHome,
  onNavigateProviders,
  onNavigateEntry,
  embedded = false,
}: MediaLibraryPageProps) {
  const initialStoredListNameRef = useRef(readStoredValue(storedListNameKey(FIRST_PROVIDER_ID)));
  const hasAppliedInitialListFallbackRef = useRef(Boolean(initialStoredListNameRef.current));
  const [filters, setFilters] = useState<MediaLibraryQueryParams>(() => ({
    provider: FIRST_PROVIDER_ID,
    listName: initialStoredListNameRef.current || undefined,
    sortBy: 'updatedAt',
    sortDir: 'desc',
    page: 1,
    pageSize: 24,
  }));
  const [items, setItems] = useState<MediaLibraryListItemDto[]>([]);
  const [availableListNames, setAvailableListNames] = useState<string[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [providerStatus, setProviderStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [lastRemoteCheckAt, setLastRemoteCheckAt] = useState<string | null>(() => readStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID)));
  const filtersRef = useRef(filters);

  useEffect(() => {
    filtersRef.current = filters;
  }, [filters]);

  const loadLibrary = useCallback(async (params: MediaLibraryQueryParams) => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await mediaApi.getLibrary(params);
      setItems(result.items);
      setAvailableListNames(result.availableListNames);
      setTotalPages(result.totalPages);
      setTotalCount(result.totalCount);
    } catch (err) {
      setAvailableListNames([]);
      setError(err instanceof Error ? err.message : 'Failed to load library');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadLibrary(filters);
  }, [filters, loadLibrary]);

  useEffect(() => {
    if (filters.provider !== FIRST_PROVIDER_ID) {
      return;
    }

    const storageKey = storedListNameKey(FIRST_PROVIDER_ID);
    if (filters.listName) {
      writeStoredValue(storageKey, filters.listName);
      return;
    }

    clearStoredValue(storageKey);
  }, [filters.listName, filters.provider]);

  useEffect(() => {
    if (!filters.listName || availableListNames.length === 0 || availableListNames.includes(filters.listName)) {
      return;
    }

    setFilters((prev) => ({ ...prev, listName: undefined, page: 1 }));
  }, [availableListNames, filters.listName]);

  useEffect(() => {
    if (hasAppliedInitialListFallbackRef.current || filters.provider !== FIRST_PROVIDER_ID || availableListNames.length === 0) {
      return;
    }

    hasAppliedInitialListFallbackRef.current = true;
    if (filters.listName || !availableListNames.includes(DEFAULT_PRIMARY_LIST_NAME)) {
      return;
    }

    setFilters((prev) => ({ ...prev, listName: DEFAULT_PRIMARY_LIST_NAME, page: 1 }));
  }, [availableListNames, filters.listName, filters.provider]);

  const refreshFromRemote = useCallback(async () => {
    setIsRefreshing(true);
    setRefreshError(null);

    try {
      const result = await mediaApi.importLibrary(FIRST_PROVIDER_ID);
      writeStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID), result.importedAt);
      setLastRemoteCheckAt(result.importedAt);
      await loadLibrary(filtersRef.current);
    } catch (err) {
      setRefreshError(err instanceof Error ? err.message : 'Failed to refresh from AniList');
    } finally {
      setIsRefreshing(false);
    }
  }, [loadLibrary]);

  useEffect(() => {
    let isCancelled = false;

    const loadProviderState = async () => {
      try {
        const status = await mediaApi.getProviderStatus(FIRST_PROVIDER_ID);
        if (isCancelled) {
          return;
        }

        setProviderStatus(status);
        if (status.isConnected && isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID)))) {
          await refreshFromRemote();
        }
      } catch (err) {
        if (!isCancelled) {
          setRefreshError(err instanceof Error ? err.message : 'Failed to check AniList status');
        }
      }
    };

    void loadProviderState();

    return () => {
      isCancelled = true;
    };
  }, [refreshFromRemote]);

  const updateFilter = <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => {
    setFilters((prev) => ({ ...prev, [key]: value, page: 1 }));
  };

  const toggleSortDir = () => {
    setFilters((prev) => ({
      ...prev,
      sortDir: prev.sortDir === 'asc' ? 'desc' : 'asc',
      page: 1,
    }));
  };

  const updateProviderFilter = (provider: string | undefined) => {
    setFilters((prev) => ({
      ...prev,
      provider,
      listName: provider === FIRST_PROVIDER_ID
        ? readStoredValue(storedListNameKey(FIRST_PROVIDER_ID)) || undefined
        : undefined,
      page: 1,
    }));
  };

  const formattedLastRemoteCheckAt = formatTimestamp(lastRemoteCheckAt);
  const hasActiveFilters = Boolean(
    filters.status
    || filters.mediaKind
    || filters.listName
    || (filters.provider && filters.provider !== FIRST_PROVIDER_ID),
  );
  const headerActions = [
    onNavigateProviders
      ? (
        <GradientButton key="providers" tone="soft" onClick={onNavigateProviders}>
          Providers
        </GradientButton>
      )
      : null,
    onNavigateHome
      ? (
        <GradientButton key="home" tone="soft" onClick={onNavigateHome}>
          ← Home
        </GradientButton>
      )
      : null,
  ].filter(Boolean);

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className={`relative z-10 mx-auto space-y-6 ${embedded ? 'max-w-440 px-4 pt-4 pb-6' : 'max-w-384 px-6 pt-8 pb-16'}`}>
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
            <h1 className="mt-1 text-3xl font-bold">My Library</h1>
            {!isLoading && totalCount > 0 ? (
              <p className="mt-1 text-sm text-gray-500">{totalCount.toLocaleString()} entries</p>
            ) : null}
            {providerStatus?.isConnected && formattedLastRemoteCheckAt ? (
              <p className="mt-1 text-xs text-gray-500">Last checked AniList: {formattedLastRemoteCheckAt}</p>
            ) : null}
          </div>
          {headerActions.length > 0 ? (
            <div className="flex gap-2">{headerActions}</div>
          ) : null}
        </header>

        {refreshError ? (
          <GlassCard className="p-4">
            <p className="text-sm text-rose-700">{refreshError}</p>
          </GlassCard>
        ) : null}

        <LibraryFilters
          filters={filters}
          availableListNames={availableListNames}
          providerStatus={providerStatus}
          isRefreshing={isRefreshing}
          onUpdateFilter={updateFilter}
          onUpdateProviderFilter={updateProviderFilter}
            onToggleSortDir={toggleSortDir}
          onRefreshFromRemote={refreshFromRemote}
        />

        {/* Content */}
        {error ? (
          <GlassCard className="p-6">
            <p className="text-sm text-rose-700">{error}</p>
            <div className="mt-3">
              <GradientButton tone="soft" onClick={() => void loadLibrary(filters)}>Retry</GradientButton>
            </div>
          </GlassCard>
        ) : isLoading ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
            {Array.from({ length: 10 }).map((_, i) => (
              <GlassCard key={i} className="aspect-[0.72] animate-pulse bg-white/50" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <GlassCard className="p-8 text-center">
            <p className="text-gray-500">No library entries found.</p>
            <p className="mt-1 text-sm text-gray-400">
              {hasActiveFilters
                ? 'Try removing some filters.'
                : 'Import your library from a provider to get started.'}
            </p>
            {!hasActiveFilters && onNavigateProviders ? (
              <div className="mt-4">
                <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onNavigateProviders}>
                  Go to Providers
                </GradientButton>
              </div>
            ) : null}
          </GlassCard>
        ) : (
          <>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
              {items.map((entry) => (
                <LibraryEntryCard
                  key={entry.id}
                  entry={entry}
                  onClick={() => onNavigateEntry(entry.id)}
                />
              ))}
            </div>

            {totalPages > 1 ? (
              <div className="flex items-center justify-center gap-2">
                <GradientButton
                  tone="soft"
                  disabled={filters.page === 1}
                  onClick={() => setFilters((prev) => ({ ...prev, page: Math.max(1, (prev.page ?? 1) - 1) }))}
                >
                  ← Previous
                </GradientButton>
                <span className="text-sm text-gray-600">
                  Page {filters.page ?? 1} of {totalPages}
                </span>
                <GradientButton
                  tone="soft"
                  disabled={(filters.page ?? 1) >= totalPages}
                  onClick={() => setFilters((prev) => ({ ...prev, page: (prev.page ?? 1) + 1 }))}
                >
                  Next →
                </GradientButton>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}
