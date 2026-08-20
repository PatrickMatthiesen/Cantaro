import { useState, type ReactNode } from "react";
import { Snackbar } from "../../../ui";
import {
  ActionRail,
  AddToLibraryActions,
  ProgressCockpit,
} from "./MediaEntryDetailActions";
import {
  resolveStreamingDestinations,
  type MediaStreamingDestinations,
} from "../../services/streamingDestinations";
import { useStreamingServicePreference } from "../../services/streamingServicePreference";
import type { StreamingServiceId } from "../../services/streamingServices";
import {
  DetailErrorState,
  DetailLoadingState,
  DetailPageLayout,
  EntryLinkDialog,
  MediaHero,
} from "./MediaEntryDetailHero";
import { EpisodesSection } from "./EpisodesSection";
import {
  CharactersSection,
  CommunitySection,
  ExpandedCharactersSection,
  InformationSection,
  ProviderSection,
  StreamingDestinationsSection,
} from "./MediaDetailSections";
import { FranchiseSection } from "./FranchiseSection";
import {
  getEntryStatusChanged,
  getPrimaryProgressSummary,
} from "./mediaEntryDetailModel";
import { DetailTabs } from "./DetailTabs";
import {
  DETAIL_TABS,
  type DetailTabId,
  type MediaEntryDetailContentProps,
  type MediaEntryDetailPageViewProps,
  type ProgressSummary,
} from "./mediaEntryDetailTypes";

function MediaDetailTabPanel({
  activeTab,
  props,
  progressSummary,
  streamingDestinations,
  preferredServiceId,
  onSelectStreamingService,
  onViewFullFranchise,
}: {
  activeTab: DetailTabId;
  props: MediaEntryDetailContentProps;
  progressSummary: ProgressSummary;
  streamingDestinations: MediaStreamingDestinations;
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  onViewFullFranchise: () => void;
}) {
  const panels: Record<DetailTabId, ReactNode> = {
    overview: (
      <div
        role="tabpanel"
        id="media-detail-panel-overview"
        aria-labelledby="media-detail-tab-overview"
        className="divide-y divide-border-subtle"
      >
        <StreamingDestinationsSection
          destinations={streamingDestinations.seriesDestinations}
          onSelect={onSelectStreamingService}
        />
        <FranchiseSection
          state={props.franchiseGraph}
          variant="preview"
          onRetry={props.onReloadFranchise}
          onViewAll={onViewFullFranchise}
          onNavigateTitle={props.onNavigateTitle}
        />
        <CharactersSection
          entry={props.entry}
          availabilityByProviderLink={props.availabilityByProviderLink}
        />
        <div className="grid gap-x-12 xl:grid-cols-2">
          <InformationSection entry={props.entry} />
          <CommunitySection
            entry={props.entry}
            progressSummary={progressSummary}
          />
        </div>
      </div>
    ),
    episodes: (
      <EpisodesSection
        entry={props.entry}
        state={props.episodeCatalog}
        streamingDestinations={streamingDestinations}
        preferredServiceId={preferredServiceId}
        onSelectStreamingService={onSelectStreamingService}
        onRefresh={props.onReloadEpisodes}
      />
    ),
    providers: (
      <div
        role="tabpanel"
        id="media-detail-panel-providers"
        aria-labelledby="media-detail-tab-providers"
      >
        <ProviderSection
          providerLinks={props.entry.providerLinks}
          availabilityByProviderLink={props.availabilityByProviderLink}
          unlinkingId={props.unlinkingId}
          lastSyncedAt={props.entry.lastSyncedAt}
          canManageLinks={
            props.entry.viewerStateStatus === "loaded" &&
            props.entry.isInLibrary
          }
          onLinkProvider={() => props.onSetShowLinkDialog(true)}
          onUnlink={props.onUnlink}
        />
      </div>
    ),
    franchise: (
      <div
        role="tabpanel"
        id="media-detail-panel-franchise"
        aria-labelledby="media-detail-tab-franchise"
      >
        <FranchiseSection
          state={props.franchiseGraph}
          variant="full"
          onRetry={props.onReloadFranchise}
          onNavigateTitle={props.onNavigateTitle}
        />
      </div>
    ),
    characters: (
      <div
        role="tabpanel"
        id="media-detail-panel-characters"
        aria-labelledby="media-detail-tab-characters"
      >
        <ExpandedCharactersSection
          entry={props.entry}
          availabilityByProviderLink={props.availabilityByProviderLink}
        />
      </div>
    ),
    details: (
      <div
        role="tabpanel"
        id="media-detail-panel-details"
        aria-labelledby="media-detail-tab-details"
      >
        <InformationSection entry={props.entry} />
      </div>
    ),
  };

  return panels[activeTab];
}

