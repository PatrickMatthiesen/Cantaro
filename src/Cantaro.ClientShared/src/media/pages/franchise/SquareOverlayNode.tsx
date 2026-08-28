import { useId, useRef } from "react";
import { DetailArtwork } from "../../components/media-entry-detail/EntryDisplayPrimitives";
import type { MediaFranchiseNodeDto } from "../../services/mediaApi";

let activeSquarePopover: HTMLDivElement | null = null;

export function SquareOverlayNode({
  node,
  meta,
  onNavigate,
}: {
  node: MediaFranchiseNodeDto;
  meta: string;
  onNavigate: (id: string) => void;
}) {
  const popoverId = useId();
  const popoverRef = useRef<HTMLDivElement>(null);
  const hideTimerRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  const cancelHide = () => {
    if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
  };
  const showDetails = (anchor: HTMLElement) => {
    cancelHide();
    const popover = popoverRef.current;
    if (!popover) return;
    if (activeSquarePopover !== popover && activeSquarePopover?.matches(":popover-open")) {
      activeSquarePopover.hidePopover();
    }
    activeSquarePopover = popover;
    const bounds = anchor.getBoundingClientRect();
    const width = 176;
    const gap = 10;
    const left = Math.min(
      Math.max(gap, bounds.left + (bounds.width - width) / 2),
      window.innerWidth - width - gap,
    );
    popover.style.left = `${left}px`;
    if (!popover.matches(":popover-open")) popover.showPopover();
    const popoverHeight = popover.getBoundingClientRect().height;
    const top = bounds.top - popoverHeight - gap >= gap
      ? bounds.top - popoverHeight - gap
      : Math.min(bounds.bottom + gap, window.innerHeight - popoverHeight - gap);
    popover.style.top = `${top}px`;
  };
  const scheduleHide = () => {
    cancelHide();
    hideTimerRef.current = setTimeout(() => {
      const popover = popoverRef.current;
      if (popover?.matches(":popover-open")) popover.hidePopover();
      if (activeSquarePopover === popover) activeSquarePopover = null;
    }, 120);
  };

  return (
    <div className="group relative min-w-0">
      <button
        type="button"
        onClick={() => onNavigate(node.mediaTitleId)}
        onPointerEnter={(event) => showDetails(event.currentTarget)}
        onPointerLeave={scheduleHide}
        onFocus={(event) => showDetails(event.currentTarget)}
        onBlur={scheduleHide}
        className="block aspect-square w-full overflow-hidden bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        aria-label={`Open ${node.canonicalTitle}`}
        aria-describedby={popoverId}
      >
        <DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} className="transition-transform duration-200 ease-out group-hover:scale-[1.035]" />
      </button>
      <div
        ref={popoverRef}
        id={popoverId}
        popover="manual"
        role="tooltip"
        className="pointer-events-none fixed inset-auto m-0 aspect-3/4 w-44 overflow-hidden bg-surface-subtle p-0 text-white shadow-lg"
      >
        <DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} />
        <span className="absolute inset-0 bg-linear-to-t from-black/95 via-black/35 via-55% to-transparent" aria-hidden />
        <div className="absolute inset-x-0 bottom-0 p-3">
          <strong className="block text-pretty text-sm leading-5">{node.canonicalTitle}</strong>
          <span className="mt-1 block text-xs text-white/80">{meta}</span>
          <span className="mt-2 flex items-center gap-2 text-xs font-semibold text-white/90">
            <span className={`size-2 rounded-full ${node.isInLibrary ? "bg-success-content" : "bg-white/65"}`} aria-hidden />
            <span>{node.isInLibrary ? "In your library" : "Not in your library"}</span>
          </span>
        </div>
      </div>
    </div>
  );
}
