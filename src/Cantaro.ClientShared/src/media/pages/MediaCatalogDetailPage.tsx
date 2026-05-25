import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { GlassCard, GradientButton } from '../../ui';
import { SanitizedSynopsis } from '../components/media-entry-detail/EntryDisplayPrimitives';
import { mediaApi, type MediaProviderTitleDetailsDto } from '../services/mediaApi';
import { mediaKindLabel } from '../services/mediaFormatting';
import { mediaProviderCatalog } from '../services/mediaProviders';

const ADD_STATUSES = [
  { value: 'planned', label: 'Planning' },
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
];

interface MediaCatalogDetailPageProps {
  providerId: string;
  providerMediaId: string;
  onNavigateBack: () => void;
  onNavigateEntry: (libraryEntryId: string) => void;
  embedded?: boolean;
}

function providerName(providerId: string): string {
  return mediaProviderCatalog.find((provider) => provider.id === providerId)?.name ?? providerId;
}

function DetailLayout({ children, embedded }: { children: ReactNode; embedded: boolean }) {
  if (embedded) {
    return <div className="mx-auto max-w-5xl space-y-5">{children}</div>;
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 px-6 py-8 text-gray-900">
      <div className="relative z-10 mx-auto max-w-5xl space-y-5">{children}</div>
    </div>
  );
}

function catalogMetadata(details: MediaProviderTitleDetailsDto): string[] {
  return [
    mediaKindLabel(details.mediaKind),
    details.startYear ? String(details.startYear) : null,
    details.episodeCount ? `${details.episodeCount} episodes` : null,
    details.chapterCount ? `${details.chapterCount} chapters` : null,
    details.volumeCount ? `${details.volumeCount} volumes` : null,
  ].filter((value): value is string => Boolean(value));
}

function CatalogPoster({ details }: { details: MediaProviderTitleDetailsDto }) {
  if (!details.posterUrl) {
    return (
      <div className="flex h-full min-h-96 items-center justify-center text-sm font-semibold text-gray-400">
        No artwork
      </div>
    );
  }

  return <img src={details.posterUrl} alt={`${details.title} cover art`} className="h-full w-full object-cover" />;
}

function CatalogLibraryBadge({ isInLibrary }: { isInLibrary: boolean }) {
  return (
    <span className={`rounded-full px-3 py-1 text-xs font-semibold ${isInLibrary ? 'bg-emerald-100 text-emerald-800' : 'bg-amber-100 text-amber-800'}`}>
      {isInLibrary ? 'In your library' : 'Not in your library'}
    </span>
  );
}

function CatalogHeroHeader({ details, providerId }: { details: MediaProviderTitleDetailsDto; providerId: string }) {
  return (
    <div className="mb-auto flex flex-wrap gap-2">
      <span className="rounded-full bg-gray-950 px-3 py-1 text-xs font-semibold text-white">
        {providerName(providerId)}
      </span>
      <CatalogLibraryBadge isInLibrary={Boolean(details.libraryState?.isInLibrary)} />
    </div>
  );
}

function CatalogHeroCopy({ details }: { details: MediaProviderTitleDetailsDto }) {
  const metadata = catalogMetadata(details);

  return (
    <>
      <h1 className="mt-8 text-4xl leading-tight font-black text-gray-950">{details.title}</h1>
      {details.nativeTitle ? <p className="mt-2 text-sm font-medium text-gray-500">{details.nativeTitle}</p> : null}
      {metadata.length > 0 ? <p className="mt-4 text-sm font-semibold text-gray-500">{metadata.join(' / ')}</p> : null}
      {details.synopsis ? (
        <SanitizedSynopsis html={details.synopsis} className="mt-5 line-clamp-5 text-sm leading-7 text-gray-600" />
      ) : null}
    </>
  );
}

function CatalogHero({ details, providerId }: { details: MediaProviderTitleDetailsDto; providerId: string }) {
  return (
    <GlassCard className="overflow-hidden p-0">
      <div className="grid gap-0 lg:grid-cols-[18rem_1fr]">
        <div className="min-h-96 bg-gray-100"><CatalogPoster details={details} /></div>
        <div className="flex flex-col justify-end p-6">
          <CatalogHeroHeader details={details} providerId={providerId} />
          <CatalogHeroCopy details={details} />
        </div>
      </div>
    </GlassCard>
  );
}

function ExistingLibraryAction({
  libraryEntryId,
  status,
  onNavigateEntry,
}: {
  libraryEntryId: string;
  status?: string;
  onNavigateEntry: (libraryEntryId: string) => void;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div>
        <p className="text-sm font-semibold text-gray-900">This title is already in your library.</p>
        {status ? <p className="mt-1 text-xs text-gray-500">Status: {status}</p> : null}
      </div>
      <GradientButton gradient="from-indigo-500 to-purple-500" onClick={() => onNavigateEntry(libraryEntryId)}>
        Open library entry
      </GradientButton>
    </div>
  );
}

function AddLibraryAction({
  selectedStatus,
  isAdding,
  onStatusChange,
  onAdd,
}: {
  selectedStatus: string;
  isAdding: boolean;
  onStatusChange: (status: string) => void;
  onAdd: () => void;
}) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1">
        <label className="text-xs font-medium tracking-wide text-gray-500 uppercase">Add as</label>
        <select
          className="rounded-xl border border-gray-200 bg-white/80 px-4 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-indigo-400 focus:outline-none"
          value={selectedStatus}
          onChange={(event) => onStatusChange(event.target.value)}
        >
          {ADD_STATUSES.map((status) => (
            <option key={status.value} value={status.value}>{status.label}</option>
          ))}
        </select>
      </div>
      <GradientButton gradient="from-indigo-500 to-purple-500" onClick={onAdd} disabled={isAdding} aria-busy={isAdding}>
        {isAdding ? 'Adding...' : 'Add to library'}
      </GradientButton>
    </div>
  );
}

