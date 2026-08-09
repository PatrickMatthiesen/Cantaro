import type { PrimaryAppSection } from './extensionAppTypes';

interface AppNavigationProps {
  activeSection?: PrimaryAppSection;
  onSelect: (section: PrimaryAppSection) => void;
}

const sections: ReadonlyArray<{ id: PrimaryAppSection; label: string }> = [
  { id: 'music', label: 'Music' },
  { id: 'media', label: 'Media' },
];

export function AppNavigation({ activeSection, onSelect }: AppNavigationProps) {
  return (
    <nav className="flex rounded-xl bg-slate-950 p-0.5" aria-label="Extension section">
      {sections.map((section) => (
        <button
          key={section.id}
          type="button"
          onClick={() => onSelect(section.id)}
          className={`min-h-9 rounded-[0.625rem] px-3 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${section.id === activeSection
            ? 'bg-surface text-content'
            : 'text-slate-300 hover:bg-white/10 hover:text-white'}`}
          aria-current={section.id === activeSection ? 'page' : undefined}
        >
          {section.label}
        </button>
      ))}
    </nav>
  );
}
