import { GradientButton } from '@cantaro/client-shared/ui';

export type AppSection = 'home' | 'music' | 'media';

interface AppNavigationProps {
  currentSection: AppSection;
  onNavigate: (target: AppSection) => void;
}

const navigationItems: Array<{ id: AppSection; label: string }> = [
  { id: 'home', label: 'Home' },
  { id: 'music', label: 'Music' },
  { id: 'media', label: 'Media' },
];

export function AppNavigation({ currentSection, onNavigate }: AppNavigationProps) {
  return (
    <nav
      className="flex flex-wrap items-center gap-2 rounded-2xl border border-white/80 bg-white/55 p-1.5 shadow-[0_8px_24px_rgba(31,41,55,0.06)] backdrop-blur"
      aria-label="Primary navigation"
    >
      {navigationItems.map((item) => (
        <GradientButton
          key={item.id}
          type="button"
          tone={item.id === currentSection ? 'dark' : 'soft'}
          className="min-w-24 px-4 py-2"
          aria-current={item.id === currentSection ? 'page' : undefined}
          onClick={() => onNavigate(item.id)}
        >
          {item.label}
        </GradientButton>
      ))}
    </nav>
  );
}
