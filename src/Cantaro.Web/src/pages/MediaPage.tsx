import { useState, useEffect } from 'react';
import { MediaProvidersPage } from './MediaProvidersPage';
import { MediaEntryDetailPage, MediaLibraryPage } from '@cantaro/client-shared/media';

type MediaView =
  | { kind: 'providers' }
  | { kind: 'library' }
  | { kind: 'entry'; id: string };

function resolveViewFromPath(path: string): MediaView {
  const entryMatch = path.match(/^\/media\/library\/([^/]+)$/);
  if (entryMatch) return { kind: 'entry', id: decodeURIComponent(entryMatch[1]) };
  if (path === '/media/library') return { kind: 'library' };
  return { kind: 'providers' };
}

interface MediaPageProps {
  onNavigateHome: () => void;
}

export function MediaPage({ onNavigateHome }: MediaPageProps) {
  const [view, setView] = useState<MediaView>(() => resolveViewFromPath(window.location.pathname));

  useEffect(() => {
    const handlePopState = () => setView(resolveViewFromPath(window.location.pathname));
    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  const navigateMedia = (path: string, state: MediaView) => {
    window.history.pushState({}, '', path);
    setView(state);
  };

  if (view.kind === 'library') {
    return (
      <MediaLibraryPage
        onNavigateHome={onNavigateHome}
        onNavigateProviders={() => navigateMedia('/media', { kind: 'providers' })}
        onNavigateEntry={(id) => navigateMedia(`/media/library/${encodeURIComponent(id)}`, { kind: 'entry', id })}
      />
    );
  }

  if (view.kind === 'entry') {
    return (
      <MediaEntryDetailPage
        libraryEntryId={view.id}
        onNavigateBack={() => navigateMedia('/media/library', { kind: 'library' })}
      />
    );
  }

  // Default: providers view
  return (
    <MediaProvidersPage
      onNavigateHome={onNavigateHome}
      onNavigateLibrary={() => navigateMedia('/media/library', { kind: 'library' })}
    />
  );
}
