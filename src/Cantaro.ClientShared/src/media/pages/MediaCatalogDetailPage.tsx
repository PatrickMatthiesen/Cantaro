import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { CheckCircle2, Plus } from 'lucide-react';
import { providerAvailabilityKey, type ProviderAvailabilityMap } from '../components/media-entry-detail/providerAvailability';
import { MediaProviderIcon } from '../components/MediaProviderIcon';
import { mediaApi, type MediaLibraryEntryDetailDto, type MediaProviderTitleDetailsDto } from '../services/mediaApi';
import { mediaProviderCatalog } from '../services/mediaProviders';
import { CharactersSection, InformationSection } from './media-entry-detail/MediaDetailSections';
import {
  DetailErrorState,
  DetailLoadingState,
  DetailPageLayout,
  MediaHero,
} from './media-entry-detail/MediaEntryDetailHero';
import { DetailTabs } from './media-entry-detail/DetailTabs';

const ADD_STATUSES = [
  { value: 'planned', label: 'Planning' },
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
] as const;

const CATALOG_TABS = [
  { id: 'overview', label: 'Overview' },
  { id: 'providers', label: 'Providers' },
  { id: 'characters', label: 'Characters' },
  { id: 'details', label: 'Details' },
] as const;

type CatalogTabId = (typeof CATALOG_TABS)[number]['id'];

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

// fallow-ignore-next-line complexity
function toCatalogEntry(details: MediaProviderTitleDetailsDto): MediaLibraryEntryDetailDto {
  const libraryEntryId = details.libraryState?.libraryEntryId ?? '';
  const mediaTitleId = details.mediaTitleId || details.libraryState?.mediaTitleId || `${details.providerId}:${details.providerMediaId}`;

  return {
    id: libraryEntryId,
    title: {
      id: mediaTitleId,
      canonicalTitle: details.title,
      originalTitle: details.nativeTitle,
      mediaKind: details.mediaKind,
      synopsis: details.synopsis,
      posterUrl: details.posterUrl,
      startYear: details.startYear,
      episodeCount: details.episodeCount,
      chapterCount: details.chapterCount,
      volumeCount: details.volumeCount,
      primaryProgressDimension: details.primaryProgressDimension,
      releaseStatusDimension: details.releaseStatusDimension,
    },
    provider: details.providerId,
    providerMediaId: details.providerMediaId,
    normalizedStatus: details.libraryState?.normalizedStatus ?? 'planned',
    progressEpisodes: details.libraryState?.progressEpisodes,
    progressChapters: details.libraryState?.progressChapters,
    progressVolumes: details.libraryState?.progressVolumes,
    isConnected: false,
    updatedAt: new Date(0).toISOString(),
    providerLinks: [{
      id: `${details.providerId}:${details.providerMediaId}`,
      provider: details.providerId,
      externalId: details.providerMediaId,
      linkSource: 'Provider catalog',
    }],
  };
}

function useOpenExistingLibraryEntry(
  libraryEntryId: string | undefined,
  onNavigateEntry: (libraryEntryId: string) => void,
) {
  useEffect(() => {
    if (libraryEntryId) onNavigateEntry(libraryEntryId);
  }, [libraryEntryId, onNavigateEntry]);
}

function CatalogAddPanel({
  selectedStatus,
  isAdding,
  error,
  onStatusChange,
  onAdd,
}: {
  selectedStatus: string;
  isAdding: boolean;
  error: string | null;
  onStatusChange: (status: string) => void;
  onAdd: () => void;
}) {
  return (
    <section className="media-detail-progress-card media-detail-add-card" aria-labelledby="add-title-heading">
      <div>
        <p className="media-detail-add-kicker">Add to your library</p>
        <h2 id="add-title-heading">Choose where to start</h2>
        <p>Planning is selected by default. You can change the status now or later.</p>
      </div>
      <label className="media-detail-add-field">
        <span>Library status</span>
        <select value={selectedStatus} onChange={(event) => onStatusChange(event.target.value)}>
          {ADD_STATUSES.map((status) => (
            <option key={status.value} value={status.value}>{status.label}</option>
          ))}
        </select>
      </label>
      <button type="button" className="media-detail-primary-action" onClick={onAdd} disabled={isAdding} aria-busy={isAdding}>
        <Plus aria-hidden />
        {isAdding ? 'Adding…' : `Add as ${ADD_STATUSES.find((status) => status.value === selectedStatus)?.label ?? 'Planning'}`}
      </button>
      {error ? <p className="media-detail-add-error" role="alert">{error}</p> : null}
    </section>
  );
}

function CatalogProviderSection({ details }: { details: MediaProviderTitleDetailsDto }) {
  const catalog = mediaProviderCatalog.find((provider) => provider.id === details.providerId);

  return (
    <section className="media-detail-section">
      <div className="media-detail-section-heading"><h3>Linked providers</h3></div>
      <div className="media-detail-provider-grid">
        <article className="media-detail-provider-card">
          {catalog ? <MediaProviderIcon providerId={catalog.iconId} aria-hidden /> : null}
          <div>
            <h4>{providerName(details.providerId)}</h4>
            <p>Catalog source</p>
            <span className="media-detail-provider-id">ID {details.providerMediaId}</span>
          </div>
          <CheckCircle2 aria-hidden className="media-detail-provider-check" />
        </article>
      </div>
    </section>
  );
}

