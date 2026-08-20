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
    <nav className="flex self-stretch" aria-label="Extension section">
      {sections.map((section) => (
        <button
          key={section.id}
          type="button"
          onClick={() => onSelect(section.id)}
          className={`min-h-14 border-b-2 px-3 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-focus ${section.id === activeSection
            ? 'border-personal-accent text-content'
            : 'border-transparent text-content-muted hover:bg-surface-hover hover:text-content'}`}
          aria-current={section.id === activeSection ? 'page' : undefined}
        >
          {section.label}
        </button>
      ))}
    </nav>
  );
}
