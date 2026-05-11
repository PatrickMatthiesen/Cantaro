import { useCallback, useEffect, useRef, useState } from 'react';
import { SanitizedSynopsis } from './media-entry-detail/EntryDisplayPrimitives';
import { GradientButton } from '../../ui';
import { mediaApi } from '../services/mediaApi';
import { mediaKindLabel } from '../services/mediaFormatting';
import { mediaProviderCatalog } from '../services/mediaProviders';
import type {
    MediaLinkConflictDto,
    MediaProviderLinkSummaryDto,
    MediaProviderSearchResultDto,
} from '../services/mediaApi';

export interface SearchLinkDialogProps {
    libraryEntryId: string;
    mediaKind: string;
    existingLinks: MediaProviderLinkSummaryDto[];
    onClose: () => void;
    onLinked: () => void;
}

interface SearchDialogConflictBannerProps {
    conflict: { providerMediaId: string; conflictInfo: MediaLinkConflictDto };
    linkingId: string | null;
    onForceRelink: (providerMediaId: string) => void;
    onCancel: () => void;
}

interface SearchResultsListProps {
    results: MediaProviderSearchResultDto[];
    query: string;
    linkingId: string | null;
    alreadyLinkedIds: Set<string>;
    onLink: (providerMediaId: string) => void;
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
    return error instanceof Error ? error.message : fallbackMessage;
}

function parseLinkConflict(error: unknown): MediaLinkConflictDto | null {
    if (!(error instanceof Error)) {
        return null;
    }

    try {
        return JSON.parse(error.message) as MediaLinkConflictDto;
    } catch {
        return null;
    }
}

function useSearchLinkDialogState({
    libraryEntryId,
    mediaKind,
    onLinked,
}: Pick<SearchLinkDialogProps, 'libraryEntryId' | 'mediaKind' | 'onLinked'>) {
    const [providerId, setProviderId] = useState<string>(mediaProviderCatalog[0]?.id ?? '');
    const [query, setQuery] = useState('');
    const [results, setResults] = useState<MediaProviderSearchResultDto[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const [searchError, setSearchError] = useState<string | null>(null);
    const [linkingId, setLinkingId] = useState<string | null>(null);
    const [conflict, setConflict] = useState<{ providerMediaId: string; conflictInfo: MediaLinkConflictDto } | null>(null);
    const [linkError, setLinkError] = useState<string | null>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        inputRef.current?.focus();
    }, []);

    const resetLinkState = useCallback(() => {
        setConflict(null);
        setLinkError(null);
    }, []);

    const selectProvider = useCallback((nextProviderId: string) => {
        setProviderId(nextProviderId);
        setResults([]);
        setSearchError(null);
        resetLinkState();
    }, [resetLinkState]);

    const handleSearch = useCallback(async () => {
        if (!query.trim()) {
            return;
        }

        setIsSearching(true);
        setSearchError(null);
        setResults([]);
        resetLinkState();

        try {
            const data = await mediaApi.searchProvider(providerId, {
                query: query.trim(),
                mediaKinds: mediaKind ? [mediaKind] : undefined,
                limit: 20,
            });
            setResults(data);
        } catch (searchFailure) {
            setSearchError(getErrorMessage(searchFailure, 'Search failed'));
        } finally {
            setIsSearching(false);
        }
    }, [mediaKind, providerId, query, resetLinkState]);

    const handleLink = useCallback(async (providerMediaId: string, forceRelink = false) => {
        setLinkingId(providerMediaId);
        resetLinkState();

        try {
            await mediaApi.linkProvider(libraryEntryId, { providerId, providerMediaId, forceRelink });
            onLinked();
        } catch (linkFailure) {
            const parsedConflict = parseLinkConflict(linkFailure);
            if (parsedConflict) {
                setConflict({ providerMediaId, conflictInfo: parsedConflict });
            } else {
                setLinkError(getErrorMessage(linkFailure, 'Failed to link provider'));
            }
        } finally {
            setLinkingId(null);
        }
    }, [libraryEntryId, onLinked, providerId, resetLinkState]);

    return {
        providerId,
        query,
        setQuery,
        results,
        isSearching,
        searchError,
        linkingId,
        conflict,
        linkError,
        inputRef,
        selectProvider,
        handleSearch,
        handleLink,
        clearConflict: () => setConflict(null),
    };
}

