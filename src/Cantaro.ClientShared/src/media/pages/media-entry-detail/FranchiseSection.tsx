import { Check, ChevronRight, RotateCcw } from 'lucide-react';
import { DetailArtwork } from '../../components/media-entry-detail/EntryDisplayPrimitives';
import { mediaKindLabel } from '../../services/mediaFormatting';
import type { MediaFranchiseGraphDto, MediaFranchiseNodeDto } from '../../services/mediaApi';
import type { FranchiseGraphState } from './mediaEntryDetailTypes';
import {
  buildFranchisePresentation,
  continuityRelationLabel,
  relationLabel,
  type FranchiseBranch,
  type FranchisePresentation,
} from './franchiseGraph';

type FranchiseVariant = 'preview' | 'full';

interface FranchiseSectionProps {
  state: FranchiseGraphState;
  variant: FranchiseVariant;
  onRetry: () => void;
  onViewAll?: () => void;
  onNavigateTitle?: (mediaTitleId: string) => void;
}

type FranchiseNavigation = Pick<FranchiseSectionProps, 'onNavigateTitle'>;

function libraryStatusLabel(status?: string): string | null {
  if (!status) return null;
  const labels: Record<string, string> = {
    completed: 'Completed',
    current: 'Watching',
    dropped: 'Dropped',
    paused: 'Paused',
    planned: 'Planning',
    repeating: 'Rewatching',
  };
  return labels[status.toLowerCase()] ?? status;
}

function nodeMetadata(node: MediaFranchiseNodeDto): string {
  const formatLabels: Record<string, string> = {
    manga: 'Manga', movie: 'Movie', music: 'Music', novel: 'Novel',
    ona: 'ONA', one_shot: 'One-shot', ova: 'OVA', special: 'Special',
    tv: 'TV', tv_short: 'TV short',
  };
  const values = [
    node.mediaFormat ? (formatLabels[node.mediaFormat] ?? node.mediaFormat) : mediaKindLabel(node.mediaKind),
    node.episodeCount ? `${node.episodeCount} episodes` : null,
    node.startYear ? String(node.startYear) : null,
  ];
  return values.filter(Boolean).join(' · ');
}

function libraryProgress(node: MediaFranchiseNodeDto): string | null {
  return node.progressEpisodes != null
    ? `${node.progressEpisodes}${node.episodeCount ? `/${node.episodeCount}` : ''} episodes`
    : null;
}

const libraryStatusTones: Record<string, string> = {
  completed: 'is-completed',
  current: 'is-active',
  dropped: 'is-dropped',
  paused: 'is-pending',
  planned: 'is-pending',
  repeating: 'is-active',
};

function libraryStatusTone(status?: string): string {
  if (!status) return '';
  return libraryStatusTones[status.toLowerCase()] ?? 'is-custom';
}

function NodeState({ node }: { node: MediaFranchiseNodeDto }) {
  if (node.isInLibrary) {
    const status = libraryStatusLabel(node.viewerStatus) ?? 'In your library';
    const progress = libraryProgress(node);
    const tone = libraryStatusTone(node.viewerStatus);
    return (
      <span className={`media-detail-franchise-state is-tracked ${tone}`.trim()}>
        <span className="media-detail-franchise-status-line">
          <Check aria-hidden />
          <span>{status}</span>
        </span>
        {progress ? <span className="media-detail-franchise-progress">{progress}</span> : null}
      </span>
    );
  }

  return <span className="media-detail-franchise-state">Not in library</span>;
}

function NodeCardContent({ node }: { node: MediaFranchiseNodeDto }) {
  return (
    <>
      <div className="media-detail-franchise-thumb">
        <DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} />
      </div>
      <span className="media-detail-franchise-copy">
        <strong>{node.canonicalTitle}</strong>
        <span>{nodeMetadata(node)}</span>
        <NodeState node={node} />
      </span>
    </>
  );
}

type NodeAction =
  | { kind: 'title'; mediaTitleId: string; navigate: (mediaTitleId: string) => void }
  | { kind: 'external'; url: string }
  | { kind: 'static' };

function getNodeAction(node: MediaFranchiseNodeDto, navigation: FranchiseNavigation): NodeAction {
  if (node.isCurrent) return { kind: 'static' };
  if (navigation.onNavigateTitle) {
    return { kind: 'title', mediaTitleId: node.mediaTitleId, navigate: navigation.onNavigateTitle };
  }
  if (node.externalUrl) return { kind: 'external', url: node.externalUrl };
  return { kind: 'static' };
}

function NodeCard({ node, ...navigation }: { node: MediaFranchiseNodeDto } & FranchiseNavigation) {
  const content = <NodeCardContent node={node} />;
  const className = `media-detail-franchise-node${node.isCurrent ? ' is-current' : ''}`;
  const action = getNodeAction(node, navigation);

  if (action.kind === 'title') {
    return <button type="button" className={className} onClick={() => action.navigate(action.mediaTitleId)}>{content}</button>;
  }

  if (action.kind === 'external') {
    return <a className={className} href={action.url} target="_blank" rel="noreferrer">{content}</a>;
  }

  return <article className={className} aria-current={node.isCurrent ? 'page' : undefined}>{content}</article>;
}

function LoadingFranchise() {
  return (
    <div className="media-detail-franchise-loading" aria-busy="true" aria-label="Loading franchise connections">
      {[0, 1, 2].map((index) => <span key={index} className="media-detail-franchise-skeleton" />)}
    </div>
  );
}

