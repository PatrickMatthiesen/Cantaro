import { useEffect, useState } from 'react';
import { mediaKindLabel } from '../services/mediaFormatting';
import { MediaEntryDetailPageView } from './media-entry-detail/MediaEntryDetailView';
import {
  useContinueWatching,
  useEntryDetailState,
  useEpisodeCatalog,
  useManualRemoteRefresh,
  useProviderAvailability,
  useProviderUnlinkAction,
  useRemoteEntryRefresh,
  useStatusSaveAction,
  useTimedSnackbar,
} from './media-entry-detail/useMediaEntryDetailState';

interface MediaEntryDetailPageProps {
  libraryEntryId: string;
  onNavigateBack: () => void;
  embedded?: boolean;
  onHeadingChange?: (heading: { eyebrow: string; title: string; details?: string[]; hidden?: boolean }) => void;
}
export function MediaEntryDetailPage({
  libraryEntryId,
  onNavigateBack,
  embedded = false,
  onHeadingChange,
}: MediaEntryDetailPageProps) {
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
  } = useEntryDetailState(libraryEntryId);
  const availabilityByProviderLink = useProviderAvailability(entry);
  const { state: episodeCatalog, reload: reloadEpisodes } = useEpisodeCatalog(entry);
  const continueWatching = useContinueWatching(entry, episodeCatalog);
  const { snackbar, showSnackbar } = useTimedSnackbar();
  useRemoteEntryRefresh(entry, reloadEntry, showSnackbar);
  const {
    isRefreshingRemote: isRefreshingProgress,
    handleRefreshFromProvider,
  } = useManualRemoteRefresh(entry, reloadEntry, showSnackbar);
  const { isSavingStatus, handleSaveStatus } = useStatusSaveAction(
    libraryEntryId,
    entry,
    selectedStatus,
    progressEpisodes,
    progressChapters,
    progressVolumes,
    setEntry,
    showSnackbar,
  );
  const { unlinkingId, handleUnlink } = useProviderUnlinkAction(libraryEntryId, setEntry, showSnackbar);
  const [showLinkDialog, setShowLinkDialog] = useState(false);

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
      libraryEntryId={libraryEntryId}
      onNavigateBack={onNavigateBack}
      embedded={embedded}
      entry={entry}
      isLoading={isLoading}
      error={error}
      onLoadEntry={loadEntry}
      availabilityByProviderLink={availabilityByProviderLink}
      snackbar={snackbar}
      isRefreshingProgress={isRefreshingProgress}
      isSavingStatus={isSavingStatus}
      showLinkDialog={showLinkDialog}
      unlinkingId={unlinkingId}
      progressEpisodes={progressEpisodes}
      progressChapters={progressChapters}
      progressVolumes={progressVolumes}
      selectedStatus={selectedStatus}
      continueWatching={continueWatching}
      episodeCatalog={episodeCatalog}
      onSetShowLinkDialog={setShowLinkDialog}
      onSetProgressEpisodes={setProgressEpisodes}
      onSetProgressChapters={setProgressChapters}
      onSetProgressVolumes={setProgressVolumes}
      onSetSelectedStatus={setSelectedStatus}
      onRefreshProgress={() => void handleRefreshFromProvider()}
      onSaveStatus={() => void handleSaveStatus()}
      onUnlink={(providerId) => void handleUnlink(providerId)}
      onReloadEpisodes={reloadEpisodes}
    />
  );
}