function LibraryActionCard({
  details,
  selectedStatus,
  isAdding,
  message,
  onStatusChange,
  onAdd,
  onNavigateEntry,
}: {
  details: MediaProviderTitleDetailsDto;
  selectedStatus: string;
  isAdding: boolean;
  message: string | null;
  onStatusChange: (status: string) => void;
  onAdd: () => void;
  onNavigateEntry: (libraryEntryId: string) => void;
}) {
  const libraryEntryId = details.libraryState?.libraryEntryId;

  return (
    <GlassCard className="p-5">
      {libraryEntryId ? (
        <ExistingLibraryAction
          libraryEntryId={libraryEntryId}
          status={details.libraryState?.normalizedStatus}
          onNavigateEntry={onNavigateEntry}
        />
      ) : (
        <AddLibraryAction
          selectedStatus={selectedStatus}
          isAdding={isAdding}
          onStatusChange={onStatusChange}
          onAdd={onAdd}
        />
      )}
      {message ? <p className="mt-3 text-sm text-rose-700">{message}</p> : null}
    </GlassCard>
  );
}

function AvailabilityCard({ details }: { details: MediaProviderTitleDetailsDto }) {
  if (!details.availabilityLinks || details.availabilityLinks.length === 0) {
    return null;
  }

  return (
    <GlassCard className="p-5">
      <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Availability</h2>
      <div className="mt-3 flex flex-wrap gap-2">
        {details.availabilityLinks.map((link) => (
          <a
            key={`${link.serviceId}:${link.url ?? link.displayName}`}
            href={link.url}
            target="_blank"
            rel="noreferrer"
            className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm font-semibold text-gray-700 transition hover:bg-white"
          >
            {link.displayName}
          </a>
        ))}
      </div>
    </GlassCard>
  );
}

function useCatalogDetails(providerId: string, providerMediaId: string) {
  const [details, setDetails] = useState<MediaProviderTitleDetailsDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadDetails = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      setDetails(await mediaApi.getTitleDetails(providerId, providerMediaId));
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Failed to load media');
    } finally {
      setIsLoading(false);
    }
  }, [providerId, providerMediaId]);

  useEffect(() => {
    void loadDetails();
  }, [loadDetails]);

  return { details, isLoading, error, loadDetails };
}

function useAddCatalogTitle(
  providerId: string,
  providerMediaId: string,
  onNavigateEntry: (libraryEntryId: string) => void,
) {
  const [selectedStatus, setSelectedStatus] = useState('planned');
  const [isAdding, setIsAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  const handleAdd = useCallback(async () => {
    setIsAdding(true);
    setAddError(null);

    try {
      const result = await mediaApi.addProviderTitleToLibrary(providerId, providerMediaId, { status: selectedStatus });
      onNavigateEntry(result.libraryEntryId);
    } catch (addFailure) {
      setAddError(addFailure instanceof Error ? addFailure.message : 'Failed to add to library');
    } finally {
      setIsAdding(false);
    }
  }, [onNavigateEntry, providerId, providerMediaId, selectedStatus]);

  return { selectedStatus, setSelectedStatus, isAdding, addError, handleAdd };
}

function CatalogLoadingState({ embedded }: { embedded: boolean }) {
  return (
    <DetailLayout embedded={embedded}>
      <GlassCard className="h-96 animate-pulse" />
    </DetailLayout>
  );
}

function CatalogErrorState({
  embedded,
  error,
  onNavigateBack,
  onRetry,
}: {
  embedded: boolean;
  error: string | null;
  onNavigateBack: () => void;
  onRetry: () => void;
}) {
  return (
    <DetailLayout embedded={embedded}>
      <GradientButton tone="soft" onClick={onNavigateBack}>Back</GradientButton>
      <GlassCard className="p-6">
        <p className="text-rose-700">{error ?? 'Media not found'}</p>
        <div className="mt-3">
          <GradientButton tone="soft" onClick={onRetry}>Retry</GradientButton>
        </div>
      </GlassCard>
    </DetailLayout>
  );
}

export function MediaCatalogDetailPage({
  providerId,
  providerMediaId,
  onNavigateBack,
  onNavigateEntry,
  embedded = false,
}: MediaCatalogDetailPageProps) {
  const { details, isLoading, error, loadDetails } = useCatalogDetails(providerId, providerMediaId);
  const addState = useAddCatalogTitle(providerId, providerMediaId, onNavigateEntry);

  if (isLoading) {
    return <CatalogLoadingState embedded={embedded} />;
  }

  if (error || !details) {
    return <CatalogErrorState embedded={embedded} error={error} onNavigateBack={onNavigateBack} onRetry={() => void loadDetails()} />;
  }

  return (
    <DetailLayout embedded={embedded}>
      <GradientButton tone="soft" onClick={onNavigateBack}>Back to search</GradientButton>
      <CatalogHero details={details} providerId={providerId} />
      <LibraryActionCard
        details={details}
        selectedStatus={addState.selectedStatus}
        isAdding={addState.isAdding}
        message={addState.addError}
        onStatusChange={addState.setSelectedStatus}
        onAdd={() => void addState.handleAdd()}
        onNavigateEntry={onNavigateEntry}
      />
      <AvailabilityCard details={details} />
    </DetailLayout>
  );
}
