import { Link, useCanGoBack, useNavigate, useRouterState } from '@tanstack/react-router';
import { ArrowLeft, Search, X } from 'lucide-react';
import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type FormEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type RefObject,
} from 'react';
import { createPortal } from 'react-dom';
import { SearchGroupedResults, SearchRankedResults } from './SearchResults';
import type { SearchResultGroupId, SearchResultItem, SearchResponse } from './searchApi';
import { searchGroups, searchTabs } from './searchGroups';
import { rankSearchResults } from './searchRanking';
import { searchResultDomId, trustedCanonicalRoute } from './searchRouting';
import {
  createGlobalSearchState,
  getMobileSearchCloseAction,
  getSearchResultLimit,
  getSearchSurfaceKind,
  normalizeSearchQuery,
  readSearchRouteState,
  searchMaxQueryLength,
  type SearchGroupId,
} from './searchState';
import { useSearchResults } from './useSearchResults';
import type { SearchResultsState } from './useSearchResults';
import { useMobileSearchViewport } from './useSearchViewport';

const previewListboxId = 'global-search-results';
const previewResultLimit = 8;

function useGlobalSearchState(initialDraft: string) {
  const [draft, setDraft] = useState(initialDraft);
  const [desktopOpen, setDesktopOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [panelStyle, setPanelStyle] = useState<CSSProperties>({});
  const desktopInputRef = useRef<HTMLInputElement | null>(null);
  const mobileInputRef = useRef<HTMLInputElement | null>(null);
  const desktopRootRef = useRef<HTMLDivElement | null>(null);
  const desktopPanelRef = useRef<HTMLDivElement | null>(null);
  const mobileDialogRef = useRef<HTMLDivElement | null>(null);
  const mobileResultsRef = useRef<HTMLDivElement | null>(null);
  const pendingInitialSelectionRef = useRef(false);
  return {
    activeIndex, desktopInputRef, desktopOpen, desktopPanelRef, desktopRootRef, draft,
    mobileDialogRef, mobileInputRef, mobileOpen, mobileResultsRef, panelStyle,
    pendingInitialSelectionRef, setActiveIndex, setDesktopOpen, setDraft, setMobileOpen, setPanelStyle,
  };
}

function useRouteSynchronization(
  pathname: string,
  routeQuery: string | undefined,
  isMobile: boolean,
  setDraft: (value: string) => void,
  setMobileOpen: (value: boolean) => void,
) {
  useEffect(() => {
    if (pathname === '/search') setDraft(routeQuery ?? '');
  }, [pathname, routeQuery, setDraft]);

  useEffect(() => {
    if (isMobile && pathname === '/search') setMobileOpen(true);
  }, [isMobile, pathname, setMobileOpen]);
}

function useActiveOptionSynchronization(
  activeIndex: number,
  isMobile: boolean,
  normalizedDraft: string,
  optionCount: number,
  response: SearchResultsState['response'],
  pendingInitialSelectionRef: RefObject<boolean>,
  desktopPanelRef: RefObject<HTMLDivElement | null>,
  mobileResultsRef: RefObject<HTMLDivElement | null>,
  setActiveIndex: (value: number) => void,
) {
  useEffect(() => {
    if (pendingInitialSelectionRef.current && optionCount > 0) {
      pendingInitialSelectionRef.current = false;
      setActiveIndex(0);
      return;
    }
    setActiveIndex(-1);
  }, [normalizedDraft, optionCount, pendingInitialSelectionRef, response, setActiveIndex]);

  useEffect(() => {
    const panel = isMobile ? mobileResultsRef.current : desktopPanelRef.current;
    const options = Array.from(panel?.querySelectorAll<HTMLElement>('[role="option"]') ?? []);
    options.forEach((option, index) => {
      const active = index === activeIndex;
      option.dataset.active = String(active);
      option.setAttribute('aria-selected', String(active));
      if (active) option.scrollIntoView({ block: 'nearest' });
    });
  }, [activeIndex, desktopPanelRef, isMobile, mobileResultsRef, response]);
}

function useDesktopSearchSurface(
  desktopOpen: boolean,
  isMobile: boolean,
  desktopInputRef: RefObject<HTMLInputElement | null>,
  desktopPanelRef: RefObject<HTMLDivElement | null>,
  desktopRootRef: RefObject<HTMLDivElement | null>,
  setDesktopOpen: (value: boolean) => void,
  setPanelStyle: (value: CSSProperties) => void,
) {
  useEffect(() => {
    if (!desktopOpen || isMobile) return;
    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!desktopRootRef.current?.contains(target) && !desktopPanelRef.current?.contains(target)) setDesktopOpen(false);
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [desktopOpen, desktopPanelRef, desktopRootRef, isMobile, setDesktopOpen]);

  useLayoutEffect(() => {
    if (!desktopOpen || isMobile) return;
    const updatePosition = () => {
      const rect = desktopInputRef.current?.getBoundingClientRect();
      if (rect) setPanelStyle({ top: rect.bottom + 8, left: rect.left, width: rect.width });
    };
    updatePosition();
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [desktopInputRef, desktopOpen, isMobile, setPanelStyle]);
}

function useMobileSearchDialog(
  isMobile: boolean,
  mobileOpen: boolean,
  mobileInputRef: RefObject<HTMLInputElement | null>,
) {
  useEffect(() => {
    if (!mobileOpen || !isMobile) return;
    const appRoot = document.getElementById('root');
    const previousOverflow = document.body.style.overflow;
    appRoot?.setAttribute('inert', '');
    document.body.style.overflow = 'hidden';
    window.requestAnimationFrame(() => mobileInputRef.current?.focus());
    return () => {
      appRoot?.removeAttribute('inert');
      document.body.style.overflow = previousOverflow;
    };
  }, [isMobile, mobileInputRef, mobileOpen]);
}

interface DesktopGlobalSearchProps {
  activeOptionId?: string;
  desktopInputRef: RefObject<HTMLInputElement | null>;
  desktopOpen: boolean;
  desktopPanelRef: RefObject<HTMLDivElement | null>;
  draft: string;
  isMobile: boolean;
  normalizedDraft: string;
  panelStyle: CSSProperties;
  resultContent: React.ReactNode;
  onClear: () => void;
  onClose: () => void;
  onDraft: (value: string) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLInputElement>) => void;
  onOpenDesktop: () => void;
  onOpenMobile: () => void;
  onSubmit: (event?: FormEvent) => void;
}

function DesktopGlobalSearch(props: DesktopGlobalSearchProps) {
  return <>
    <form onSubmit={props.onSubmit}>
      <SearchComboboxInput
        inputRef={props.desktopInputRef}
        helpId="global-search-help"
        mobileSurface={false}
        expanded={props.desktopOpen && Boolean(props.normalizedDraft)}
        activeOptionId={props.activeOptionId}
        draft={props.draft}
        isMobileViewport={props.isMobile}
        onDraft={props.onDraft}
        onOpenDesktop={props.onOpenDesktop}
        onOpenMobile={props.onOpenMobile}
        onClear={props.onClear}
        onKeyDown={props.onKeyDown}
      />
    </form>
    <DesktopSearchSurface
      visible={props.desktopOpen && !props.isMobile && Boolean(props.normalizedDraft)}
      panelRef={props.desktopPanelRef}
      panelStyle={props.panelStyle}
      query={props.normalizedDraft}
      onClose={props.onClose}
    >
      {props.resultContent}
    </DesktopSearchSurface>
  </>;
}

interface MobileSearchHeaderProps {
  activeOptionId?: string;
  draft: string;
  mobileInputRef: RefObject<HTMLInputElement | null>;
  onClear: () => void;
  onClose: () => void;
  onDraft: (value: string) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLInputElement>) => void;
  onOpenDesktop: () => void;
  onOpenMobile: () => void;
  onSubmit: (event?: FormEvent) => void;
}

