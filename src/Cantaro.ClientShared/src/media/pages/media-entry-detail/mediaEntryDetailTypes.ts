import type { SnackbarNotification } from '../../../ui';
import type { ProviderAvailabilityMap } from '../../components/media-entry-detail/providerAvailability';
import type {
  MediaContinueWatchingDto,
  MediaEpisodeCatalogDto,
  MediaEntryDetailModel,
  MediaFranchiseGraphDto,
} from '../../services/mediaApi';

export const DETAIL_TABS = [
  { id: 'overview', label: 'Overview' },
  { id: 'episodes', label: 'Episodes' },
  { id: 'providers', label: 'Providers' },
  { id: 'franchise', label: 'Franchise' },
  { id: 'characters', label: 'Characters' },
  { id: 'details', label: 'Details' },
] as const;

export type DetailTabId = (typeof DETAIL_TABS)[number]['id'];

export interface StatusDraft {
  selectedStatus: string;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
}
export interface ProgressSummary {
  label: string;
  noun: string;
  value: number | undefined;
  total?: number;
  progressLabel: string;
}

export type ContinueWatchingState =
  | { status: 'loading' }
  | { status: 'loaded'; value: MediaContinueWatchingDto }
  | { status: 'error' };

export type EpisodeCatalogState =
  | { status: 'loading' }
  | { status: 'loaded'; value: MediaEpisodeCatalogDto }
  | { status: 'error' };

export type FranchiseGraphState =
  | { status: 'loading' }
  | { status: 'loaded'; value: MediaFranchiseGraphDto }
  | { status: 'error'; error: string };

export interface MediaEntryDetailContentProps {
  mediaTitleId: string;
  entry: MediaEntryDetailModel;
  embedded: boolean;
  availabilityByProviderLink: ProviderAvailabilityMap;
  isRefreshingProgress: boolean;
  isSavingStatus: boolean;
  isSavingScore: boolean;
  isAddingToLibrary: boolean;
  showLinkDialog: boolean;
  unlinkingId: string | null;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
  selectedStatus: string;
  continueWatching: ContinueWatchingState;
  episodeCatalog: EpisodeCatalogState;
  franchiseGraph: FranchiseGraphState;
  onNavigateBack: () => void;
  onNavigateTitle?: (mediaTitleId: string) => void;
  onLoadEntry: () => Promise<void>;
  onSetShowLinkDialog: (visible: boolean) => void;
  onSetProgressEpisodes: (value: number) => void;
  onSetProgressChapters: (value: number) => void;
  onSetProgressVolumes: (value: number) => void;
  onSetSelectedStatus: (value: string) => void;
  onRefreshProgress: () => void;
  onSaveStatus: () => void;
  onScoreChange: (score: number | null) => void;
  onAddToLibrary: () => void;
  onUnlink: (providerId: string) => void;
  onReloadEpisodes: () => void;
  onReloadFranchise: () => void;
}

export interface MediaEntryDetailPageViewProps extends Omit<MediaEntryDetailContentProps, 'entry'> {
  entry: MediaEntryDetailModel | null;
  isLoading: boolean;
  error: string | null;
  snackbar: SnackbarNotification | null;
}
