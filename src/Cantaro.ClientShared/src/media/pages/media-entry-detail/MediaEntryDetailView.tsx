import { useState, type ReactNode } from 'react';
import { Snackbar } from '../../../ui';
import {
  ActionRail,
  getCrunchyrollSeriesUrl,
  ProgressCockpit,
} from './MediaEntryDetailActions';
import {
  DetailErrorState,
  DetailLoadingState,
  DetailPageLayout,
  EntryLinkDialog,
  MediaHero,
} from './MediaEntryDetailHero';
import { EpisodesSection } from './EpisodesSection';
import {
  CharactersSection,
  CommunitySection,
  FranchiseSection,
  InformationSection,
  ProviderSection,
} from './MediaDetailSections';
import { getEntryStatusChanged, getPrimaryProgressSummary } from './mediaEntryDetailModel';
import {
  DETAIL_TABS,
  type DetailTabId,
  type MediaEntryDetailContentProps,
  type MediaEntryDetailPageViewProps,
  type ProgressSummary,
} from './mediaEntryDetailTypes';

function DetailTabs({ activeTab, onChange }: { activeTab: DetailTabId; onChange: (tab: DetailTabId) => void }) {
  return (
    <nav className="media-detail-tabs" aria-label="Media detail sections" role="tablist">
      {DETAIL_TABS.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          id={`media-detail-tab-${tab.id}`}
          aria-controls={`media-detail-panel-${tab.id}`}
          aria-selected={activeTab === tab.id}
          className={activeTab === tab.id ? 'is-active' : undefined}
          onClick={() => onChange(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}
function MediaDetailTabPanel({
  activeTab,
  props,
  progressSummary,
  crunchyrollSeriesUrl,
}: {
  activeTab: DetailTabId;
  props: MediaEntryDetailContentProps;
  progressSummary: ProgressSummary;
  crunchyrollSeriesUrl: string | null;
}) {
  const panels: Record<DetailTabId, ReactNode> = {
    overview: (
      <div role="tabpanel" id="media-detail-panel-overview" aria-labelledby="media-detail-tab-overview">
        <FranchiseSection entry={props.entry} />
        <CharactersSection entry={props.entry} availabilityByProviderLink={props.availabilityByProviderLink} />
        <div className="media-detail-overview-meta-grid">
          <InformationSection entry={props.entry} />
          <CommunitySection entry={props.entry} progressSummary={progressSummary} />
        </div>
      </div>
    ),
    episodes: (
      <EpisodesSection
        entry={props.entry}
        state={props.episodeCatalog}
        seriesUrl={crunchyrollSeriesUrl}
        onRefresh={props.onReloadEpisodes}
      />
    ),
    progress: (
      <div role="tabpanel" id="media-detail-panel-progress" aria-labelledby="media-detail-tab-progress">
        <CommunitySection entry={props.entry} progressSummary={progressSummary} />
      </div>
    ),
    providers: (
      <div role="tabpanel" id="media-detail-panel-providers" aria-labelledby="media-detail-tab-providers">
        <ProviderSection
          providerLinks={props.entry.providerLinks}
          availabilityByProviderLink={props.availabilityByProviderLink}
          unlinkingId={props.unlinkingId}
          lastSyncedAt={props.entry.lastSyncedAt}
          onLinkProvider={() => props.onSetShowLinkDialog(true)}
          onUnlink={props.onUnlink}
        />
      </div>
    ),
    franchise: (
      <div role="tabpanel" id="media-detail-panel-franchise" aria-labelledby="media-detail-tab-franchise">
        <FranchiseSection entry={props.entry} />
      </div>
    ),
    characters: (
      <div role="tabpanel" id="media-detail-panel-characters" aria-labelledby="media-detail-tab-characters">
        <CharactersSection entry={props.entry} availabilityByProviderLink={props.availabilityByProviderLink} />
      </div>
    ),
    details: (
      <div role="tabpanel" id="media-detail-panel-details" aria-labelledby="media-detail-tab-details">
        <InformationSection entry={props.entry} />
      </div>
    ),
  };

  return panels[activeTab];
}

function MediaEntryDetailContent(props: MediaEntryDetailContentProps) {
  const [activeTab, setActiveTab] = useState<DetailTabId>('overview');
  const mediaKind = props.entry.title.mediaKind;
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);
  const crunchyrollSeriesUrl = getCrunchyrollSeriesUrl(props.availabilityByProviderLink);

  return (
    <>
      <DetailPageLayout embedded={props.embedded}>
        <div className="media-detail-layout">
          <section className="media-detail-top-area">
            <MediaHero entry={props.entry} progressSummary={progressSummary} onNavigateBack={props.onNavigateBack} />
            <div className="media-detail-desktop-progress">
              <ProgressCockpit {...props} />
            </div>
            <div className="media-detail-mobile-progress">
              <ProgressCockpit {...props} />
            </div>
          </section>
          <div className="media-detail-main-column">
            <ActionRail
              hasStatusChanged={hasStatusChanged}
              isSavingStatus={props.isSavingStatus}
              isRefreshingProgress={props.isRefreshingProgress}
              onSaveStatus={props.onSaveStatus}
              onLinkProvider={() => props.onSetShowLinkDialog(true)}
              continueWatching={props.continueWatching}
              crunchyrollSeriesUrl={crunchyrollSeriesUrl}
              nextReleaseAt={props.entry.nextReleaseAt}
              nextReleaseLabel={props.entry.nextReleaseLabel}
            />
            <section className="media-detail-overview-card">
              <DetailTabs activeTab={activeTab} onChange={setActiveTab} />
              <MediaDetailTabPanel
                activeTab={activeTab}
                props={props}
                progressSummary={progressSummary}
                crunchyrollSeriesUrl={crunchyrollSeriesUrl}
              />
            </section>
          </div>
        </div>
      </DetailPageLayout>
      <EntryLinkDialog
        showLinkDialog={props.showLinkDialog}
        libraryEntryId={props.libraryEntryId}
        mediaKind={mediaKind}
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
    return <DetailErrorState error={error} embedded={embedded} onNavigateBack={onNavigateBack} onRetry={onLoadEntry} />;
  }

  return (
    <>
      <MediaEntryDetailContent
        {...contentProps}
        entry={entry}
      />
      <Snackbar notification={snackbar} />
    </>
  );
}
