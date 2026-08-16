import type { ReactNode } from 'react';
import { MediaProviderIcon, ProviderPanel } from '@cantaro/client-shared/media';
import { ActionButton } from '@cantaro/client-shared/ui';
import { mediaProviderCatalog } from '@cantaro/client-shared/media';

interface MediaProvidersPageProps {
  onNavigateLibrary?: () => void;
  navigation?: ReactNode;
  embedded?: boolean;
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

export function MediaProvidersPage({ onNavigateLibrary, navigation, embedded = false }: MediaProvidersPageProps) {
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
          <ProviderPanel
            key={provider.id}
            providerId={provider.id}
            name={provider.name}
            icon={<MediaProviderIcon providerId={provider.iconId} className="h-12 w-12" aria-hidden />}
            description={provider.description}
          />
        ))}
      </section>
    </>
  );

  if (embedded) {
    return <div className="mx-auto max-w-5xl">{content}</div>;
  }

  return (
    <div className="app-gradient-shell relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-content">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-5xl px-6 pt-8 pb-16">
        {content}
      </div>
    </div>
  );
}