interface MobileGlobalSearchProps {
  mobileDialogRef: RefObject<HTMLDivElement | null>;
  mobileResultsRef: RefObject<HTMLDivElement | null>;
  footer: React.ReactNode;
  header: React.ReactNode;
  resultContent: React.ReactNode;
  tabs: React.ReactNode;
  onDialogKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
}

function MobileSearchHeader({ activeOptionId, draft, mobileInputRef, onClear, onClose, onDraft, onKeyDown, onOpenDesktop, onOpenMobile, onSubmit }: MobileSearchHeaderProps) {
  return <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-border-subtle bg-canvas px-3 pt-[max(0.75rem,env(safe-area-inset-top))] pb-3">
    <button type="button" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-content focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none" aria-label="Close search" onClick={onClose}><ArrowLeft className="h-5 w-5" aria-hidden /></button>
    <form className="min-w-0 flex-1" onSubmit={onSubmit}><SearchComboboxInput inputRef={mobileInputRef} helpId="mobile-search-help" mobileSurface expanded activeOptionId={activeOptionId} draft={draft} isMobileViewport onDraft={onDraft} onOpenDesktop={onOpenDesktop} onOpenMobile={onOpenMobile} onClear={onClear} onKeyDown={onKeyDown} /></form>
  </header>;
}

