import { useEffect, useState } from 'react';
import { mediaApi } from '../services/mediaApi';
import { mediaKindLabel } from '../services/mediaFormatting';
import { subscribeToMediaProgressUpdates } from '../services/mediaProgressEvents';
import { MediaEntryDetailPageView } from './media-entry-detail/MediaEntryDetailView';
import {
  useContinueWatching,
  useEntryDetailState,
  useEpisodeCatalog,
  useFranchiseGraph,
  useManualRemoteRefresh,
  useProviderAvailability,
  useProviderUnlinkAction,
  useRemoteEntryRefresh,
  useScoreSaveAction,
  useStatusSaveAction,
  useTimedSnackbar,
} from './media-entry-detail/useMediaEntryDetailState';
import type { DetailTabId } from './media-entry-detail/mediaEntryDetailTypes';

interface MediaEntryDetailPageProps {
  mediaTitleId: string;
  franchiseMediaTitleId?: string;
  onNavigateBack: () => void;
  onNavigateTitle?: (mediaTitleId: string) => void;
  activeTab?: DetailTabId;
  onTabChange?: (tab: DetailTabId) => void;
  embedded?: boolean;
  onHeadingChange?: (heading: { eyebrow: string; title: string; details?: string[]; hidden?: boolean }) => void;
}

// Page-level composition intentionally coordinates the independent detail resources and actions.
// fallow-ignore-next-line complexity
export function MediaEntryDetailPage({
  mediaTitleId,
  franchiseMediaTitleId,
  onNavigateBack,
  onNavigateTitle,
  activeTab,
  onTabChange,
  embedded = false,
  onHeadingChange,
}: MediaEntryDetailPageProps) {
  const [localActiveTab, setLocalActiveTab] = useState<DetailTabId>('overview');
  const resolvedActiveTab = activeTab ?? localActiveTab;
  const handleTabChange = onTabChange ?? setLocalActiveTab;
  const {
    entry,
    setEntry,
    isLoading,
    error,
    loadEntry,
    reloadEntry,
    progressEpisodes,
    setProgressEpisodes,
    progressChapters,
    setProgressChapters,
    progressVolumes,
    setProgressVolumes,
    selectedStatus,
    setSelectedStatus,
  } = useEntryDetailState(mediaTitleId);
  const {
    availabilityByProviderLink,
    reload: reloadProviderAvailability,
  } = useProviderAvailability(entry);
  const { state: episodeCatalog, reload: reloadEpisodes } = useEpisodeCatalog(entry);
  const { state: loadedFranchiseGraph, reload: reloadFranchise } = useFranchiseGraph(
    entry,
    franchiseMediaTitleId,
  );
  const franchiseGraph = loadedFranchiseGraph.status === 'loaded'
    ? {
      ...loadedFranchiseGraph,
      value: {
        ...loadedFranchiseGraph.value,
        currentMediaTitleId: mediaTitleId,
        nodes: loadedFranchiseGraph.value.nodes.map((node) => ({
          ...node,
          isCurrent: node.mediaTitleId === mediaTitleId,
        })),
      },
    }
    : loadedFranchiseGraph;
  const continueWatching = useContinueWatching(entry, episodeCatalog);
  const { snackbar, showSnackbar } = useTimedSnackbar();
  useRemoteEntryRefresh(entry, reloadEntry, showSnackbar);
  const {
    isRefreshingRemote: isRefreshingProgress,
    handleRefreshFromProvider,
  } = useManualRemoteRefresh(entry, reloadEntry, showSnackbar);
  const { isSavingStatus, handleSaveStatus } = useStatusSaveAction(
    mediaTitleId,
    entry,
    selectedStatus,
    progressEpisodes,
    progressChapters,
    progressVolumes,
    setEntry,
    showSnackbar,
  );
  const { isSavingScore, handleScoreChange } = useScoreSaveAction(
    mediaTitleId,
    entry,
    setEntry,
    reloadEntry,
    showSnackbar,
  );
  const { unlinkingId, handleUnlink } = useProviderUnlinkAction(mediaTitleId, setEntry, showSnackbar);
  const [showLinkDialog, setShowLinkDialog] = useState(false);
  const [isAddingToLibrary, setIsAddingToLibrary] = useState(false);

  useEffect(() => subscribeToMediaProgressUpdates((notification) => {
    if (notification.mediaTitleId !== mediaTitleId) {
      return;
    }
    void Promise.all([
      reloadEntry(),
      Promise.resolve(reloadEpisodes()),
      Promise.resolve(reloadProviderAvailability()),
    ]);
  }), [mediaTitleId, reloadEntry, reloadEpisodes, reloadProviderAvailability]);

  const handleAddToLibrary = async () => {
    if (!entry) return;
    setIsAddingToLibrary(true);
    try {
      await mediaApi.addToLibrary(mediaTitleId, { status: selectedStatus || 'planned' });
      await reloadEntry();
      showSnackbar({ message: 'Added to your library', variant: 'success' });
    } catch (addError) {
      showSnackbar({
        message: addError instanceof Error ? addError.message : 'Failed to add title',
        variant: 'error',
      });
    } finally {
      setIsAddingToLibrary(false);
    }
  };

  useEffect(() => {
    if (!entry || !onHeadingChange) {
      return;
    }

    onHeadingChange({
      eyebrow: 'Cantaro · Media',
      title: entry.title.canonicalTitle,
      details: [mediaKindLabel(entry.title.mediaKind), entry.isConnected ? 'Synced' : 'Not synced'],
      hidden: true,
    });
  }, [entry, onHeadingChange]);

  return (
    <MediaEntryDetailPageView
      mediaTitleId={mediaTitleId}
      onNavigateBack={onNavigateBack}
      onNavigateTitle={onNavigateTitle}
      embedded={embedded}
      entry={entry}
      isLoading={isLoading}
      error={error}
      onLoadEntry={loadEntry}
      availabilityByProviderLink={availabilityByProviderLink}
      snackbar={snackbar}
      isRefreshingProgress={isRefreshingProgress}
      isSavingStatus={isSavingStatus}
      isSavingScore={isSavingScore}
      isAddingToLibrary={isAddingToLibrary}
      showLinkDialog={showLinkDialog}
      unlinkingId={unlinkingId}
      progressEpisodes={progressEpisodes}
      progressChapters={progressChapters}
      progressVolumes={progressVolumes}
      selectedStatus={selectedStatus}
      continueWatching={continueWatching}
      episodeCatalog={episodeCatalog}
      franchiseGraph={franchiseGraph}
      activeTab={resolvedActiveTab}
      onTabChange={handleTabChange}
      onSetShowLinkDialog={setShowLinkDialog}
      onSetProgressEpisodes={setProgressEpisodes}
      onSetProgressChapters={setProgressChapters}
      onSetProgressVolumes={setProgressVolumes}
      onSetSelectedStatus={setSelectedStatus}
      onRefreshProgress={() => void handleRefreshFromProvider()}
      onSaveStatus={() => void handleSaveStatus()}
      onScoreChange={(score) => void handleScoreChange(score)}
      onAddToLibrary={() => void handleAddToLibrary()}
      onUnlink={(providerId) => void handleUnlink(providerId)}
      onReloadEpisodes={() => {
        reloadEpisodes();
        reloadProviderAvailability();
      }}
      onReloadFranchise={reloadFranchise}
    />
  );
}