function SearchDialogConflictBanner({ conflict, linkingId, onForceRelink, onCancel }: SearchDialogConflictBannerProps) {
    return (
        <div className="rounded-xl bg-amber-50 px-4 py-3 text-sm">
            <p className="font-semibold text-amber-800">This entry is already linked to another title</p>
            <p className="mt-1 text-amber-700">
                Currently linked to: <span className="font-medium">{conflict.conflictInfo.conflictingCanonicalTitle}</span>
            </p>
            <div className="mt-2 flex gap-2">
                <GradientButton
                    gradient="from-amber-500 to-orange-500"
                    onClick={() => onForceRelink(conflict.providerMediaId)}
                    disabled={!!linkingId}
                >
                    Force relink
                </GradientButton>
                <GradientButton tone="soft" onClick={onCancel}>
                    Cancel
                </GradientButton>
            </div>
        </div>
    );
}

function resultMetadata(result: MediaProviderSearchResultDto): string[] {
    return [
        result.startYear ? String(result.startYear) : null,
        result.episodeCount ? `${result.episodeCount} ep` : null,
        result.chapterCount ? `${result.chapterCount} ch` : null,
    ].filter((value): value is string => value !== null);
}

function SearchResultPoster({ posterUrl, title }: Pick<MediaProviderSearchResultDto, 'posterUrl' | 'title'>) {
    return (
        <div className="h-16 w-12 shrink-0 overflow-hidden rounded-lg bg-gray-100">
            {posterUrl ? (
                <img
                    src={posterUrl}
                    alt={`${title} artwork`}
                    className="h-full w-full object-cover"
                    onError={(event) => { event.currentTarget.style.display = 'none'; }}
                />
            ) : (
                <div className="flex h-full w-full items-center justify-center text-xl">🎌</div>
            )}
        </div>
    );
}

function SearchResultAction({
    providerMediaId,
    linkingId,
    isLinked,
    onLink,
}: {
    providerMediaId: string;
    linkingId: string | null;
    isLinked: boolean;
    onLink: (providerMediaId: string) => void;
}) {
    if (isLinked) {
        return <span className="rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">Linked</span>;
    }

    const isLinking = linkingId === providerMediaId;

    return (
        <GradientButton
            gradient="from-indigo-500 to-purple-500"
            className="px-3 py-2 text-xs"
            disabled={!!linkingId}
            aria-busy={isLinking}
            onClick={() => onLink(providerMediaId)}
        >
            {isLinking ? '…' : 'Link'}
        </GradientButton>
    );
}

function SearchResultsList({ results, query, linkingId, alreadyLinkedIds, onLink }: SearchResultsListProps) {
    if (results.length === 0) {
        return (
            <p className="py-8 text-center text-sm text-gray-400">
                {query.trim() ? 'No results found.' : 'Enter a title to search.'}
            </p>
        );
    }

    return (
        <>
            {results.map((result) => {
                const isLinked = alreadyLinkedIds.has(result.providerMediaId);
                const metadata = resultMetadata(result);

                return (
                    <div
                        key={result.providerMediaId}
                        className="flex items-start gap-3 rounded-2xl border border-gray-100 bg-white/70 p-3"
                    >
                        <SearchResultPoster posterUrl={result.posterUrl} title={result.title} />
                        <div className="min-w-0 flex-1">
                            <p className="leading-tight font-medium text-gray-900">{result.title}</p>
                            {result.nativeTitle ? <p className="truncate text-xs text-gray-500">{result.nativeTitle}</p> : null}
                            <div className="mt-1 flex flex-wrap items-center gap-1.5">
                                <span className="rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700">
                                    {mediaKindLabel(result.mediaKind)}
                                </span>
                                {metadata.map((value) => <span key={value} className="text-xs text-gray-400">{value}</span>)}
                            </div>
                            {result.synopsis ? (
                                <SanitizedSynopsis
                                    html={result.synopsis}
                                    className="mt-1 line-clamp-2 text-xs leading-relaxed text-gray-500"
                                />
                            ) : null}
                        </div>
                        <div className="shrink-0">
                            <SearchResultAction
                                providerMediaId={result.providerMediaId}
                                linkingId={linkingId}
                                isLinked={isLinked}
                                onLink={onLink}
                            />
                        </div>
                    </div>
                );
            })}
        </>
    );
}