function AvailabilitySection({ details }: { details: MediaProviderTitleDetailsDto }) {
  return (
    <section className="media-detail-section">
      <div className="media-detail-section-heading"><h3>Where to watch</h3></div>
      {details.availabilityLinks.length === 0 ? (
        <div className="media-detail-episode-empty">
          <p>No verified destinations yet</p>
          <span>Cantaro will show provider destinations here when they become available.</span>
        </div>
      ) : (
        <div className="media-detail-availability-list">
          {details.availabilityLinks.map((link) => link.url ? (
            <a key={`${link.serviceId}:${link.url}`} href={link.url} target="_blank" rel="noreferrer">
              {link.displayName}
            </a>
          ) : (
            <span key={`${link.serviceId}:${link.displayName}`}>{link.displayName}</span>
          ))}
        </div>
      )}
    </section>
  );
}

function CatalogPanel({
  activeTab,
  details,
  entry,
  availability,
}: {
  activeTab: CatalogTabId;
  details: MediaProviderTitleDetailsDto;
  entry: MediaLibraryEntryDetailDto;
  availability: ProviderAvailabilityMap;
}) {
  const panels: Record<CatalogTabId, ReactNode> = {
    overview: (
      <div role="tabpanel" id="media-catalog-panel-overview" aria-labelledby="media-catalog-tab-overview">
        <AvailabilitySection details={details} />
        <CharactersSection entry={entry} availabilityByProviderLink={availability} />
        <div className="media-detail-overview-meta-grid"><InformationSection entry={entry} /></div>
      </div>
    ),
    providers: (
      <div role="tabpanel" id="media-catalog-panel-providers" aria-labelledby="media-catalog-tab-providers">
        <CatalogProviderSection details={details} />
      </div>
    ),
    characters: (
      <div role="tabpanel" id="media-catalog-panel-characters" aria-labelledby="media-catalog-tab-characters">
        <CharactersSection entry={entry} availabilityByProviderLink={availability} />
      </div>
    ),
    details: (
      <div role="tabpanel" id="media-catalog-panel-details" aria-labelledby="media-catalog-tab-details">
        <InformationSection entry={entry} />
      </div>
    ),
  };

  return panels[activeTab];
}

function CatalogDetailContent({
  details,
  embedded,
  onNavigateBack,
  addState,
}: {
  details: MediaProviderTitleDetailsDto;
  embedded: boolean;
  onNavigateBack: () => void;
  addState: ReturnType<typeof useAddCatalogTitle>;
}) {
  const [activeTab, setActiveTab] = useState<CatalogTabId>('overview');
  const entry = useMemo(() => toCatalogEntry(details), [details]);
  const availability = useMemo<ProviderAvailabilityMap>(() => ({
    [providerAvailabilityKey(details.providerId, details.providerMediaId)]: {
      status: 'loaded',
      links: details.availabilityLinks,
      characters: details.characters,
    },
  }), [details]);

  return (
    <DetailPageLayout embedded={embedded}>
      <div className="media-detail-layout">
        <section className="media-detail-top-area">
          <MediaHero
            entry={entry}
            onNavigateBack={onNavigateBack}
            membershipLabel="Not in your library"
          />
          <div className="media-detail-desktop-progress">
            <CatalogAddPanel
              selectedStatus={addState.selectedStatus}
              isAdding={addState.isAdding}
              error={addState.addError}
              onStatusChange={addState.setSelectedStatus}
              onAdd={() => void addState.handleAdd()}
            />
          </div>
          <div className="media-detail-mobile-progress">
            <CatalogAddPanel
              selectedStatus={addState.selectedStatus}
              isAdding={addState.isAdding}
              error={addState.addError}
              onStatusChange={addState.setSelectedStatus}
              onAdd={() => void addState.handleAdd()}
            />
          </div>
        </section>
        <div className="media-detail-main-column">
          <section className="media-detail-overview-card">
            <DetailTabs tabs={CATALOG_TABS} activeTab={activeTab} idPrefix="media-catalog" onChange={setActiveTab} />
            <CatalogPanel activeTab={activeTab} details={details} entry={entry} availability={availability} />
          </section>
        </div>
      </div>
    </DetailPageLayout>
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
  const existingLibraryEntryId = details?.libraryState?.libraryEntryId;

  useOpenExistingLibraryEntry(existingLibraryEntryId, onNavigateEntry);

  if (isLoading || existingLibraryEntryId) return <DetailLoadingState embedded={embedded} />;
  if (error || !details) {
    return <DetailErrorState error={error} embedded={embedded} onNavigateBack={onNavigateBack} onRetry={loadDetails} />;
  }

  return <CatalogDetailContent details={details} embedded={embedded} onNavigateBack={onNavigateBack} addState={addState} />;
}
