export interface DetailTab<T extends string> {
  id: T;
  label: string;
}

function moveTabFocus(event: KeyboardEvent<HTMLButtonElement>) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  const tabs = Array.from(
    event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>(
      '[role="tab"]',
    ) ?? [],
  );
  if (tabs.length === 0) return;
  event.preventDefault();
  const currentIndex = tabs.indexOf(event.currentTarget);
  const nextIndex = getNextTabIndex(event.key, currentIndex, tabs.length);
  tabs[nextIndex]?.focus();
  tabs[nextIndex]?.click();
}

function getNextTabIndex(key: string, currentIndex: number, tabCount: number) {
  if (key === "Home") return 0;
  if (key === "End") return tabCount - 1;
  const direction = key === "ArrowRight" ? 1 : -1;
  return (currentIndex + direction + tabCount) % tabCount;
}

export function DetailTabs<T extends string>({
  tabs,
  activeTab,
  idPrefix,
  onChange,
}: {
  tabs: readonly DetailTab<T>[];
  activeTab: T;
  idPrefix: string;
  onChange: (tab: T) => void;
}) {
  return (
    <nav
      className="flex min-w-0 gap-6 overflow-x-auto border-b border-border-subtle px-4 sm:px-7 xl:px-9"
      aria-label="Media detail sections"
      role="tablist"
    >
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          id={`${idPrefix}-tab-${tab.id}`}
          aria-controls={`${idPrefix}-panel-${tab.id}`}
          aria-selected={activeTab === tab.id}
          tabIndex={activeTab === tab.id ? 0 : -1}
          className={`relative min-h-14 shrink-0 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-focus ${activeTab === tab.id ? "text-content after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:bg-personal-accent" : "text-content-muted hover:text-content"}`}
          onClick={() => onChange(tab.id)}
          onKeyDown={moveTabFocus}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}
import type { KeyboardEvent } from "react";
