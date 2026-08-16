import type { ReactNode } from 'react';
import { Search } from 'lucide-react';
import { mediaProviderCatalog } from '../../services/mediaProviders';

export type LibrarySearchMode = 'library' | string;

export interface LibrarySearchBarProps {
    query: string;
    mode: LibrarySearchMode;
    connectedProviderIds: string[];
    isSearchingProvider: boolean;
    navigation?: ReactNode;
    onQueryChange: (query: string) => void;
    onModeChange: (mode: LibrarySearchMode) => void;
    onSubmit: () => void;
}

function modeLabel(mode: LibrarySearchMode): string {
    if (mode === 'library') {
        return 'My library';
    }

    return mediaProviderCatalog.find((provider) => provider.id === mode)?.name ?? mode;
}

function SearchModeTabs({
    mode,
    connectedProviderIds,
    onModeChange,
}: Pick<LibrarySearchBarProps, 'mode' | 'connectedProviderIds' | 'onModeChange'>) {
    const providerModes = mediaProviderCatalog.filter((provider) => connectedProviderIds.includes(provider.id));
    const options = [
        { value: 'library', label: 'My library' },
        ...providerModes.map((provider) => ({ value: provider.id, label: provider.name })),
    ];

    return (
        <div className="flex min-w-0 items-center gap-5 overflow-x-auto" aria-label="Search source">
            {options.map((option) => {
                const isActive = option.value === mode;
                return (
                    <button
                        key={option.value}
                        type="button"
                        aria-pressed={isActive}
                        className={`relative min-h-11 shrink-0 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${isActive ? 'text-content after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:bg-personal-accent' : 'text-content-muted hover:text-personal-accent-strong'}`}
                        onClick={() => onModeChange(option.value)}
                    >
                        {option.label}
                    </button>
                );
            })}
        </div>
    );
}

export function LibrarySearchBar({
    query,
    mode,
    connectedProviderIds,
    isSearchingProvider,
    navigation,
    onQueryChange,
    onModeChange,
    onSubmit,
}: LibrarySearchBarProps) {
    const isProviderMode = mode !== 'library';

    return (
        <section className="border-y border-border-subtle py-3">
            <form
                className="grid gap-3 md:grid-cols-[auto_minmax(14rem,1fr)_auto] md:items-center"
                onSubmit={(event) => {
                    event.preventDefault();
                    onSubmit();
                }}
            >
                <div className="flex min-w-0 items-center gap-4">
                    {navigation ? <div className="shrink-0">{navigation}</div> : null}
                    <SearchModeTabs mode={mode} connectedProviderIds={connectedProviderIds} onModeChange={onModeChange} />
                </div>
                <label className="relative min-w-0">
                    <span className="sr-only">Search {modeLabel(mode)}</span>
                    <Search className="pointer-events-none absolute top-1/2 left-3 size-5 -translate-y-1/2 text-content-subtle" aria-hidden />
                    <input
                        type="search"
                        value={query}
                        onChange={(event) => onQueryChange(event.target.value)}
                        placeholder={isProviderMode ? `Search ${modeLabel(mode)}…` : 'Search your library…'}
                        className="h-11 w-full border border-border-strong bg-surface pr-4 pl-10 text-sm text-content outline-none transition-colors placeholder:text-content-subtle hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
                    />
                </label>
                <span className="min-h-4 self-center text-xs text-content-muted md:text-right" aria-live="polite">
                    {isSearchingProvider ? 'Searching…' : query.trim() ? 'Updates automatically' : ''}
                </span>
            </form>
        </section>
    );
}
