import { Link, useNavigate } from '@tanstack/react-router';
import { MusicPlatformIcon, platformManager, playlistSyncApi, type PlaylistCreateCandidate, type PlaylistSyncDetails, type PlaylistSyncLink } from '@cantaro/client-shared/music';
import { useEffect, useId, useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import { ArrowUpRight, Check, Ellipsis, RefreshCw } from 'lucide-react';
import { MusicRelativeTime } from './MusicRelativeTime';
import { MusicPlaylistNameProposal } from './MusicPlaylistNameProposal';
import { MusicPlaylistInitializationRetry } from './MusicPlaylistInitializationRetry';
import { MusicPlaylistAttach, MusicPlaylistAttachForm } from './MusicPlaylistAttach';
import { PlaylistCreatePanel, PlaylistCreateTrigger } from './MusicPlaylistCreateReview';
import { usePlaylistCreateReview, type PlaylistCreateReviewState } from './usePlaylistCreateReview';
import { MusicPlaylistMatchReview } from './MusicPlaylistMatchReview';
import { platformName } from './musicPresentation';
import { isPlaylistProviderCooldown, playlistLinkResultLabel, playlistLinkSyncActivity, playlistLinkSyncButton } from './playlistLinkResultLabel';
import { useDismissiblePanel } from './useDismissiblePanel';

type LinkAction = (label: string, operation: () => Promise<PlaylistSyncDetails | void>) => Promise<boolean>;

function modeLabel(link: PlaylistSyncLink) {
  const labels: Record<string, string> = {
    import_only: `${platformName(link.service)} to Cantaro`,
    from_cantaro: `Cantaro to ${platformName(link.service)}`,
  };
  return labels[link.syncMode] ?? 'Two-way';
}

type ReviewTrigger = { reviewOpen: boolean; reviewId: string; reviewRef: RefObject<HTMLButtonElement | null>; onReview: () => void };

function RetryStatus({ link, now }: { link: PlaylistSyncLink; now: number }) {
  if (!isPlaylistProviderCooldown(link.lastSyncStatus) || !link.nextAttemptAt || new Date(link.nextAttemptAt).getTime() <= now) return null;
  return <span>· Retries <MusicRelativeTime value={link.nextAttemptAt} /></span>;
}

function MatchProgress({ link }: { link: PlaylistSyncLink }) {
  if (!['pending', 'running', 'rate_limited', 'quota_limited'].includes(link.lastSyncStatus ?? '') || link.matchingTotalCount <= 0) return null;
  return <span>· Matching {link.matchingProcessedCount} of {link.matchingTotalCount}</span>;
}

function UnmatchedResult({ link, reviewOpen, reviewId, reviewRef, onReview }: { link: PlaylistSyncLink } & ReviewTrigger) {
  if (link.unresolvedCount <= 0) return null;
  return <><span>· {link.resolvedCount} of {link.totalCount} tracks</span><button ref={reviewRef} type="button" aria-expanded={reviewOpen} aria-controls={reviewId} onClick={onReview} className="inline-flex min-h-8 items-center text-warning-content underline underline-offset-4 hover:text-content focus-visible:outline-2 focus-visible:outline-focus">{link.unresolvedCount} unmatched</button></>;
}

function LinkResult({ link, now, reviewOpen, reviewId, reviewRef, onReview }: { link: PlaylistSyncLink; now: number } & ReviewTrigger) {
  const status = playlistLinkResultLabel(link);
  const showTime = ['Synced', 'Partially synced'].includes(status);
  return <p className="flex flex-wrap items-center gap-x-1.5 text-xs text-content-muted">
        {status === 'Synced' ? <Check className="size-3.5 text-success-content" aria-hidden /> : null}
        <span>{status}</span>
        <RetryStatus link={link} now={now} />
        <MatchProgress link={link} />
        {showTime && link.lastSyncedAt ? <MusicRelativeTime value={link.lastSyncedAt} /> : null}
        <UnmatchedResult link={link} reviewOpen={reviewOpen} reviewId={reviewId} reviewRef={reviewRef} onReview={onReview} />
      </p>;
}

function LinkSummary({ link, now, reviewOpen, reviewId, reviewRef, onReview }: { link: PlaylistSyncLink; now: number } & ReviewTrigger) {
  return <div className="flex min-w-0 items-center gap-3">
    <MusicPlatformIcon platformId={link.service} className="h-6 w-6 shrink-0" />
    <div className="min-w-0">
      <p className="text-sm font-semibold text-content">{!link.servicePlaylistId.startsWith('pending:') ? <Link to="/music/platforms/$platformId/playlists/$playlistId" params={{ platformId: link.service, playlistId: link.servicePlaylistId }} aria-label={`Open linked ${platformName(link.service)} playlist`} className="inline-flex min-h-7 items-center gap-1.5 hover:underline focus-visible:outline-2 focus-visible:outline-focus">{platformName(link.service)}<ArrowUpRight className="size-3.5 text-content-muted" aria-hidden /></Link> : platformName(link.service)}</p>
      <LinkResult link={link} now={now} reviewOpen={reviewOpen} reviewId={reviewId} reviewRef={reviewRef} onReview={onReview} />
    </div>
  </div>;
}

function UnlinkReview({ playlistId, link, isLast, busy, onAction, onClose }: {
  playlistId: string;
  link: PlaylistSyncLink;
  isLast: boolean;
  busy: boolean;
  onAction: LinkAction;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const [deleteRemote, setDeleteRemote] = useState(false);
  const [deleteLocal, setDeleteLocal] = useState(false);
  async function unlink() {
    const succeeded = await onAction(`unlink-${link.mappingId}`, () => playlistSyncApi.unlink(playlistId, link.mappingId, deleteRemote, deleteLocal));
    if (succeeded && deleteLocal) void navigate({ to: '/music/playlists' });
    if (succeeded) onClose();
  }
  return <div className="mt-3 space-y-2 border border-warning-border bg-warning-surface p-3 text-sm text-content">
    <p className="font-bold">Unlink {platformName(link.service)}?</p>
    {link.canDeleteRemote ? <label className="flex items-center gap-2"><input type="checkbox" checked={deleteRemote} onChange={(event) => setDeleteRemote(event.target.checked)} />Delete the {platformName(link.service)} playlist</label> : null}
    {isLast ? <label className="flex items-center gap-2"><input type="checkbox" checked={deleteLocal} onChange={(event) => setDeleteLocal(event.target.checked)} />Delete the Cantaro playlist too</label> : null}
    <div className="flex gap-2">
      <button type="button" disabled={busy} onClick={() => void unlink()} className="min-h-11 bg-danger-action px-4 font-bold text-danger-action-content disabled:opacity-50">Confirm unlink</button>
      <button type="button" disabled={busy} onClick={onClose} className="min-h-11 px-2 font-semibold underline">Cancel</button>
    </div>
  </div>;
}

function OrderConflictReview({ playlistId, link, busy, onAction }: {
  playlistId: string;
  link: PlaylistSyncLink;
  busy: boolean;
  onAction: LinkAction;
}) {
  if (link.lastSyncStatus !== 'order_conflict') return null;
  return <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-warning-content">
    <span>Playlist order differs.</span>
    <button type="button" disabled={busy} onClick={() => void onAction('order', () => playlistSyncApi.resolveOrder(playlistId, link.mappingId, 'cantaro'))} className="min-h-11 border border-warning-border px-3 font-bold disabled:opacity-50">Use Cantaro order</button>
    <button type="button" disabled={busy} onClick={() => void onAction('order', () => playlistSyncApi.resolveOrder(playlistId, link.mappingId, 'platform'))} className="min-h-11 border border-warning-border px-3 font-bold disabled:opacity-50">Use {platformName(link.service)} order</button>
  </div>;
}

function CreationRecovery({ playlistId, playlistName, link, busy, onAction }: {
  playlistId: string;
  playlistName: string;
  link: PlaylistSyncLink;
  busy: boolean;
  onAction: LinkAction;
}) {
  if (link.state === 'creation_uncertain') return <div className="mt-2 flex flex-wrap"><MusicPlaylistAttach playlistId={playlistId} playlistName={playlistName} service={link.service} busy={busy} onAttached={(operation) => onAction('recover-link', operation)} /></div>;
  return null;
}

function PlatformOptions({ link, busy, onUnlink }: { link: PlaylistSyncLink; busy: boolean; onUnlink: () => void }) {
  const { open, setOpen, triggerRef, panelRef, panelId } = useDismissiblePanel();
  return <div className="relative justify-self-end">
    <button type="button" ref={triggerRef} aria-label={`${platformName(link.service)} options`} title={`${platformName(link.service)} options`} aria-expanded={open} aria-controls={panelId} onClick={() => setOpen(!open)} className="flex size-11 items-center justify-center text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus"><Ellipsis className="size-5" aria-hidden /></button>
    {open ? <div ref={panelRef} id={panelId} className="absolute top-0 right-full z-10 mr-2 w-52 border border-border-strong bg-surface-raised p-1">
      <p className="px-3 py-2 text-xs text-content-muted">{modeLabel(link)} sync</p>
      <button type="button" disabled={busy} onClick={() => { setOpen(false); onUnlink(); }} className="min-h-11 w-full px-3 text-left text-sm text-danger-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Unlink playlist</button>
    </div> : null}
  </div>;
}

function isCoolingDown(link: PlaylistSyncLink, now: number) {
  if (!isPlaylistProviderCooldown(link.lastSyncStatus) || !link.nextAttemptAt) return false;
  return new Date(link.nextAttemptAt).getTime() > now;
}

function canSync(link: PlaylistSyncLink, now: number, busy: boolean) {
  return !busy && !link.reconnectRequired && link.state === 'active'
    && playlistLinkSyncActivity(link) === 'idle' && !isCoolingDown(link, now);
}

function LinkPrimaryAction({ playlistId, link, now, busy, syncing, onAction, create, createOpen, createTriggerRef, createPanelId }: {
  playlistId: string; link: PlaylistSyncLink; now: number; busy: boolean; syncing: boolean; onAction: LinkAction; create: PlaylistCreateReviewState;
  createOpen: boolean; createTriggerRef: RefObject<HTMLButtonElement | null>; createPanelId: string;
}) {
  if (link.state === 'creation_failed') return <PlaylistCreateTrigger label="Retry create" state={create} open={createOpen} busy={busy || !!link.reconnectRequired || isCoolingDown(link, now)} triggerRef={createTriggerRef} panelId={createPanelId} />;
  const { label, spinning } = playlistLinkSyncButton(link, syncing);
  return <button type="button" disabled={!canSync(link, now, busy)} aria-busy={spinning} onClick={() => void onAction(`sync-${link.mappingId}`, () => playlistSyncApi.run(playlistId, link.service))} className="inline-flex min-h-11 items-center gap-2 border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><RefreshCw className={`size-4 ${spinning ? 'motion-safe:animate-spin' : ''}`} aria-hidden />{label}</button>;
}

function showLinkError(link: PlaylistSyncLink) {
  return link.reconnectRequired || (link.lastError && !['pending', 'running', 'rate_limited', 'quota_limited'].includes(link.lastSyncStatus ?? ''));
}

function LinkError({ playlistId, link, busy, onAction }: { playlistId: string; link: PlaylistSyncLink; busy: boolean; onAction: LinkAction }) {
  if (!showLinkError(link)) return null;
  return <div className="mt-2 ml-9 flex flex-wrap items-center gap-x-3 gap-y-1">
      <p role="alert" className="min-w-0 text-sm leading-relaxed wrap-anywhere text-danger-content">{link.reconnectRequired ? `Reconnect ${platformName(link.service)} to allow playlist syncing.` : link.lastError}</p>
      {link.reconnectRequired ? <button type="button" disabled={busy} onClick={() => void onAction(`reconnect-${link.mappingId}`, () => platformManager.connect(link.service, { route: `/music/playlists/${playlistId}`, trigger: 'playlist-sync-reconnect' }))} className="min-h-11 shrink-0 border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Reconnect</button> : null}
    </div>;
}

function LinkAttachPanel({ playlistId, playlistName, link, busy, onAction, open, setOpen,
  panelRef, panelId, preselected, triggerRef }: {
  playlistId: string; playlistName: string; link: PlaylistSyncLink; busy: boolean; onAction: LinkAction;
  open: boolean; setOpen: Dispatch<SetStateAction<boolean>>;
  panelRef: RefObject<HTMLDivElement | null>; panelId: string;
  preselected: PlaylistCreateCandidate | undefined; triggerRef: RefObject<HTMLButtonElement | null>;
}) {
  if (!open || !preselected) return null;
  function closeAttach() { setOpen(false); triggerRef.current?.focus(); }
  return <div ref={panelRef} id={panelId} className="pt-3" onKeyDown={(event) => {
      if (event.key !== 'Escape') return;
      event.stopPropagation();
      closeAttach();
    }}><MusicPlaylistAttachForm key={preselected.servicePlaylistId} playlistId={playlistId} playlistName={playlistName} service={link.service} busy={busy} preselected={preselected} onAttached={(operation) => onAction('recover-link', operation)} onClose={closeAttach} /></div>;
}

function LinkReviewPanels({ playlistId, link, linkCount, busy, onAction, reviewOpen, reviewId, closeReview, unlinkOpen, closeUnlink }: {
  playlistId: string; link: PlaylistSyncLink; linkCount: number; busy: boolean; onAction: LinkAction;
  reviewOpen: boolean; reviewId: string; closeReview: () => void; unlinkOpen: boolean; closeUnlink: () => void;
}) {
  return <>
    {reviewOpen ? <MusicPlaylistMatchReview playlistId={playlistId} link={link} busy={busy} panelId={reviewId} onClose={closeReview} onSync={() => onAction(`sync-${link.mappingId}`, () => playlistSyncApi.run(playlistId, link.service))} /> : null}
    {unlinkOpen ? <UnlinkReview playlistId={playlistId} link={link} isLast={linkCount === 1} busy={busy} onAction={onAction} onClose={closeUnlink} /> : null}
  </>;
}

export function MusicPlaylistLinkRow({ playlistId, playlistName, link, linkCount, busy, syncing, onAction }: {
  playlistId: string;
  playlistName: string;
  link: PlaylistSyncLink;
  linkCount: number;
  busy: boolean;
  syncing: boolean;
  onAction: LinkAction;
}) {
  const [unlinkOpen, setUnlinkOpen] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!link.nextAttemptAt) return;
    const timer = window.setInterval(() => setNow(Date.now()), 10_000);
    return () => window.clearInterval(timer);
  }, [link.nextAttemptAt]);
  const reviewId = useId();
  const reviewRef = useRef<HTMLButtonElement>(null);
  const { open: createOpen, setOpen: setCreateOpen, triggerRef: createTriggerRef, panelRef: createPanelRef, panelId: createPanelId } = useDismissiblePanel();
  const create = usePlaylistCreateReview(playlistId, link.service, (operation) => onAction('retry-create', operation), createOpen, setCreateOpen);
  const [preselected, setPreselected] = useState<PlaylistCreateCandidate | undefined>();
  const { open: attaching, setOpen: setAttaching, panelRef: attachPanelRef, panelId: attachPanelId } = useDismissiblePanel();
  function closeReview() { setReviewOpen(false); reviewRef.current?.focus(); }
  return <div className="border-t border-border-subtle py-4">
    <div className="grid items-center gap-3 sm:grid-cols-[minmax(0,1fr)_15rem]">
      <LinkSummary link={link} now={now} reviewOpen={reviewOpen} reviewId={reviewId} reviewRef={reviewRef} onReview={() => setReviewOpen(!reviewOpen)} />
      <div className="grid grid-cols-[minmax(0,1fr)_6rem] items-center gap-2 max-sm:ml-9">
        <LinkPrimaryAction playlistId={playlistId} link={link} now={now} busy={busy} syncing={syncing} onAction={onAction} create={create} createOpen={createOpen} createTriggerRef={createTriggerRef} createPanelId={createPanelId} />
        <PlatformOptions link={link} busy={busy} onUnlink={() => setUnlinkOpen(true)} />
      </div>
    </div>
    <PlaylistCreatePanel open={createOpen} panelRef={createPanelRef} panelId={createPanelId} service={link.service} state={create} onSelect={(candidate) => { create.close(); setPreselected(candidate); setAttaching(true); }} />
    <LinkAttachPanel playlistId={playlistId} playlistName={playlistName} link={link} busy={busy} onAction={onAction} open={attaching} setOpen={setAttaching} panelRef={attachPanelRef} panelId={attachPanelId} preselected={preselected} triggerRef={createTriggerRef} />
    <LinkError playlistId={playlistId} link={link} busy={busy} onAction={onAction} />
    <OrderConflictReview playlistId={playlistId} link={link} busy={busy} onAction={onAction} />
    <CreationRecovery playlistId={playlistId} playlistName={playlistName} link={link} busy={busy} onAction={onAction} />
    <MusicPlaylistInitializationRetry playlistId={playlistId} link={link} busy={busy} onAction={onAction} />
    <MusicPlaylistNameProposal playlistId={playlistId} link={link} busy={busy} onAction={onAction} />
    <LinkReviewPanels playlistId={playlistId} link={link} linkCount={linkCount} busy={busy} onAction={onAction} reviewOpen={reviewOpen} reviewId={reviewId} closeReview={closeReview} unlinkOpen={unlinkOpen} closeUnlink={() => setUnlinkOpen(false)} />
  </div>;
}
