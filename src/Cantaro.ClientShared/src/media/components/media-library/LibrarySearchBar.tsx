import type { ReactNode } from 'react';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import { SegmentedSwitch } from '../../../ui';

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
        <SegmentedSwitch
            value={mode}
            options={options}
            onChange={onModeChange}
            className="segmented-switch--embedded shrink-0"
        />
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
        <section className="rounded-3xl border border-border-subtle bg-surface-translucent p-3 shadow-sm backdrop-blur">
            <form
                className="flex flex-wrap gap-2 justify-between"
                onSubmit={(event) => {
                    event.preventDefault();
                    onSubmit();
                }}
            >
                {navigation ? <div className="shrink-0">{navigation}</div> : null}
                {navigation ? <div className="h-px w-full shrink-0 bg-border-subtle sm:h-8 sm:w-px" aria-hidden /> : null}
                <SearchModeTabs mode={mode} connectedProviderIds={connectedProviderIds} onModeChange={onModeChange} />
                <label className="min-w-48 flex-1">
                    <span className="sr-only">Search {modeLabel(mode)}</span>
                    <input
                        type="search"
                        value={query}
                        onChange={(event) => onQueryChange(event.target.value)}
                        placeholder={isProviderMode ? `Search ${modeLabel(mode)}…` : 'Search your library…'}
                        className="h-11 w-full rounded-xl border border-border-subtle bg-surface px-4 text-sm text-content outline-none transition focus:border-focus focus:ring-2 focus:ring-focus/20"
                    />
                </label>
                <span className="self-center px-1 text-xs text-content-muted" aria-live="polite">
                    {isSearchingProvider ? 'Searching…' : query.trim() ? 'Updates automatically' : ''}
                </span>
            </form>
        </section>
    );
}
