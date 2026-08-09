import { AppNavigation } from './AppNavigation';
import type { AppSection, PrimaryAppSection } from './extensionAppTypes';

interface PopupHeaderProps {
  section: AppSection;
  configured: boolean;
  loading: boolean;
  onSelectSection: (section: PrimaryAppSection) => void;
  onOpenSettings: () => void;
}

export function PopupHeader({ section, configured, loading, onSelectSection, onOpenSettings }: PopupHeaderProps) {
  const primarySection = section === 'settings' ? undefined : section;

  return (
    <header className="flex min-h-12 items-center gap-2 rounded-xl border border-border-subtle bg-surface p-1.5">
      <AppNavigation activeSection={primarySection} onSelect={onSelectSection} />
      <span className="min-w-0 flex-1 truncate pl-1 text-sm font-semibold text-content">Cantaro</span>
      {!loading && !configured ? (
        <span className="rounded-lg bg-warning-surface px-2 py-1 text-xs font-semibold text-warning-content">
          Setup needed
        </span>
      ) : null}
      <button
        type="button"
        className={`inline-flex size-9 items-center justify-center rounded-xl transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${section === 'settings'
          ? 'bg-accent-soft text-accent-strong'
          : 'text-content-muted hover:bg-surface-hover'}`}
        onClick={onOpenSettings}
        aria-label="Open extension settings"
        aria-current={section === 'settings' ? 'page' : undefined}
        title="Settings"
      >
        <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
          <path strokeLinecap="round" strokeLinejoin="round" d="M9.6 3.7 10 2h4l.4 1.7a8.6 8.6 0 0 1 1.5.9l1.7-.5 2 3.5-1.3 1.2c.1.5.2 1.1.2 1.7s-.1 1.2-.2 1.7l1.3 1.2-2 3.5-1.7-.5a8.6 8.6 0 0 1-1.5.9L14 19h-4l-.4-1.7a8.6 8.6 0 0 1-1.5-.9l-1.7.5-2-3.5 1.3-1.2a7.8 7.8 0 0 1 0-3.4L4.4 7.6l2-3.5 1.7.5a8.6 8.6 0 0 1 1.5-.9Z" />
          <circle cx="12" cy="10.5" r="2.5" />
        </svg>
      </button>
    </header>
  );
}