function ExpandedSearchTabs({ activeGroup, normalizedDraft, visible }: { activeGroup: SearchGroupId; normalizedDraft: string; visible: boolean }) {
  return visible ? <MobileSearchTabs activeGroup={activeGroup} query={normalizedDraft} /> : null;
}

function MobileSearchFooter({ isExpandedSurface, normalizedDraft }: { isExpandedSurface: boolean; normalizedDraft: string }) {
  if (isExpandedSurface || !normalizedDraft) return null;
  return <div className="border-t border-border-subtle bg-canvas px-3 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]"><Link to="/search" search={createGlobalSearchState(normalizedDraft)} replace className="flex h-12 w-full items-center justify-center rounded-xl bg-action text-sm font-black text-action-content transition hover:bg-action-hover focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none">See all results</Link></div>;
}

function OptionalSearchSurface({ children, visible }: { children: React.ReactNode; visible: boolean }) {
  return visible ? children : null;
}

function MobileGlobalSearch({ mobileDialogRef, mobileResultsRef, footer, header, resultContent, tabs, onDialogKeyDown }: MobileGlobalSearchProps) {
  return createPortal(
    <div ref={mobileDialogRef} className="global-search-surface fixed inset-0 z-60 flex h-[100dvh] flex-col bg-canvas text-content" role="dialog" aria-modal="true" aria-label="Search Cantaro" onKeyDown={onDialogKeyDown}>
      {header}
      {tabs}
      <div ref={mobileResultsRef} className="min-h-0 flex-1 overflow-y-auto px-3 pb-[max(1rem,env(safe-area-inset-bottom))]">{resultContent}</div>
      {footer}
    </div>,
    document.body,
  );
}

function visibleResultLimit(isExpandedSurface: boolean): number | undefined {
  return isExpandedSurface ? undefined : previewResultLimit;
}

function getVisibleSearchResultLimit(isExpandedSurface: boolean, activeGroup: SearchGroupId): number {
  return isExpandedSurface ? getSearchResultLimit(activeGroup) : previewResultLimit;
}

function initialSearchDraft(pathname: string, routeQuery?: string): string {
  return pathname === '/search' ? routeQuery ?? '' : '';
}

function searchPreviewActive(desktopOpen: boolean, mobileOpen: boolean, isMobile: boolean): boolean {
  return (desktopOpen && !isMobile) || (mobileOpen && isMobile);
}

function activeSearchGroup(isExpandedSurface: boolean, routeGroup: SearchGroupId): SearchGroupId {
  return isExpandedSurface ? routeGroup : 'all';
}

function searchGroupIds(activeGroup: SearchGroupId): SearchResultGroupId[] {
  return activeGroup === 'all' ? searchGroups.map((group) => group.id) : [activeGroup];
}

function visibleSearchOptions(
  response: SearchResponse | null | undefined,
  groupIds: SearchResultGroupId[],
  normalizedDraft: string,
  maxVisibleResults?: number,
): SearchResultItem[] {
  const candidates = groupIds.length > 1
    ? rankSearchResults(response ?? null, groupIds, normalizedDraft, maxVisibleResults).map(({ item }) => item)
    : groupIds.flatMap((groupId) => response?.groups[groupId].items ?? []);
  return candidates.filter((item) => trustedCanonicalRoute(item.canonicalRoute));
}

function activeSearchOptionId(activeIndex: number, optionItems: SearchResultItem[]): string | undefined {
  const item = activeIndex >= 0 ? optionItems[activeIndex] : undefined;
  return item ? searchResultDomId(item) : undefined;
}

function searchStatusMessage(normalizedDraft: string, results: SearchResultsState): string {
  if (!normalizedDraft) return '';
  if (results.loading) return 'Searching.';
  return `${resultCount(results.response)} results available.`;
}

function resultCount(response: ReturnType<typeof useSearchResults>['response']): number {
  return response
    ? Object.values(response.groups).reduce((total, group) => total + group.items.length, 0)
    : 0;
}

