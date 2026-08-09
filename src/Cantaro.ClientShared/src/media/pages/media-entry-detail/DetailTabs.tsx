export interface DetailTab<T extends string> {
  id: T;
  label: string;
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
    <nav className="media-detail-tabs" aria-label="Media detail sections" role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          id={`${idPrefix}-tab-${tab.id}`}
          aria-controls={`${idPrefix}-panel-${tab.id}`}
          aria-selected={activeTab === tab.id}
          className={activeTab === tab.id ? 'is-active' : undefined}
          onClick={() => onChange(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}
