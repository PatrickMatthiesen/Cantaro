import { useCallback, useEffect, useRef, useState } from 'react';
import { SanitizedSynopsis } from './media-entry-detail/EntryDisplayPrimitives';
import { GradientButton } from '../../ui';
import { mediaApi } from '../services/mediaApi';
import { mediaKindLabel } from '../services/mediaFormatting';
import { mediaProviderCatalog } from '../services/mediaProviders';
import { MediaProviderIcon } from './MediaProviderIcon';
import type {
    MediaLinkConflictDto,
    MediaProviderLinkSummaryDto,
    MediaProviderSearchResultDto,
} from '../services/mediaApi';

export interface SearchLinkDialogProps {
    libraryEntryId: string;
    currentTitle: string;
    mediaKind: string;
    existingLinks: MediaProviderLinkSummaryDto[];
    onClose: () => void;
    onLinked: () => void;
}

interface SearchDialogConflictBannerProps {
    conflict: { result: MediaProviderSearchResultDto; conflictInfo: MediaLinkConflictDto };
    providerName: string;
    onCancel: () => void;
}

interface SearchResultsListProps {
    results: MediaProviderSearchResultDto[];
    query: string;
    linkingId: string | null;
    alreadyLinkedIds: Set<string>;
    onLink: (result: MediaProviderSearchResultDto) => void;
}

interface LinkFeedbackState {
    linkingId: string | null;
    conflict: { result: MediaProviderSearchResultDto; conflictInfo: MediaLinkConflictDto } | null;
    linkError: string | null;
    setLinkingId: (linkingId: string | null) => void;
    setConflict: (conflict: { result: MediaProviderSearchResultDto; conflictInfo: MediaLinkConflictDto } | null) => void;
    setLinkError: (linkError: string | null) => void;
    resetLinkState: () => void;
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
    return error instanceof Error ? error.message : fallbackMessage;
}

function providerDisplayName(providerId: string): string {
    return mediaProviderCatalog.find((provider) => provider.id === providerId)?.name ?? providerId;
}

function parseLinkConflict(error: unknown): MediaLinkConflictDto | null {
    if (!(error instanceof Error)) {
        return null;
    }

    const responseBody = 'responseBody' in error ? error.responseBody : undefined;
    if (responseBody && typeof responseBody === 'object' && 'code' in responseBody) {
        return responseBody as MediaLinkConflictDto;
    }

    return null;
}

function useLinkFeedbackState(): LinkFeedbackState {
    const [linkingId, setLinkingId] = useState<string | null>(null);
    const [conflict, setConflict] = useState<{ result: MediaProviderSearchResultDto; conflictInfo: MediaLinkConflictDto } | null>(null);
    const [linkError, setLinkError] = useState<string | null>(null);

    const resetLinkState = useCallback(() => {
        setConflict(null);
        setLinkError(null);
    }, []);

    return { linkingId, conflict, linkError, setLinkingId, setConflict, setLinkError, resetLinkState };
}

function useSearchFields(resetLinkState: () => void) {
    const [providerId, setProviderId] = useState<string>(mediaProviderCatalog[0]?.id ?? '');
    const [query, setQuery] = useState('');
    const inputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        inputRef.current?.focus();
    }, []);

    const selectProvider = useCallback((nextProviderId: string) => {
        setProviderId(nextProviderId);
        resetLinkState();
    }, [resetLinkState]);

    return { providerId, query, setQuery, inputRef, selectProvider };
}

