import { mediaProviderCatalog } from '../../services/mediaProviders';

export type LibrarySearchMode = 'library' | string;

export interface LibrarySearchBarProps {
    query: string;
    mode: LibrarySearchMode;
    connectedProviderIds: string[];
    isSearchingProvider: boolean;
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

function modeButtonClassName(isSelected: boolean): string {
    return `rounded-xl px-3 py-2 text-sm font-semibold transition ${isSelected
        ? 'bg-white text-gray-950 shadow-sm'
        : 'text-gray-500 hover:text-gray-800'
        }`;
}

function SearchModeTabs({
    mode,
    connectedProviderIds,
    onModeChange,
}: Pick<LibrarySearchBarProps, 'mode' | 'connectedProviderIds' | 'onModeChange'>) {
    const providerModes = mediaProviderCatalog.filter((provider) => connectedProviderIds.includes(provider.id));

    return (
        <div className="flex shrink-0 flex-wrap gap-1 rounded-2xl bg-gray-100/80 p-1">
            <button
                type="button"
                className={modeButtonClassName(mode === 'library')}
                onClick={() => onModeChange('library')}
            >
                My library
            </button>
            {providerModes.map((provider) => (
                <button
                    key={provider.id}
                    type="button"
                    className={modeButtonClassName(mode === provider.id)}
                    onClick={() => onModeChange(provider.id)}
                >
                    {provider.name}
                </button>
            ))}
        </div>
    );
}

function SearchTextInput({
    query,
    mode,
    onQueryChange,
}: Pick<LibrarySearchBarProps, 'query' | 'mode' | 'onQueryChange'>) {
    return (
        <div className="flex min-w-0 flex-1 items-center gap-2 rounded-2xl border border-gray-200 bg-white px-4 py-2 focus-within:ring-2 focus-within:ring-indigo-300">
            <input
                type="search"
                value={query}
                onChange={(event) => onQueryChange(event.target.value)}
                placeholder={`Search ${modeLabel(mode)}...`}
                className="min-w-0 flex-1 bg-transparent text-sm text-gray-900 outline-none placeholder:text-gray-400"
            />
        </div>
    );
}

function SearchSubmitButton({
    query,
    isProviderMode,
    isSearchingProvider,
}: Pick<LibrarySearchBarProps, 'query' | 'isSearchingProvider'> & { isProviderMode: boolean }) {
    return (
        <button
            type="submit"
            className="inline-flex h-11 shrink-0 items-center justify-center rounded-2xl bg-gray-950 px-5 text-sm font-semibold text-white transition hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-50"
            disabled={isProviderMode && (!query.trim() || isSearchingProvider)}
            aria-busy={isSearchingProvider}
        >
            {isProviderMode && isSearchingProvider ? 'Searching...' : 'Search'}
        </button>
    );
}

export function LibrarySearchBar({
    query,
    mode,
    connectedProviderIds,
    isSearchingProvider,
    onQueryChange,
    onModeChange,
    onSubmit,
}: LibrarySearchBarProps) {
    const isProviderMode = mode !== 'library';

    return (
        <section className="rounded-3xl border border-white/70 bg-white/80 p-3 shadow-sm shadow-indigo-100/50 backdrop-blur">
            <form
                className="flex flex-col gap-3 lg:flex-row lg:items-center"
                onSubmit={(event) => {
                    event.preventDefault();
                    onSubmit();
                }}
            >
                <SearchModeTabs mode={mode} connectedProviderIds={connectedProviderIds} onModeChange={onModeChange} />
                <SearchTextInput query={query} mode={mode} onQueryChange={onQueryChange} />
                <SearchSubmitButton query={query} isProviderMode={isProviderMode} isSearchingProvider={isSearchingProvider} />
            </form>
        </section>
    );
}
