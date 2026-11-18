import { useEffect, type ReactNode } from 'react';
import { MediaProviderIcon, mediaProviderPanelId, ProviderPanel } from '@cantaro/client-shared/media';
import { ActionButton } from '@cantaro/client-shared/ui';
import { mediaProviderCatalog } from '@cantaro/client-shared/media';

interface MediaProvidersPageProps {
  onNavigateLibrary?: () => void;
  navigation?: ReactNode;
  embedded?: boolean;
  hash?: string;
}

function MediaProvidersHeader({
  navigation,
  onNavigateLibrary,
}: {
  navigation?: ReactNode;
  onNavigateLibrary?: () => void;
}) {
  return (
    <header className="border-y border-border-subtle py-7 sm:flex sm:items-end sm:justify-between sm:gap-8">
      <div className="max-w-2xl">
        <p className="text-sm font-semibold text-personal-accent">Connections</p>
        <h1 className="mt-2 text-3xl font-black tracking-tight text-content sm:text-4xl">Media providers</h1>
        <p className="mt-3 text-sm leading-6 text-content-muted">
          Connect a tracking service to bring its library and progress into Cantaro. Your saved
          Cantaro library remains available when a provider cannot be reached.
        </p>
      </div>
      <div className="mt-5 flex shrink-0 flex-wrap items-center gap-3 sm:mt-0">
        {navigation}
        {onNavigateLibrary ? (
          <ActionButton tone="ghost" onClick={onNavigateLibrary}>View library</ActionButton>
        ) : null}
      </div>
    </header>
  );
}

export function MediaProvidersPage({ onNavigateLibrary, navigation, embedded = false, hash = '' }: MediaProvidersPageProps) {
  useEffect(() => {
    const encodedId = hash.startsWith('#') ? hash.slice(1) : hash;
    if (!encodedId) return;

    let panelId: string;
    try {
      panelId = decodeURIComponent(encodedId);
    } catch {
      return;
    }

    const provider = mediaProviderCatalog.find((item) => mediaProviderPanelId(item.id) === panelId);
    if (!provider) return;

    const target = document.getElementById(panelId);
    if (!(target instanceof HTMLElement)) return;

    target.scrollIntoView({ block: 'start', behavior: 'instant' });
    target.focus({ preventScroll: true });
  }, [hash]);

  const content = (
    <>
      {embedded && navigation ? (
        <div>{navigation}</div>
      ) : null}

      <MediaProvidersHeader
        navigation={embedded ? undefined : navigation}
        onNavigateLibrary={embedded ? undefined : onNavigateLibrary}
      />

      <section className="divide-y divide-border-subtle border-b border-border-subtle" aria-label="Media provider accounts">
        {mediaProviderCatalog.map((provider) => (
          <div
            key={provider.id}
            id={mediaProviderPanelId(provider.id)}
            tabIndex={-1}
            role="region"
            aria-label={`${provider.name} connection`}
            className="scroll-mt-6 focus-visible:outline-2 focus-visible:outline-focus"
          >
            <ProviderPanel
              providerId={provider.id}
              name={provider.name}
              icon={<MediaProviderIcon providerId={provider.iconId} className="h-12 w-12" aria-hidden />}
              description={provider.description}
            />
          </div>
        ))}
      </section>
    </>
  );

  if (embedded) {
    return <div className="mx-auto max-w-5xl">{content}</div>;
  }

  return (
    <div className="min-h-screen bg-canvas text-content">
      <div className="mx-auto max-w-5xl px-6 pt-8 pb-16">
        {content}
      </div>
    </div>
  );
}
