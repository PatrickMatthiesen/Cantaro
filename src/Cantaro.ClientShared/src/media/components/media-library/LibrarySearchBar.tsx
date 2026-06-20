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

function SearchSubmitButton({
    query,
    mode,
    isProviderMode,
    isSearchingProvider,
}: Pick<LibrarySearchBarProps, 'query' | 'mode' | 'isSearchingProvider'> & { isProviderMode: boolean }) {
    return (
        <button
            type="submit"
            className="inline-flex h-11 shrink-0 items-center justify-center rounded-2xl bg-gray-950 px-5 text-sm font-semibold text-white transition hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-50"
            disabled={isProviderMode && (!query.trim() || isSearchingProvider)}
            aria-busy={isSearchingProvider}
        >
            {isProviderMode && isSearchingProvider ? 'Searching...' : `Search ${modeLabel(mode)}`}
        </button>
    );
}

export function LibrarySearchBar({
    query,
    mode,
    connectedProviderIds,
    isSearchingProvider,
    navigation,
    onModeChange,
    onSubmit,
}: LibrarySearchBarProps) {
    const isProviderMode = mode !== 'library';

    return (
        <section className="rounded-3xl border border-white/70 bg-white/80 p-3 shadow-sm shadow-indigo-100/50 backdrop-blur">
            <form
                className="flex flex-col gap-3 md:flex-row md:items-center"
                onSubmit={(event) => {
                    event.preventDefault();
                    onSubmit();
                }}
            >
                {navigation ? <div className="shrink-0">{navigation}</div> : null}
                {navigation ? <div className="h-px w-full shrink-0 bg-gray-200/80 md:h-8 md:w-px" aria-hidden /> : null}
                <SearchModeTabs mode={mode} connectedProviderIds={connectedProviderIds} onModeChange={onModeChange} />
                <div className="min-w-0 flex-1" />
                <SearchSubmitButton query={query} mode={mode} isProviderMode={isProviderMode} isSearchingProvider={isSearchingProvider} />
            </form>
        </section>
    );
}