// fallow-ignore-next-line complexity
export function SearchLinkDialog({ libraryEntryId, mediaKind, existingLinks, onClose, onLinked }: SearchLinkDialogProps) {
    const {
        providerId,
        query,
        setQuery,
        results,
        isSearching,
        searchError,
        linkingId,
        conflict,
        linkError,
        inputRef,
        selectProvider,
        handleSearch,
        handleLink,
        clearConflict,
    } = useSearchLinkDialogState({ libraryEntryId, mediaKind, onLinked });
    const alreadyLinkedIds = new Set(existingLinks.filter((link) => link.provider === providerId).map((link) => link.externalId));

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm"
            onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}
        >
            <div className="flex max-h-[90vh] w-full max-w-2xl flex-col rounded-3xl border border-white/80 bg-white/90 shadow-2xl backdrop-blur">
                <div className="flex items-center justify-between border-b border-gray-100 px-6 py-4">
                    <h2 className="text-lg font-semibold text-gray-900">Search &amp; link a provider entry</h2>
                    <button
                        type="button"
                        className="rounded-xl px-3 py-1.5 text-sm text-gray-500 transition hover:bg-gray-100"
                        onClick={onClose}
                        aria-label="Close dialog"
                    >
                        ✕
                    </button>
                </div>

                <div className="space-y-3 px-6 pt-4 pb-3">
                    {mediaProviderCatalog.length > 1 ? (
                        <div className="flex gap-2">
                            {mediaProviderCatalog.map((provider) => (
                                <button
                                    key={provider.id}
                                    type="button"
                                    onClick={() => selectProvider(provider.id)}
                                    className={`rounded-xl px-4 py-2 text-sm font-medium transition ${providerId === provider.id
                                        ? 'bg-indigo-500 text-white'
                                        : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                                        }`}
                                >
                                    {provider.icon} {provider.name}
                                </button>
                            ))}
                        </div>
                    ) : null}

                    <div className="flex gap-2">
                        <input
                            ref={inputRef}
                            type="text"
                            value={query}
                            onChange={(event) => setQuery(event.target.value)}
                            onKeyDown={(event) => { if (event.key === 'Enter') void handleSearch(); }}
                            placeholder="Search by title…"
                            className="flex-1 rounded-xl border border-gray-200 bg-white px-4 py-2 text-sm focus:ring-2 focus:ring-indigo-400 focus:outline-none"
                        />
                        <GradientButton
                            gradient="from-indigo-500 to-purple-500"
                            onClick={() => void handleSearch()}
                            disabled={isSearching || !query.trim()}
                            aria-busy={isSearching}
                        >
                            {isSearching ? 'Searching…' : 'Search'}
                        </GradientButton>
                    </div>

                    {searchError ? <p className="rounded-xl bg-rose-50 px-4 py-2 text-sm text-rose-700">{searchError}</p> : null}
                    {linkError ? <p className="rounded-xl bg-rose-50 px-4 py-2 text-sm text-rose-700">{linkError}</p> : null}
                    {conflict ? (
                        <SearchDialogConflictBanner
                            conflict={conflict}
                            linkingId={linkingId}
                            onForceRelink={(providerMediaId) => void handleLink(providerMediaId, true)}
                            onCancel={clearConflict}
                        />
                    ) : null}
                </div>

                <div className="flex-1 space-y-2 overflow-y-auto px-6 pb-6">
                    {!isSearching ? (
                        <SearchResultsList
                            results={results}
                            query={query}
                            linkingId={linkingId}
                            alreadyLinkedIds={alreadyLinkedIds}
                            onLink={(providerMediaId) => void handleLink(providerMediaId)}
                        />
                    ) : (
                        <p className="py-8 text-center text-sm text-gray-400">Searching…</p>
                    )}
                </div>
            </div>
        </div>
    );
}
