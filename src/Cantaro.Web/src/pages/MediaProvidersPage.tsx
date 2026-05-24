import type { ReactNode } from 'react';
import { ProviderPanel } from '@cantaro/client-shared/media';
import { GlassCard, GradientButton } from '@cantaro/client-shared/ui';
import { mediaProviderCatalog } from '@cantaro/client-shared/media';

interface MediaProvidersPageProps {
  onNavigateLibrary?: () => void;
  navigation?: ReactNode;
  embedded?: boolean;
}

export function MediaProvidersPage({ onNavigateLibrary, navigation, embedded = false }: MediaProvidersPageProps) {
  const content = (
    <>
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
            <h1 className="mt-1 text-3xl font-bold">Media providers</h1>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            {navigation}
            {onNavigateLibrary ? (
              <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onNavigateLibrary}>
                View Library
              </GradientButton>
            ) : null}
          </div>
        </header>

        <GlassCard className="p-5">
          <p className="text-sm text-gray-700">
            Connect media tracking services to import your anime and manga library into Cantaro. Once
            connected, you can import your list and track progress across providers.
          </p>
        </GlassCard>

        <section className="space-y-4" aria-label="Media provider accounts">
          {mediaProviderCatalog.map((provider) => (
            <ProviderPanel
              key={provider.id}
              providerId={provider.id}
              name={provider.name}
              icon={provider.icon}
              gradient={provider.gradient}
              description={provider.description}
            />
          ))}
        </section>
    </>
  );

  if (embedded) {
    return <div className="mx-auto max-w-3xl space-y-6">{content}</div>;
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-3xl space-y-6 px-6 pt-8 pb-16">
        {content}
      </div>
    </div>
  );
}