function ContinuityLane({
  graph,
  nodes,
  navigation,
}: {
  graph: MediaFranchiseGraphDto;
  nodes: MediaFranchiseNodeDto[];
  navigation: FranchiseNavigation;
}) {
  return (
    <ol className="media-detail-franchise-sequence" aria-label="Episode continuity order">
      {nodes.map((node, index) => (
        <li key={node.mediaTitleId} className="media-detail-franchise-step">
          {index > 0 ? (
            <span className="media-detail-franchise-connector">
              <ChevronRight aria-hidden />
              <span>{continuityRelationLabel(graph, nodes[index - 1].mediaTitleId, node.mediaTitleId)}</span>
            </span>
          ) : null}
          <NodeCard node={node} {...navigation} />
        </li>
      ))}
    </ol>
  );
}

function BranchCard({
  branch,
  navigation,
}: {
  branch: FranchiseBranch;
  navigation: FranchiseNavigation;
}) {
  return (
    <li>
      <span className="media-detail-franchise-relation">{relationLabel(branch.displayRelationType)}</span>
      <NodeCard node={branch.node} {...navigation} />
    </li>
  );
}

function FranchiseHeading({ onViewAll }: { onViewAll?: () => void }) {
  return (
    <div className="media-detail-section-heading">
      <div>
        <h3>Franchise order</h3>
        <p>Direct episode continuity, followed by related stories.</p>
      </div>
      {onViewAll ? <button type="button" onClick={onViewAll}>View full graph</button> : null}
    </div>
  );
}

function LoadingSection() {
  return (
    <section className="media-detail-section">
      <FranchiseHeading />
      <LoadingFranchise />
    </section>
  );
}

function ErrorSection({ onRetry }: { onRetry: () => void }) {
  return (
    <section className="media-detail-section">
      <FranchiseHeading />
      <div className="media-detail-franchise-message" role="alert">
        <p>Couldn’t load franchise connections from AniList.</p>
        <button type="button" onClick={onRetry}><RotateCcw aria-hidden />Retry</button>
      </div>
    </section>
  );
}

function FranchiseSummary({
  variant,
  hasRelations,
  relatedTitleCount,
}: {
  variant: FranchiseVariant;
  hasRelations: boolean;
  relatedTitleCount: number;
}) {
  if (!hasRelations) {
    return <p className="media-detail-franchise-empty">AniList lists no prequels, sequels, or related titles for this entry.</p>;
  }
  if (variant !== 'preview' || relatedTitleCount === 0) return null;
  const noun = relatedTitleCount === 1 ? 'title' : 'titles';
  return <p className="media-detail-franchise-summary">{relatedTitleCount} related {noun} in the full graph.</p>;
}

function FranchiseBranches({
  presentation,
  navigation,
}: {
  presentation: FranchisePresentation;
  navigation: FranchiseNavigation;
}) {
  if (presentation.branchGroups.length === 0) return null;
  return (
    <div className="media-detail-franchise-branches">
      <h4>Related stories and adaptations</h4>
      {presentation.branchGroups.map((group) => (
        <section key={group.source.mediaTitleId}>
          <p>From <strong>{group.source.canonicalTitle}</strong></p>
          <ul>
            {group.branches.map((branch) => (
              <BranchCard
                key={`${branch.displayRelationType}:${branch.node.mediaTitleId}`}
                branch={branch}
                navigation={navigation}
              />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

function FullFranchiseBranches({
  variant,
  presentation,
  navigation,
}: {
  variant: FranchiseVariant;
  presentation: FranchisePresentation;
  navigation: FranchiseNavigation;
}) {
  if (variant !== 'full') return null;
  return <FranchiseBranches presentation={presentation} navigation={navigation} />;
}

function FranchiseSource({ graph }: { graph: MediaFranchiseGraphDto }) {
  const providerName = graph.sourceProvider === 'anilist' ? 'AniList' : graph.sourceProvider;
  const continuityNote = graph.continuity.isComplete ? '' : ' · Continuity may be incomplete';
  return <p className="media-detail-franchise-source">Relations from {providerName}{continuityNote}</p>;
}

function getViewAllAction(
  variant: FranchiseVariant,
  relatedTitleCount: number,
  onViewAll?: () => void,
) {
  return variant === 'preview' && relatedTitleCount > 0 ? onViewAll : undefined;
}

function LoadedSection({
  graph,
  variant,
  onViewAll,
  navigation,
}: {
  graph: MediaFranchiseGraphDto;
  variant: FranchiseVariant;
  onViewAll?: () => void;
  navigation: FranchiseNavigation;
}) {
  const presentation = buildFranchisePresentation(graph);
  const hasRelations = graph.relations.length > 0 || graph.nodes.length > 1;
  const viewAllAction = getViewAllAction(variant, presentation.relatedTitleCount, onViewAll);

  return (
    <section className="media-detail-section">
      <FranchiseHeading onViewAll={viewAllAction} />
      <ContinuityLane graph={graph} nodes={presentation.continuityNodes} navigation={navigation} />
      <FranchiseSummary
        variant={variant}
        hasRelations={hasRelations}
        relatedTitleCount={presentation.relatedTitleCount}
      />
      <FullFranchiseBranches variant={variant} presentation={presentation} navigation={navigation} />
      <FranchiseSource graph={graph} />
    </section>
  );
}

export function FranchiseSection(props: FranchiseSectionProps) {
  if (props.state.status === 'loading') return <LoadingSection />;
  if (props.state.status === 'error') return <ErrorSection onRetry={props.onRetry} />;
  return (
    <LoadedSection
      graph={props.state.value}
      variant={props.variant}
      onViewAll={props.onViewAll}
      navigation={{
        onNavigateTitle: props.onNavigateTitle,
      }}
    />
  );
}