function focusableElements(container: HTMLElement | null): HTMLElement[] {
  return Array.from(container?.querySelectorAll<HTMLElement>(
    'input, a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])',
  ) ?? []).filter((element) => !element.hasAttribute('inert'));
}

function trapDialogFocus(event: ReactKeyboardEvent<HTMLDivElement>, dialog: HTMLElement | null) {
  if (event.key !== 'Tab') return;
  const focusable = focusableElements(dialog);
  if (focusable.length === 0) return;
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  const boundary = event.shiftKey ? first : last;
  if (document.activeElement !== boundary) return;
  event.preventDefault();
  (event.shiftKey ? last : first).focus();
}

function MobileSearchTabs({ activeGroup, query }: { activeGroup: SearchGroupId; query: string }) {
  return (
    <nav className="overflow-x-auto px-4 py-2" aria-label="Search result groups">
      <div className="flex min-w-max gap-1 rounded-2xl bg-surface-subtle p-1">
        {searchTabs.map((tab) => (
          <Link
            key={tab.id}
            to="/search"
            search={{ q: normalizeSearchQuery(query), group: tab.id }}
            replace
            className={`rounded-xl px-3 py-2 text-sm font-black transition focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none ${
              activeGroup === tab.id ? 'bg-action text-action-content' : 'text-content-muted hover:bg-surface-hover'
            }`}
            aria-current={activeGroup === tab.id ? 'page' : undefined}
          >
            {tab.label}
          </Link>
        ))}
      </div>
    </nav>
  );
}

function SearchComboboxInput({
  inputRef,
  helpId,
  mobileSurface,
  expanded,
  activeOptionId,
  draft,
  isMobileViewport,
  onDraft,
  onOpenDesktop,
  onOpenMobile,
  onClear,
  onKeyDown,
}: {
  inputRef: RefObject<HTMLInputElement | null>;
  helpId: string;
  mobileSurface: boolean;
  expanded: boolean;
  activeOptionId?: string;
  draft: string;
  isMobileViewport: boolean;
  onDraft: (value: string) => void;
  onOpenDesktop: () => void;
  onOpenMobile: (trigger: HTMLElement) => void;
  onClear: () => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLInputElement>) => void;
}) {
  return (
    <label className="relative block">
      <span className="sr-only">Search all music and media</span>
      <span id={helpId} className="sr-only">Enter up to {searchMaxQueryLength} characters.</span>
      <Search className="pointer-events-none absolute top-1/2 left-4 h-4 w-4 -translate-y-1/2 text-content-muted" aria-hidden />
      <input
        ref={inputRef}
        className="app-top-search-input h-12 w-full rounded-2xl border border-border-subtle bg-surface-translucent pr-10 pl-11 text-base font-medium text-content transition outline-none placeholder:text-content-subtle focus:border-focus focus:bg-surface focus:ring-2 focus:ring-focus/20 md:text-sm"
        placeholder="Search music and media..."
        type="search"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={expanded}
        aria-controls={previewListboxId}
        aria-activedescendant={activeOptionId}
        aria-describedby={helpId}
        autoComplete="off"
        maxLength={searchMaxQueryLength}
        value={draft}
        onChange={(event) => {
          onDraft(event.target.value);
          if (isMobileViewport && !mobileSurface) onOpenMobile(event.currentTarget);
          else if (!isMobileViewport) onOpenDesktop();
        }}
        onClick={(event) => {
          if (mobileSurface) return;
          if (isMobileViewport) onOpenMobile(event.currentTarget);
          else onOpenDesktop();
        }}
        onFocus={() => {
          if (!isMobileViewport && !mobileSurface) onOpenDesktop();
        }}
        onKeyDown={onKeyDown}
      />
      {draft ? (
        <button
          type="button"
          className="absolute top-1/2 right-2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-xl text-content-muted hover:text-content"
          aria-label="Clear search"
          onClick={onClear}
        >
          <X className="h-4 w-4" aria-hidden />
        </button>
      ) : null}
    </label>
  );
}