function useProviderSearch(
    mediaKind: string,
    providerId: string,
    query: string,
    resetLinkState: () => void,
) {
    const [results, setResults] = useState<MediaProviderSearchResultDto[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const [searchError, setSearchError] = useState<string | null>(null);

    const clearSearchResults = useCallback(() => {
        setResults([]);
        setSearchError(null);
    }, []);

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

    return { results, isSearching, searchError, clearSearchResults, handleSearch };
}

function useProviderLink(
    libraryEntryId: string,
    providerId: string,
    onLinked: () => void,
    feedback: LinkFeedbackState,
    onReplacementRequired: (result: MediaProviderSearchResultDto) => void,
) {
    const handleLink = useCallback(async (result: MediaProviderSearchResultDto, replaceExisting = false) => {
        feedback.setLinkingId(result.providerMediaId);
        feedback.resetLinkState();

        try {
            await mediaApi.linkProvider(libraryEntryId, {
                providerId,
                providerMediaId: result.providerMediaId,
                confirmReplacement: replaceExisting,
            });
            onLinked();
        } catch (linkFailure) {
            const parsedConflict = parseLinkConflict(linkFailure);
            if (parsedConflict?.code === 'replacement_confirmation_required') {
                onReplacementRequired(result);
            } else if (parsedConflict) {
                feedback.setConflict({ result, conflictInfo: parsedConflict });
            } else {
                feedback.setLinkError(getErrorMessage(linkFailure, 'Failed to link provider'));
            }
        } finally {
            feedback.setLinkingId(null);
        }
    }, [feedback, libraryEntryId, onLinked, onReplacementRequired, providerId]);

    return handleLink;
}

function useSearchLinkDialogState({
    libraryEntryId,
    mediaKind,
    onLinked,
}: Pick<SearchLinkDialogProps, 'libraryEntryId' | 'mediaKind' | 'onLinked'>,
onReplacementRequired: (result: MediaProviderSearchResultDto) => void) {
    const feedback = useLinkFeedbackState();
    const fields = useSearchFields(feedback.resetLinkState);
    const search = useProviderSearch(mediaKind, fields.providerId, fields.query, feedback.resetLinkState);
    const handleLink = useProviderLink(libraryEntryId, fields.providerId, onLinked, feedback, onReplacementRequired);

    const selectProvider = useCallback((nextProviderId: string) => {
        fields.selectProvider(nextProviderId);
        search.clearSearchResults();
    }, [fields, search]);

    return {
        providerId: fields.providerId,
        query: fields.query,
        setQuery: fields.setQuery,
        results: search.results,
        isSearching: search.isSearching,
        searchError: search.searchError,
        linkingId: feedback.linkingId,
        conflict: feedback.conflict,
        linkError: feedback.linkError,
        inputRef: fields.inputRef,
        selectProvider,
        handleSearch: search.handleSearch,
        handleLink,
        clearConflict: () => feedback.setConflict(null),
    };
}

function SearchDialogConflictBanner({ conflict, providerName, onCancel }: SearchDialogConflictBannerProps) {
    return (
        <div className="rounded-xl bg-warning-surface px-4 py-3 text-sm">
            <p className="font-semibold text-warning-content">This {providerName} title is already used elsewhere</p>
            <p className="mt-1 text-warning-content">
                <span className="font-medium">{conflict.result.title}</span> is currently linked to{' '}
                <span className="font-medium">{conflict.conflictInfo.conflictingCanonicalTitle ?? 'another title'}</span> in Cantaro.
                Cantaro will not move a provider identity away from another title here.
            </p>
            <div className="mt-2 flex gap-2">
                <GradientButton tone="soft" onClick={onCancel}>
                    Keep existing links
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
        <div className="h-16 w-12 shrink-0 overflow-hidden rounded-lg bg-surface-subtle">
            {posterUrl ? (
                <img
                    src={posterUrl}
                    alt={`${title} artwork`}
                    className="h-full w-full object-cover"
                    onError={(event) => { event.currentTarget.style.display = 'none'; }}
                />
            ) : (
                <div className="flex h-full w-full items-center justify-center">
                    <MediaProviderIcon providerId="anilist" className="h-8 w-8" aria-hidden />
                </div>
            )}
        </div>
    );
}

function SearchResultAction({
    result,
    linkingId,
    isLinked,
    onLink,
}: {
    linkingId: string | null;
    isLinked: boolean;
    onLink: (result: MediaProviderSearchResultDto) => void;
    result: MediaProviderSearchResultDto;
}) {
    if (isLinked) {
        return <span className="rounded-full bg-success-surface px-3 py-1 text-xs font-semibold text-success-content">Linked</span>;
    }

    const isLinking = linkingId === result.providerMediaId;

    return (
        <GradientButton
            gradient="from-indigo-500 to-purple-500"
            className="px-3 py-2 text-xs"
            disabled={!!linkingId}
            aria-busy={isLinking}
            onClick={() => onLink(result)}
        >
            {isLinking ? '…' : 'Link'}
        </GradientButton>
    );
}

function ReplacementConfirmation({
    currentTitle,
    currentExternalId,
    providerName,
    result,
    linkingId,
    onConfirm,
    onCancel,
}: {
    currentTitle: string;
    currentExternalId: string;
    providerName: string;
    result: MediaProviderSearchResultDto;
    linkingId: string | null;
    onConfirm: () => void;
    onCancel: () => void;
}) {
    return (
        <div className="rounded-xl bg-warning-surface px-4 py-3 text-sm text-warning-content">
            <p className="font-semibold">Replace the current {providerName} link?</p>
            <dl className="mt-2 grid gap-1">
                <div><dt className="inline font-medium">Current:</dt> <dd className="inline">{currentTitle} ({currentExternalId})</dd></div>
                <div><dt className="inline font-medium">New:</dt> <dd className="inline">{result.title} ({result.providerMediaId})</dd></div>
            </dl>
            <p className="mt-2">This changes which {providerName} identity Cantaro uses for this title and for future synchronization.</p>
            <div className="mt-3 flex flex-wrap gap-2">
                <GradientButton
                    gradient="from-amber-500 to-orange-500"
                    onClick={onConfirm}
                    disabled={!!linkingId}
                >
                    Replace {providerName} link
                </GradientButton>
                <GradientButton tone="soft" onClick={onCancel}>Keep current link</GradientButton>
            </div>
        </div>
    );
}

function SearchResultsList({ results, query, linkingId, alreadyLinkedIds, onLink }: SearchResultsListProps) {
    if (results.length === 0) {
        return (
            <p className="py-8 text-center text-sm text-content-subtle">
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
                        className="flex items-start gap-3 rounded-2xl border border-border-subtle bg-surface-translucent p-3"
                    >
                        <SearchResultPoster posterUrl={result.posterUrl} title={result.title} />
                        <div className="min-w-0 flex-1">
                            <p className="leading-tight font-medium text-content">{result.title}</p>
                            {result.nativeTitle ? <p className="truncate text-xs text-content-muted">{result.nativeTitle}</p> : null}
                            <div className="mt-1 flex flex-wrap items-center gap-1.5">
                                <span className="rounded-full bg-accent-soft px-2 py-0.5 text-xs text-accent-strong">
                                    {mediaKindLabel(result.mediaKind)}
                                </span>
                                {metadata.map((value) => <span key={value} className="text-xs text-content-subtle">{value}</span>)}
                            </div>
                            {result.synopsis ? (
                                <SanitizedSynopsis
                                    html={result.synopsis}
                                    className="mt-1 line-clamp-2 text-xs leading-relaxed text-content-muted"
                                />
                            ) : null}
                        </div>
                        <div className="shrink-0">
                            <SearchResultAction
                                result={result}
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
export function SearchLinkDialog({ libraryEntryId, currentTitle, mediaKind, existingLinks, onClose, onLinked }: SearchLinkDialogProps) {
    const [replacement, setReplacement] = useState<MediaProviderSearchResultDto | null>(null);
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
    } = useSearchLinkDialogState({ libraryEntryId, mediaKind, onLinked }, setReplacement);
    const alreadyLinkedIds = new Set(existingLinks.filter((link) => link.provider === providerId).map((link) => link.externalId));
    const currentProviderLink = existingLinks.find((link) => link.provider === providerId);
    const providerName = providerDisplayName(providerId);

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm"
            onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}
        >
            <div className="flex max-h-[90vh] w-full max-w-2xl flex-col rounded-3xl border border-border-subtle bg-surface shadow-2xl backdrop-blur">
                <div className="flex items-center justify-between border-b border-border-subtle px-6 py-4">
                    <h2 className="text-lg font-semibold text-content">Search &amp; link a provider entry</h2>
                    <button
                        type="button"
                        className="rounded-xl px-3 py-1.5 text-sm text-content-muted transition hover:bg-surface-hover"
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
                                        ? 'bg-action text-action-content'
                                        : 'bg-surface-subtle text-content hover:bg-surface-hover'
                                        }`}
                                >
                                    <MediaProviderIcon providerId={provider.iconId} className="mr-1 inline h-4 w-4" aria-hidden /> {provider.name}
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
                            className="flex-1 rounded-xl border border-border-subtle bg-surface px-4 py-2 text-sm text-content placeholder:text-content-subtle focus:ring-2 focus:ring-focus focus:outline-none"
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

                    {searchError ? <p className="rounded-xl bg-danger-surface px-4 py-2 text-sm text-danger-content">{searchError}</p> : null}
                    {linkError ? <p className="rounded-xl bg-danger-surface px-4 py-2 text-sm text-danger-content">{linkError}</p> : null}
                    {conflict ? (
                        <SearchDialogConflictBanner
                            conflict={conflict}
                            providerName={providerName}
                            onCancel={clearConflict}
                        />
                    ) : null}
                    {replacement ? (
                        <ReplacementConfirmation
                            currentTitle={currentTitle}
                            currentExternalId={currentProviderLink?.externalId ?? ''}
                            providerName={providerName}
                            result={replacement}
                            linkingId={linkingId}
                            onConfirm={() => {
                                const selectedResult = replacement;
                                setReplacement(null);
                                void handleLink(selectedResult, true);
                            }}
                            onCancel={() => setReplacement(null)}
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
                            onLink={(result) => void handleLink(result)}
                        />
                    ) : (
                        <p className="py-8 text-center text-sm text-content-subtle">Searching…</p>
                    )}
                </div>
            </div>
        </div>
    );
}