function MediaEntryDetailContent(props: MediaEntryDetailContentProps) {
  const [activeTab, setActiveTab] = useState<DetailTabId>("overview");
  const mediaKind = props.entry.title.mediaKind;
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);
  const [preferredServiceId, setPreferredServiceId] =
    useStreamingServicePreference();
  const availabilityLinks = Object.values(
    props.availabilityByProviderLink,
  ).flatMap((state) => (state.status === "loaded" ? state.links : []));
  const episodeCatalog =
    props.episodeCatalog.status === "loaded"
      ? props.episodeCatalog.value
      : null;
  const streamingDestinations = resolveStreamingDestinations(
    availabilityLinks,
    episodeCatalog,
    preferredServiceId,
  );
  const nextEpisodeNumber = (props.progressEpisodes ?? 0) + 1;
  const nextEpisodeDestinations =
    streamingDestinations.episodes.find(
      (episode) => episode.episodeNumber === nextEpisodeNumber,
    )?.destinations ?? [];

  return (
    <>
      <DetailPageLayout embedded={props.embedded}>
        <div className="mx-auto w-full max-w-360">
          <section className="min-w-0">
            <MediaHero
              entry={props.entry}
              onNavigateBack={props.onNavigateBack}
              actions={
                props.entry.isInLibrary ? (
                  <ActionRail
                    hasStatusChanged={hasStatusChanged}
                    isSavingStatus={props.isSavingStatus}
                    isRefreshingProgress={props.isRefreshingProgress}
                    onSaveStatus={props.onSaveStatus}
                    continueWatching={props.continueWatching}
                    seriesDestinations={
                      streamingDestinations.seriesDestinations
                    }
                    episodeDestinations={nextEpisodeDestinations}
                    preferredServiceId={preferredServiceId}
                    onSelectStreamingService={setPreferredServiceId}
                    canonicalTitle={props.entry.title.canonicalTitle}
                    nextReleaseAt={props.entry.nextReleaseAt}
                    nextReleaseLabel={props.entry.nextReleaseLabel}
                  />
                ) : (
                  <AddToLibraryActions
                    selectedStatus={props.selectedStatus}
                    isAddingToLibrary={props.isAddingToLibrary}
                    onSetSelectedStatus={props.onSetSelectedStatus}
                    onAddToLibrary={props.onAddToLibrary}
                  />
                )
              }
            />
            <ProgressCockpit {...props} />
          </section>
          <section>
            <DetailTabs
              tabs={DETAIL_TABS}
              activeTab={activeTab}
              idPrefix="media-detail"
              onChange={setActiveTab}
            />
            <div className="px-4 sm:px-7 xl:px-9">
              <MediaDetailTabPanel
                activeTab={activeTab}
                props={props}
                progressSummary={progressSummary}
                streamingDestinations={streamingDestinations}
                preferredServiceId={preferredServiceId}
                onSelectStreamingService={setPreferredServiceId}
                onViewFullFranchise={() => setActiveTab("franchise")}
              />
            </div>
          </section>
        </div>
      </DetailPageLayout>
      <EntryLinkDialog
        showLinkDialog={props.showLinkDialog}
        mediaTitleId={props.mediaTitleId}
        mediaKind={mediaKind}
        currentTitle={props.entry.title.canonicalTitle}
        existingLinks={props.entry.providerLinks}
        onClose={() => props.onSetShowLinkDialog(false)}
        onLinked={() => {
          props.onSetShowLinkDialog(false);
          void props.onLoadEntry();
        }}
      />
    </>
  );
}

export function MediaEntryDetailPageView({
  entry,
  isLoading,
  error,
  snackbar,
  ...contentProps
}: MediaEntryDetailPageViewProps) {
  const { embedded, onNavigateBack, onLoadEntry } = contentProps;

  if (isLoading) {
    return <DetailLoadingState embedded={embedded} />;
  }

  if (error || !entry) {
    return (
      <DetailErrorState
        error={error}
        embedded={embedded}
        onNavigateBack={onNavigateBack}
        onRetry={onLoadEntry}
      />
    );
  }

  return (
    <>
      <MediaEntryDetailContent {...contentProps} entry={entry} />
      <Snackbar notification={snackbar} />
    </>
  );
}