function GlobalResultContent({
  query,
  results,
  groupIds,
  isMobile,
  maxResults,
  onNavigate,
}: {
  query: string;
  results: SearchResultsState;
  groupIds: SearchResultGroupId[];
  isMobile: boolean;
  maxResults?: number;
  onNavigate: (route: string) => boolean | void;
}) {
  if (!query) {
    return <div id={previewListboxId} role="listbox" className="sr-only" aria-label="Search results" />;
  }

  if (groupIds.length > 1) {
    return (
      <SearchRankedResults
        query={query}
        response={results.response}
        error={results.error}
        loading={results.loading}
        groupIds={groupIds}
        onRetry={results.retry}
        onNavigate={onNavigate}
        asListbox
        listboxId={previewListboxId}
        maxResults={maxResults}
      />
    );
  }

  return (
    <SearchGroupedResults
      query={query}
      response={results.response}
      error={results.error}
      loading={results.loading}
      groupIds={groupIds}
      compact
      onRetry={results.retry}
      onNavigate={onNavigate}
      asListbox
      listboxId={previewListboxId}
      idPrefix={isMobile ? 'mobile-global-search' : 'desktop-global-search'}
    />
  );
}

function DesktopSearchSurface({
  visible,
  panelRef,
  panelStyle,
  query,
  children,
  onClose,
}: {
  visible: boolean;
  panelRef: RefObject<HTMLDivElement | null>;
  panelStyle: CSSProperties;
  query: string;
  children: React.ReactNode;
  onClose: () => void;
}) {
  if (!visible) return null;
  return createPortal(
    <div
      ref={panelRef}
      style={panelStyle}
      className="global-search-surface fixed z-50 max-h-[min(70vh,42rem)] overflow-y-auto rounded-2xl border border-border-subtle bg-surface p-2 shadow-[0_8px_24px_rgba(15,23,42,0.16)]"
    >
      {children}
      {query ? (
        <Link
          to="/search"
          search={createGlobalSearchState(query)}
          data-search-result
          className="mt-2 flex w-full items-center justify-center rounded-xl bg-accent-soft px-4 py-3 text-sm font-black text-accent-strong focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
          onClick={onClose}
        >
          See all results
        </Link>
      ) : null}
    </div>,
    document.body,
  );
}

