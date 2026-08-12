import { type ReactNode } from 'react';
import { CalendarDays, Clock3, Tv } from 'lucide-react';
import { DetailArtwork, SanitizedSynopsis } from '../../components/media-entry-detail/EntryDisplayPrimitives';
import { SearchLinkDialog } from '../../components/SearchLinkDialog';
import { GradientButton } from '../../../ui';
import { formatNextReleaseDisplay, mediaKindLabel } from '../../services/mediaFormatting';
import type { MediaEntryDetailModel, MediaProviderLinkSummaryDto } from '../../services/mediaApi';
import { progressKindLabel } from './mediaEntryDetailModel';

export function DetailPageLayout({ children, className = '', embedded = false }: { children: ReactNode; className?: string; embedded?: boolean }) {
  if (embedded) {
    return (
      <div className={`media-detail-shell ${className}`}>
        {children}
      </div>
    );
  }

  return (
    <div className="media-detail-standalone">
      <div className={`media-detail-shell ${className}`}>
        {children}
      </div>
    </div>
  );
}

export function DetailLoadingState({ embedded = false }: { embedded?: boolean }) {
  return (
    <DetailPageLayout embedded={embedded}>
      <div className="media-detail-skeleton" />
    </DetailPageLayout>
  );
}

export function DetailErrorState({
  error,
  embedded = false,
  onNavigateBack,
  onRetry,
}: {
  error: string | null;
  embedded?: boolean;
  onNavigateBack: () => void;
  onRetry: () => Promise<void>;
}) {
  return (
    <DetailPageLayout className="space-y-4" embedded={embedded}>
      <GradientButton tone="soft" onClick={onNavigateBack}>Back to library</GradientButton>
      <div className="media-detail-empty-panel">
        <p>{error ?? 'Entry not found'}</p>
        <button type="button" onClick={() => void onRetry()}>Retry</button>
      </div>
    </DetailPageLayout>
  );
}

export function EntryLinkDialog({
  showLinkDialog,
  mediaTitleId,
  mediaKind,
  existingLinks,
  currentTitle,
  onClose,
  onLinked,
}: {
  showLinkDialog: boolean;
  mediaTitleId: string;
  mediaKind: string;
  existingLinks: MediaProviderLinkSummaryDto[];
  currentTitle: string;
  onClose: () => void;
  onLinked: () => void;
}) {
  if (!showLinkDialog) {
    return null;
  }

  return (
    <SearchLinkDialog
      mediaTitleId={mediaTitleId}
      currentTitle={currentTitle}
      mediaKind={mediaKind}
      existingLinks={existingLinks}
      onClose={onClose}
      onLinked={onLinked}
    />
  );
}

function DetailTopBar({ onNavigateBack }: { onNavigateBack: () => void }) {
  return (
    <header className="media-detail-topbar">
      <button type="button" className="media-detail-icon-button" onClick={onNavigateBack} aria-label="Back to library">
        <span aria-hidden>←</span>
      </button>
    </header>
  );
}

function getTitleCountLabels(title: MediaEntryDetailModel['title']): string[] {
  return [
    title.episodeCount ? `${title.episodeCount} Episodes` : null,
    title.chapterCount ? `${title.chapterCount} Chapters` : null,
    title.volumeCount ? `${title.volumeCount} Volumes` : null,
  ].filter((value): value is string => Boolean(value));
}

function HeroBackdrop({ posterUrl }: { posterUrl?: string }) {
  return posterUrl
    ? <img className="media-detail-hero-bg" src={posterUrl} alt="" aria-hidden />
    : null;
}

function HeroTitleMeta({ entry }: { entry: MediaEntryDetailModel }) {
  const { title } = entry;
  const nextRelease = formatNextReleaseDisplay(entry.nextReleaseAt);
  const titleCounts = getTitleCountLabels(title);

  return (
    <>
      <div className="media-detail-meta-row">
        {title.startYear ? (
          <span><CalendarDays aria-hidden />{title.startYear}</span>
        ) : null}
        {titleCounts.map((count) => (
          <span key={count}><Tv aria-hidden />{count}</span>
        ))}
        {nextRelease ? <span><Clock3 aria-hidden />{nextRelease.relative}</span> : null}
      </div>
    </>
  );
}

function HeroDescription({ entry }: { entry: MediaEntryDetailModel }) {
  const { title } = entry;
  return (
    <>
      {title.synopsis ? (
        <SanitizedSynopsis html={title.synopsis} className="media-detail-synopsis" />
      ) : null}
    </>
  );
}

export function MediaHero({
  entry,
  onNavigateBack,
}: {
  entry: MediaEntryDetailModel;
  onNavigateBack: () => void;
}) {
  const { title } = entry;

  return (
    <section className="media-detail-hero">
      <HeroBackdrop posterUrl={title.posterUrl} />
      <div className="media-detail-hero-scrim" aria-hidden />
      <div className="media-detail-hero-content">
        <DetailTopBar onNavigateBack={onNavigateBack} />
        <div className="media-detail-hero-grid">
          <div className="media-detail-poster">
            <DetailArtwork posterUrl={title.posterUrl} title={title.canonicalTitle} />
          </div>
          <div className="media-detail-title-stack">
            <h2>{title.canonicalTitle}</h2>
            {title.originalTitle && title.originalTitle !== title.canonicalTitle ? (
              <p className="media-detail-original-title">{title.originalTitle}</p>
            ) : null}
            <div className="media-detail-tag-row">
              <span className="media-detail-dot media-detail-dot--violet" />
              <span>{mediaKindLabel(title.mediaKind)}</span>
              <span className="media-detail-dot media-detail-dot--blue" />
              <span>{progressKindLabel(title)}</span>
            </div>
            <HeroTitleMeta entry={entry} />
            <HeroDescription entry={entry} />
          </div>
        </div>
      </div>
    </section>
  );
}
