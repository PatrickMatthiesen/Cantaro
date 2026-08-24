import { useRef, type ReactNode, type RefObject } from "react";
import {
  ArrowLeft,
  CalendarDays,
  Clock3,
  Maximize2,
  Tv,
  X,
} from "lucide-react";
import {
  DetailArtwork,
  SanitizedSynopsis,
} from "../../components/media-entry-detail/EntryDisplayPrimitives";
import { SearchLinkDialog } from "../../components/SearchLinkDialog";
import { ActionButton, IconButton } from "../../../ui";
import {
  formatNextReleaseDisplay,
  mediaKindLabel,
} from "../../services/mediaFormatting";
import type {
  MediaEntryDetailModel,
  MediaProviderLinkSummaryDto,
} from "../../services/mediaApi";
import { progressKindLabel } from "./mediaEntryDetailModel";

export function DetailPageLayout({
  children,
  className = "",
  embedded = false,
}: {
  children: ReactNode;
  className?: string;
  embedded?: boolean;
}) {
  return (
    <div
      className={`${embedded ? "" : "min-h-screen bg-canvas px-4 py-6 sm:px-8"} ${className}`}
    >
      {children}
    </div>
  );
}

export function DetailLoadingState({
  embedded = false,
}: {
  embedded?: boolean;
}) {
  return (
    <DetailPageLayout embedded={embedded}>
      <div
        className="mx-auto min-h-[34rem] w-full max-w-360 animate-pulse bg-surface-subtle"
        aria-label="Loading media details"
        aria-busy="true"
      />
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
    <DetailPageLayout embedded={embedded}>
      <section className="mx-auto grid min-h-[28rem] max-w-3xl content-center gap-5 text-center">
        <div>
          <h1 className="text-2xl font-bold text-content">
            Media details unavailable
          </h1>
          <p className="mt-2 text-content-muted">
            {error ?? "Entry not found"}
          </p>
        </div>
        <div className="flex flex-wrap justify-center gap-3">
          <ActionButton tone="secondary" onClick={onNavigateBack}>
            <ArrowLeft size={18} aria-hidden /> Back to library
          </ActionButton>
          <ActionButton tone="personal" onClick={() => void onRetry()}>
            Try again
          </ActionButton>
        </div>
      </section>
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
  if (!showLinkDialog) return null;

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

function getTitleCountLabels(title: MediaEntryDetailModel["title"]): string[] {
  return [
    title.episodeCount ? `${title.episodeCount} episodes` : null,
    title.chapterCount ? `${title.chapterCount} chapters` : null,
    title.volumeCount ? `${title.volumeCount} volumes` : null,
  ].filter((value): value is string => Boolean(value));
}

function HeroTitleMeta({ entry }: { entry: MediaEntryDetailModel }) {
  const nextRelease = formatNextReleaseDisplay(entry.nextReleaseAt);
  return (
    <div className="mt-4 flex flex-wrap gap-x-4 gap-y-2 text-sm text-immersive-content-muted">
      {entry.title.startYear ? (
        <span className="inline-flex items-center gap-1.5">
          <CalendarDays size={16} aria-hidden /> {entry.title.startYear}
        </span>
      ) : null}
      {getTitleCountLabels(entry.title).map((count) => (
        <span key={count} className="inline-flex items-center gap-1.5">
          <Tv size={16} aria-hidden /> {count}
        </span>
      ))}
      {nextRelease ? (
        <span className="inline-flex items-center gap-1.5">
          <Clock3 size={16} aria-hidden /> {nextRelease.relative}
        </span>
      ) : null}
    </div>
  );
}

function PosterDialog({
  dialogRef,
  posterUrl,
  title,
}: {
  dialogRef: RefObject<HTMLDialogElement | null>;
  posterUrl?: string;
  title: string;
}) {
  return (
    <dialog
      ref={dialogRef}
      aria-label={`${title} poster`}
      className="m-auto max-h-[92dvh] max-w-[min(92vw,44rem)] bg-transparent p-0 backdrop:bg-black/80"
      onClick={(event) => {
        if (event.target === event.currentTarget) event.currentTarget.close();
      }}
    >
      <div className="relative max-h-[92dvh]">
        <DetailArtwork
          posterUrl={posterUrl}
          title={title}
          className="max-h-[92dvh] w-auto object-contain"
        />
        <IconButton
          label="Close poster"
          onClick={() => dialogRef.current?.close()}
          className="absolute right-3 top-3 bg-black/70 text-white hover:bg-black/90 hover:text-white"
        >
          <X size={20} aria-hidden />
        </IconButton>
      </div>
    </dialog>
  );
}

function HeroBackdrop({ url }: { url?: string }) {
  if (!url) return null;
  return (
    <div
      className="absolute inset-0 bg-cover bg-center opacity-50 dark:opacity-65"
      style={{ backgroundImage: `url(${url})` }}
      aria-hidden
    />
  );
}

function MobilePosterTrigger({
  posterUrl,
  onOpen,
}: {
  posterUrl?: string;
  onOpen: () => void;
}) {
  if (!posterUrl) return null;
  return (
    <button
      type="button"
      aria-label="View full poster"
      onClick={onOpen}
      className="group absolute inset-x-0 top-0 z-10 h-44 text-right outline-none md:hidden"
    >
      <Maximize2
        size={18}
        className="absolute right-5 top-5 text-immersive-content/55 transition-transform group-hover:scale-110 group-focus-visible:text-immersive-content motion-reduce:transition-none"
        aria-hidden
      />
    </button>
  );
}

function DesktopPoster({
  posterUrl,
  title,
  onOpen,
}: {
  posterUrl?: string;
  title: string;
  onOpen: () => void;
}) {
  if (!posterUrl) return null;
  return (
    <button
      type="button"
      onClick={onOpen}
      aria-label="View full poster"
      className="group relative hidden w-48 shrink-0 overflow-hidden bg-white/75 p-2 outline-none focus-visible:ring-2 focus-visible:ring-focus md:block dark:bg-[#11141a] xl:w-56"
    >
      <DetailArtwork
        posterUrl={posterUrl}
        title={title}
        className="h-auto w-full object-contain transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 group-focus-visible:scale-[1.025] motion-reduce:transition-none"
      />
      <span className="absolute right-4 top-4 inline-flex size-9 items-center justify-center bg-black/70 text-white opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100">
        <Maximize2 size={16} aria-hidden />
      </span>
    </button>
  );
}

function HeroCopy({
  entry,
  actions,
}: {
  entry: MediaEntryDetailModel;
  actions?: ReactNode;
}) {
  const { title } = entry;
  const kindLabel = mediaKindLabel(title.mediaKind);
  const formatLabel = progressKindLabel(title);
  const typeLabel =
    kindLabel.toLowerCase() === formatLabel.toLowerCase()
      ? kindLabel
      : `${kindLabel} · ${formatLabel}`;
  const showOriginalTitle =
    title.originalTitle && title.originalTitle !== title.canonicalTitle;
  return (
    <div className="min-w-0 max-w-3xl pb-1">
      <p className="font-semibold text-immersive-content-muted">
        {typeLabel}
      </p>
      <h1
        id="media-detail-title"
        className="mt-3 text-4xl font-black leading-[1.04] tracking-[-0.04em] text-immersive-content text-balance sm:text-5xl xl:text-6xl"
      >
        {title.canonicalTitle}
      </h1>
      {showOriginalTitle ? (
        <p className="mt-2 text-sm text-immersive-content-muted">
          {title.originalTitle}
        </p>
      ) : null}
      <HeroTitleMeta entry={entry} />
      {title.synopsis ? (
        <SanitizedSynopsis
          html={title.synopsis}
          className="mt-6 max-h-36 max-w-[68ch] overflow-hidden text-base leading-7 text-immersive-content-muted"
        />
      ) : null}
      {actions ? (
        <div className="mt-7 flex flex-wrap items-center gap-3">{actions}</div>
      ) : null}
    </div>
  );
}

export function MediaHero({
  entry,
  actions,
  onNavigateBack,
}: {
  entry: MediaEntryDetailModel;
  actions?: ReactNode;
  onNavigateBack: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const { title } = entry;
  const openPoster = () => dialogRef.current?.showModal();

  return (
    <>
      <section
        aria-labelledby="media-detail-title"
        className="relative isolate overflow-hidden bg-immersive-canvas text-immersive-content"
      >
        <HeroBackdrop url={title.backgroundUrl ?? title.posterUrl} />
        <div
          className="absolute inset-0 bg-linear-to-r from-immersive-scrim-strong via-immersive-scrim-medium to-immersive-scrim-soft"
          aria-hidden
        />
        <div
          className="absolute inset-0 bg-linear-to-t from-immersive-scrim-base via-transparent to-immersive-scrim-soft"
          aria-hidden
        />

        <MobilePosterTrigger posterUrl={title.posterUrl} onOpen={openPoster} />

        <IconButton
          label="Back to library"
          onClick={onNavigateBack}
          className="absolute left-4 top-4 z-30 bg-black/35 text-white hover:bg-black/60 hover:text-white sm:left-6 sm:top-6"
        >
          <ArrowLeft size={19} aria-hidden />
        </IconButton>

        <div className="relative z-20 flex min-h-[31rem] items-end gap-8 px-4 pb-9 pt-44 sm:px-7 md:pt-24 xl:px-9">
          <DesktopPoster
            posterUrl={title.posterUrl}
            title={title.canonicalTitle}
            onOpen={openPoster}
          />
          <HeroCopy entry={entry} actions={actions} />
        </div>
      </section>
      <PosterDialog
        dialogRef={dialogRef}
        posterUrl={title.posterUrl}
        title={title.canonicalTitle}
      />
    </>
  );
}