export function GlobalSearch() {
  const navigate = useNavigate();
  const canGoBack = useCanGoBack();
  const location = useRouterState({ select: (state) => state.location });
  const pathname = location.pathname;
  const routeState = readSearchRouteState(location.search as Record<string, unknown>);
  const isMobile = useMobileSearchViewport();
  const surfaceKind = getSearchSurfaceKind(isMobile, pathname);
  const {
    activeIndex, desktopInputRef, desktopOpen, desktopPanelRef, desktopRootRef, draft,
    mobileDialogRef, mobileInputRef, mobileOpen, mobileResultsRef, panelStyle,
    pendingInitialSelectionRef, setActiveIndex, setDesktopOpen, setDraft, setMobileOpen, setPanelStyle,
  } = useGlobalSearchState(initialSearchDraft(pathname, routeState.q));
  const normalizedDraft = normalizeSearchQuery(draft) ?? '';
  const previewActive = searchPreviewActive(desktopOpen, mobileOpen, isMobile);
  const isExpandedSurface = surfaceKind === 'mobile-expanded' && routeState.preview !== true;
  const activeGroup = activeSearchGroup(isExpandedSurface, routeState.group);
  const maxVisibleResults = visibleResultLimit(isExpandedSurface);
  const results = useSearchResults(normalizedDraft, {
    debounceMs: 220,
    enabled: previewActive,
    includeDiscovery: isExpandedSurface,
    limitPerGroup: getVisibleSearchResultLimit(isExpandedSurface, activeGroup),
  });
  const groupIds = searchGroupIds(activeGroup);
  const optionItems = visibleSearchOptions(results.response, groupIds, normalizedDraft, maxVisibleResults);
  const activeOptionId = activeSearchOptionId(activeIndex, optionItems);
  useRouteSynchronization(pathname, routeState.q, isMobile, setDraft, setMobileOpen);
  useActiveOptionSynchronization(
    activeIndex, isMobile, normalizedDraft, optionItems.length, results.response,
    pendingInitialSelectionRef, desktopPanelRef, mobileResultsRef, setActiveIndex,
  );
  useDesktopSearchSurface(
    desktopOpen, isMobile, desktopInputRef, desktopPanelRef, desktopRootRef, setDesktopOpen, setPanelStyle,
  );
  useMobileSearchDialog(isMobile, mobileOpen, mobileInputRef);

  const submit = (event?: FormEvent) => {
    event?.preventDefault();
    if (!normalizedDraft) return;
    setDesktopOpen(false);
    void navigate({
      to: '/search',
      search: createGlobalSearchState(normalizedDraft),
      replace: isMobile && pathname === '/search',
    });
  };

  const openMobile = () => {
    void navigate({
      to: '/search',
      search: {
        q: normalizeSearchQuery(draft),
        group: 'all',
        preview: true,
      },
    });
  };

  const closeMobile = () => {
    const action = getMobileSearchCloseAction(pathname, canGoBack);
    if (action === 'back') {
      window.history.back();
    } else if (action === 'fallback') {
      setMobileOpen(false);
      void navigate({ to: '/music', replace: true });
    } else {
      setMobileOpen(false);
    }
  };

  const moveActiveOption = (delta: number) => {
    if (optionItems.length === 0) return;
    setActiveIndex((current) => {
      if (current < 0) return delta > 0 ? 0 : optionItems.length - 1;
      return (current + delta + optionItems.length) % optionItems.length;
    });
  };

  const handleArrowKey = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    event.preventDefault();
    if (!isMobile && !desktopOpen) {
      setDesktopOpen(true);
      if (event.key === 'ArrowDown') {
        if (optionItems.length > 0) setActiveIndex(0);
        else pendingInitialSelectionRef.current = true;
        return;
      }
    }
    moveActiveOption(event.key === 'ArrowDown' ? 1 : -1);
  };

  const handleEnterKey = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (activeIndex < 0) return;
    const panel = isMobile ? mobileResultsRef.current : desktopPanelRef.current;
    const option = panel?.querySelectorAll<HTMLElement>('[role="option"]')[activeIndex];
    if (!option) return;
    event.preventDefault();
    option.click();
  };

  const handleComboboxKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      if (isMobile) closeMobile();
      else setDesktopOpen(false);
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') handleArrowKey(event);
    if (event.key === 'Enter') handleEnterKey(event);
  };

  const handleDialogKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeMobile();
      return;
    }
    trapDialogFocus(event, mobileDialogRef.current);
  };

  const statusMessage = searchStatusMessage(normalizedDraft, results);

  const clearDraft = () => {
    setDraft('');
    setActiveIndex(-1);
  };
  const handleResultNavigate = (route: string) => {
    setDesktopOpen(false);
    if (!isMobile) return false;

    setMobileOpen(false);
    if (routeState.preview !== true) return false;
    void navigate({ to: route as never, replace: true });
    return true;
  };
  const resultContent = (
    <>
      <GlobalResultContent
        query={normalizedDraft}
        results={results}
        groupIds={groupIds}
        isMobile={isMobile}
        maxResults={maxVisibleResults}
        onNavigate={handleResultNavigate}
      />
      <p className="sr-only" aria-live="polite">{statusMessage}</p>
    </>
  );

  return (
    <div ref={desktopRootRef} className="min-w-0 flex-1">
      <DesktopGlobalSearch activeOptionId={activeOptionId} desktopInputRef={desktopInputRef} desktopOpen={desktopOpen} desktopPanelRef={desktopPanelRef} draft={draft} isMobile={isMobile} normalizedDraft={normalizedDraft} panelStyle={panelStyle} resultContent={resultContent} onClear={clearDraft} onClose={() => setDesktopOpen(false)} onDraft={setDraft} onKeyDown={handleComboboxKeyDown} onOpenDesktop={() => setDesktopOpen(true)} onOpenMobile={openMobile} onSubmit={submit} />
      <OptionalSearchSurface visible={mobileOpen && isMobile}>
        <MobileGlobalSearch
          mobileDialogRef={mobileDialogRef}
          mobileResultsRef={mobileResultsRef}
          header={<MobileSearchHeader activeOptionId={activeOptionId} draft={draft} mobileInputRef={mobileInputRef} onClear={clearDraft} onClose={closeMobile} onDraft={setDraft} onKeyDown={handleComboboxKeyDown} onOpenDesktop={() => setDesktopOpen(true)} onOpenMobile={openMobile} onSubmit={submit} />}
          tabs={<ExpandedSearchTabs activeGroup={activeGroup} normalizedDraft={normalizedDraft} visible={isExpandedSurface} />}
          resultContent={resultContent}
          footer={<MobileSearchFooter isExpandedSurface={isExpandedSurface} normalizedDraft={normalizedDraft} />}
          onDialogKeyDown={handleDialogKeyDown}
        />
      </OptionalSearchSurface>
    </div>
  );
}
