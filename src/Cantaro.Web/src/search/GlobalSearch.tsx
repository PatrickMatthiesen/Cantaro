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
import type { SearchResultGroupId } from './searchApi';
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
      <div className="bg-soft flex min-w-max gap-1 rounded-2xl p-1">
        {searchTabs.map((tab) => (
          <Link
            key={tab.id}
            to="/search"
            search={{ q: normalizeSearchQuery(query), group: tab.id }}
            replace
            className={`rounded-xl px-3 py-2 text-sm font-black ${
              activeGroup === tab.id ? 'bg-action text-action-content' : 'text-muted'
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
      <Search className="text-muted pointer-events-none absolute top-1/2 left-4 h-4 w-4 -translate-y-1/2" aria-hidden />
      <input
        ref={inputRef}
        className="app-top-search-input h-12 w-full rounded-2xl border border-[#e3def8] bg-white/70 pr-10 pl-11 text-base font-medium text-slate-800 transition outline-none placeholder:text-slate-400 focus:border-violet-300 focus:bg-white md:text-sm"
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
          className="text-muted hover:text-ink absolute top-1/2 right-2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-xl"
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
  onNavigate,
}: {
  query: string;
  results: SearchResultsState;
  groupIds: SearchResultGroupId[];
  isMobile: boolean;
  onNavigate: (route: string) => boolean | void;
}) {
  if (!query) {
    return (
      <div id={previewListboxId} role="listbox" className="text-muted px-4 py-8 text-center text-sm font-semibold">
        Start typing to search songs, artists, playlists, and media.
      </div>
    );
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
      className="global-search-surface bg-panel-solid border-line fixed z-50 max-h-[min(70vh,42rem)] overflow-y-auto rounded-2xl border p-2 shadow-[0_8px_24px_rgba(15,23,42,0.16)]"
    >
      {children}
      {query ? (
        <Link
          to="/search"
          search={createGlobalSearchState(query)}
          data-search-result
          className="bg-soft text-accent-content mt-2 flex w-full items-center justify-center rounded-xl px-4 py-3 text-sm font-black focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
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
  const [draft, setDraft] = useState(pathname === '/search' ? routeState.q ?? '' : '');
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
  const normalizedDraft = normalizeSearchQuery(draft) ?? '';
  const previewActive = (desktopOpen && !isMobile) || (mobileOpen && isMobile);
  const isExpandedSurface = surfaceKind === 'mobile-expanded' && routeState.preview !== true;
  const activeGroup = isExpandedSurface ? routeState.group : 'all';
  const results = useSearchResults(normalizedDraft, {
    debounceMs: 220,
    enabled: previewActive,
    includeDiscovery: isExpandedSurface,
    limitPerGroup: isExpandedSurface ? getSearchResultLimit(activeGroup) : isMobile ? 4 : 3,
  });
  const groupIds = activeGroup === 'all'
    ? searchGroups.map((group) => group.id)
    : [activeGroup];
  const optionItems = (groupIds.length > 1
    ? rankSearchResults(results.response, groupIds, normalizedDraft).map(({ item }) => item)
    : groupIds.flatMap((groupId) => results.response?.groups[groupId].items ?? []))
    .filter((item) => trustedCanonicalRoute(item.canonicalRoute));
  const activeOptionId = activeIndex >= 0 && optionItems[activeIndex]
    ? searchResultDomId(optionItems[activeIndex])
    : undefined;
  useEffect(() => {
    if (pathname === '/search') setDraft(routeState.q ?? '');
  }, [pathname, routeState.q]);

  useEffect(() => {
    if (isMobile && pathname === '/search') setMobileOpen(true);
  }, [isMobile, pathname]);

  useEffect(() => {
    if (pendingInitialSelectionRef.current && optionItems.length > 0) {
      pendingInitialSelectionRef.current = false;
      setActiveIndex(0);
      return;
    }
    setActiveIndex(-1);
  }, [normalizedDraft, optionItems.length, results.response]);

  useEffect(() => {
    const panel = isMobile ? mobileResultsRef.current : desktopPanelRef.current;
    const options = Array.from(panel?.querySelectorAll<HTMLElement>('[role="option"]') ?? []);
    options.forEach((option, index) => {
      const active = index === activeIndex;
      option.dataset.active = String(active);
      option.setAttribute('aria-selected', String(active));
      if (active) option.scrollIntoView({ block: 'nearest' });
    });
  }, [activeIndex, isMobile, results.response]);

  useEffect(() => {
    if (!desktopOpen || isMobile) return;
    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!desktopRootRef.current?.contains(target) && !desktopPanelRef.current?.contains(target)) {
        setDesktopOpen(false);
      }
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [desktopOpen, isMobile]);

  useLayoutEffect(() => {
    if (!desktopOpen || isMobile) return;
    const updatePosition = () => {
      const rect = desktopInputRef.current?.getBoundingClientRect();
      if (!rect) return;
      setPanelStyle({
        top: rect.bottom + 8,
        left: rect.left,
        width: rect.width,
      });
    };
    updatePosition();
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [desktopOpen, isMobile]);

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
  }, [isMobile, mobileOpen]);

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

  const handleComboboxKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case 'Escape':
        event.preventDefault();
        if (isMobile) closeMobile();
        else setDesktopOpen(false);
        break;
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();
        if (!isMobile && !desktopOpen) {
          setDesktopOpen(true);
          if (event.key === 'ArrowDown') {
            if (optionItems.length > 0) setActiveIndex(0);
            else pendingInitialSelectionRef.current = true;
            break;
          }
        }
        moveActiveOption(event.key === 'ArrowDown' ? 1 : -1);
        break;
      case 'Enter': {
        if (activeIndex < 0) return;
        const panel = isMobile ? mobileResultsRef.current : desktopPanelRef.current;
        const option = panel?.querySelectorAll<HTMLElement>('[role="option"]')[activeIndex];
        if (!option) return;
        event.preventDefault();
        option.click();
        break;
      }
    }
  };

  const handleDialogKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeMobile();
      return;
    }
    trapDialogFocus(event, mobileDialogRef.current);
  };

  const statusMessage = !normalizedDraft
    ? 'Start typing to search.'
    : results.loading
      ? 'Searching.'
      : `${resultCount(results.response)} results available.`;

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
        onNavigate={handleResultNavigate}
      />
      <p className="sr-only" aria-live="polite">{statusMessage}</p>
    </>
  );

  return (
    <div ref={desktopRootRef} className="min-w-0 flex-1">
      <form onSubmit={submit}>
        <SearchComboboxInput
          inputRef={desktopInputRef}
          helpId="global-search-help"
          mobileSurface={false}
          expanded={desktopOpen}
          activeOptionId={activeOptionId}
          draft={draft}
          isMobileViewport={isMobile}
          onDraft={setDraft}
          onOpenDesktop={() => setDesktopOpen(true)}
          onOpenMobile={openMobile}
          onClear={clearDraft}
          onKeyDown={handleComboboxKeyDown}
        />
      </form>

      <DesktopSearchSurface
        visible={desktopOpen && !isMobile}
        panelRef={desktopPanelRef}
        panelStyle={panelStyle}
        query={normalizedDraft}
        onClose={() => setDesktopOpen(false)}
      >
        {resultContent}
      </DesktopSearchSurface>

      {mobileOpen && isMobile ? createPortal(
        <div
          ref={mobileDialogRef}
          className="global-search-surface bg-canvas text-ink fixed inset-0 z-60 flex h-[100dvh] flex-col"
          role="dialog"
          aria-modal="true"
          aria-label="Search Cantaro"
          onKeyDown={handleDialogKeyDown}
        >
          <header className="border-line bg-canvas sticky top-0 z-10 flex items-center gap-2 border-b px-3 pt-[max(0.75rem,env(safe-area-inset-top))] pb-3">
            <button
              type="button"
              className="text-ink flex h-11 w-11 shrink-0 items-center justify-center rounded-xl focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
              aria-label="Close search"
              onClick={closeMobile}
            >
              <ArrowLeft className="h-5 w-5" aria-hidden />
            </button>
            <form className="min-w-0 flex-1" onSubmit={submit}>
              <SearchComboboxInput
                inputRef={mobileInputRef}
                helpId="mobile-search-help"
                mobileSurface
                expanded={mobileOpen}
                activeOptionId={activeOptionId}
                draft={draft}
                isMobileViewport={isMobile}
                onDraft={setDraft}
                onOpenDesktop={() => setDesktopOpen(true)}
                onOpenMobile={openMobile}
                onClear={clearDraft}
                onKeyDown={handleComboboxKeyDown}
              />
            </form>
          </header>
          {isExpandedSurface ? <MobileSearchTabs activeGroup={activeGroup} query={normalizedDraft} /> : null}
          <div ref={mobileResultsRef} className="min-h-0 flex-1 overflow-y-auto px-3 pb-[max(1rem,env(safe-area-inset-bottom))]">
            {resultContent}
          </div>
          {!isExpandedSurface && normalizedDraft ? (
            <div className="border-line bg-canvas border-t px-3 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]">
              <Link
                to="/search"
                search={createGlobalSearchState(normalizedDraft)}
                replace
                className="bg-action text-action-content flex h-12 w-full items-center justify-center rounded-xl text-sm font-black"
              >
                See all results
              </Link>
            </div>
          ) : null}
        </div>,
        document.body,
      ) : null}
    </div>
  );
}
