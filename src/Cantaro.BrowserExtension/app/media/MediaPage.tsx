import {
  MediaEntryDetailPage,
  MediaLibraryPage,
} from '@cantaro/client-shared/media';
import { startTransition, useState } from 'react';
import type { ActiveTabContextState } from '../shell/extensionAppTypes';
import { EpisodeTrackingControls } from './EpisodeTrackingControls';
import { MediaSetup } from './MediaSetup';
import { useEpisodeTracking } from './useEpisodeTracking';
import './mediaDetail.css';

type MediaRoute = { kind: 'library' } | { kind: 'title'; id: string };

interface MediaPageProps {
  configured: boolean;
  isSigningIn: boolean;
  activeTabContext: ActiveTabContextState;
  onSignIn: () => Promise<boolean>;
  onOpenSettings: () => void;
  onNotice: (message: string, tone: 'success' | 'error') => void;
}

const suppressEmbeddedHeading = () => undefined;

export function MediaPage(props: MediaPageProps) {
  const [route, setRoute] = useState<MediaRoute>({ kind: 'library' });
  const tracking = useEpisodeTracking((message) => props.onNotice(message, 'success'));

  if (!props.configured) {
    return (
      <MediaSetup
        isSigningIn={props.isSigningIn}
        onSignIn={props.onSignIn}
        onOpenSettings={props.onOpenSettings}
      />
    );
  }

  return (
    <section className="cantaro-extension-media flex min-h-full flex-col" aria-label="Media">
      <EpisodeTrackingControls
        paused={tracking.paused}
        pausedUntil={tracking.pausedUntil}
        onPause={tracking.pause}
        onResume={tracking.resume}
      />
      <div className="min-h-0 flex-1 pb-3">
        {route.kind === 'library' ? (
          <MediaLibraryPage
            embedded
            density="compact"
            onHeadingChange={suppressEmbeddedHeading}
            onNavigateEntry={(id) => startTransition(() => setRoute({ kind: 'title', id }))}
          />
        ) : (
          <MediaEntryDetailPage
            mediaTitleId={route.id}
            embedded
            onNavigateBack={() => startTransition(() => setRoute({ kind: 'library' }))}
            onNavigateTitle={(id) => startTransition(() => setRoute({ kind: 'title', id }))}
          />
        )}
      </div>
    </section>
  );
}
